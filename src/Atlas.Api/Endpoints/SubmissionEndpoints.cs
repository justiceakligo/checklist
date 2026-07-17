using System.Text.Json;
using Atlas.Api.Email;
using Atlas.Application.Abstractions;
using Atlas.Application.Email;
using Atlas.Application.Settings;
using Atlas.Application.Storage;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Atlas.Api.Endpoints;

public static class SubmissionEndpoints
{
    public static IEndpointRouteBuilder MapAtlasSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/submissions").WithTags("Submissions");

        group.MapGet("", async (
            int? page,
            int? pageSize,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
            }

            var normalizedPage = EndpointHelpers.NormalizePage(page);
            var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
            var query = dbContext.Submissions.AsNoTracking();
            var total = await query.CountAsync(cancellationToken);
            var submissions = await query
                .OrderByDescending(item => item.SubmittedAt)
                .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
                .Take(normalizedPageSize)
                .Select(item => new SubmissionSummaryResponse(
                    item.Id,
                    item.ActionId,
                    item.ActionRecipientId,
                    item.VersionNumber,
                    item.Status,
                    item.SubmittedAt,
                    item.ReviewedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                items = submissions,
                page = normalizedPage,
                pageSize = normalizedPageSize,
                total
            });
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
            }

            var submission = await dbContext.Submissions
                .AsNoTracking()
                .Include(item => item.Responses)
                .Include(item => item.Files)
                .ThenInclude(item => item.FileAsset)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (submission is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission was not found.", StatusCodes.Status404NotFound);
            }

