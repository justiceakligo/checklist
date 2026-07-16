using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Storage;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
                submission.Files.Select(item => new SubmittedFileValue(item.RequirementId, item.FileAssetId)).ToList()));
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
                httpContext,
                cancellationToken);
        });

        group.MapPost("/{id:guid}/request-changes", async (
            Guid id,
            ReviewSubmissionRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IAtlasClock clock,
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

    private static async Task<IResult> ReviewSubmissionAsync(
        Guid id,
        SubmissionStatus status,
        string? comment,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
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
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (submission?.Action is null)
        {
            return EndpointHelpers.Problem("not_found", "Submission was not found.", StatusCodes.Status404NotFound);
        }

        if (submission.Status != SubmissionStatus.Submitted)
        {
            return EndpointHelpers.Problem("state_conflict", "Only submitted packages can be reviewed.", StatusCodes.Status409Conflict);
        }

        submission.Status = status;
        submission.ReviewedAt = clock.UtcNow;
        submission.ReviewedByUserId = tenantContext.UserId;
        submission.ReviewComment = comment;
        if (status == SubmissionStatus.Accepted)
        {
            submission.Action.Status = ChecklistActionStatus.Completed;
            submission.Action.CompletedAt = clock.UtcNow;
        }

        var eventType = status == SubmissionStatus.Accepted
            ? "submission.accepted"
            : "submission.changes_requested";
        SecurityEndpoints.AddAudit(dbContext, organizationId, submission.ActionId, tenantContext, eventType, new { submission.Id, submission.VersionNumber }, httpContext);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(new SubmissionSummaryResponse(
            submission.Id,
            submission.ActionId,
            submission.ActionRecipientId,
            submission.VersionNumber,
            submission.Status,
            submission.SubmittedAt,
            submission.ReviewedAt));
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

public sealed record SubmittedFileValue(Guid RequirementId, Guid FileAssetId);

public sealed record SubmittedFileExportValue(
    Guid RequirementId,
    Guid FileAssetId,
    string OriginalFileName,
    string MimeType,
    long SizeBytes,
    FileScanStatus ScanStatus);

public sealed record ReviewSubmissionRequest(string? Comment);
