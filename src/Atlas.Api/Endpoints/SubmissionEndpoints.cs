using System.Text.Json;
using Atlas.Application.Abstractions;
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
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }

            var submissions = await dbContext.Submissions
                .AsNoTracking()
                .OrderByDescending(item => item.SubmittedAt)
                .Select(item => new SubmissionSummaryResponse(
                    item.Id,
                    item.ActionId,
                    item.ActionRecipientId,
                    item.VersionNumber,
                    item.Status,
                    item.SubmittedAt,
                    item.ReviewedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { items = submissions });
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
                Convert.ToHexString(submission.ContentHash).ToLowerInvariant(),
                submission.Responses.Select(item => new SubmittedResponseValue(
                    item.RequirementId,
                    item.ValueJson == null ? null : JsonSerializer.Deserialize<JsonElement>(item.ValueJson, EndpointHelpers.JsonOptions))).ToList(),
                submission.Files.Select(item => new SubmittedFileValue(item.RequirementId, item.FileAssetId)).ToList()));
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
    string ContentHash,
    IReadOnlyList<SubmittedResponseValue> Responses,
    IReadOnlyList<SubmittedFileValue> Files);

public sealed record SubmittedResponseValue(Guid RequirementId, JsonElement? Value);

public sealed record SubmittedFileValue(Guid RequirementId, Guid FileAssetId);

public sealed record ReviewSubmissionRequest(string? Comment);