            return Results.Ok(new SubmissionDetailResponse(
                submission.Id,
                submission.ActionId,
                submission.ActionRecipientId,
                submission.VersionNumber,
                submission.Status,
                submission.SubmittedAt,
                submission.ReviewedAt,
                submission.ReviewedByUserId,
                submission.ReviewComment,
                submission.DeclarationAcceptedAt,
                submission.DeclarationIpAddress?.ToString(),
                Convert.ToHexString(submission.ContentHash).ToLowerInvariant(),
                submission.Responses.Select(item => new SubmittedResponseValue(
                    item.RequirementId,
                    item.ValueJson == null ? null : JsonSerializer.Deserialize<JsonElement>(item.ValueJson, EndpointHelpers.JsonOptions))).ToList(),
                submission.Files.Select(ToSubmittedFileValue).ToList()));
        });

        group.MapGet("/{id:guid}/export", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
            }

            var submission = await dbContext.Submissions
                .AsNoTracking()
                .Include(item => item.Action)
                .Include(item => item.ActionRecipient)
                .Include(item => item.Responses)
                .Include(item => item.Files)
                .ThenInclude(item => item.FileAsset)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (submission?.Action is null || submission.ActionRecipient is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission was not found.", StatusCodes.Status404NotFound);
            }

            var timeline = await dbContext.AuditEvents
                .AsNoTracking()
                .Where(item => item.ActionId == submission.ActionId)
                .OrderBy(item => item.CreatedAt)
                .Select(item => new AuditEventResponse(item.Id, item.EventType, item.ActorType, item.ActorId, item.EventData, item.CreatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new SubmissionExportResponse(
                submission.Id,
                submission.ActionId,
                submission.Action.Title,
                submission.ActionRecipientId,
                submission.ActionRecipient.Name,
                submission.ActionRecipient.Email,
                submission.VersionNumber,
                submission.Status,
                submission.SubmittedAt,
                submission.DeclarationAcceptedAt,
                submission.DeclarationIpAddress?.ToString(),
                Convert.ToHexString(submission.ContentHash).ToLowerInvariant(),
                submission.Responses.Select(item => new SubmittedResponseValue(
                    item.RequirementId,
                    item.ValueJson == null ? null : JsonSerializer.Deserialize<JsonElement>(item.ValueJson, EndpointHelpers.JsonOptions))).ToList(),
                submission.Files.Select(item => new SubmittedFileExportValue(
                    item.RequirementId,
                    item.FileAssetId,
                    item.FileAsset?.OriginalFileName ?? string.Empty,
                    item.FileAsset?.MimeType ?? string.Empty,
                    item.FileAsset?.SizeBytes ?? 0,
                    item.FileAsset?.ScanStatus ?? FileScanStatus.Pending)).ToList(),
                timeline));
        });

        group.MapPost("/{id:guid}/accept", async (
            Guid id,
            ReviewSubmissionRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            IAdminSettingService settings,
            IEmailService emailService,
            IConfiguration configuration,
            ISecretHasher secretHasher,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            return await ReviewSubmissionAsync(
                id,
                SubmissionStatus.Accepted,
                request.Comment,
                dbContext,
                tenantContext,
                clock,
                settings,
                emailService,
                configuration,
                secretHasher,
                httpContext,
                cancellationToken);
        });

        group.MapPost("/{id:guid}/request-changes", async (
            Guid id,
            ReviewSubmissionRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
            IAdminSettingService settings,
            IEmailService emailService,
            IConfiguration configuration,
            ISecretHasher secretHasher,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            return await ReviewSubmissionAsync(
                id,
                SubmissionStatus.ChangesRequested,
                request.Comment,
                dbContext,
                tenantContext,
                clock,
                settings,
                emailService,
                configuration,
                secretHasher,
                httpContext,
                cancellationToken);
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IObjectStorageService storage,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }
            if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
            {
                return sandboxProblem;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "admin:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Admin scope is required.", StatusCodes.Status403Forbidden);
            }

            var submission = await dbContext.Submissions
                .Include(item => item.Action)
                .Include(item => item.Files)
                .ThenInclude(item => item.FileAsset)
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (submission?.Action is null)
            {
                return EndpointHelpers.Problem("not_found", "Submission was not found.", StatusCodes.Status404NotFound);
            }

            var deletedFileIds = new List<Guid>();
            var retainedFileIds = new List<Guid>();
            foreach (var submissionFile in submission.Files)
            {
                var file = submissionFile.FileAsset;
                if (file is null)
                {
                    continue;
                }

                var referencedElsewhere = await dbContext.SubmissionFiles
                    .IgnoreQueryFilters()
                    .AnyAsync(item => item.FileAssetId == file.Id && item.SubmissionId != submission.Id, cancellationToken);
                if (referencedElsewhere)
                {
                    retainedFileIds.Add(file.Id);
                    continue;
                }

                await storage.DeleteObjectAsync(file.StorageKey, cancellationToken);
                file.DeletedAt = clock.UtcNow;
                deletedFileIds.Add(file.Id);
            }

            SecurityEndpoints.AddAudit(
                dbContext,
                organizationId,
                submission.ActionId,
                tenantContext,
                "submission.deleted",
                new { submission.Id, deletedFileIds, retainedFileIds },
                httpContext);
            dbContext.Submissions.Remove(submission);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        return app;
    }

    private static SubmittedFileValue ToSubmittedFileValue(SubmissionFile file)
    {
        return new SubmittedFileValue(
            file.RequirementId,
            file.FileAssetId,
            file.FileAsset?.OriginalFileName ?? file.DocumentName ?? string.Empty,
            file.FileAsset?.OriginalFileName ?? file.DocumentName ?? string.Empty,
            file.FileAsset?.MimeType ?? string.Empty,
            file.FileAsset?.SizeBytes ?? 0,
            file.FileAsset?.ScanStatus ?? FileScanStatus.Pending,
            file.IsPreviouslySubmitted,
            file.DocumentName,
            file.DocumentExpiresAt);
    }

    private static async Task<IResult> ReviewSubmissionAsync(
        Guid id,
        SubmissionStatus status,
        string? comment,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        IAdminSettingService settings,
        IEmailService emailService,
        IConfiguration configuration,
        ISecretHasher secretHasher,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
        {
            return problem!;
        }
        if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
        {
            return sandboxProblem;
        }

        var submission = await dbContext.Submissions
            .Include(item => item.Action)
            .ThenInclude(action => action!.Organization)
            .Include(item => item.Action)
            .ThenInclude(action => action!.CreatedByUser)
            .Include(item => item.ActionRecipient)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (submission?.Action?.Organization is null || submission.Action.CreatedByUser is null || submission.ActionRecipient is null)
        {
            return EndpointHelpers.Problem("not_found", "Submission was not found.", StatusCodes.Status404NotFound);
        }

        if (status == SubmissionStatus.Accepted && submission.Status != SubmissionStatus.Submitted)
        {
            return EndpointHelpers.Problem("state_conflict", "Only submitted packages can be reviewed.", StatusCodes.Status409Conflict);
        }

        if (status == SubmissionStatus.ChangesRequested
            && submission.Status is not (SubmissionStatus.Submitted or SubmissionStatus.ChangesRequested))
        {
            return EndpointHelpers.Problem("state_conflict", "Only submitted packages or existing change requests can be sent back to recipients.", StatusCodes.Status409Conflict);
        }

        var now = clock.UtcNow;
        var wasAlreadyChangesRequested = submission.Status == SubmissionStatus.ChangesRequested;
        submission.Status = status;
        submission.ReviewedAt = now;
        submission.ReviewedByUserId = tenantContext.UserId;
        submission.ReviewComment = comment;
        if (status == SubmissionStatus.Accepted)
        {
            submission.Action.Status = ChecklistActionStatus.Completed;
            submission.Action.CompletedAt = now;
            submission.Action.UpdatedAt = now;
            SecurityEndpoints.AddAudit(dbContext, organizationId, submission.ActionId, tenantContext, "submission.accepted", new { submission.Id, submission.VersionNumber }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToSummary(submission));
        }

        submission.Action.Status = ChecklistActionStatus.InProgress;
        submission.Action.CompletedAt = null;
        submission.Action.UpdatedAt = now;
        submission.ActionRecipient.Status = ActionRecipientStatus.Started;
        submission.ActionRecipient.LastActivityAt = now;

        var graceDaysSetting = await settings.GetAsync(organizationId, "security", "recipientTokenGraceDays", cancellationToken)
            ?? await settings.GetAsync(organizationId, "security", "recipientTokenDays", cancellationToken);
        var graceDays = EndpointHelpers.ReadPositiveIntSetting(graceDaysSetting?.ValueJson, 30);
        submission.Action.ExpiresAt = ResolveRecipientLinkExpiry(submission.Action.DueAt, submission.Action.ExpiresAt, now, graceDays);

        var rawToken = EndpointHelpers.NewOpaqueToken();
        submission.ActionRecipient.AccessTokenHash = secretHasher.HashSecret(rawToken);
        submission.ActionRecipient.TokenExpiresAt = submission.Action.ExpiresAt;
        submission.ActionRecipient.OtpCodeHash = null;
        submission.ActionRecipient.OtpExpiresAt = null;
        submission.ActionRecipient.OtpAttemptCount = 0;

        SecurityEndpoints.AddAudit(
            dbContext,
            organizationId,
            submission.ActionId,
            tenantContext,
            wasAlreadyChangesRequested ? "submission.changes_requested_updated" : "submission.changes_requested",
            new { submission.Id, submission.VersionNumber },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        var recipientLink = await EndpointHelpers.BuildRecipientLinkAsync(
            settings,
            configuration,
            organizationId,
            httpContext,
            rawToken,
            cancellationToken);
        var email = TransactionalEmailTemplates.SubmissionChangesRequested(
            FirstName(submission.ActionRecipient.Name),
            submission.Action.CreatedByUser.FullName,
            submission.Action.Organization.Name,
            submission.Action.Title,
            submission.Action.PublicReference,
            comment,
            FormatDate(submission.ActionRecipient.TokenExpiresAt),
            recipientLink);
        var send = await emailService.SendAsync(
            submission.ActionRecipient.Email,
            email.Subject,
            email.TextBody,
            email.HtmlBody,
            "requests@reqara.com",
            $"{submission.Action.Organization.Name} via Reqara",
            cancellationToken,
            new Dictionary<string, string>
            {
                ["X-Atlas-Email-Type"] = "recipient-changes-requested",
                ["X-Atlas-Action-Id"] = submission.ActionId.ToString(),
                ["X-Atlas-Recipient-Id"] = submission.ActionRecipientId.ToString(),
                ["X-Atlas-Submission-Id"] = submission.Id.ToString()
            },
            submission.Action.CreatedByUser.Email);

        AddNotificationDelivery(dbContext, organizationId, submission.ActionRecipientId, "recipient_changes_requested", send, clock);
        SecurityEndpoints.AddAudit(
            dbContext,
            organizationId,
            submission.ActionId,
            tenantContext,
            send.Sent ? "recipient.changes_requested_sent" : "recipient.changes_requested_send_failed",
            new
            {
                submission.Id,
                submission.VersionNumber,
                TokenPrefix = EndpointHelpers.TokenLogPrefix(rawToken),
                send.MessageId,
                send.Error
            },
            httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        return send.Sent
            ? Results.Ok(ToSummary(submission))
            : EndpointHelpers.Problem("email_send_failed", "Change request email could not be sent. The request is saved and can be resent.", StatusCodes.Status503ServiceUnavailable);
    }

    private static DateTimeOffset ResolveRecipientLinkExpiry(DateTimeOffset? dueAt, DateTimeOffset? currentExpiresAt, DateTimeOffset now, int graceDays)
    {
        if (currentExpiresAt is not null && currentExpiresAt > now)
        {
            return currentExpiresAt.Value;
        }

        var baseDate = dueAt is not null && dueAt > now ? dueAt.Value : now;
        return baseDate.AddDays(graceDays);
    }

    private static void AddNotificationDelivery(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid recipientId,
        string templateKey,
        EmailSendResult result,
        IAtlasClock clock)
    {
        dbContext.NotificationDeliveries.Add(new NotificationDelivery
        {
            OrganizationId = organizationId,
            ActionRecipientId = recipientId,
            Channel = DeliveryChannel.Email,
            TemplateKey = templateKey,
            ProviderMessageId = result.MessageId,
            Status = result.Sent ? NotificationDeliveryStatus.Delivered : NotificationDeliveryStatus.Failed,
            ErrorCode = Truncate(result.Error, 100),
            SentAt = result.Sent ? clock.UtcNow : null,
            DeliveredAt = result.Sent ? clock.UtcNow : null,
            CreatedAt = clock.UtcNow
        });
    }

    private static SubmissionSummaryResponse ToSummary(Submission submission)
    {
        return new SubmissionSummaryResponse(
            submission.Id,
            submission.ActionId,
            submission.ActionRecipientId,
            submission.VersionNumber,
            submission.Status,
            submission.SubmittedAt,
            submission.ReviewedAt);
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
}

public sealed record SubmissionSummaryResponse(
    Guid Id,
    Guid ActionId,
    Guid ActionRecipientId,
    int VersionNumber,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt);

public sealed record SubmissionDetailResponse(
    Guid Id,
    Guid ActionId,
    Guid ActionRecipientId,
    int VersionNumber,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt,
    Guid? ReviewedByUserId,
    string? ReviewComment,
    DateTimeOffset? DeclarationAcceptedAt,
    string? DeclarationIpAddress,
    string ContentHash,
    IReadOnlyList<SubmittedResponseValue> Responses,
    IReadOnlyList<SubmittedFileValue> Files);

public sealed record SubmissionExportResponse(
    Guid Id,
    Guid ActionId,
    string ChecklistTitle,
    Guid ActionRecipientId,
    string RecipientName,
    string RecipientEmail,
    int VersionNumber,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? DeclarationAcceptedAt,
    string? DeclarationIpAddress,
    string ContentHash,
    IReadOnlyList<SubmittedResponseValue> Responses,
    IReadOnlyList<SubmittedFileExportValue> Files,
    IReadOnlyList<AuditEventResponse> Timeline);

public sealed record SubmittedResponseValue(Guid RequirementId, JsonElement? Value);

public sealed record SubmittedFileValue(
    Guid RequirementId,
    Guid FileAssetId,
    string FileName,
    string OriginalFileName,
    string MimeType,
    long SizeBytes,
    FileScanStatus ScanStatus,
    bool IsPreviouslySubmitted,
    string? DocumentName,
    DateTimeOffset? DocumentExpiresAt);

public sealed record SubmittedFileExportValue(
    Guid RequirementId,
    Guid FileAssetId,
    string OriginalFileName,
    string MimeType,
    long SizeBytes,
    FileScanStatus ScanStatus);

public sealed record ReviewSubmissionRequest(string? Comment);
