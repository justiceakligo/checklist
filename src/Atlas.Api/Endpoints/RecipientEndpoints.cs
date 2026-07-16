using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Atlas.Api.Email;
using Atlas.Api.Filters;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
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
        group.MapPost("/access/resend", ResendAccessCode);
        group.MapGet("/checklist", GetChecklist);
        group.MapPatch("/responses/{requirementId:guid}", AutosaveResponse);
        group.MapPost("/uploads", CreateRecipientUploadIntent);
        group.MapGet("/uploads/{fileId:guid}", GetRecipientUploadStatus);
        group.MapPost("/uploads/{fileId:guid}/complete", CompleteRecipientUpload);
        group.MapDelete("/uploads/{fileId:guid}", DeleteRecipientUpload);
        group.MapGet("/requirements/{requirementId:guid}/previously-submitted/{fileAssetId:guid}/download-url", GetPreviouslySubmittedDownloadUrl);
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

    private static async Task<IResult> ResendAccessCode(
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IEmailService emailService,
        IAdminSettingService settings,
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
            .ThenInclude(action => action!.Organization)
            .FirstOrDefaultAsync(item => item.Id == recipientId, cancellationToken);
        if (session is null || recipient?.Action?.Organization is null)
        {
            return EndpointHelpers.Problem("recipient_session_required", "A valid recipient session is required.", StatusCodes.Status401Unauthorized);
        }

        if (!recipient.OtpRequired)
        {
            session.OtpVerified = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { sent = false, otpRequired = false, otpVerified = true });
        }

        if (session.OtpVerified)
        {
            return Results.Ok(new { sent = false, otpRequired = true, otpVerified = true });
        }

        var resend = await SendRecipientOtpAsync(
            recipient,
            dbContext,
            emailService,
            settings,
            secretHasher,
            clock,
            httpContext,
            cancellationToken,
            forceNewCode: true);
        if (resend.Problem is not null)
        {
            return resend.Problem;
        }

        if (resend.Delivery is not null)
        {
            dbContext.NotificationDeliveries.Add(resend.Delivery);
            dbContext.AuditEvents.Add(new AuditEvent
            {
                OrganizationId = organizationId,
                ActionId = recipient.ActionId,
                ActorType = ActorType.Recipient,
                ActorId = recipient.Email,
                EventType = "recipient.otp_resent",
                EventData = JsonSerializer.Serialize(new { recipient.Id, recipient.OtpExpiresAt }, EndpointHelpers.JsonOptions),
                IpAddress = httpContext.Connection.RemoteIpAddress,
                UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault()
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Results.Accepted(value: new { sent = true, otpRequired = true, otpVerified = false, expiresAt = recipient.OtpExpiresAt });
    }

    private static async Task<OtpEmailAttempt> SendRecipientOtpAsync(
        ActionRecipient recipient,
        AtlasDbContext dbContext,
        IEmailService emailService,
        IAdminSettingService settings,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        bool forceNewCode = false)
    {
        var otpMinutes = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(recipient.Action!.OrganizationId, "security", "otpMinutes", cancellationToken))?.ValueJson,
            10);
        var activeCodeExists = recipient.OtpCodeHash is not null
            && recipient.OtpExpiresAt is not null
            && recipient.OtpExpiresAt > clock.UtcNow;
        var lastSuccessfulSentAt = await dbContext.NotificationDeliveries
            .IgnoreQueryFilters()
            .Where(item => item.ActionRecipientId == recipient.Id
                && item.TemplateKey == "recipient_otp"
                && item.Status == NotificationDeliveryStatus.Delivered
                && item.SentAt != null)
            .OrderByDescending(item => item.SentAt)
            .Select(item => item.SentAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (!forceNewCode && activeCodeExists && lastSuccessfulSentAt is not null)
        {
            return new OtpEmailAttempt(null, null);
        }

        if (forceNewCode && lastSuccessfulSentAt is not null)
        {
            var cooldownSeconds = EndpointHelpers.ReadPositiveIntSetting(
                (await settings.GetAsync(recipient.Action.OrganizationId, "security", "otpResendCooldownSeconds", cancellationToken))?.ValueJson,
                60);
            var retryAt = lastSuccessfulSentAt.Value.AddSeconds(cooldownSeconds);
            if (retryAt > clock.UtcNow)
            {
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((retryAt - clock.UtcNow).TotalSeconds));
                return new OtpEmailAttempt(
                    EndpointHelpers.Problem(
                        "otp_resend_too_soon",
                        $"Please wait {retryAfterSeconds} seconds before requesting another access code.",
                        StatusCodes.Status429TooManyRequests),
                    null);
            }
        }

        var code = EndpointHelpers.NewOtpCode();
        recipient.OtpCodeHash = secretHasher.HashSecret(code);
        recipient.OtpExpiresAt = clock.UtcNow.AddMinutes(otpMinutes);
        recipient.OtpVerifiedAt = null;
        recipient.OtpAttemptCount = 0;

        await dbContext.SaveChangesAsync(cancellationToken);

        var organizationName = recipient.Action.Organization!.Name;
        var checklistTitle = recipient.Action.Title;
        var email = TransactionalEmailTemplates.RecipientOtp(organizationName, checklistTitle, code, otpMinutes);

        var result = await emailService.SendAsync(
            recipient.Email,
            email.Subject,
            email.TextBody,
            email.HtmlBody,
            "requests@reqara.com",
            cancellationToken: cancellationToken,
            headers: new Dictionary<string, string>
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
        IAdminSettingService settings,
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
        var action = recipient.Action!;
        var organization = action.Organization!;
        var requirementRows = await dbContext.Requirements
            .Where(item => item.ActionId == action.Id)
            .OrderBy(item => item.DisplayOrder)
            .ToListAsync(cancellationToken);
        var requirements = new List<RecipientRequirementResponse>();
        foreach (var requirement in requirementRows)
        {
            requirements.Add(new RecipientRequirementResponse(
                requirement.Id,
                requirement.Key,
                requirement.Type,
                requirement.Label,
                requirement.Description,
                requirement.IsRequired,
                requirement.DisplayOrder,
                ParseJson(requirement.ConfigurationJson),
                ParseJson(requirement.ValidationJson),
                requirement.ConditionJson == null ? null : ParseJson(requirement.ConditionJson),
                await FindPreviouslySubmittedDocumentAsync(
                    dbContext,
                    settings,
                    organization.Id,
                    action.Id,
                    recipient.Email,
                    requirement,
                    clock.UtcNow,
                    cancellationToken)));
        }

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
        IEntitlementService entitlements,
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

        var storageLimit = await entitlements.CanStoreFileAsync(recipient.Action!.OrganizationId, clock.UtcNow, request.SizeBytes, cancellationToken);
        if (!storageLimit.Allowed)
        {
            return EndpointHelpers.EntitlementProblem(storageLimit);
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
            RetentionUntil = clock.UtcNow.AddDays(recipient.Action.Organization!.RetentionDays),
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
            signedUrl.ExpiresAt,
            file.RetentionUntil));
    }

    private static async Task<IResult> GetRecipientUploadStatus(
        Guid fileId,
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
        var file = await dbContext.FileAssets.AsNoTracking().FirstOrDefaultAsync(
            item => item.Id == fileId && item.ActionRecipientId == recipient.Id,
            cancellationToken);
        if (file is null)
        {
            return EndpointHelpers.Problem("not_found", "File was not found.", StatusCodes.Status404NotFound);
        }

        return Results.Ok(new FileStatusResponse(file.Id, file.ScanStatus, file.ScanCompletedAt));
    }

    private static async Task<IResult> GetPreviouslySubmittedDownloadUrl(
        Guid requirementId,
        Guid fileAssetId,
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
        var requirement = await dbContext.Requirements.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == requirementId && item.ActionId == recipient.ActionId, cancellationToken);
        if (requirement is null || requirement.Type != RequirementType.File)
        {
            return EndpointHelpers.Problem("not_found", "File requirement was not found.", StatusCodes.Status404NotFound);
        }

        var candidate = await FindPreviouslySubmittedDocumentAsync(
            dbContext,
            settings,
            recipient.Action!.OrganizationId,
            recipient.ActionId,
            recipient.Email,
            requirement,
            clock.UtcNow,
            cancellationToken);
        if (candidate is null || candidate.FileAssetId != fileAssetId)
        {
            return EndpointHelpers.Problem("not_found", "Previously submitted document was not found.", StatusCodes.Status404NotFound);
        }

        if (!candidate.CanUse)
        {
            return EndpointHelpers.Problem("previous_document_not_usable", candidate.Message, StatusCodes.Status409Conflict);
        }

        var file = await dbContext.FileAssets.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == fileAssetId, cancellationToken);
        if (file is null || file.DeletedAt is not null || file.ScanStatus != FileScanStatus.Clean)
        {
            return EndpointHelpers.Problem("file_not_available", "Previously submitted document is not available for preview.", StatusCodes.Status409Conflict);
        }

        var downloadMinutes = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(file.OrganizationId, "files", "downloadUrlMinutes", cancellationToken))?.ValueJson,
            5);
        var signedUrl = await storage.CreateDownloadUrlAsync(
            new PresignedDownloadRequest(
                file.StorageKey,
                file.OriginalFileName,
                file.MimeType,
                TimeSpan.FromMinutes(downloadMinutes)),
            cancellationToken);

        SecurityEndpoints.AddAudit(
            dbContext,
            file.OrganizationId,
            recipient.ActionId,
            tenantContext,
            "recipient.previous_document_previewed",
            new { FileId = file.Id, RequirementId = requirement.Id },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(signedUrl);
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

        var isSubmitted = await dbContext.SubmissionFiles
            .AnyAsync(item => item.FileAssetId == file.Id, cancellationToken);
        if (isSubmitted)
        {
            return EndpointHelpers.Problem("state_conflict", "Submitted files cannot be deleted from the recipient portal.", StatusCodes.Status409Conflict);
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
        IAdminSettingService settings,
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
        var requestedFiles = request.Files ?? Array.Empty<SubmitFileRequest>();

        var submittedFileRequirementIds = requestedFiles.Select(item => item.RequirementId).ToHashSet();
        var duplicateFileRequirements = requestedFiles
            .GroupBy(item => item.RequirementId)
            .Where(grouping => grouping.Count() > 1)
            .Select(grouping => grouping.Key.ToString())
            .ToList();
        if (duplicateFileRequirements.Count > 0)
        {
            return EndpointHelpers.Problem("validation_failed", "Only one file choice is allowed per file requirement.", StatusCodes.Status422UnprocessableEntity);
        }

        var missingRequired = requirements
            .Where(requirement => requirement.IsRequired
                && ((requirement.Type != RequirementType.File
                        && drafts.All(draft => draft.RequirementId != requirement.Id || string.IsNullOrWhiteSpace(draft.ValueJson)))
                    || (requirement.Type == RequirementType.File
                        && !submittedFileRequirementIds.Contains(requirement.Id))))
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
            SubmittedAt = clock.UtcNow,
            DeclarationAcceptedAt = clock.UtcNow,
            DeclarationIpAddress = httpContext.Connection.RemoteIpAddress
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

        foreach (var file in requestedFiles)
        {
            var requirement = requirements.FirstOrDefault(item => item.Id == file.RequirementId);
            if (requirement is null || requirement.Type != RequirementType.File)
            {
                return EndpointHelpers.Problem("not_found", "File requirement was not found.", StatusCodes.Status404NotFound);
            }

            if (file.UsePreviouslySubmitted)
            {
                var candidate = await FindPreviouslySubmittedDocumentAsync(
                    dbContext,
                    settings,
                    action.OrganizationId,
                    action.Id,
                    recipient.Email,
                    requirement,
                    clock.UtcNow,
                    cancellationToken);
                if (candidate is null || candidate.FileAssetId != file.FileId)
                {
                    return EndpointHelpers.Problem("not_found", "Previously submitted document was not found.", StatusCodes.Status404NotFound);
                }

                if (!candidate.CanUse)
                {
                    return EndpointHelpers.Problem("previous_document_not_usable", candidate.Message, StatusCodes.Status409Conflict);
                }

                var reusedFile = await dbContext.FileAssets.FirstOrDefaultAsync(
                    item => item.Id == file.FileId && item.OrganizationId == action.OrganizationId,
                    cancellationToken);
                if (reusedFile is null || reusedFile.DeletedAt is not null || reusedFile.ScanStatus != FileScanStatus.Clean)
                {
                    return EndpointHelpers.Problem("file_not_available", "Previously submitted document is no longer available.", StatusCodes.Status409Conflict);
                }

                var extendedRetention = clock.UtcNow.AddDays(Math.Max(1, action.Organization?.RetentionDays ?? 365));
                if (!reusedFile.RetentionUntil.HasValue || reusedFile.RetentionUntil.Value < extendedRetention)
                {
                    reusedFile.RetentionUntil = extendedRetention;
                }

                submission.Files.Add(new SubmissionFile
                {
                    RequirementId = file.RequirementId,
                    FileAssetId = file.FileId,
                    IsPreviouslySubmitted = true,
                    SourceSubmissionId = candidate.SourceSubmissionId,
                    SourceSubmissionFileId = candidate.SourceSubmissionFileId,
                    ReuseConfirmedAt = clock.UtcNow,
                    ReuseConsentIpAddress = httpContext.Connection.RemoteIpAddress,
                    ReuseConsentText = "Recipient confirmed use of a previously submitted document for this checklist.",
                    DocumentName = string.IsNullOrWhiteSpace(file.DocumentName) ? candidate.DocumentName : file.DocumentName.Trim(),
                    DocumentExpiresAt = file.DocumentExpiresAt ?? candidate.DocumentExpiresAt,
                    CreatedAt = clock.UtcNow
                });
                SecurityEndpoints.AddAudit(
                    dbContext,
                    action.OrganizationId,
                    action.Id,
                    tenantContext,
                    "recipient.previous_document_confirmed",
                    new { requirement.Id, candidate.FileAssetId, candidate.SourceSubmissionId, candidate.SourceSubmissionFileId },
                    httpContext);
                continue;
            }

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
                DocumentName = string.IsNullOrWhiteSpace(file.DocumentName) ? null : file.DocumentName.Trim(),
                DocumentExpiresAt = file.DocumentExpiresAt,
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
        IAdminSettingService settings,
        IEmailService emailService,
        IConfiguration configuration,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetRecipientTenant(tenantContext, out var organizationId, out var recipientId, out var problem))
        {
            return problem!;
        }

        var recipient = await dbContext.ActionRecipients
            .Include(item => item.Action)
            .ThenInclude(action => action!.Organization)
            .Include(item => item.Action)
            .ThenInclude(action => action!.CreatedByUser)
            .FirstOrDefaultAsync(item => item.Id == recipientId, cancellationToken);
        if (recipient?.Action?.Organization is null)
        {
            return EndpointHelpers.Problem("recipient_session_required", "A valid recipient session is required.", StatusCodes.Status401Unauthorized);
        }

        if (recipient.Action.Status is ChecklistActionStatus.Cancelled or ChecklistActionStatus.Expired)
        {
            return EndpointHelpers.Problem("inactive_link", "Recipient link is no longer active.", StatusCodes.Status410Gone);
        }

        var graceDaysSetting = await settings.GetAsync(organizationId, "security", "recipientTokenGraceDays", cancellationToken)
            ?? await settings.GetAsync(organizationId, "security", "recipientTokenDays", cancellationToken);
        var graceDays = EndpointHelpers.ReadPositiveIntSetting(graceDaysSetting?.ValueJson, 30);
        recipient.Action.ExpiresAt ??= (recipient.Action.DueAt ?? clock.UtcNow).AddDays(graceDays);
        if (recipient.Action.ExpiresAt <= clock.UtcNow)
        {
            return EndpointHelpers.Problem("expired_link", "Recipient link has expired.", StatusCodes.Status410Gone);
        }

        var rawToken = EndpointHelpers.NewOpaqueToken();
        recipient.AccessTokenHash = secretHasher.HashSecret(rawToken);
        recipient.TokenExpiresAt = recipient.Action.ExpiresAt;
        recipient.LastActivityAt = clock.UtcNow;
        var link = await EndpointHelpers.BuildRecipientLinkAsync(settings, configuration, organizationId, httpContext, rawToken, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var email = TransactionalEmailTemplates.ReturnLink(
            FirstName(recipient.Name),
            recipient.Action.Organization.Name,
            recipient.Action.Title,
            FormatDate(recipient.TokenExpiresAt),
            link);
        var result = await emailService.SendAsync(
            recipient.Email,
            email.Subject,
            email.TextBody,
            email.HtmlBody,
            "requests@reqara.com",
            cancellationToken: cancellationToken,
            headers: new Dictionary<string, string>
            {
                ["X-Atlas-Email-Type"] = "recipient-return-link",
                ["X-Atlas-Action-Id"] = recipient.ActionId.ToString(),
                ["X-Atlas-Recipient-Id"] = recipient.Id.ToString()
            },
            replyToEmail: recipient.Action.CreatedByUser?.Email);

        dbContext.NotificationDeliveries.Add(new NotificationDelivery
        {
            OrganizationId = organizationId,
            ActionRecipientId = recipient.Id,
            Channel = DeliveryChannel.Email,
            TemplateKey = "recipient_return_link",
            ProviderMessageId = result.MessageId,
            Status = result.Sent ? NotificationDeliveryStatus.Delivered : NotificationDeliveryStatus.Failed,
            ErrorCode = Truncate(result.Error, 100),
            SentAt = result.Sent ? clock.UtcNow : null,
            DeliveredAt = result.Sent ? clock.UtcNow : null,
            CreatedAt = clock.UtcNow
        });
        SecurityEndpoints.AddAudit(
            dbContext,
            organizationId,
            recipient.ActionId,
            tenantContext,
            result.Sent ? "recipient.return_link_sent" : "recipient.return_link_send_failed",
            new { recipient.Id, TokenPrefix = EndpointHelpers.TokenLogPrefix(rawToken), result.Error },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        return result.Sent
            ? Results.Accepted(value: new { sent = true })
            : EndpointHelpers.Problem("email_send_failed", "Return link email could not be sent. Please try again later.", StatusCodes.Status503ServiceUnavailable);
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

    private static async Task<PreviouslySubmittedDocumentResponse?> FindPreviouslySubmittedDocumentAsync(
        AtlasDbContext dbContext,
        IAdminSettingService settings,
        Guid organizationId,
        Guid currentActionId,
        string recipientEmail,
        Requirement requirement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var policy = await ResolveDocumentReusePolicyAsync(settings, organizationId, requirement, cancellationToken);
        if (!policy.Enabled || !policy.RequirementAllowsReuse)
        {
            return null;
        }

        var normalizedEmail = recipientEmail.Trim().ToLowerInvariant();
        var candidate = await dbContext.SubmissionFiles
            .AsNoTracking()
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.Action)
            .Include(item => item.Submission)
            .ThenInclude(submission => submission!.ActionRecipient)
            .Include(item => item.Requirement)
            .Include(item => item.FileAsset)
            .Where(item => item.Submission != null
                && item.Submission.Action != null
                && item.Submission.ActionRecipient != null
                && item.Requirement != null
                && item.FileAsset != null
                && item.Submission.Status == SubmissionStatus.Accepted
                && item.Submission.ActionId != currentActionId
                && item.Submission.Action.OrganizationId == organizationId
                && item.Submission.ActionRecipient.Email == normalizedEmail
                && item.Requirement.Type == RequirementType.File
                && item.FileAsset.OrganizationId == organizationId
                && item.FileAsset.DeletedAt == null
                && (requirement.SourceTemplateRequirementId.HasValue
                    ? item.Requirement.SourceTemplateRequirementId == requirement.SourceTemplateRequirementId
                        || item.Requirement.Key == requirement.Key
                    : item.Requirement.Key == requirement.Key))
            .OrderByDescending(item => item.Submission!.ReviewedAt ?? item.Submission.SubmittedAt)
            .ThenByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (candidate?.Submission is null || candidate.Requirement is null || candidate.FileAsset is null)
        {
            return null;
        }

        var ageDays = Math.Max(0, (int)Math.Floor((now - candidate.Submission.SubmittedAt).TotalDays));
        var tooOld = ageDays > policy.MaximumAgeDays;
        var documentExpired = candidate.DocumentExpiresAt.HasValue && candidate.DocumentExpiresAt.Value <= now;
        var retentionExpired = candidate.FileAsset.RetentionUntil.HasValue && candidate.FileAsset.RetentionUntil.Value <= now;
        var fileUnavailable = candidate.FileAsset.ScanStatus != FileScanStatus.Clean || candidate.FileAsset.DeletedAt.HasValue || retentionExpired;
        var canUse = !fileUnavailable
            && !tooOld
            && (!documentExpired || policy.ReuseExpiredDocuments);
        var status = canUse
            ? "Available"
            : fileUnavailable
                ? "Unavailable"
                : documentExpired
                    ? "Expired"
                    : "TooOld";

        var documentName = string.IsNullOrWhiteSpace(candidate.DocumentName)
            ? candidate.FileAsset.OriginalFileName
            : candidate.DocumentName;
        var message = status switch
        {
            "Available" => $"We found {WithArticle(documentName)} you previously submitted to this organization on {FormatDate(candidate.Submission.SubmittedAt)}.",
            "Expired" => $"Previous document found, but it expired on {FormatDate(candidate.DocumentExpiresAt)}. Please upload a new one.",
            "TooOld" => $"Previous document found, but it is older than the {policy.MaximumAgeDays}-day reuse limit. Please upload a new one.",
            _ => "Previous document found, but it is no longer available. Please upload a new one."
        };

        return new PreviouslySubmittedDocumentResponse(
            candidate.FileAssetId,
            candidate.SubmissionId,
            candidate.Id,
            candidate.FileAsset.OriginalFileName,
            documentName,
            candidate.Submission.SubmittedAt,
            candidate.Submission.ReviewedAt,
            candidate.FileAsset.ScanStatus,
            candidate.DocumentExpiresAt,
            candidate.FileAsset.RetentionUntil,
            ageDays,
            policy.MaximumAgeDays,
            canUse,
            status,
            message,
            policy.RequireRecipientConfirmation);
    }

    private static async Task<DocumentReusePolicy> ResolveDocumentReusePolicyAsync(
        IAdminSettingService settings,
        Guid organizationId,
        Requirement requirement,
        CancellationToken cancellationToken)
    {
        var requirementPolicy = ReadRequirementReusePolicy(requirement);
        var enabled = EndpointHelpers.ReadBoolSetting(
            (await settings.GetAsync(organizationId, "documentReuse", "enabled", cancellationToken))?.ValueJson,
            true);
        var maximumAgeDays = EndpointHelpers.ReadPositiveIntSetting(
            (await settings.GetAsync(organizationId, "documentReuse", "maximumAgeDays", cancellationToken))?.ValueJson,
            365);
        var requireRecipientConfirmation = EndpointHelpers.ReadBoolSetting(
            (await settings.GetAsync(organizationId, "documentReuse", "requireRecipientConfirmation", cancellationToken))?.ValueJson,
            true);
        var reuseExpiredDocuments = EndpointHelpers.ReadBoolSetting(
            (await settings.GetAsync(organizationId, "documentReuse", "reuseExpiredDocuments", cancellationToken))?.ValueJson,
            false);

        return new DocumentReusePolicy(
            enabled,
            requirementPolicy.AllowReuse,
            requirementPolicy.MaximumAgeDays ?? maximumAgeDays,
            requirementPolicy.RequireRecipientConfirmation ?? requireRecipientConfirmation,
            requirementPolicy.ReuseExpiredDocuments ?? reuseExpiredDocuments);
    }

    private static RequirementReusePolicy ReadRequirementReusePolicy(Requirement requirement)
    {
        if (requirement.Type != RequirementType.File)
        {
            return new RequirementReusePolicy(false, null, null, null);
        }

        try
        {
            var config = ParseJson(requirement.ConfigurationJson);
            var nested = TryGetJsonObject(config, "fileReuse", out var fileReuse)
                || TryGetJsonObject(config, "previouslySubmitted", out fileReuse)
                ? fileReuse
                : default;

            var allowReuse = TryReadBoolProperty(config, "allowReuse", out var rootAllowReuse)
                ? rootAllowReuse
                : TryReadBoolProperty(config, "allowPreviouslySubmitted", out var rootAllowPreviouslySubmitted)
                    ? rootAllowPreviouslySubmitted
                    : nested.ValueKind == JsonValueKind.Object && TryReadBoolProperty(nested, "allowReuse", out var nestedAllowReuse)
                        ? nestedAllowReuse
                        : nested.ValueKind == JsonValueKind.Object && TryReadBoolProperty(nested, "allowPreviouslySubmitted", out var nestedAllowPreviouslySubmitted)
                            ? nestedAllowPreviouslySubmitted
                            : false;

            int? maximumAgeDays = null;
            if (nested.ValueKind == JsonValueKind.Object && TryReadPositiveIntProperty(nested, "maximumAgeDays", out var nestedMaximumAgeDays))
            {
                maximumAgeDays = nestedMaximumAgeDays;
            }
            else if (nested.ValueKind == JsonValueKind.Object && TryReadPositiveIntProperty(nested, "maxAgeDays", out var nestedMaxAgeDays))
            {
                maximumAgeDays = nestedMaxAgeDays;
            }
            else if (TryReadPositiveIntProperty(config, "maximumReuseAgeDays", out var rootMaximumAgeDays))
            {
                maximumAgeDays = rootMaximumAgeDays;
            }

            bool? requireRecipientConfirmation = null;
            if (nested.ValueKind == JsonValueKind.Object && TryReadBoolProperty(nested, "requireRecipientConfirmation", out var nestedRequireConfirmation))
            {
                requireRecipientConfirmation = nestedRequireConfirmation;
            }

            bool? reuseExpiredDocuments = null;
            if (nested.ValueKind == JsonValueKind.Object && TryReadBoolProperty(nested, "reuseExpiredDocuments", out var nestedReuseExpired))
            {
                reuseExpiredDocuments = nestedReuseExpired;
            }

            return new RequirementReusePolicy(allowReuse, maximumAgeDays, requireRecipientConfirmation, reuseExpiredDocuments);
        }
        catch (JsonException)
        {
            return new RequirementReusePolicy(false, null, null, null);
        }
    }

    private static bool TryGetJsonObject(JsonElement value, string propertyName, out JsonElement property)
    {
        property = default;
        return value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty(propertyName, out property)
            && property.ValueKind == JsonValueKind.Object;
    }

    private static bool TryReadBoolProperty(JsonElement value, string propertyName, out bool result)
    {
        result = default;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
        {
            result = parsed;
            return true;
        }

        return false;
    }

    private static bool TryReadPositiveIntProperty(JsonElement value, string propertyName, out int result)
    {
        result = default;
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var parsedNumber) && parsedNumber > 0)
        {
            result = parsedNumber;
            return true;
        }

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedString)
            && parsedString > 0)
        {
            result = parsedString;
            return true;
        }

        return false;
    }

    private static string WithArticle(string documentName)
    {
        var trimmed = documentName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "a previously submitted document";
        }

        var first = char.ToLowerInvariant(trimmed[0]);
        var article = first is 'a' or 'e' or 'i' or 'o' or 'u' ? "an" : "a";
        return $"{article} {trimmed}";
    }

    private static DateTimeOffset MinDate(DateTimeOffset? left, DateTimeOffset right)
    {
        return left is not null && left < right ? left.Value : right;
    }

    private static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static string FormatDate(DateTimeOffset? value)
    {
        return value is null
            ? "Not set"
            : value.Value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength ? value : value[..maxLength];
    }

    private static JsonElement ParseJson(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json, EndpointHelpers.JsonOptions);
    }

    private sealed record RecipientAccess(ActionRecipient? Recipient, RecipientAccessSession? Session, IResult? Problem);

    private sealed record OtpEmailAttempt(IResult? Problem, NotificationDelivery? Delivery);

    private sealed record RequirementReusePolicy(
        bool AllowReuse,
        int? MaximumAgeDays,
        bool? RequireRecipientConfirmation,
        bool? ReuseExpiredDocuments);

    private sealed record DocumentReusePolicy(
        bool Enabled,
        bool RequirementAllowsReuse,
        int MaximumAgeDays,
        bool RequireRecipientConfirmation,
        bool ReuseExpiredDocuments);
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
    JsonElement? Condition,
    PreviouslySubmittedDocumentResponse? PreviouslySubmitted);

public sealed record PreviouslySubmittedDocumentResponse(
    Guid FileAssetId,
    Guid SourceSubmissionId,
    Guid SourceSubmissionFileId,
    string FileName,
    string DocumentName,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    FileScanStatus ScanStatus,
    DateTimeOffset? DocumentExpiresAt,
    DateTimeOffset? RetentionUntil,
    int AgeDays,
    int MaximumAgeDays,
    bool CanUse,
    string Status,
    string Message,
    bool RequireRecipientConfirmation);

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

public sealed record SubmitFileRequest(
    Guid RequirementId,
    Guid FileId,
    bool UsePreviouslySubmitted,
    string? DocumentName,
    DateTimeOffset? DocumentExpiresAt);

public sealed record SubmissionResultResponse(
    Guid Id,
    int VersionNumber,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    string ContentHash);
