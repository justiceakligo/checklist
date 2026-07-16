using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Api.Filters;
using Atlas.Application.Abstractions;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class RecipientEndpoints
{
    public static IEndpointRouteBuilder MapAtlasRecipientEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/r/{token}", ResolveRecipientLink).WithTags("Recipient");
        app.MapGet("/c/{token}", ResolveRecipientLink).WithTags("Recipient");

        var group = app.MapGroup("/v1/recipient").WithTags("Recipient");
        group.MapPost("/access/verify", VerifyAccessCode);
        group.MapGet("/checklist", GetChecklist);
        group.MapPatch("/responses/{requirementId:guid}", AutosaveResponse);
        group.MapPost("/uploads", CreateRecipientUploadIntent);
        group.MapPost("/uploads/{fileId:guid}/complete", CompleteRecipientUpload);
        group.MapDelete("/uploads/{fileId:guid}", DeleteRecipientUpload);
        group.MapPost("/return-link", RequestReturnLink);
        group.MapPost("/submit", SubmitChecklist);
        group.MapGet("/receipt", GetReceipt);
        return app;
    }

    private static async Task<IResult> ResolveRecipientLink(
        string token,
        AtlasDbContext dbContext,
        IEmailService emailService,
        IAdminSettingService settings,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return EndpointHelpers.Problem("not_found", "Recipient link was not found.", StatusCodes.Status404NotFound);
        }

        var tokenHash = secretHasher.HashSecret(token);
        var recipient = await dbContext.ActionRecipients.IgnoreQueryFilters()
            .Include(item => item.Action)
            .ThenInclude(action => action!.Organization)
            .FirstOrDefaultAsync(item => item.AccessTokenHash == tokenHash, cancellationToken);

        if (recipient?.Action?.Organization is null)
        {
            return EndpointHelpers.Problem("not_found", "Recipient link was not found.", StatusCodes.Status404NotFound);
        }

        if (recipient.TokenExpiresAt is not null && recipient.TokenExpiresAt <= clock.UtcNow)
        {
            return EndpointHelpers.Problem("expired_link", "Recipient link has expired.", StatusCodes.Status410Gone);
        }

        if (recipient.Action.Status is ChecklistActionStatus.Cancelled or ChecklistActionStatus.Expired)
        {
            return EndpointHelpers.Problem("inactive_link", "Recipient link is no longer active.", StatusCodes.Status410Gone);
        }

        recipient.FirstViewedAt ??= clock.UtcNow;
        recipient.LastActivityAt = clock.UtcNow;
        if (recipient.Status == ActionRecipientStatus.Pending)
        {
            recipient.Status = ActionRecipientStatus.Viewed;
        }

        var otpEmailResult = recipient.OtpRequired && !recipient.OtpVerifiedAt.HasValue
            ? await SendRecipientOtpAsync(recipient, dbContext, emailService, settings, secretHasher, clock, httpContext, cancellationToken)
            : null;
        if (otpEmailResult?.Problem is not null)
        {
            return otpEmailResult.Problem;
        }

        var sessionToken = EndpointHelpers.NewOpaqueToken();
        var session = new RecipientAccessSession
        {
            ActionRecipientId = recipient.Id,
            SessionTokenHash = secretHasher.HashSecret(sessionToken),
            OtpVerified = !recipient.OtpRequired || recipient.OtpVerifiedAt.HasValue,
            CreatedAt = clock.UtcNow,
            LastSeenAt = clock.UtcNow,
            ExpiresAt = MinDate(recipient.TokenExpiresAt, clock.UtcNow.AddHours(4))
        };
        dbContext.RecipientAccessSessions.Add(session);
        if (otpEmailResult?.Delivery is not null)
        {
            dbContext.NotificationDeliveries.Add(otpEmailResult.Delivery);
            dbContext.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = recipient.Action.OrganizationId,
                ActionId = recipient.ActionId,
                ActorType = ActorType.Recipient,
                ActorId = recipient.Email,
                EventType = "recipient.otp_sent",
                EventData = JsonSerializer.Serialize(new { recipient.Id, recipient.OtpExpiresAt }, EndpointHelpers.JsonOptions),
                IpAddress = httpContext.Connection.RemoteIpAddress,
                UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault()
            });
        }
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = recipient.Action.OrganizationId,
            ActionId = recipient.ActionId,
            ActorType = ActorType.Recipient,
            ActorId = recipient.Email,
            EventType = "recipient.link_resolved",
            EventData = JsonSerializer.Serialize(new { recipient.Id }, EndpointHelpers.JsonOptions),
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault()
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        httpContext.Response.Cookies.Append(
            TenantContextMiddleware.RecipientSessionCookie,
            sessionToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.None,
                Path = "/",
                Expires = session.ExpiresAt
            });

        return Results.Ok(new RecipientAccessResponse(
            recipient.OtpRequired,
            session.OtpVerified,
            session.ExpiresAt,
            recipient.Action.Organization.Name,
            recipient.Action.Title));
    }

    private static async Task<IResult> VerifyAccessCode(
        VerifyAccessCodeRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetRecipientTenant(tenantContext, out var organizationId, out var recipientId, out var problem))
        {
            return problem!;
        }

        var session = await GetCurrentRecipientSessionAsync(dbContext, httpContext, cancellationToken);
        var recipient = await dbContext.ActionRecipients
            .Include(item => item.Action)
            .FirstOrDefaultAsync(item => item.Id == recipientId, cancellationToken);
        if (session is null || recipient?.Action is null)
        {
            return EndpointHelpers.Problem("recipient_session_required", "A valid recipient session is required.", StatusCodes.Status401Unauthorized);
        }

        if (!recipient.OtpRequired)
        {
            session.OtpVerified = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { verified = true });
        }

        if (recipient.OtpAttemptCount >= 5)
        {
            return EndpointHelpers.Problem("otp_locked", "Too many verification attempts.", StatusCodes.Status429TooManyRequests);
        }

        if (recipient.OtpExpiresAt is null || recipient.OtpExpiresAt <= clock.UtcNow)
        {
            return EndpointHelpers.Problem("otp_expired", "Access code has expired. Reopen the recipient link to request a new code.", StatusCodes.Status410Gone);
        }

        if (recipient.OtpCodeHash is null || string.IsNullOrWhiteSpace(request.Code)
            || !secretHasher.VerifySecret(request.Code.Trim(), recipient.OtpCodeHash))
        {
            recipient.OtpAttemptCount += 1;
            await dbContext.SaveChangesAsync(cancellationToken);
            return EndpointHelpers.Problem("invalid_otp", "Access code is invalid.", StatusCodes.Status422UnprocessableEntity);
        }

        recipient.OtpVerifiedAt = clock.UtcNow;
        recipient.OtpAttemptCount = 0;
        session.OtpVerified = true;
        SecurityEndpoints.AddAudit(dbContext, organizationId, recipient.ActionId, tenantContext, "recipient.otp_verified", new { recipient.Id }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { verified = true });
    }

    private static async Task<OtpEmailAttempt> SendRecipientOtpAsync(
        ActionRecipient recipient,
        AtlasDbContext dbContext,
        IEmailService emailService,
        IAdminSettingService settings,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var otpMinutes = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(recipient.Action!.OrganizationId, "security", "otpMinutes", cancellationToken))?.ValueJson,
            10);
        var code = EndpointHelpers.NewOtpCode();
        recipient.OtpCodeHash = secretHasher.HashSecret(code);
        recipient.OtpExpiresAt = clock.UtcNow.AddMinutes(otpMinutes);
        recipient.OtpVerifiedAt = null;
        recipient.OtpAttemptCount = 0;

        await dbContext.SaveChangesAsync(cancellationToken);

        var organizationName = recipient.Action.Organization!.Name;
        var checklistTitle = recipient.Action.Title;
        var textBody = $"Your Project Atlas access code for \"{checklistTitle}\" is {code}. It expires in {otpMinutes} minutes.";
        var htmlBody =
            $"<p>Your Project Atlas access code for <strong>{System.Net.WebUtility.HtmlEncode(checklistTitle)}</strong> is:</p>" +
            $"<p style=\"font-size:24px;font-weight:700;letter-spacing:4px\">{code}</p>" +
            $"<p>This code expires in {otpMinutes} minutes.</p>" +
            $"<p>Organization: {System.Net.WebUtility.HtmlEncode(organizationName)}</p>";

        var result = await emailService.SendAsync(
            recipient.Email,
            "Your Project Atlas access code",
            textBody,
            htmlBody,
            "requests@projectatlas.app",
            "Project Atlas",
            cancellationToken,
            new Dictionary<string, string>
            {
                ["X-Atlas-Email-Type"] = "recipient-otp",
                ["X-Atlas-Recipient-Id"] = recipient.Id.ToString()
            });

        if (result.Sent)
        {
            return new OtpEmailAttempt(
                null,
                new NotificationDelivery
                {
                    OrganizationId = recipient.Action.OrganizationId,
                    ActionRecipientId = recipient.Id,
                    Channel = DeliveryChannel.Email,
                    TemplateKey = "recipient_otp",
                    ProviderMessageId = result.MessageId,
                    Status = NotificationDeliveryStatus.Delivered,
                    SentAt = clock.UtcNow,
                    DeliveredAt = clock.UtcNow,
                    CreatedAt = clock.UtcNow
                });
        }

        dbContext.NotificationDeliveries.Add(new NotificationDelivery
        {
            OrganizationId = recipient.Action.OrganizationId,
            ActionRecipientId = recipient.Id,
            Channel = DeliveryChannel.Email,
            TemplateKey = "recipient_otp",
            Status = NotificationDeliveryStatus.Failed,
            ErrorCode = result.Error,
            CreatedAt = clock.UtcNow
        });
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = recipient.Action.OrganizationId,
            ActionId = recipient.ActionId,
            ActorType = ActorType.Recipient,
            ActorId = recipient.Email,
            EventType = "recipient.otp_send_failed",
            EventData = JsonSerializer.Serialize(new { recipient.Id, result.Error }, EndpointHelpers.JsonOptions),
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault()
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new OtpEmailAttempt(
            EndpointHelpers.Problem("otp_email_failed", "Access code email could not be sent. Please try again later.", StatusCodes.Status503ServiceUnavailable),
            null);
    }

    private static async Task<IResult> GetChecklist(
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireVerifiedRecipientAsync(dbContext, tenantContext, httpContext, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var recipient = access.Recipient!;
        var action = recipient.Action!;
        var organization = action.Organization!;
        var requirementRows = await dbContext.Requirements
            .Where(item => item.ActionId == action.Id)
            .OrderBy(item => item.DisplayOrder)
            .ToListAsync(cancellationToken);
        var requirements = requirementRows
            .Select(item => new RecipientRequirementResponse(
                item.Id,
                item.Key,
                item.Type,
                item.Label,
                item.Description,
                item.IsRequired,
                item.DisplayOrder,
                ParseJson(item.ConfigurationJson),
                ParseJson(item.ValidationJson),
                item.ConditionJson == null ? null : ParseJson(item.ConditionJson)))
            .ToList();

        var draftRows = await dbContext.DraftResponses
            .Where(item => item.ActionRecipientId == recipient.Id)
            .ToListAsync(cancellationToken);
        var drafts = draftRows
            .Select(item => new RecipientDraftResponse(
                item.RequirementId,
                item.ValueJson == null ? null : ParseJson(item.ValueJson),
                item.Version,
                item.UpdatedAt))
            .ToList();

        return Results.Ok(new RecipientChecklistResponse(
            new RecipientOrganizationBranding(
                organization.Name,
                organization.Slug,
                organization.AccentColor,
                organization.PrivacyStatement,
                organization.LogoFileId),
            new RecipientChecklistMetadata(
                action.Id,
                action.PublicReference,
                action.Title,
                action.Description,
                action.Status,
                action.DueAt,
                action.ExpiresAt),
            requirements,
            drafts));
    }

    private static async Task<IResult> AutosaveResponse(
        Guid requirementId,
        AutosaveResponseRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireVerifiedRecipientAsync(dbContext, tenantContext, httpContext, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var recipient = access.Recipient!;
        var requirementExists = await dbContext.Requirements
            .AnyAsync(item => item.Id == requirementId && item.ActionId == recipient.ActionId, cancellationToken);
        if (!requirementExists)
        {
            return EndpointHelpers.Problem("not_found", "Requirement was not found.", StatusCodes.Status404NotFound);
        }

        var draft = await dbContext.DraftResponses
            .FirstOrDefaultAsync(item => item.ActionRecipientId == recipient.Id && item.RequirementId == requirementId, cancellationToken);

        if (draft is not null && request.ExpectedVersion.HasValue && draft.Version != request.ExpectedVersion.Value)
        {
            return EndpointHelpers.Problem("concurrency_conflict", "Draft response version has changed.", StatusCodes.Status409Conflict);
        }

        if (draft is null)
        {
            draft = new DraftResponse
            {
                ActionRecipientId = recipient.Id,
                RequirementId = requirementId,
                Version = 1
            };
            dbContext.DraftResponses.Add(draft);
        }
        else
        {
            draft.Version += 1;
        }

        draft.ValueJson = request.Value.GetRawText();
        draft.UpdatedBySession = access.Session!.Id;
        draft.UpdatedAt = clock.UtcNow;
        recipient.StartedAt ??= clock.UtcNow;
        recipient.LastActivityAt = clock.UtcNow;
        if (recipient.Status is ActionRecipientStatus.Pending or ActionRecipientStatus.Viewed)
        {
            recipient.Status = ActionRecipientStatus.Started;
        }
        if (recipient.Action!.Status == ChecklistActionStatus.Sent)
        {
            recipient.Action.Status = ChecklistActionStatus.InProgress;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AutosaveResponseResult(requirementId, draft.Version, draft.UpdatedAt));
    }

    private static async Task<IResult> CreateRecipientUploadIntent(
        RecipientUploadIntentRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAdminSettingService settings,
        IObjectStorageService storage,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireVerifiedRecipientAsync(dbContext, tenantContext, httpContext, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var recipient = access.Recipient!;
        var requirement = await dbContext.Requirements.FirstOrDefaultAsync(
            item => item.Id == request.RequirementId && item.ActionId == recipient.ActionId,
            cancellationToken);
        if (requirement is null || requirement.Type != RequirementType.File)
        {
            return EndpointHelpers.Problem("not_found", "File requirement was not found.", StatusCodes.Status404NotFound);
        }

        if (string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.MimeType) || request.SizeBytes <= 0)
        {
            return EndpointHelpers.Problem("validation_failed", "File name, MIME type and positive size are required.", StatusCodes.Status422UnprocessableEntity);
        }

        var maxUpload = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(recipient.Action!.OrganizationId, "files", "maxUploadBytes", cancellationToken))?.ValueJson,
            10 * 1024 * 1024);
        if (request.SizeBytes > maxUpload)
        {
            return EndpointHelpers.Problem("upload_too_large", "Upload exceeds configured size limit.", StatusCodes.Status413PayloadTooLarge);
        }

        var uploadMinutes = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(recipient.Action.OrganizationId, "files", "uploadUrlMinutes", cancellationToken))?.ValueJson,
            10);
        var fileId = Guid.NewGuid();
        var storageKey = storage.BuildQuarantineKey(recipient.Action.OrganizationId, fileId, request.FileName);
        var file = new FileAsset
        {
            Id = fileId,
            OrganizationId = recipient.Action.OrganizationId,
            ActionId = recipient.ActionId,
            ActionRecipientId = recipient.Id,
            StorageKey = storageKey,
            OriginalFileName = request.FileName,
            MimeType = request.MimeType,
            Extension = Path.GetExtension(request.FileName),
            SizeBytes = request.SizeBytes,
            ScanStatus = FileScanStatus.Pending,
            CreatedAt = clock.UtcNow
        };
        dbContext.FileAssets.Add(file);
        await dbContext.SaveChangesAsync(cancellationToken);

        var signedUrl = await storage.CreateUploadUrlAsync(
            new PresignedUploadRequest(storageKey, request.MimeType, request.SizeBytes, TimeSpan.FromMinutes(uploadMinutes)),
            cancellationToken);

        return Results.Created($"/v1/recipient/uploads/{fileId}", new UploadIntentResponse(
            fileId,
            storageKey,
            signedUrl.UploadUrl,
            signedUrl.Headers,
            signedUrl.ExpiresAt));
    }

    private static async Task<IResult> CompleteRecipientUpload(
        Guid fileId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IObjectStorageService storage,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireVerifiedRecipientAsync(dbContext, tenantContext, httpContext, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var recipient = access.Recipient!;
        var file = await dbContext.FileAssets.FirstOrDefaultAsync(
            item => item.Id == fileId && item.ActionRecipientId == recipient.Id,
            cancellationToken);
        if (file is null)
        {
            return EndpointHelpers.Problem("not_found", "File was not found.", StatusCodes.Status404NotFound);
        }

        var metadata = await storage.GetObjectMetadataAsync(file.StorageKey, cancellationToken);
        if (metadata is null)
        {
            return EndpointHelpers.Problem("upload_not_found", "Uploaded object was not found in storage.", StatusCodes.Status409Conflict);
        }

        if (metadata.SizeBytes != file.SizeBytes)
        {
            return EndpointHelpers.Problem("upload_mismatch", "Uploaded object size does not match the declared size.", StatusCodes.Status409Conflict);
        }

        file.ScanStatus = FileScanStatus.Pending;
        SecurityEndpoints.AddAudit(dbContext, file.OrganizationId, file.ActionId, tenantContext, "file.uploaded", new { file.Id, file.OriginalFileName, file.SizeBytes }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new FileStatusResponse(file.Id, file.ScanStatus, file.ScanCompletedAt));
    }

    private static async Task<IResult> DeleteRecipientUpload(
        Guid fileId,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IObjectStorageService storage,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireVerifiedRecipientAsync(dbContext, tenantContext, httpContext, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var recipient = access.Recipient!;
        var file = await dbContext.FileAssets.FirstOrDefaultAsync(
            item => item.Id == fileId && item.ActionRecipientId == recipient.Id,
            cancellationToken);
        if (file is null)
        {
            return EndpointHelpers.Problem("not_found", "File was not found.", StatusCodes.Status404NotFound);
        }

        await storage.DeleteObjectAsync(file.StorageKey, cancellationToken);
        file.DeletedAt = clock.UtcNow;
        SecurityEndpoints.AddAudit(dbContext, file.OrganizationId, file.ActionId, tenantContext, "file.deleted", new { file.Id }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SubmitChecklist(
        SubmitRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireVerifiedRecipientAsync(dbContext, tenantContext, httpContext, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (!request.DeclarationAccepted)
        {
            return EndpointHelpers.Problem("declaration_required", "Declaration must be accepted before submission.", StatusCodes.Status422UnprocessableEntity);
        }

        var recipient = access.Recipient!;
        var action = recipient.Action!;
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
        var requestHash = EndpointHelpers.ComputeRequestHash(request);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await dbContext.IdempotencyRecords
                .FirstOrDefaultAsync(item => item.Key == idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    return EndpointHelpers.Problem("idempotency_conflict", "Idempotency key was already used with a different request.", StatusCodes.Status409Conflict);
                }

                var replay = JsonSerializer.Deserialize<JsonElement>(existing.ResponseJson, EndpointHelpers.JsonOptions);
                return Results.Json(replay, statusCode: existing.StatusCode);
            }
        }

        var latestSubmission = await dbContext.Submissions
            .Where(item => item.ActionRecipientId == recipient.Id)
            .OrderByDescending(item => item.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (latestSubmission?.Status is SubmissionStatus.Submitted or SubmissionStatus.Accepted)
        {
            return EndpointHelpers.Problem("already_submitted", "This checklist has already been submitted.", StatusCodes.Status409Conflict);
        }

        var requirements = await dbContext.Requirements.Where(item => item.ActionId == action.Id).ToListAsync(cancellationToken);
        var drafts = await dbContext.DraftResponses.Where(item => item.ActionRecipientId == recipient.Id).ToListAsync(cancellationToken);

        var missingRequired = requirements
            .Where(requirement => requirement.IsRequired
                && requirement.Type != RequirementType.File
                && drafts.All(draft => draft.RequirementId != requirement.Id || string.IsNullOrWhiteSpace(draft.ValueJson)))
            .Select(requirement => requirement.Key)
            .ToList();
        if (missingRequired.Count > 0)
        {
            return Results.Problem(
                title: "Validation failed",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                type: "https://docs.atlas.example/errors/validation_failed",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "validation_failed",
                    ["errors"] = new Dictionary<string, string[]> { ["requirements"] = missingRequired.ToArray() }
                });
        }

        var latestVersion = latestSubmission?.VersionNumber ?? 0;

        var submission = new Submission
        {
            ActionId = action.Id,
            ActionRecipientId = recipient.Id,
            VersionNumber = latestVersion + 1,
            Status = SubmissionStatus.Submitted,
            SubmittedAt = clock.UtcNow
        };

        foreach (var draft in drafts)
        {
            submission.Responses.Add(new SubmissionResponseEntry
            {
                RequirementId = draft.RequirementId,
                ValueJson = draft.ValueJson,
                CreatedAt = clock.UtcNow
            });
        }

        foreach (var file in request.Files)
        {
            var fileAsset = await dbContext.FileAssets.FirstOrDefaultAsync(
                item => item.Id == file.FileId
                    && item.ActionRecipientId == recipient.Id
                    && item.ActionId == action.Id,
                cancellationToken);
            if (fileAsset is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission file was not found.", StatusCodes.Status404NotFound);
            }

            if (fileAsset.ScanStatus != FileScanStatus.Clean)
            {
                return EndpointHelpers.Problem("file_not_available", "Submission file has not passed scanning.", StatusCodes.Status409Conflict);
            }

            submission.Files.Add(new SubmissionFile
            {
                RequirementId = file.RequirementId,
                FileAssetId = file.FileId,
                CreatedAt = clock.UtcNow
            });
        }

        submission.ContentHash = ComputeSubmissionHash(submission);
        recipient.SubmittedAt = clock.UtcNow;
        recipient.Status = ActionRecipientStatus.Submitted;
        action.Status = ChecklistActionStatus.Submitted;

        dbContext.Submissions.Add(submission);
        SecurityEndpoints.AddAudit(dbContext, action.OrganizationId, action.Id, tenantContext, "action.submitted", new { submission.Id, submission.VersionNumber }, httpContext);
        var response = new SubmissionResultResponse(
            submission.Id,
            submission.VersionNumber,
            submission.Status,
            submission.SubmittedAt,
            Convert.ToHexString(submission.ContentHash).ToLowerInvariant());

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            dbContext.IdempotencyRecords.Add(new IdempotencyRecord
            {
                OrganizationId = action.OrganizationId,
                Key = idempotencyKey,
                RequestHash = requestHash,
                StatusCode = StatusCodes.Status201Created,
                ResponseJson = JsonSerializer.Serialize(response, EndpointHelpers.JsonOptions),
                CreatedAt = clock.UtcNow,
                ExpiresAt = clock.UtcNow.AddHours(24)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/v1/recipient/receipt", response);
    }

    private static async Task<IResult> RequestReturnLink(
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetRecipientTenant(tenantContext, out var organizationId, out var recipientId, out var problem))
        {
            return problem!;
        }

        var recipient = await dbContext.ActionRecipients
            .Include(item => item.Action)
            .FirstOrDefaultAsync(item => item.Id == recipientId, cancellationToken);
        if (recipient?.Action is null)
        {
            return EndpointHelpers.Problem("recipient_session_required", "A valid recipient session is required.", StatusCodes.Status401Unauthorized);
        }

        dbContext.NotificationDeliveries.Add(new NotificationDelivery
        {
            OrganizationId = organizationId,
            ActionRecipientId = recipient.Id,
            Channel = DeliveryChannel.Email,
            TemplateKey = "recipient_return_link",
            Status = NotificationDeliveryStatus.Queued
        });
        SecurityEndpoints.AddAudit(dbContext, organizationId, recipient.ActionId, tenantContext, "recipient.return_link_requested", new { recipient.Id }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Accepted(value: new { queued = true });
    }

    private static async Task<IResult> GetReceipt(
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequireVerifiedRecipientAsync(dbContext, tenantContext, httpContext, cancellationToken);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var recipient = access.Recipient!;
        var submission = await dbContext.Submissions
            .Where(item => item.ActionRecipientId == recipient.Id)
            .OrderByDescending(item => item.VersionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (submission is null)
        {
            return EndpointHelpers.Problem("not_found", "Submission receipt was not found.", StatusCodes.Status404NotFound);
        }

        return Results.Ok(new SubmissionResultResponse(
            submission.Id,
            submission.VersionNumber,
            submission.Status,
            submission.SubmittedAt,
            Convert.ToHexString(submission.ContentHash).ToLowerInvariant()));
    }

    private static async Task<RecipientAccess> RequireVerifiedRecipientAsync(
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetRecipientTenant(tenantContext, out _, out var recipientId, out var problem))
        {
            return new RecipientAccess(null, null, problem);
        }

        var session = await GetCurrentRecipientSessionAsync(dbContext, httpContext, cancellationToken);
        var recipient = await dbContext.ActionRecipients
            .Include(item => item.Action)
            .ThenInclude(action => action!.Organization)
            .FirstOrDefaultAsync(item => item.Id == recipientId, cancellationToken);
        if (session is null || recipient?.Action?.Organization is null)
        {
            return new RecipientAccess(null, null, EndpointHelpers.Problem("recipient_session_required", "A valid recipient session is required.", StatusCodes.Status401Unauthorized));
        }

        if (recipient.OtpRequired && !session.OtpVerified)
        {
            return new RecipientAccess(null, null, EndpointHelpers.Problem("otp_required", "Access code verification is required.", StatusCodes.Status403Forbidden));
        }

        return new RecipientAccess(recipient, session, null);
    }

    private static async Task<RecipientAccessSession?> GetCurrentRecipientSessionAsync(
        AtlasDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.Items["AtlasRecipientSessionId"] is not Guid sessionId)
        {
            return null;
        }

        return await dbContext.RecipientAccessSessions
            .FirstOrDefaultAsync(item => item.Id == sessionId, cancellationToken);
    }

    private static byte[] ComputeSubmissionHash(Submission submission)
    {
        var payload = JsonSerializer.Serialize(new
        {
            submission.ActionId,
            submission.ActionRecipientId,
            submission.VersionNumber,
            Responses = submission.Responses
                .OrderBy(item => item.RequirementId)
                .Select(item => new { item.RequirementId, item.ValueJson }),
            Files = submission.Files
                .OrderBy(item => item.RequirementId)
                .Select(item => new { item.RequirementId, item.FileAssetId })
        }, EndpointHelpers.JsonOptions);

        return SHA256.HashData(Encoding.UTF8.GetBytes(payload));
    }

    private static DateTimeOffset MinDate(DateTimeOffset? left, DateTimeOffset right)
    {
        return left is not null && left < right ? left.Value : right;
    }

    private static JsonElement ParseJson(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json, EndpointHelpers.JsonOptions);
    }

    private sealed record RecipientAccess(ActionRecipient? Recipient, RecipientAccessSession? Session, IResult? Problem);

    private sealed record OtpEmailAttempt(IResult? Problem, NotificationDelivery? Delivery);
}

public sealed record RecipientAccessResponse(
    bool OtpRequired,
    bool OtpVerified,
    DateTimeOffset SessionExpiresAt,
    string OrganizationName,
    string ChecklistTitle);

public sealed record VerifyAccessCodeRequest(string Code);

public sealed record RecipientOrganizationBranding(
    string Name,
    string Slug,
    string? AccentColor,
    string? PrivacyStatement,
    Guid? LogoFileId);

public sealed record RecipientChecklistMetadata(
    Guid Id,
    string PublicReference,
    string Title,
    string? Description,
    ChecklistActionStatus Status,
    DateTimeOffset? DueAt,
    DateTimeOffset? ExpiresAt);

public sealed record RecipientRequirementResponse(
    Guid Id,
    string Key,
    RequirementType Type,
    string Label,
    string? Description,
    bool Required,
    int DisplayOrder,
    JsonElement Configuration,
    JsonElement Validation,
    JsonElement? Condition);

public sealed record RecipientDraftResponse(
    Guid RequirementId,
    JsonElement? Value,
    int Version,
    DateTimeOffset UpdatedAt);

public sealed record RecipientChecklistResponse(
    RecipientOrganizationBranding Organization,
    RecipientChecklistMetadata Checklist,
    IReadOnlyList<RecipientRequirementResponse> Requirements,
    IReadOnlyList<RecipientDraftResponse> Drafts);

public sealed record AutosaveResponseRequest(JsonElement Value, int? ExpectedVersion);

public sealed record AutosaveResponseResult(Guid RequirementId, int Version, DateTimeOffset UpdatedAt);

public sealed record RecipientUploadIntentRequest(Guid RequirementId, string FileName, long SizeBytes, string MimeType);

public sealed record FileStatusResponse(Guid FileId, FileScanStatus ScanStatus, DateTimeOffset? ScanCompletedAt);

public sealed record SubmitRequest(bool DeclarationAccepted, IReadOnlyList<SubmitFileRequest> Files);

public sealed record SubmitFileRequest(Guid RequirementId, Guid FileId);

public sealed record SubmissionResultResponse(
    Guid Id,
    int VersionNumber,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    string ContentHash);
