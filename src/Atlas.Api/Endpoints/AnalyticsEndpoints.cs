using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class AnalyticsEndpoints
{
    private static readonly PlatformStaffRole[] PlatformAnalyticsRoles =
        [PlatformStaffRole.Owner, PlatformStaffRole.Admin, PlatformStaffRole.Support, PlatformStaffRole.Finance];

    private static readonly PlatformStaffRole[] InvestorRoles =
        [PlatformStaffRole.Owner, PlatformStaffRole.Admin, PlatformStaffRole.Finance];

    private const int MinimumBenchmarkCohortSize = 5;
    private const string BenchmarkPrivacyNote = "Benchmarks are anonymized and aggregated. Reqara never exposes another organization's name, recipient, checklist, file, or raw activity to a customer dashboard.";

    public static IEndpointRouteBuilder MapAtlasAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");
        var organization = v1.MapGroup("/analytics").WithTags("Organization Analytics");

        organization.MapGet("/overview", GetOrganizationOverview);
        organization.MapGet("/completion", GetOrganizationCompletion);
        organization.MapGet("/templates", GetOrganizationTemplateAnalytics);
        organization.MapGet("/requirements", GetOrganizationRequirementAnalytics);
        organization.MapGet("/reminders", GetOrganizationReminderAnalytics);
        organization.MapGet("/recipient-experience", GetOrganizationRecipientExperience);
        organization.MapGet("/compliance", GetOrganizationCompliance);
        organization.MapGet("/benchmarks", GetOrganizationBenchmarks);
        organization.MapGet("/metric-definitions", GetOrganizationMetricDefinitions);
        organization.MapPost("/events", TrackOrganizationAnalyticsEvent);

        v1.MapGet("/usage/current", GetOrganizationUsageCurrent).WithTags("Usage");

        var platform = v1.MapGroup("/platform");
        var platformAnalytics = platform.MapGroup("/analytics").WithTags("Platform Analytics");
        platformAnalytics.MapGet("/overview", GetPlatformOverview);
        platformAnalytics.MapGet("/growth", GetPlatformGrowth);
        platformAnalytics.MapGet("/activation", GetPlatformActivation);
        platformAnalytics.MapGet("/engagement", GetPlatformEngagement);
        platformAnalytics.MapGet("/revenue", GetPlatformRevenue);
        platformAnalytics.MapGet("/retention/cohorts", GetPlatformRetentionCohorts);
        platformAnalytics.MapGet("/unit-economics", GetPlatformUnitEconomics);
        platformAnalytics.MapGet("/requirements", GetPlatformRequirementAnalytics);
        platformAnalytics.MapGet("/compliance", GetPlatformCompliance);
        platformAnalytics.MapGet("/segments", GetPlatformSegments);
        platformAnalytics.MapGet("/time-saved", GetPlatformTimeSaved);
        platformAnalytics.MapGet("/predictive-insights", GetPlatformPredictiveInsights);
        platformAnalytics.MapGet("/benchmarks", GetPlatformBenchmarks);
        platformAnalytics.MapGet("/operations", GetPlatformOperations);
        platformAnalytics.MapGet("/system-health", GetPlatformSystemHealth);
        platformAnalytics.MapGet("/security", GetPlatformSecurity);
        platformAnalytics.MapGet("/events", ListPlatformAnalyticsEvents);
        platformAnalytics.MapGet("/metric-definitions", GetPlatformMetricDefinitions);

        var investor = platform.MapGroup("/investor").WithTags("Investor & Board");
        investor.MapGet("/summary", GetInvestorSummary);
        investor.MapGet("/revenue-trend", GetInvestorRevenueTrend);
        investor.MapGet("/organization-growth", GetInvestorOrganizationGrowth);
        investor.MapGet("/product-usage", GetInvestorProductUsage);
        investor.MapGet("/retention", GetInvestorRetention);
        investor.MapGet("/unit-economics", GetInvestorUnitEconomics);
        investor.MapGet("/verticals", GetInvestorVerticals);
        investor.MapGet("/milestones", GetInvestorMilestones);
        investor.MapGet("/reports", ListInvestorReports);
        investor.MapPost("/reports", CreateInvestorReport);
        investor.MapGet("/reports/{id:guid}", GetInvestorReport);

        return app;
    }

    private static async Task<IResult> GetOrganizationOverview(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var now = clock.UtcNow;
        var actions = await dbContext.Actions.AsNoTracking()
            .Include(item => item.Recipients)
            .Include(item => item.Requirements)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.AsNoTracking()
            .ToListAsync(cancellationToken);
        var draftResponses = await dbContext.DraftResponses.AsNoTracking()
            .ToListAsync(cancellationToken);

        var activeActions = actions
            .Where(action => action.Status is ChecklistActionStatus.Sent or ChecklistActionStatus.InProgress or ChecklistActionStatus.Submitted)
            .ToList();
        var overdueActions = activeActions
            .Where(action => action.DueAt.HasValue && action.DueAt.Value < now)
            .ToList();
        var awaitingRecipient = activeActions
            .SelectMany(action => action.Recipients.Select(recipient => new { action, recipient }))
            .Count(item => item.recipient.Status is ActionRecipientStatus.Pending or ActionRecipientStatus.Viewed);
        var inProgress = activeActions
            .SelectMany(action => action.Recipients.Select(recipient => new { action, recipient }))
            .Count(item => item.action.Status == ChecklistActionStatus.InProgress
                || item.recipient.Status == ActionRecipientStatus.Started);
        var submitted = submissions.Count(item => item.SubmittedAt >= period.From && item.SubmittedAt <= period.To);

        var needsAttention = activeActions
            .SelectMany(action => action.Recipients.Select(recipient => new { action, recipient }))
            .Where(item => item.recipient.Status != ActionRecipientStatus.Submitted
                && item.action.Status is not ChecklistActionStatus.Completed
                    and not ChecklistActionStatus.Cancelled
                    and not ChecklistActionStatus.Expired)
            .OrderByDescending(item => item.action.DueAt.HasValue && item.action.DueAt.Value < now)
            .ThenBy(item => item.action.DueAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(item => item.recipient.LastActivityAt ?? item.recipient.CreatedAt)
            .Take(10)
            .Select(item =>
            {
                var required = item.action.Requirements.Where(requirement => requirement.IsRequired).ToList();
                var completedDraftRequirementIds = draftResponses
                    .Where(draft => draft.ActionRecipientId == item.recipient.Id && !string.IsNullOrWhiteSpace(draft.ValueJson))
                    .Select(draft => draft.RequirementId)
                    .ToHashSet();
                var missingRequired = required.Count(requirement => !completedDraftRequirementIds.Contains(requirement.Id));
                var daysOverdue = item.action.DueAt.HasValue && item.action.DueAt.Value < now
                    ? Math.Max(0, (int)Math.Ceiling((now - item.action.DueAt.Value).TotalDays))
                    : 0;

                return new OrganizationNeedsAttentionItem(
                    item.action.Id,
                    item.recipient.Id,
                    item.recipient.Name,
                    item.recipient.Email,
                    item.action.Title,
                    missingRequired,
                    item.action.DueAt,
                    daysOverdue,
                    item.recipient.LastActivityAt,
                    item.action.DueAt.HasValue && item.action.DueAt.Value < now ? "send_overdue_reminder" : "send_reminder");
            })
            .ToList();

        var upcomingDueDates = activeActions
            .Where(item => item.DueAt.HasValue && item.DueAt.Value >= now)
            .OrderBy(item => item.DueAt)
            .Take(10)
            .Select(item => new DueDateAnalyticsItem(item.Id, item.Title, item.DueAt!.Value, item.Status))
            .ToList();

        var recentSubmissions = await dbContext.Submissions.AsNoTracking()
            .Include(item => item.Action)
            .Include(item => item.ActionRecipient)
            .OrderByDescending(item => item.SubmittedAt)
            .Take(10)
            .Select(item => new RecentSubmissionAnalyticsItem(
                item.Id,
                item.ActionId,
                item.Action == null ? string.Empty : item.Action.Title,
                item.ActionRecipientId,
                item.ActionRecipient == null ? string.Empty : item.ActionRecipient.Name,
                item.ActionRecipient == null ? string.Empty : item.ActionRecipient.Email,
                item.Status,
                item.SubmittedAt,
                item.ReviewedAt))
            .ToListAsync(cancellationToken);

        var expiringDocuments = await dbContext.SubmissionFiles.AsNoTracking()
            .Where(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value >= now && item.DocumentExpiresAt.Value <= now.AddDays(90))
            .Include(item => item.Submission)
            .ThenInclude(item => item!.ActionRecipient)
            .OrderBy(item => item.DocumentExpiresAt)
            .Take(10)
            .Select(item => new ExpiringDocumentAnalyticsItem(
                item.FileAssetId,
                item.DocumentName,
                item.DocumentExpiresAt!.Value,
                item.Submission == null ? null : item.Submission.ActionRecipient == null ? null : item.Submission.ActionRecipient.Name,
                item.Submission == null ? null : item.Submission.ActionRecipient == null ? null : item.Submission.ActionRecipient.Email))
            .ToListAsync(cancellationToken);

        return Results.Ok(new OrganizationOverviewAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            new OrganizationOverviewSummary(activeActions.Count, awaitingRecipient, inProgress, overdueActions.Count, submitted),
            needsAttention,
            upcomingDueDates,
            recentSubmissions,
            expiringDocuments));
    }

    private static async Task<IResult> GetOrganizationCompletion(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var recipients = await dbContext.ActionRecipients.AsNoTracking()
            .Include(item => item.Action)
            .Where(item => item.Action != null
                && item.Action.SentAt.HasValue
                && item.Action.SentAt.Value >= period.From
                && item.Action.SentAt.Value <= period.To)
            .ToListAsync(cancellationToken);
        var recipientIds = recipients.Select(item => item.Id).ToHashSet();
        var submissions = await dbContext.Submissions.AsNoTracking()
            .Include(item => item.Action)
            .Where(item => recipientIds.Contains(item.ActionRecipientId))
            .ToListAsync(cancellationToken);
        var deliveries = await dbContext.NotificationDeliveries.AsNoTracking()
            .Where(item => item.TemplateKey == "recipient_invitation"
                && item.CreatedAt >= period.From
                && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);

        var sent = recipients.Count;
        var delivered = deliveries.Count(item => item.Status == NotificationDeliveryStatus.Delivered);
        var viewed = recipients.Count(item => item.FirstViewedAt.HasValue);
        var started = recipients.Count(item => item.StartedAt.HasValue || item.Status is ActionRecipientStatus.Started or ActionRecipientStatus.Submitted);
        var submitted = recipients.Count(item => item.SubmittedAt.HasValue || item.Status == ActionRecipientStatus.Submitted);
        var accepted = submissions.Count(item => item.Status == SubmissionStatus.Accepted);
        var submittedRows = submissions.Where(item => item.SubmittedAt >= period.From && item.SubmittedAt <= period.To).ToList();

        var completionDurations = submittedRows
            .Where(item => item.Action?.SentAt is not null && item.SubmittedAt >= item.Action.SentAt.Value)
            .Select(item => item.SubmittedAt - item.Action!.SentAt!.Value);
        var completedWithDueDate = submittedRows.Where(item => item.Action?.DueAt is not null).ToList();
        var completedBeforeDueRate = Rate(
            completedWithDueDate.Count(item => item.SubmittedAt <= item.Action!.DueAt!.Value),
            completedWithDueDate.Count);
        var firstAttemptAcceptanceRate = Rate(
            submissions.Count(item => item.Status == SubmissionStatus.Accepted && item.VersionNumber == 1),
            accepted);

        return Results.Ok(new CompletionAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            new CompletionFunnel(sent, delivered, viewed, started, submitted, accepted),
            new CompletionRates(
                Rate(delivered, sent),
                Rate(viewed, delivered),
                Rate(started, viewed),
                Rate(submitted, started),
                Rate(accepted, submitted)),
            new CompletionPerformance(
                MedianMinutes(completionDurations),
                completedBeforeDueRate,
                firstAttemptAcceptanceRate)));
    }

    private static async Task<IResult> GetOrganizationTemplateAnalytics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? templateId,
        int? page,
        int? pageSize,
        string? sort,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var actions = await dbContext.Actions.AsNoTracking()
            .Include(item => item.Template)
            .Include(item => item.Requirements)
            .Where(item => item.SentAt.HasValue
                && item.SentAt.Value >= period.From
                && item.SentAt.Value <= period.To
                && (!templateId.HasValue || item.TemplateId == templateId.Value))
            .ToListAsync(cancellationToken);
        var actionIds = actions.Select(item => item.Id).ToHashSet();
        var submissions = await dbContext.Submissions.AsNoTracking()
            .Include(item => item.Action)
            .Where(item => actionIds.Contains(item.ActionId))
            .ToListAsync(cancellationToken);
        var reminders = await dbContext.ReminderSchedules.AsNoTracking()
            .Include(item => item.ActionRecipient)
            .Where(item => item.SentAt.HasValue
                && item.SentAt.Value >= period.From
                && item.SentAt.Value <= period.To
                && item.ActionRecipient != null
                && actionIds.Contains(item.ActionRecipient.ActionId))
            .ToListAsync(cancellationToken);

        var rows = actions
            .GroupBy(item => new
            {
                TemplateId = item.TemplateId,
                TemplateName = item.Template == null ? "Custom checklist" : item.Template.Name
            })
            .Select(group =>
            {
                var ids = group.Select(item => item.Id).ToHashSet();
                var templateSubmissions = submissions.Where(item => ids.Contains(item.ActionId)).ToList();
                var sent = group.Count();
                var submitted = templateSubmissions.Select(item => item.ActionRecipientId).Distinct().Count();
                var accepted = templateSubmissions.Count(item => item.Status == SubmissionStatus.Accepted);
                var durations = templateSubmissions
                    .Where(item => item.Action?.SentAt is not null && item.SubmittedAt >= item.Action.SentAt.Value)
                    .Select(item => item.SubmittedAt - item.Action!.SentAt!.Value);
                var reminderCount = reminders.Count(item => item.ActionRecipient != null && ids.Contains(item.ActionRecipient.ActionId));

                return new TemplateAnalyticsItem(
                    group.Key.TemplateId,
                    group.Key.TemplateName,
                    sent,
                    submitted,
                    Rate(submitted, sent),
                    MedianMinutes(durations),
                    sent == 0 ? null : Math.Round((decimal)reminderCount / sent, 2),
                    Rate(templateSubmissions.Count(item => item.Status == SubmissionStatus.Accepted && item.VersionNumber == 1), accepted),
                    null,
                    null,
                    null);
            })
            .ToList();

        rows = (sort ?? "sent_desc").Trim().ToLowerInvariant() switch
        {
            "completion_rate_asc" => rows.OrderBy(item => item.CompletionRate ?? -1).ToList(),
            "completion_rate_desc" => rows.OrderByDescending(item => item.CompletionRate ?? -1).ToList(),
            "name_asc" => rows.OrderBy(item => item.TemplateName).ToList(),
            "name_desc" => rows.OrderByDescending(item => item.TemplateName).ToList(),
            _ => rows.OrderByDescending(item => item.Sent).ToList()
        };

        var normalizedPage = EndpointHelpers.NormalizePage(page);
        var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize, fallback: 20);
        var total = rows.Count;
        var items = rows
            .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
            .Take(normalizedPageSize)
            .ToList();

        return Results.Ok(new PagedAnalyticsResponse<TemplateAnalyticsItem>(
            items,
            new PaginationResponse(normalizedPage, normalizedPageSize, total)));
    }

    private static async Task<IResult> GetOrganizationRequirementAnalytics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? templateId,
        int? page,
        int? pageSize,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var requirements = await dbContext.Requirements.AsNoTracking()
            .Include(item => item.Action)
            .Where(item => item.Action != null
                && item.Action.SentAt.HasValue
                && item.Action.SentAt.Value >= period.From
                && item.Action.SentAt.Value <= period.To
                && (!templateId.HasValue || item.Action.TemplateId == templateId.Value))
            .ToListAsync(cancellationToken);
        var requirementIds = requirements.Select(item => item.Id).ToHashSet();
        var draftResponses = await dbContext.DraftResponses.AsNoTracking()
            .Where(item => requirementIds.Contains(item.RequirementId))
            .ToListAsync(cancellationToken);
        var submissionResponses = await dbContext.SubmissionResponses.AsNoTracking()
            .Where(item => requirementIds.Contains(item.RequirementId))
            .ToListAsync(cancellationToken);
        var submissionFiles = await dbContext.SubmissionFiles.AsNoTracking()
            .Where(item => requirementIds.Contains(item.RequirementId))
            .ToListAsync(cancellationToken);

        var rows = requirements
            .GroupBy(item => new { item.SourceTemplateRequirementId, item.Key, item.Label, item.Type })
            .Select(group =>
            {
                var ids = group.Select(item => item.Id).ToHashSet();
                var completed = group.Key.Type == RequirementType.File
                    ? submissionFiles.Count(item => ids.Contains(item.RequirementId))
                    : submissionResponses.Count(item => ids.Contains(item.RequirementId) && !string.IsNullOrWhiteSpace(item.ValueJson));

                return new RequirementAnalyticsItem(
                    group.Key.SourceTemplateRequirementId,
                    group.Key.Key,
                    group.Key.Label,
                    group.Key.Type,
                    group.Count(),
                    completed,
                    Rate(completed, group.Count()),
                    null,
                    null,
                    null,
                    null,
                    null,
                    Math.Max(0, group.Count() - draftResponses.Count(item => ids.Contains(item.RequirementId))));
            })
            .OrderByDescending(item => item.PresentedCount)
            .ThenBy(item => item.Label)
            .ToList();

        var normalizedPage = EndpointHelpers.NormalizePage(page);
        var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize, fallback: 20);
        var total = rows.Count;
        var items = rows
            .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
            .Take(normalizedPageSize)
            .ToList();

        return Results.Ok(new PagedAnalyticsResponse<RequirementAnalyticsItem>(
            items,
            new PaginationResponse(normalizedPage, normalizedPageSize, total)));
    }

    private static async Task<IResult> GetOrganizationReminderAnalytics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var reminders = await dbContext.ReminderSchedules.AsNoTracking()
            .Include(item => item.ActionRecipient)
            .Where(item => item.SentAt.HasValue && item.SentAt.Value >= period.From && item.SentAt.Value <= period.To)
            .ToListAsync(cancellationToken);
        var recipientIds = reminders
            .Where(item => item.ActionRecipientId != Guid.Empty)
            .Select(item => item.ActionRecipientId)
            .ToHashSet();
        var submissions = await dbContext.Submissions.AsNoTracking()
            .Where(item => recipientIds.Contains(item.ActionRecipientId))
            .ToListAsync(cancellationToken);

        var automaticSent = reminders.Count(item => item.Type is ReminderType.BeforeDue or ReminderType.Overdue);
        var manualSent = reminders.Count(item => item.Type == ReminderType.Manual);
        var completionWithin24 = reminders.Count(reminder => submissions.Any(submission =>
            submission.ActionRecipientId == reminder.ActionRecipientId
            && reminder.SentAt.HasValue
            && submission.SubmittedAt >= reminder.SentAt.Value
            && submission.SubmittedAt <= reminder.SentAt.Value.AddHours(24)));
        var completionWithin72 = reminders.Count(reminder => submissions.Any(submission =>
            submission.ActionRecipientId == reminder.ActionRecipientId
            && reminder.SentAt.HasValue
            && submission.SubmittedAt >= reminder.SentAt.Value
            && submission.SubmittedAt <= reminder.SentAt.Value.AddHours(72)));
        var submittedRecipients = submissions.Select(item => item.ActionRecipientId).Distinct().ToHashSet();
        decimal? remindersBeforeCompletion = submittedRecipients.Count == 0
            ? null
            : Math.Round((decimal)reminders.Count(item => submittedRecipients.Contains(item.ActionRecipientId)) / submittedRecipients.Count, 2);

        var timing = reminders
            .GroupBy(item => item.Type.ToString())
            .Select(group =>
            {
                var sent = group.Count();
                var within24 = group.Count(reminder => submissions.Any(submission =>
                    submission.ActionRecipientId == reminder.ActionRecipientId
                    && reminder.SentAt.HasValue
                    && submission.SubmittedAt >= reminder.SentAt.Value
                    && submission.SubmittedAt <= reminder.SentAt.Value.AddHours(24)));
                return new ReminderTimingPerformance(group.Key, sent, within24, Rate(within24, sent));
            })
            .OrderBy(item => item.Timing)
            .ToList();

        return Results.Ok(new ReminderAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            new ReminderAnalyticsSummary(
                automaticSent,
                manualSent,
                Rate(completionWithin24, reminders.Count),
                Rate(completionWithin72, reminders.Count),
                remindersBeforeCompletion),
            timing));
    }

    private static async Task<IResult> GetOrganizationRecipientExperience(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var sessions = await dbContext.RecipientAccessSessions.AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.AsNoTracking()
            .Where(item => item.SubmittedAt >= period.From && item.SubmittedAt <= period.To)
            .ToListAsync(cancellationToken);
        var recipientSessionCounts = sessions
            .GroupBy(item => item.ActionRecipientId)
            .ToDictionary(group => group.Key, group => group.Count());
        var returningRecipients = recipientSessionCounts.Count(item => item.Value > 1);
        var sessionDurations = sessions
            .Where(item => item.LastSeenAt >= item.CreatedAt)
            .Select(item => item.LastSeenAt - item.CreatedAt);
        var submittedRecipientIds = submissions.Select(item => item.ActionRecipientId).Distinct().ToHashSet();
        var sessionsForSubmitted = recipientSessionCounts
            .Where(item => submittedRecipientIds.Contains(item.Key))
            .Select(item => item.Value)
            .ToList();

        return Results.Ok(new RecipientExperienceAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            MobileShare: null,
            DesktopShare: null,
            SaveAndReturnRate: Rate(returningRecipients, recipientSessionCounts.Count),
            AverageSessionsPerSubmission: sessionsForSubmitted.Count == 0 ? null : Math.Round((decimal)sessionsForSubmitted.Sum() / sessionsForSubmitted.Count, 2),
            MedianRecipientSessionDurationMinutes: MedianMinutes(sessionDurations),
            UploadFailureRate: null,
            SubmissionRetryRate: Rate(submissions.Count(item => item.VersionNumber > 1), submissions.Count),
            TurnstileFailureRate: null,
            LinkRecoveryRequests: null,
            RecipientSatisfaction: null));
    }

    private static async Task<IResult> GetOrganizationCompliance(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var now = clock.UtcNow;
        var documents = await dbContext.SubmissionFiles.AsNoTracking()
            .ToListAsync(cancellationToken);
        var actions = await dbContext.Actions.AsNoTracking()
            .Include(item => item.Requirements)
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.AsNoTracking()
            .ToListAsync(cancellationToken);
        var files = await dbContext.FileAssets.AsNoTracking()
            .ToListAsync(cancellationToken);

        var activeActionIds = actions
            .Where(item => item.Status is ChecklistActionStatus.Sent or ChecklistActionStatus.InProgress or ChecklistActionStatus.Submitted)
            .Select(item => item.Id)
            .ToHashSet();
        var submittedActionIds = submissions.Select(item => item.ActionId).ToHashSet();
        var missingRequiredDocuments = actions
            .Where(item => activeActionIds.Contains(item.Id) && !submittedActionIds.Contains(item.Id))
            .Sum(item => item.Requirements.Count(requirement => requirement.IsRequired && requirement.Type == RequirementType.File));

        return Results.Ok(new ComplianceAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            DocumentsExpiring30Days: documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value >= now && item.DocumentExpiresAt.Value <= now.AddDays(30)),
            DocumentsExpiring60Days: documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value >= now && item.DocumentExpiresAt.Value <= now.AddDays(60)),
            DocumentsExpiring90Days: documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value >= now && item.DocumentExpiresAt.Value <= now.AddDays(90)),
            ExpiredDocuments: documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value < now),
            MissingRequiredDocuments: missingRequiredDocuments,
            PendingReviews: submissions.Count(item => item.Status == SubmissionStatus.Submitted),
            FilesPendingScan: files.Count(item => item.ScanStatus == FileScanStatus.Pending),
            FilesQuarantined: files.Count(item => item.ScanStatus == FileScanStatus.Rejected),
            RetentionDeletionsUpcoming: files.Count(item => item.RetentionUntil.HasValue && item.RetentionUntil.Value >= now && item.RetentionUntil.Value <= now.AddDays(30)),
            ReusedPreviousSubmissions: documents.Count(item => item.IsPreviouslySubmitted)));
    }

    private static async Task<IResult> GetOrganizationBenchmarks(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
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
        if (!EndpointHelpers.HasScope(tenantContext, "dashboard:read"))
        {
            return EndpointHelpers.Problem("forbidden", "Organization analytics access is required.", StatusCodes.Status403Forbidden);
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var rows = await BuildBenchmarkRowsAsync(dbContext, period, cancellationToken);
        var current = rows.FirstOrDefault(item => item.OrganizationId == organizationId)
            ?? OrganizationBenchmarkRow.Empty(organizationId, "free");
        var cohortRows = rows
            .Where(item => item.OrganizationId != organizationId
                && item.SentRecipients > 0
                && string.Equals(item.PlanCode, current.PlanCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var benchmarksAvailable = cohortRows.Count >= MinimumBenchmarkCohortSize;
        var metrics = BuildOrganizationBenchmarkMetrics(current, cohortRows, benchmarksAvailable);

        return Results.Ok(new OrganizationBenchmarkAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            Cohort: $"same_plan:{current.PlanCode}",
            CohortLabel: $"Similar {current.PlanCode} workspaces",
            CohortSampleSize: cohortRows.Count,
            MinimumCohortSize: MinimumBenchmarkCohortSize,
            BenchmarksAvailable: benchmarksAvailable,
            Metrics: metrics,
            DelayDrivers: BuildBenchmarkDelayDrivers(current),
            Insights: BuildOrganizationBenchmarkInsights(current, metrics, benchmarksAvailable),
            PrivacyNote: BenchmarkPrivacyNote));
    }

    private static async Task<IResult> GetOrganizationUsageCurrent(
        IEntitlementService entitlements,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }

        var snapshot = await entitlements.GetOrganizationEntitlementsAsync(tenantContext.OrganizationId!.Value, clock.UtcNow, cancellationToken);
        return Results.Ok(new UsageCurrentResponse(
            snapshot.Billing,
            snapshot.Plan,
            snapshot.Usage,
            ApiRequests: null,
            WebhookDeliveries: null,
            AiCredits: null));
    }

    private static IResult GetOrganizationMetricDefinitions(ITenantContext tenantContext)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }

        return Results.Ok(new { items = OrganizationMetricDefinitions() });
    }

    private static async Task<IResult> TrackOrganizationAnalyticsEvent(
        TrackAnalyticsEventRequest request,
        AtlasDbContext dbContext,
        ITenantContext tenantContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryRequireOrganizationAnalytics(tenantContext, out var problem))
        {
            return problem!;
        }

        if (string.IsNullOrWhiteSpace(request.EventName))
        {
            return EndpointHelpers.Problem("validation_failed", "eventName is required.", StatusCodes.Status422UnprocessableEntity);
        }

        var eventName = request.EventName.Trim();
        if (eventName.Length > 160)
        {
            return EndpointHelpers.Problem("validation_failed", "eventName must be 160 characters or fewer.", StatusCodes.Status422UnprocessableEntity);
        }

        var analyticsEvent = new AnalyticsEvent
        {
            OrganizationId = tenantContext.OrganizationId,
            ChecklistId = request.ChecklistId,
            TemplateId = request.TemplateId,
            RecipientId = request.RecipientId,
            UserId = tenantContext.UserId,
            EventName = eventName,
            SessionId = string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId.Trim(),
            PropertiesJson = EndpointHelpers.JsonOrDefault(request.Properties),
            OccurredAt = request.OccurredAt ?? clock.UtcNow,
            ReceivedAt = clock.UtcNow
        };

        dbContext.AnalyticsEvents.Add(analyticsEvent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"/v1/analytics/events/{analyticsEvent.Id}", new TrackAnalyticsEventResponse(analyticsEvent.Id, true));
    }

    private static async Task<IResult> GetPlatformOverview(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var stats = await BuildPlatformStatsAsync(dbContext, entitlements, period.From, period.To, clock.UtcNow, cancellationToken);
        return Results.Ok(new PlatformAnalyticsOverviewResponse(
            new AnalyticsPeriod(period.From, period.To),
            stats.Business,
            stats.Product,
            stats.Retention,
            stats.Operations));
    }

    private static async Task<IResult> GetPlatformGrowth(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? interval,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem, defaultDays: 180))
        {
            return periodProblem!;
        }

        var normalizedInterval = NormalizeInterval(interval);
        var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var usage = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.EventType == "action_sent" && item.OccurredAt >= period.From && item.OccurredAt <= period.To)
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Status == SubmissionStatus.Accepted && item.SubmittedAt >= period.From && item.SubmittedAt <= period.To)
            .ToListAsync(cancellationToken);

        var periods = PeriodKeys(period.From, period.To, normalizedInterval);
        var rows = periods
            .Select(key =>
            {
                var window = PeriodWindow(key, normalizedInterval);
                var activeOrganizations = usage
                    .Where(item => item.OccurredAt >= window.From && item.OccurredAt < window.To)
                    .Select(item => item.OrganizationId)
                    .Distinct()
                    .Count();
                return new PlatformGrowthSeriesItem(
                    key,
                    organizations.Count(item => item.CreatedAt >= window.From && item.CreatedAt < window.To),
                    usage.Where(item => item.OccurredAt >= window.From && item.OccurredAt < window.To)
                        .Select(item => item.OrganizationId)
                        .Distinct()
                        .Count(),
                    null,
                    activeOrganizations,
                    submissions.Count(item => item.SubmittedAt >= window.From && item.SubmittedAt < window.To));
            })
            .ToList();

        return Results.Ok(new PlatformGrowthAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            normalizedInterval,
            rows));
    }

    private static async Task<IResult> GetPlatformActivation(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var organizationIds = organizations.Select(item => item.Id).ToHashSet();
        var templates = await dbContext.Templates.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OrganizationId.HasValue && organizationIds.Contains(item.OrganizationId.Value))
            .ToListAsync(cancellationToken);
        var actions = await dbContext.Actions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Recipients)
            .Where(item => organizationIds.Contains(item.OrganizationId))
            .ToListAsync(cancellationToken);
        var usage = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => organizationIds.Contains(item.OrganizationId) && item.EventType == "action_sent")
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Action)
            .Where(item => item.Action != null && organizationIds.Contains(item.Action.OrganizationId))
            .ToListAsync(cancellationToken);
        var revenueOrganizationIds = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OrganizationId.HasValue && organizationIds.Contains(item.OrganizationId.Value))
            .Select(item => item.OrganizationId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var signedUp = organizations.Count;
        var organizationConfigured = organizations.Count(item => !string.IsNullOrWhiteSpace(item.Name)
            && !string.IsNullOrWhiteSpace(item.Slug)
            && !string.IsNullOrWhiteSpace(item.Timezone));
        var templateSelectedOrCreated = actions.Where(item => item.TemplateId.HasValue).Select(item => item.OrganizationId)
            .Concat(templates.Select(item => item.OrganizationId!.Value))
            .Distinct()
            .Count();
        var firstChecklistCreated = actions.Select(item => item.OrganizationId).Distinct().Count();
        var firstChecklistSent = usage.Select(item => item.OrganizationId).Distinct().Count();
        var firstChecklistViewed = actions
            .Where(item => item.Recipients.Any(recipient => recipient.FirstViewedAt.HasValue))
            .Select(item => item.OrganizationId)
            .Distinct()
            .Count();
        var firstChecklistCompleted = submissions
            .Where(item => item.Status is SubmissionStatus.Submitted or SubmissionStatus.Accepted)
            .Select(item => item.Action!.OrganizationId)
            .Distinct()
            .Count();
        var secondChecklistSent = usage
            .GroupBy(item => item.OrganizationId)
            .Count(group => group.Sum(item => item.Quantity) >= 2);
        var paid = revenueOrganizationIds.Count;

        return Results.Ok(new PlatformActivationAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            new ActivationFunnel(
                signedUp,
                organizationConfigured,
                templateSelectedOrCreated,
                firstChecklistCreated,
                firstChecklistSent,
                firstChecklistViewed,
                firstChecklistCompleted,
                secondChecklistSent,
                paid),
            new ActivationRates(
                Rate(firstChecklistCreated, signedUp),
                Rate(firstChecklistSent, signedUp),
                Rate(firstChecklistCompleted, signedUp),
                Rate(firstChecklistSent, signedUp),
                Rate(secondChecklistSent, firstChecklistSent),
                Rate(paid, signedUp))));
    }

    private static async Task<IResult> GetPlatformEngagement(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var usage = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.EventType == "action_sent" && item.OccurredAt >= period.From && item.OccurredAt <= period.To)
            .ToListAsync(cancellationToken);
        var activeOrganizationIds = usage.Select(item => item.OrganizationId).Distinct().ToHashSet();
        var actions = await dbContext.Actions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Recipients)
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var templates = await dbContext.Templates.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var reminders = await dbContext.ReminderSchedules.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.ActionRecipient)
            .ThenInclude(item => item!.Action)
            .Where(item => item.SentAt.HasValue && item.SentAt.Value >= period.From && item.SentAt.Value <= period.To)
            .ToListAsync(cancellationToken);
        var apiKeys = await dbContext.ApiKeys.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var webhooks = await dbContext.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var reusedFiles = await dbContext.SubmissionFiles.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Submission)
            .ThenInclude(item => item!.Action)
            .Where(item => item.IsPreviouslySubmitted
                && item.CreatedAt >= period.From
                && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);

        var activeOrganizations = activeOrganizationIds.Count;
        var uniqueRecipients = actions
            .SelectMany(item => item.Recipients.Select(recipient => recipient.Email))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var autoReminderOrganizations = reminders
            .Where(item => item.Type is ReminderType.BeforeDue or ReminderType.Overdue && item.ActionRecipient != null)
            .Select(item => item.ActionRecipient!.Action!.OrganizationId)
            .Where(id => activeOrganizationIds.Contains(id))
            .Distinct()
            .Count();

        return Results.Ok(new PlatformEngagementAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            ActiveOrganizations: activeOrganizations,
            ChecklistsPerActiveOrganization: activeOrganizations == 0 ? null : Math.Round(usage.Sum(item => item.Quantity) / activeOrganizations, 2),
            TemplatesPerOrganization: organizations.Count == 0 ? null : Math.Round((decimal)templates.Count / organizations.Count, 2),
            UniqueRecipientsPerOrganization: activeOrganizations == 0 ? null : Math.Round((decimal)uniqueRecipients / activeOrganizations, 2),
            ActiveOrganizationUsers: await dbContext.OrganizationUsers.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.Status == MembershipStatus.Active)
                .Select(item => item.UserId)
                .Distinct()
                .CountAsync(cancellationToken),
            AutoReminderAdoptionRate: Rate(autoReminderOrganizations, activeOrganizations),
            ApiAdoptionRate: Rate(apiKeys.Select(item => item.OrganizationId).Distinct().Count(), organizations.Count),
            WebhookAdoptionRate: Rate(webhooks.Select(item => item.OrganizationId).Distinct().Count(), organizations.Count),
            CustomTemplateAdoptionRate: Rate(templates.Where(item => item.OrganizationId.HasValue).Select(item => item.OrganizationId!.Value).Distinct().Count(), organizations.Count),
            RenewalFeatureAdoptionRate: null,
            PreviousDocumentReuseAdoptionRate: Rate(reusedFiles.Select(item => item.Submission?.Action?.OrganizationId).Where(item => item.HasValue).Select(item => item!.Value).Distinct().Count(), activeOrganizations)));
    }

    private static async Task<IResult> GetPlatformRevenue(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var revenue = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OccurredAt >= period.From && item.OccurredAt <= period.To)
            .ToListAsync(cancellationToken);
        var recurring = await BuildRecurringRevenueSnapshotAsync(dbContext, entitlements, period.To, cancellationToken);
        var creditsAndRefunds = revenue
            .Where(item => item.Type is PlatformRevenueEventType.Credit or PlatformRevenueEventType.Refund)
            .Sum(item => Math.Abs(item.Amount));
        var closingMrr = recurring.Mrr;

        return Results.Ok(new PlatformRevenueAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            OpeningMrr: null,
            NewMrr: closingMrr,
            ExpansionMrr: revenue.Where(item => item.Type == PlatformRevenueEventType.Usage).Sum(item => item.Amount),
            ReactivationMrr: null,
            ContractionMrr: revenue.Where(item => item.Type == PlatformRevenueEventType.Adjustment && item.Amount < 0).Sum(item => Math.Abs(item.Amount)),
            ChurnedMrr: creditsAndRefunds,
            ClosingMrr: closingMrr,
            Arr: closingMrr * 12,
            Arpa: null,
            TrialToPaidConversionRate: null,
            RevenueByCurrency: revenue
                .GroupBy(item => item.Currency)
                .Select(group => new RevenueByCurrency(group.Key, group.Sum(item => item.Amount)))
                .OrderBy(item => item.Currency)
                .ToList()));
    }

    private static async Task<IResult> GetPlatformRetentionCohorts(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? cohortBy,
        string? activity,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem, defaultDays: 365))
        {
            return periodProblem!;
        }

        var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var usage = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.EventType == "action_sent" && item.OccurredAt >= period.From && item.OccurredAt <= period.To)
            .ToListAsync(cancellationToken);

        var cohorts = organizations
            .GroupBy(item => item.CreatedAt.ToUniversalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var ids = group.Select(item => item.Id).ToHashSet();
                var start = PeriodWindow(group.Key, "month").From;
                var retention = new Dictionary<string, decimal?>();
                for (var month = 0; month <= 12; month++)
                {
                    var windowStart = start.AddMonths(month);
                    var windowEnd = windowStart.AddMonths(1);
                    var retained = usage
                        .Where(item => ids.Contains(item.OrganizationId)
                            && item.OccurredAt >= windowStart
                            && item.OccurredAt < windowEnd)
                        .Select(item => item.OrganizationId)
                        .Distinct()
                        .Count();
                    retention[$"month{month}"] = Rate(retained, ids.Count);
                }

                return new RetentionCohortItem(group.Key, ids.Count, retention);
            })
            .ToList();

        return Results.Ok(new RetentionCohortAnalyticsResponse(
            CohortBy: string.IsNullOrWhiteSpace(cohortBy) ? "signup_month" : cohortBy,
            Activity: string.IsNullOrWhiteSpace(activity) ? "checklist_sent" : activity,
            new AnalyticsPeriod(period.From, period.To),
            cohorts));
    }

    private static async Task<IResult> GetPlatformUnitEconomics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var sent = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.EventType == "action_sent" && item.OccurredAt >= period.From && item.OccurredAt <= period.To)
            .SumAsync(item => (decimal?)item.Quantity, cancellationToken) ?? 0;
        var completed = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.Status == SubmissionStatus.Accepted
                && item.SubmittedAt >= period.From
                && item.SubmittedAt <= period.To,
                cancellationToken);
        var storageBytes = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.DeletedAt == null)
            .SumAsync(item => (long?)item.SizeBytes, cancellationToken) ?? 0;

        return Results.Ok(new UnitEconomicsAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            InfrastructureCost: null,
            CostPerChecklistSent: null,
            CostPerCompletedChecklist: null,
            CostPerThousandChecklists: null,
            StorageBytes: storageBytes,
            StorageCost: null,
            EmailCost: null,
            MalwareScanningCost: null,
            AiProcessingCost: null,
            GrossMarginPercent: null,
            Cac: null,
            PaybackPeriodMonths: null,
            Ltv: null,
            SupportCostPerOrganization: null,
            ChecklistsSent: sent,
            CompletedChecklists: completed));
    }

    private static async Task<IResult> GetPlatformRequirementAnalytics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? page,
        int? pageSize,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var requirements = await dbContext.Requirements.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Action)
            .Where(item => item.Action != null
                && item.Action.SentAt.HasValue
                && item.Action.SentAt.Value >= period.From
                && item.Action.SentAt.Value <= period.To)
            .ToListAsync(cancellationToken);
        var requirementIds = requirements.Select(item => item.Id).ToHashSet();
        var responses = await dbContext.SubmissionResponses.IgnoreQueryFilters().AsNoTracking()
            .Where(item => requirementIds.Contains(item.RequirementId))
            .ToListAsync(cancellationToken);
        var files = await dbContext.SubmissionFiles.IgnoreQueryFilters().AsNoTracking()
            .Where(item => requirementIds.Contains(item.RequirementId))
            .ToListAsync(cancellationToken);

        var rows = requirements
            .GroupBy(item => new { item.Key, item.Label, item.Type })
            .Select(group =>
            {
                var ids = group.Select(item => item.Id).ToHashSet();
                var completed = group.Key.Type == RequirementType.File
                    ? files.Count(item => ids.Contains(item.RequirementId))
                    : responses.Count(item => ids.Contains(item.RequirementId) && !string.IsNullOrWhiteSpace(item.ValueJson));
                return new RequirementAnalyticsItem(null, group.Key.Key, group.Key.Label, group.Key.Type, group.Count(), completed, Rate(completed, group.Count()), null, null, null, null, null, Math.Max(0, group.Count() - completed));
            })
            .OrderByDescending(item => item.PresentedCount)
            .ThenBy(item => item.Label)
            .ToList();

        var normalizedPage = EndpointHelpers.NormalizePage(page);
        var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize, fallback: 20);
        return Results.Ok(new PagedAnalyticsResponse<RequirementAnalyticsItem>(
            rows.Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize)).Take(normalizedPageSize).ToList(),
            new PaginationResponse(normalizedPage, normalizedPageSize, rows.Count)));
    }

    private static async Task<IResult> GetPlatformCompliance(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var now = clock.UtcNow;
        var documents = await dbContext.SubmissionFiles.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var activeActions = await dbContext.Actions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Requirements)
            .Where(item => item.Status == ChecklistActionStatus.Sent
                || item.Status == ChecklistActionStatus.InProgress
                || item.Status == ChecklistActionStatus.Submitted)
            .ToListAsync(cancellationToken);
        var activeActionIds = activeActions.Select(item => item.Id).ToHashSet();
        var submissions = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Files)
            .Where(item => activeActionIds.Contains(item.ActionId))
            .ToListAsync(cancellationToken);
        var files = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var fileRequirementIdsByAction = submissions
            .SelectMany(submission => submission.Files.Select(file => new { submission.ActionId, file.RequirementId }))
            .GroupBy(item => item.ActionId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.RequirementId).ToHashSet());
        var missingRequiredDocuments = activeActions.Sum(action =>
        {
            fileRequirementIdsByAction.TryGetValue(action.Id, out var submittedFileRequirementIds);
            submittedFileRequirementIds ??= [];
            return action.Requirements.Count(requirement =>
                requirement.IsRequired
                && requirement.Type == RequirementType.File
                && !submittedFileRequirementIds.Contains(requirement.Id));
        });

        return Results.Ok(new ComplianceAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value >= now && item.DocumentExpiresAt.Value <= now.AddDays(30)),
            documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value >= now && item.DocumentExpiresAt.Value <= now.AddDays(60)),
            documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value >= now && item.DocumentExpiresAt.Value <= now.AddDays(90)),
            documents.Count(item => item.DocumentExpiresAt.HasValue && item.DocumentExpiresAt.Value < now),
            MissingRequiredDocuments: missingRequiredDocuments,
            submissions.Count(item => item.Status == SubmissionStatus.Submitted),
            files.Count(item => item.ScanStatus == FileScanStatus.Pending),
            files.Count(item => item.ScanStatus == FileScanStatus.Rejected),
            files.Count(item => item.RetentionUntil.HasValue && item.RetentionUntil.Value >= now && item.RetentionUntil.Value <= now.AddDays(30)),
            documents.Count(item => item.IsPreviouslySubmitted)));
    }

    private static async Task<IResult> GetPlatformSegments(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var interests = await dbContext.PlatformOrganizationInterests.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var templates = await dbContext.Templates.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var actions = await dbContext.Actions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Template)
            .Where(item => item.SentAt.HasValue && item.SentAt.Value >= period.From && item.SentAt.Value <= period.To)
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Action)
            .ThenInclude(item => item!.Template)
            .Where(item => item.SubmittedAt >= period.From && item.SubmittedAt <= period.To)
            .ToListAsync(cancellationToken);

        var industries = interests
            .Where(item => !string.IsNullOrWhiteSpace(item.Source) || !string.IsNullOrWhiteSpace(item.Region))
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Source) ? "Unknown" : item.Source!)
            .Select(group => new SegmentAnalyticsItem(
                group.Key,
                group.Count(),
                PayingOrganizations: null,
                Mrr: null,
                CompletionRate: null,
                MedianCompletionMinutes: null,
                RetentionRate: null,
                AverageChecklistVolume: null,
                AverageContractValue: null,
                SupportBurden: null))
            .OrderByDescending(item => item.Organizations)
            .ToList();

        var templateRows = actions
            .GroupBy(item => item.Template?.Name ?? "Custom checklist")
            .Select(group =>
            {
                var sent = group.Count();
                var templateSubmissions = submissions.Where(item => item.Action?.Template?.Name == group.Key || (item.Action?.Template == null && group.Key == "Custom checklist")).ToList();
                return new TemplateIntelligenceItem(
                    group.Key,
                    sent,
                    templateSubmissions.Count,
                    Rate(templateSubmissions.Count, sent),
                    RepeatUsageRate: null,
                    ConversionToPaidRate: null,
                    IndustriesUsingTemplate: [],
                    RetentionCorrelation: null);
            })
            .OrderByDescending(item => item.Usage)
            .ToList();

        return Results.Ok(new SegmentAnalyticsResponse(new AnalyticsPeriod(period.From, period.To), industries, templateRows));
    }

    private static async Task<IResult> GetPlatformTimeSaved(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        const decimal minutesSavedPerCompletedChecklist = 20;
        var completed = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.Status == SubmissionStatus.Accepted && item.SubmittedAt >= period.From && item.SubmittedAt <= period.To, cancellationToken);
        var reused = await dbContext.SubmissionFiles.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.IsPreviouslySubmitted && item.CreatedAt >= period.From && item.CreatedAt <= period.To, cancellationToken);
        var estimatedMinutes = completed * minutesSavedPerCompletedChecklist + reused * 5;

        return Results.Ok(new TimeSavedAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            CompletedChecklists: completed,
            ReusedDocuments: reused,
            EstimatedMinutesSaved: estimatedMinutes,
            EstimatedHoursSaved: Math.Round(estimatedMinutes / 60, 2),
            Methodology: "Estimate: 20 minutes saved per accepted checklist plus 5 minutes per previously submitted document reuse. This is directional, not audited."));
    }

    private static async Task<IResult> GetPlatformPredictiveInsights(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var now = clock.UtcNow;
        var overdue = await dbContext.Actions.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.DueAt.HasValue
                && item.DueAt.Value < now
                && (item.Status == ChecklistActionStatus.Sent || item.Status == ChecklistActionStatus.InProgress),
                cancellationToken);
        var pendingScans = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.ScanStatus == FileScanStatus.Pending, cancellationToken);

        var insights = new List<PredictiveInsightItem>();
        if (overdue > 0)
        {
            insights.Add(new PredictiveInsightItem(
                "overdue_backlog",
                "Overdue checklist backlog",
                "medium",
                $"There are {overdue} sent or in-progress checklists past due.",
                "Review reminder performance and consider an overdue follow-up campaign.",
                Confidence: 0.75m));
        }
        if (pendingScans > 0)
        {
            insights.Add(new PredictiveInsightItem(
                "scan_backlog",
                "File scan backlog",
                "high",
                $"There are {pendingScans} files still pending scan.",
                "Check scanner worker health before recipients or reviewers are blocked.",
                Confidence: 0.8m));
        }

        return Results.Ok(new PredictiveInsightsResponse(new AnalyticsPeriod(period.From, period.To), insights));
    }

    private static async Task<IResult> GetPlatformBenchmarks(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var rows = await BuildBenchmarkRowsAsync(dbContext, period, cancellationToken);
        var activeRows = rows.Where(item => item.SentRecipients > 0).ToList();
        var metrics = BuildPlatformBenchmarkMetrics(activeRows);
        var sentValues = activeRows
            .Select(item => (decimal)item.SentRecipients)
            .OrderBy(item => item)
            .ToList();

        return Results.Ok(new BenchmarkAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            new BenchmarkItem("checklists_sent_per_org", "Checklists sent per active organization", Percentile(sentValues, 50), Percentile(sentValues, 75), Percentile(sentValues, 90), "count"),
            Metrics: metrics,
            Cohorts: BuildPlatformBenchmarkCohorts(activeRows),
            DelayDrivers: BuildBenchmarkDelayDrivers(activeRows),
            Notes: "Benchmarks are internal anonymized platform distribution benchmarks for the selected period. Customer-facing benchmarks use same-plan cohorts with privacy thresholds; industry cohorts should be enabled after structured organization vertical data is collected."));
    }

    private static async Task<IResult> GetPlatformOperations(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var notifications = await dbContext.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var webhooks = await dbContext.WebhookDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var files = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);

        return Results.Ok(new PlatformOperationsAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            ApiUptimeRate: null,
            ApiErrorRate: null,
            P50LatencyMs: null,
            P95LatencyMs: null,
            P99LatencyMs: null,
            JobBacklog: null,
            FailedJobs: null,
            EmailDeliverySuccessRate: Rate(notifications.Count(item => item.Status == NotificationDeliveryStatus.Delivered), notifications.Count),
            EmailBounceRate: Rate(notifications.Count(item => item.Status == NotificationDeliveryStatus.Bounced), notifications.Count),
            UploadFailureRate: null,
            FileScanSuccessRate: Rate(files.Count(item => item.ScanStatus == FileScanStatus.Clean), files.Count(item => item.ScanStatus != FileScanStatus.Pending)),
            FileScanFailureRate: Rate(files.Count(item => item.ScanStatus == FileScanStatus.Rejected), files.Count(item => item.ScanStatus != FileScanStatus.Pending)),
            WebhookDeliverySuccessRate: Rate(webhooks.Count(item => item.Status == WebhookDeliveryStatus.Succeeded), webhooks.Count),
            DatabaseHealthy: true,
            StorageBytes: await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.DeletedAt == null)
                .SumAsync(item => (long?)item.SizeBytes, cancellationToken) ?? 0,
            TurnstileFailureCount: null,
            RateLimitTriggerCount: null));
    }

    private static async Task<IResult> GetPlatformSystemHealth(
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var pendingScans = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.ScanStatus == FileScanStatus.Pending, cancellationToken);
        var failedNotifications = await dbContext.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.Status == NotificationDeliveryStatus.Failed, cancellationToken);
        var pendingWebhooks = await dbContext.WebhookDeliveries.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.Status == WebhookDeliveryStatus.Pending, cancellationToken);
        var storageBytes = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.DeletedAt == null)
            .SumAsync(item => (long?)item.SizeBytes, cancellationToken) ?? 0;

        var status = pendingScans > 100 || failedNotifications > 100 ? "degraded" : "ok";
        return Results.Ok(new SystemHealthAnalyticsResponse(
            AsOf: clock.UtcNow,
            Status: status,
            DatabaseHealthy: true,
            ApiHealthy: true,
            PendingFileScans: pendingScans,
            FailedNotificationDeliveries: failedNotifications,
            PendingWebhookDeliveries: pendingWebhooks,
            StorageBytes: storageBytes,
            OpenIncidents: null));
    }

    private static async Task<IResult> GetPlatformSecurity(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var audit = await dbContext.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var platformAudit = await dbContext.PlatformAuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var files = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);

        return Results.Ok(new PlatformSecurityAnalyticsResponse(
            new AnalyticsPeriod(period.From, period.To),
            FailedOrganizationLogins: null,
            SuspiciousRecipientLinkActivity: audit.Count(item => item.EventType.Contains("recipient", StringComparison.OrdinalIgnoreCase)
                && item.EventType.Contains("failed", StringComparison.OrdinalIgnoreCase)),
            InvalidTokenAttempts: null,
            RevokedLinkAttempts: null,
            RateLimitTriggers: null,
            MalwareDetections: files.Count(item => item.ScanStatus == FileScanStatus.Rejected),
            QuarantinedFiles: files.Count(item => item.ScanStatus == FileScanStatus.Rejected),
            CrossTenantAuthorizationDenials: null,
            ApiKeyFailures: null,
            WebhookSignatureFailures: null,
            AdministrativeRoleChanges: platformAudit.Count(item => item.EventType is "platform.staff_created" or "platform.staff_updated" or "platform.staff_disabled"),
            DataExports: audit.Count(item => item.EventType.Contains("export", StringComparison.OrdinalIgnoreCase)),
            DeletionRequests: audit.Count(item => item.EventType.Contains("deleted", StringComparison.OrdinalIgnoreCase))));
    }

    private static async Task<IResult> ListPlatformAnalyticsEvents(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? organizationId,
        string? eventName,
        int? page,
        int? pageSize,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var query = dbContext.AnalyticsEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OccurredAt >= period.From && item.OccurredAt <= period.To);
        if (organizationId.HasValue)
        {
            query = query.Where(item => item.OrganizationId == organizationId.Value);
        }
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            query = query.Where(item => item.EventName == eventName.Trim());
        }

        var normalizedPage = EndpointHelpers.NormalizePage(page);
        var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize, fallback: 50, max: 250);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.OccurredAt)
            .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
            .Take(normalizedPageSize)
            .Select(item => new AnalyticsEventResponse(
                item.Id,
                item.EventName,
                item.OrganizationId,
                item.ChecklistId,
                item.TemplateId,
                item.RecipientId,
                item.UserId,
                item.SessionId,
                item.PropertiesJson,
                item.OccurredAt,
                item.ReceivedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedAnalyticsResponse<AnalyticsEventResponse>(
            items,
            new PaginationResponse(normalizedPage, normalizedPageSize, total)));
    }

    private static async Task<IResult> GetInvestorSummary(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem))
        {
            return periodProblem!;
        }

        var stats = await BuildPlatformStatsAsync(dbContext, entitlements, period.From, period.To, clock.UtcNow, cancellationToken);
        return Results.Ok(new InvestorSummaryResponse(
            AsOf: period.To,
            Business: new InvestorBusinessSummary(
                stats.Business.Mrr,
                stats.Business.Arr,
                MrrGrowthPercent: null,
                stats.Business.PayingOrganizations,
                stats.Business.PayingOrganizations == 0 ? null : Math.Round(stats.Business.Mrr / stats.Business.PayingOrganizations, 2),
                GrossMarginPercent: null,
                MonthlyBurn: null,
                RunwayMonths: null),
            Product: new InvestorProductSummary(
                stats.Business.MonthlyActiveOrganizations,
                stats.Product.ChecklistsCompleted,
                stats.Product.CompletedPerActiveOrganization,
                stats.Product.CompletionRate,
                stats.Product.MedianCompletionMinutes,
                stats.Product.SecondChecklistRate),
            Retention: new InvestorRetentionSummary(
                stats.Retention.LogoRetention90Day,
                stats.Retention.GrossRevenueRetention,
                stats.Retention.NetRevenueRetention,
                MonthlyLogoChurn: null),
            Growth: new InvestorGrowthSummary(
                NewActivatedOrganizations: stats.Business.NewOrganizations,
                TrialToPaidRate: null,
                QualifiedPipelineValue: null,
                AverageSalesCycleDays: null,
                AverageContractValue: null)));
    }

    private static async Task<IResult> GetInvestorRevenueTrend(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? interval,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem, defaultDays: 365))
        {
            return periodProblem!;
        }

        var normalizedInterval = NormalizeInterval(interval);
        var revenue = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.OccurredAt >= period.From && item.OccurredAt <= period.To)
            .ToListAsync(cancellationToken);
        var series = PeriodKeys(period.From, period.To, normalizedInterval)
            .Select(key =>
            {
                var window = PeriodWindow(key, normalizedInterval);
                var mrr = revenue
                    .Where(item => item.Type == PlatformRevenueEventType.Subscription && item.OccurredAt >= window.From && item.OccurredAt < window.To)
                    .Sum(item => item.Amount);
                return new InvestorRevenueTrendItem(key, mrr, mrr * 12);
            })
            .ToList();

        return Results.Ok(new InvestorTrendResponse<InvestorRevenueTrendItem>(new AnalyticsPeriod(period.From, period.To), normalizedInterval, series));
    }

    private static async Task<IResult> GetInvestorOrganizationGrowth(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? interval,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        return await GetPlatformGrowth(from, to, interval, dbContext, httpContext, clock, cancellationToken);
    }

    private static async Task<IResult> GetInvestorProductUsage(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? interval,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (!TryResolvePeriod(from, to, clock.UtcNow, out var period, out var periodProblem, defaultDays: 365))
        {
            return periodProblem!;
        }

        var normalizedInterval = NormalizeInterval(interval);
        var usage = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.EventType == "action_sent" && item.OccurredAt >= period.From && item.OccurredAt <= period.To)
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.SubmittedAt >= period.From && item.SubmittedAt <= period.To)
            .ToListAsync(cancellationToken);
        var series = PeriodKeys(period.From, period.To, normalizedInterval)
            .Select(key =>
            {
                var window = PeriodWindow(key, normalizedInterval);
                var sent = usage.Where(item => item.OccurredAt >= window.From && item.OccurredAt < window.To).Sum(item => item.Quantity);
                var completed = submissions.Count(item => item.Status == SubmissionStatus.Accepted && item.SubmittedAt >= window.From && item.SubmittedAt < window.To);
                return new InvestorProductUsageTrendItem(key, sent, completed, Rate(completed, sent));
            })
            .ToList();

        return Results.Ok(new InvestorTrendResponse<InvestorProductUsageTrendItem>(new AnalyticsPeriod(period.From, period.To), normalizedInterval, series));
    }

    private static async Task<IResult> GetInvestorRetention(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        return await GetPlatformRetentionCohorts(from, to, "signup_month", "checklist_sent", dbContext, httpContext, clock, cancellationToken);
    }

    private static async Task<IResult> GetInvestorUnitEconomics(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        return await GetPlatformUnitEconomics(from, to, dbContext, httpContext, clock, cancellationToken);
    }

    private static async Task<IResult> GetInvestorVerticals(
        DateTimeOffset? from,
        DateTimeOffset? to,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        return await GetPlatformSegments(from, to, dbContext, httpContext, clock, cancellationToken);
    }

    private static async Task<IResult> GetInvestorMilestones(
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var orgs = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking().CountAsync(cancellationToken);
        var sent = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.EventType == "action_sent")
            .SumAsync(item => (decimal?)item.Quantity, cancellationToken) ?? 0;
        var completed = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(item => item.Status == SubmissionStatus.Accepted, cancellationToken);

        return Results.Ok(new InvestorMilestonesResponse(
            clock.UtcNow,
            [
                new InvestorMilestoneItem("organizations_created", "Organizations created", orgs, null, orgs > 0 ? "achieved" : "pending"),
                new InvestorMilestoneItem("checklists_sent", "Checklists sent", sent, null, sent > 0 ? "achieved" : "pending"),
                new InvestorMilestoneItem("accepted_submissions", "Accepted submissions", completed, null, completed > 0 ? "achieved" : "pending")
            ]));
    }

    private static async Task<IResult> CreateInvestorReport(
        CreateInvestorReportRequest request,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        IEntitlementService entitlements,
        IAtlasClock clock,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }
        if (string.IsNullOrWhiteSpace(request.ReportType) || string.IsNullOrWhiteSpace(request.Period))
        {
            return EndpointHelpers.Problem("validation_failed", "reportType and period are required.", StatusCodes.Status422UnprocessableEntity);
        }

        var period = ResolveReportPeriod(request.Period, clock.UtcNow);
        var stats = await BuildPlatformStatsAsync(dbContext, entitlements, period.From, period.To, clock.UtcNow, cancellationToken);
        var payload = new
        {
            request.ReportType,
            request.Period,
            request.IncludeSections,
            GeneratedAt = clock.UtcNow,
            Summary = new PlatformAnalyticsOverviewResponse(new AnalyticsPeriod(period.From, period.To), stats.Business, stats.Product, stats.Retention, stats.Operations)
        };
        var report = new InvestorReport
        {
            ReportType = request.ReportType.Trim(),
            Period = request.Period.Trim(),
            IncludeSections = request.IncludeSections?.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct().ToArray() ?? [],
            Status = InvestorReportStatus.Completed,
            RequestedByStaffId = access.Staff!.Id,
            ResultJson = JsonSerializer.Serialize(payload, EndpointHelpers.JsonOptions),
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            CompletedAt = clock.UtcNow
        };
        dbContext.InvestorReports.Add(report);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/v1/platform/investor/reports/{report.Id}", ToInvestorReportResponse(report));
    }

    private static async Task<IResult> ListInvestorReports(
        int? page,
        int? pageSize,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var normalizedPage = EndpointHelpers.NormalizePage(page);
        var normalizedPageSize = EndpointHelpers.NormalizePageSize(pageSize);
        var query = dbContext.InvestorReports.AsNoTracking().OrderByDescending(item => item.CreatedAt);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip(EndpointHelpers.PageSkip(normalizedPage, normalizedPageSize))
            .Take(normalizedPageSize)
            .Select(item => ToInvestorReportResponse(item))
            .ToListAsync(cancellationToken);
        return Results.Ok(new PagedAnalyticsResponse<InvestorReportResponse>(items, new PaginationResponse(normalizedPage, normalizedPageSize, total)));
    }

    private static async Task<IResult> GetInvestorReport(
        Guid id,
        AtlasDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, InvestorRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var report = await dbContext.InvestorReports.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return report is null
            ? EndpointHelpers.Problem("not_found", "Investor report was not found.", StatusCodes.Status404NotFound)
            : Results.Ok(ToInvestorReportResponse(report));
    }

    private static async Task<IResult> GetPlatformMetricDefinitions(
        AtlasDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var access = await RequirePlatformStaffAsync(dbContext, httpContext, cancellationToken, PlatformAnalyticsRoles);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        return Results.Ok(new { items = PlatformMetricDefinitions() });
    }

    private static async Task<IReadOnlyList<OrganizationBenchmarkRow>> BuildBenchmarkRowsAsync(
        AtlasDbContext dbContext,
        AnalyticsPeriod period,
        CancellationToken cancellationToken)
    {
        var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.DeletedAt == null && item.Status == OrganizationStatus.Active)
            .Select(item => new { item.Id })
            .ToListAsync(cancellationToken);

        var recipients = await dbContext.ActionRecipients.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Action)
            .Where(item => item.Action != null
                && item.Action.DeletedAt == null
                && item.Action.SentAt.HasValue
                && item.Action.SentAt.Value >= period.From
                && item.Action.SentAt.Value <= period.To)
            .ToListAsync(cancellationToken);
        var sentActionIds = recipients.Select(item => item.ActionId).ToHashSet();
        var submissions = sentActionIds.Count == 0
            ? new List<Submission>()
            : await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
                .Include(item => item.Action)
                .Where(item => sentActionIds.Contains(item.ActionId))
                .ToListAsync(cancellationToken);
        var submissionIds = submissions.Select(item => item.Id).ToHashSet();
        var submissionFiles = submissionIds.Count == 0
            ? new List<SubmissionFile>()
            : await dbContext.SubmissionFiles.IgnoreQueryFilters().AsNoTracking()
                .Where(item => submissionIds.Contains(item.SubmissionId))
                .ToListAsync(cancellationToken);
        var fileAssets = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= period.From && item.CreatedAt <= period.To)
            .ToListAsync(cancellationToken);
        var reminders = await dbContext.ReminderSchedules.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.ActionRecipient)
            .ThenInclude(item => item!.Action)
            .Where(item => item.SentAt.HasValue
                && item.SentAt.Value >= period.From
                && item.SentAt.Value <= period.To)
            .ToListAsync(cancellationToken);
        var planCodes = await BuildBenchmarkPlanCodesAsync(dbContext, cancellationToken);

        var recipientsByOrg = recipients
            .Where(item => item.Action is not null)
            .GroupBy(item => item.Action!.OrganizationId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var submissionsByOrg = submissions
            .Where(item => item.Action is not null)
            .GroupBy(item => item.Action!.OrganizationId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var submissionOrgById = submissions
            .Where(item => item.Action is not null)
            .ToDictionary(item => item.Id, item => item.Action!.OrganizationId);
        var submissionFilesByOrg = submissionFiles
            .Where(item => submissionOrgById.ContainsKey(item.SubmissionId))
            .GroupBy(item => submissionOrgById[item.SubmissionId])
            .ToDictionary(group => group.Key, group => group.ToList());
        var fileAssetsByOrg = fileAssets
            .GroupBy(item => item.OrganizationId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var remindersByOrg = reminders
            .Where(item => item.ActionRecipient?.Action is not null)
            .GroupBy(item => item.ActionRecipient!.Action!.OrganizationId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var rows = new List<OrganizationBenchmarkRow>();
        foreach (var organization in organizations)
        {
            var orgRecipients = recipientsByOrg.TryGetValue(organization.Id, out var foundRecipients)
                ? foundRecipients
                : new List<ActionRecipient>();
            var orgSubmissions = submissionsByOrg.TryGetValue(organization.Id, out var foundSubmissions)
                ? foundSubmissions
                : new List<Submission>();
            var orgSubmissionFiles = submissionFilesByOrg.TryGetValue(organization.Id, out var foundSubmissionFiles)
                ? foundSubmissionFiles
                : new List<SubmissionFile>();
            var orgFileAssets = fileAssetsByOrg.TryGetValue(organization.Id, out var foundFileAssets)
                ? foundFileAssets
                : new List<FileAsset>();
            var orgReminders = remindersByOrg.TryGetValue(organization.Id, out var foundReminders)
                ? foundReminders
                : new List<ReminderSchedule>();

            var sent = orgRecipients.Count;
            var viewed = orgRecipients.Count(item => item.FirstViewedAt.HasValue);
            var started = orgRecipients.Count(item => item.StartedAt.HasValue || item.Status is ActionRecipientStatus.Started or ActionRecipientStatus.Submitted);
            var submitted = orgRecipients.Count(item => item.SubmittedAt.HasValue || item.Status == ActionRecipientStatus.Submitted);
            var accepted = orgSubmissions.Count(item => item.Status == SubmissionStatus.Accepted);
            var completionDurations = orgRecipients
                .Where(item => item.SubmittedAt.HasValue
                    && item.Action?.SentAt is not null
                    && item.SubmittedAt.Value >= item.Action.SentAt.Value)
                .Select(item => item.SubmittedAt!.Value - item.Action!.SentAt!.Value);
            var firstOpenDurations = orgRecipients
                .Where(item => item.FirstViewedAt.HasValue
                    && item.Action?.SentAt is not null
                    && item.FirstViewedAt.Value >= item.Action.SentAt.Value)
                .Select(item => item.FirstViewedAt!.Value - item.Action!.SentAt!.Value);
            var reminderRecipientCount = orgReminders
                .Where(item => item.SentAt.HasValue)
                .Select(item => item.ActionRecipientId)
                .Distinct()
                .Count();

            rows.Add(new OrganizationBenchmarkRow(
                organization.Id,
                planCodes.PlansByOrganization.TryGetValue(organization.Id, out var planCode) ? planCode : planCodes.DefaultPlanCode,
                sent,
                viewed,
                started,
                submitted,
                accepted,
                reminderRecipientCount,
                orgSubmissionFiles.Count,
                orgSubmissionFiles.Count(item => item.IsPreviouslySubmitted),
                orgFileAssets.Count(item => item.ScanStatus == FileScanStatus.Pending),
                Rate(submitted, sent),
                Rate(viewed, sent),
                MedianMinutes(completionDurations),
                MedianMinutes(firstOpenDurations),
                Rate(orgSubmissionFiles.Count(item => item.IsPreviouslySubmitted), orgSubmissionFiles.Count),
                Rate(reminderRecipientCount, sent)));
        }

        return rows;
    }

    private static async Task<BenchmarkPlanCodes> BuildBenchmarkPlanCodesAsync(
        AtlasDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var billingSettings = await dbContext.AdminSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Category == "billing"
                && (item.Key == "defaultPlanCode" || item.Key == "plan" || item.Key == "planCode"))
            .ToListAsync(cancellationToken);
        var defaultPlanCode = billingSettings
            .Where(item => item.OrganizationId == null && item.Key == "defaultPlanCode")
            .Select(item => ReadBenchmarkPlanCode(item.ValueJson, "free"))
            .FirstOrDefault() ?? "free";
        var plansByOrg = billingSettings
            .Where(item => item.OrganizationId.HasValue && (item.Key == "plan" || item.Key == "planCode"))
            .GroupBy(item => item.OrganizationId!.Value)
            .ToDictionary(
                group => group.Key,
                group => ReadBenchmarkPlanCode(
                    group.OrderByDescending(item => item.Key == "plan").First().ValueJson,
                    defaultPlanCode));

        return new BenchmarkPlanCodes(defaultPlanCode, plansByOrg);
    }

    private static IReadOnlyList<OrganizationBenchmarkMetric> BuildOrganizationBenchmarkMetrics(
        OrganizationBenchmarkRow current,
        IReadOnlyList<OrganizationBenchmarkRow> cohortRows,
        bool benchmarksAvailable)
    {
        return
        [
            BuildOrganizationBenchmarkMetric("recipient_completion_rate", "Recipient completion rate", "percent", current.CompletionRate, cohortRows, item => item.CompletionRate, benchmarksAvailable, higherIsBetter: true),
            BuildOrganizationBenchmarkMetric("median_completion_minutes", "Median completion time", "minutes", current.MedianCompletionMinutes, cohortRows, item => item.MedianCompletionMinutes, benchmarksAvailable, higherIsBetter: false),
            BuildOrganizationBenchmarkMetric("median_first_open_minutes", "Median time to first open", "minutes", current.MedianFirstOpenMinutes, cohortRows, item => item.MedianFirstOpenMinutes, benchmarksAvailable, higherIsBetter: false),
            BuildOrganizationBenchmarkMetric("document_reuse_rate", "Previously submitted document reuse", "percent", current.DocumentReuseRate, cohortRows, item => item.DocumentReuseRate, benchmarksAvailable, higherIsBetter: true),
            BuildOrganizationBenchmarkMetric("reminder_coverage_rate", "Reminder coverage", "percent", current.ReminderCoverageRate, cohortRows, item => item.ReminderCoverageRate, benchmarksAvailable, higherIsBetter: true),
            BuildOrganizationBenchmarkMetric("checklists_sent", "Checklists sent", "count", current.SentRecipients, cohortRows, item => item.SentRecipients, benchmarksAvailable, higherIsBetter: true)
        ];
    }

    private static OrganizationBenchmarkMetric BuildOrganizationBenchmarkMetric(
        string key,
        string label,
        string unit,
        decimal? organizationValue,
        IReadOnlyList<OrganizationBenchmarkRow> cohortRows,
        Func<OrganizationBenchmarkRow, decimal?> selector,
        bool benchmarksAvailable,
        bool higherIsBetter)
    {
        var values = benchmarksAvailable
            ? cohortRows.Select(selector)
                .Where(item => item.HasValue)
                .Select(item => item.GetValueOrDefault())
                .OrderBy(item => item)
                .ToList()
            : new List<decimal>();
        var metricAvailable = values.Count >= MinimumBenchmarkCohortSize;
        var median = metricAvailable ? Percentile(values, 50) : null;
        var delta = organizationValue.HasValue && median.HasValue
            ? Math.Round(organizationValue.Value - median.Value, 2)
            : (decimal?)null;

        return new OrganizationBenchmarkMetric(
            key,
            label,
            unit,
            organizationValue,
            median,
            metricAvailable ? Percentile(values, 75) : null,
            metricAvailable ? Percentile(values, 90) : null,
            delta,
            ResolveBenchmarkDirection(delta, higherIsBetter),
            higherIsBetter,
            Suppressed: !metricAvailable);
    }

    private static IReadOnlyList<PlatformBenchmarkMetric> BuildPlatformBenchmarkMetrics(
        IReadOnlyList<OrganizationBenchmarkRow> rows)
    {
        return
        [
            BuildPlatformBenchmarkMetric("checklists_sent_per_active_org", "Checklists sent per active organization", "count", rows, item => item.SentRecipients, higherIsBetter: true),
            BuildPlatformBenchmarkMetric("recipient_completion_rate", "Recipient completion rate", "percent", rows, item => item.CompletionRate, higherIsBetter: true),
            BuildPlatformBenchmarkMetric("median_completion_minutes", "Median completion time", "minutes", rows, item => item.MedianCompletionMinutes, higherIsBetter: false),
            BuildPlatformBenchmarkMetric("median_first_open_minutes", "Median time to first open", "minutes", rows, item => item.MedianFirstOpenMinutes, higherIsBetter: false),
            BuildPlatformBenchmarkMetric("document_reuse_rate", "Previously submitted document reuse", "percent", rows, item => item.DocumentReuseRate, higherIsBetter: true),
            BuildPlatformBenchmarkMetric("reminder_coverage_rate", "Reminder coverage", "percent", rows, item => item.ReminderCoverageRate, higherIsBetter: true)
        ];
    }

    private static PlatformBenchmarkMetric BuildPlatformBenchmarkMetric(
        string key,
        string label,
        string unit,
        IReadOnlyList<OrganizationBenchmarkRow> rows,
        Func<OrganizationBenchmarkRow, decimal?> selector,
        bool higherIsBetter)
    {
        var values = rows.Select(selector)
            .Where(item => item.HasValue)
            .Select(item => item.GetValueOrDefault())
            .OrderBy(item => item)
            .ToList();

        return new PlatformBenchmarkMetric(
            key,
            label,
            unit,
            OrganizationsWithValue: values.Count,
            P50: Percentile(values, 50),
            P75: Percentile(values, 75),
            P90: Percentile(values, 90),
            higherIsBetter);
    }

    private static IReadOnlyList<PlatformBenchmarkCohort> BuildPlatformBenchmarkCohorts(
        IReadOnlyList<OrganizationBenchmarkRow> rows)
    {
        return rows
            .GroupBy(item => item.PlanCode)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var cohortRows = group.ToList();
                return new PlatformBenchmarkCohort(
                    CohortType: "plan",
                    CohortKey: group.Key,
                    CohortLabel: $"{group.Key} workspaces",
                    SampleSize: cohortRows.Count,
                    Metrics: BuildPlatformBenchmarkMetrics(cohortRows));
            })
            .ToList();
    }

    private static IReadOnlyList<BenchmarkDelayDriver> BuildBenchmarkDelayDrivers(OrganizationBenchmarkRow row)
    {
        return BuildBenchmarkDelayDrivers(
            NotOpened: Math.Max(0, row.SentRecipients - row.ViewedRecipients),
            StartedNotSubmitted: Math.Max(0, row.StartedRecipients - row.SubmittedRecipients),
            PendingScans: row.PendingScans,
            NoReminderSent: Math.Max(0, row.SentRecipients - row.ReminderedRecipients));
    }

    private static IReadOnlyList<BenchmarkDelayDriver> BuildBenchmarkDelayDrivers(
        IReadOnlyList<OrganizationBenchmarkRow> rows)
    {
        return BuildBenchmarkDelayDrivers(
            NotOpened: rows.Sum(item => Math.Max(0, item.SentRecipients - item.ViewedRecipients)),
            StartedNotSubmitted: rows.Sum(item => Math.Max(0, item.StartedRecipients - item.SubmittedRecipients)),
            PendingScans: rows.Sum(item => item.PendingScans),
            NoReminderSent: rows.Sum(item => Math.Max(0, item.SentRecipients - item.ReminderedRecipients)));
    }

    private static IReadOnlyList<BenchmarkDelayDriver> BuildBenchmarkDelayDrivers(
        int NotOpened,
        int StartedNotSubmitted,
        int PendingScans,
        int NoReminderSent)
    {
        var items = new List<BenchmarkDelayDriver>
        {
            new("not_opened", "Recipients have not opened", NotOpened, "count", "Use a short reminder or verify the recipient email address."),
            new("started_not_submitted", "Recipients started but have not submitted", StartedNotSubmitted, "count", "Review confusing requirements and consider simplifying the checklist."),
            new("files_pending_scan", "Files pending scan", PendingScans, "count", "Check scanner health if this remains above zero for more than a few minutes."),
            new("no_reminder_sent", "Requests without a sent reminder", NoReminderSent, "count", "Enable automatic reminders for requests that are still waiting.")
        };

        return items
            .Where(item => item.Value > 0)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key)
            .Take(5)
            .ToList();
    }

    private static IReadOnlyList<BenchmarkInsight> BuildOrganizationBenchmarkInsights(
        OrganizationBenchmarkRow current,
        IReadOnlyList<OrganizationBenchmarkMetric> metrics,
        bool benchmarksAvailable)
    {
        var insights = new List<BenchmarkInsight>();
        if (current.SentRecipients == 0)
        {
            insights.Add(new BenchmarkInsight(
                "send_first_request",
                "No sent checklists in this period",
                "info",
                "Benchmarks become more useful after this workspace sends real requests.",
                "Send a checklist or change the date range to a period with activity."));
            return insights;
        }

        if (!benchmarksAvailable)
        {
            insights.Add(new BenchmarkInsight(
                "cohort_building",
                "Benchmark cohort is still building",
                "info",
                $"Reqara needs at least {MinimumBenchmarkCohortSize} comparable workspaces before showing customer-facing benchmark values.",
                "Keep using operational metrics here; benchmark comparison will unlock automatically."));
        }

        var completion = metrics.FirstOrDefault(item => item.Key == "recipient_completion_rate");
        if (completion?.DeltaFromMedian is < -5)
        {
            insights.Add(new BenchmarkInsight(
                "completion_below_cohort",
                "Completion is below similar workspaces",
                "warning",
                $"This workspace is {Math.Abs(completion.DeltaFromMedian.Value):0.##} percentage points below the same-plan median.",
                "Look at the delay drivers and simplify the highest-friction requirements first."));
        }

        var firstOpen = metrics.FirstOrDefault(item => item.Key == "median_first_open_minutes");
        if (firstOpen?.DeltaFromMedian is > 120)
        {
            insights.Add(new BenchmarkInsight(
                "slow_first_open",
                "Recipients are slower to open",
                "warning",
                "The first-open time is materially slower than the same-plan median.",
                "Review invite subject lines, sender identity, and reminder timing."));
        }

        if (current.SubmissionFiles > 0 && current.DocumentReuseRate.GetValueOrDefault() < 10)
        {
            insights.Add(new BenchmarkInsight(
                "document_reuse_opportunity",
                "Previously submitted documents can reduce repeat work",
                "opportunity",
                "File reuse is low for this period.",
                "Enable previously submitted documents on stable requirements such as IDs, licences, certificates, and insurance."));
        }

        if (current.ReminderCoverageRate.GetValueOrDefault() < 50)
        {
            insights.Add(new BenchmarkInsight(
                "reminder_coverage_opportunity",
                "Reminder coverage is low",
                "opportunity",
                "Many sent requests have not had a reminder in this period.",
                "Use automatic before-due and overdue reminders for waiting recipients."));
        }

        return insights
            .Take(4)
            .ToList();
    }

    private static string ResolveBenchmarkDirection(decimal? delta, bool higherIsBetter)
    {
        if (!delta.HasValue)
        {
            return "not_available";
        }

        if (Math.Abs(delta.Value) < 0.01m)
        {
            return "at_benchmark";
        }

        var better = higherIsBetter ? delta.Value > 0 : delta.Value < 0;
        return better ? "above_benchmark" : "below_benchmark";
    }

    private static string ReadBenchmarkPlanCode(string? valueJson, string fallback)
    {
        if (EndpointHelpers.ReadStringSetting(valueJson) is { } configuredPlan)
        {
            return NormalizeBenchmarkPlanCode(configuredPlan, fallback);
        }

        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return NormalizeBenchmarkPlanCode(fallback, "free");
        }

        try
        {
            var state = JsonSerializer.Deserialize<OrganizationBillingState>(valueJson, EndpointHelpers.JsonOptions);
            if (state is not null)
            {
                return NormalizeBenchmarkPlanCode(state.PlanCode, fallback);
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            using var document = JsonDocument.Parse(valueJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "planCode", "PlanCode", "code" })
                {
                    if (document.RootElement.TryGetProperty(propertyName, out var value)
                        && value.ValueKind == JsonValueKind.String)
                    {
                        return NormalizeBenchmarkPlanCode(value.GetString(), fallback);
                    }
                }
            }
        }
        catch (JsonException)
        {
        }

        return NormalizeBenchmarkPlanCode(fallback, "free");
    }

    private static string NormalizeBenchmarkPlanCode(string? planCode, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(planCode) ? fallback : planCode;
        return string.IsNullOrWhiteSpace(value)
            ? "free"
            : value.Trim().ToLowerInvariant();
    }

    private static bool TryRequireOrganizationAnalytics(ITenantContext tenantContext, out IResult? problem)
    {
        if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out problem))
        {
            return false;
        }
        if (EndpointHelpers.RequireProductionApiOrDashboard(tenantContext) is { } sandboxProblem)
        {
            problem = sandboxProblem;
            return false;
        }

        if (!EndpointHelpers.HasScope(tenantContext, "dashboard:read"))
        {
            problem = EndpointHelpers.Problem("forbidden", "Organization analytics access is required.", StatusCodes.Status403Forbidden);
            return false;
        }

        problem = null;
        return true;
    }

    private static async Task<PlatformAccess> RequirePlatformStaffAsync(
        AtlasDbContext dbContext,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        params PlatformStaffRole[] roles)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true
            || httpContext.User.FindFirstValue(PlatformEndpoints.ActorKindClaim) != "platform"
            || !Guid.TryParse(httpContext.User.FindFirstValue(PlatformEndpoints.PlatformStaffIdClaim), out var staffId))
        {
            return new PlatformAccess(null, EndpointHelpers.Problem("platform_auth_required", "Platform staff authentication is required.", StatusCodes.Status401Unauthorized));
        }

        var staff = await dbContext.PlatformStaff.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == staffId, cancellationToken);
        if (staff is null || staff.Status != PlatformStaffStatus.Active)
        {
            return new PlatformAccess(null, EndpointHelpers.Problem("platform_auth_required", "Platform staff authentication is required.", StatusCodes.Status401Unauthorized));
        }

        if (roles.Length > 0 && !roles.Contains(staff.Role))
        {
            return new PlatformAccess(null, EndpointHelpers.Problem("forbidden", "Platform role is not allowed for this analytics view.", StatusCodes.Status403Forbidden));
        }

        return new PlatformAccess(staff, null);
    }

    private static async Task<PlatformStats> BuildPlatformStatsAsync(
        AtlasDbContext dbContext,
        IEntitlementService entitlements,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking().ToListAsync(cancellationToken);
        var usage = await dbContext.UsageEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.EventType == "action_sent" && item.OccurredAt >= from && item.OccurredAt <= to)
            .ToListAsync(cancellationToken);
        var submissions = await dbContext.Submissions.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Action)
            .Where(item => item.SubmittedAt >= from && item.SubmittedAt <= to)
            .ToListAsync(cancellationToken);
        var notifications = await dbContext.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= from && item.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var webhooks = await dbContext.WebhookDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= from && item.CreatedAt <= to)
            .ToListAsync(cancellationToken);
        var files = await dbContext.FileAssets.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.CreatedAt >= from && item.CreatedAt <= to)
            .ToListAsync(cancellationToken);

        var activeOrganizationIds = usage.Select(item => item.OrganizationId).Distinct().ToHashSet();
        var eligibleFor90DayRetention = organizations.Where(item => item.CreatedAt <= now.AddDays(-90)).ToList();
        var retained90 = eligibleFor90DayRetention.Count == 0
            ? null
            : Rate(
                eligibleFor90DayRetention.Count(item => activeOrganizationIds.Contains(item.Id)),
                eligibleFor90DayRetention.Count);
        var recurringRevenue = await BuildRecurringRevenueSnapshotAsync(dbContext, entitlements, now, cancellationToken);
        var mrr = recurringRevenue.Mrr;
        var completed = submissions.Count(item => item.Status == SubmissionStatus.Accepted);
        var sent = usage.Sum(item => item.Quantity);
        var activeOrganizationCount = activeOrganizationIds.Count;
        var completionDurations = submissions
            .Where(item => item.Action?.SentAt is not null && item.SubmittedAt >= item.Action.SentAt.Value)
            .Select(item => item.SubmittedAt - item.Action!.SentAt!.Value);
        var secondChecklistOrganizations = usage
            .GroupBy(item => item.OrganizationId)
            .Count(group => group.Sum(item => item.Quantity) >= 2);

        return new PlatformStats(
            new PlatformBusinessAnalytics(
                Mrr: mrr,
                Arr: mrr * 12,
                PayingOrganizations: recurringRevenue.PayingOrganizations,
                MonthlyActiveOrganizations: activeOrganizationCount,
                NewOrganizations: organizations.Count(item => item.CreatedAt >= from && item.CreatedAt <= to)),
            new PlatformProductAnalytics(
                ChecklistsSent: sent,
                ChecklistsCompleted: completed,
                CompletionRate: Rate(completed, sent),
                CompletedPerActiveOrganization: activeOrganizationCount == 0 ? null : Math.Round((decimal)completed / activeOrganizationCount, 2),
                MedianCompletionMinutes: MedianMinutes(completionDurations),
                SecondChecklistRate: Rate(secondChecklistOrganizations, activeOrganizationCount)),
            new PlatformRetentionAnalytics(
                LogoRetention90Day: retained90,
                GrossRevenueRetention: null,
                NetRevenueRetention: null),
            new PlatformOperationsSummary(
                EmailDeliverySuccessRate: Rate(notifications.Count(item => item.Status == NotificationDeliveryStatus.Delivered), notifications.Count),
                WebhookSuccessRate: Rate(webhooks.Count(item => item.Status == WebhookDeliveryStatus.Succeeded), webhooks.Count),
                FileScanSuccessRate: Rate(files.Count(item => item.ScanStatus == FileScanStatus.Clean), files.Count(item => item.ScanStatus != FileScanStatus.Pending)),
                OpenIncidents: null));
    }

    private static async Task<RecurringRevenueSnapshot> BuildRecurringRevenueSnapshotAsync(
        AtlasDbContext dbContext,
        IEntitlementService entitlements,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var organizations = await dbContext.Organizations.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Status == OrganizationStatus.Active && item.DeletedAt == null)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        decimal mrr = 0;
        var payingOrganizations = 0;
        foreach (var organizationId in organizations)
        {
            var snapshot = await entitlements.GetOrganizationEntitlementsAsync(organizationId, asOf, cancellationToken);
            if (!HasActiveRecurringRevenue(snapshot, asOf))
            {
                continue;
            }

            var planMrr = MonthlyRecurringRevenue(snapshot.Plan, snapshot.Billing);
            if (planMrr <= 0)
            {
                continue;
            }

            payingOrganizations++;
            mrr += planMrr;
        }

        return new RecurringRevenueSnapshot(Math.Round(mrr, 2), payingOrganizations);
    }

    private static bool HasActiveRecurringRevenue(OrganizationEntitlementSnapshot snapshot, DateTimeOffset asOf)
    {
        var hasConfiguredPrice = (snapshot.Plan.MonthlyPriceCents.HasValue && snapshot.Plan.MonthlyPriceCents.Value > 0)
            || (snapshot.Plan.AnnualPriceCents.HasValue && snapshot.Plan.AnnualPriceCents.Value > 0);
        if (!hasConfiguredPrice)
        {
            return false;
        }

        var status = snapshot.Billing.Status.Trim().ToLowerInvariant();
        if (status is not ("active" or "trialing" or "past_due"))
        {
            return false;
        }

        return !snapshot.Billing.CurrentPeriodEnd.HasValue || snapshot.Billing.CurrentPeriodEnd.Value > asOf;
    }

    private static decimal MonthlyRecurringRevenue(BillingPlan plan, OrganizationBillingState billing)
    {
        var billingCycle = billing.BillingCycle.Trim().ToLowerInvariant();
        if (billingCycle is "annual" or "yearly" or "year")
        {
            return plan.AnnualPriceCents.HasValue
                ? plan.AnnualPriceCents.Value / 1200m
                : 0;
        }

        return plan.MonthlyPriceCents.HasValue
            ? plan.MonthlyPriceCents.Value / 100m
            : 0;
    }

    private static IReadOnlyList<MetricDefinitionResponse> OrganizationMetricDefinitions()
    {
        return
        [
            Definition("active_checklists", "Active Checklists", "Checklists currently sent, in progress, or submitted but not completed.", "count", "actions", "near_real_time", "Product", false),
            Definition("recipient_completion_rate", "Recipient Completion Rate", "Recipients who submitted divided by sent checklist requests for the period.", "percent", "action_recipients", "near_real_time", "Product", false),
            Definition("median_completion_minutes", "Median Completion Time", "Median elapsed minutes between checklist sent time and recipient submission time.", "minutes", "actions, submissions", "near_real_time", "Product", false),
            Definition("median_first_open_minutes", "Median Time To First Open", "Median elapsed minutes between checklist sent time and the recipient's first view.", "minutes", "action_recipients", "near_real_time", "Benchmarking", false),
            Definition("document_reuse_rate", "Previously Submitted Document Reuse Rate", "Submission files reused with recipient confirmation divided by all submitted files.", "percent", "submission_files", "near_real_time", "Benchmarking", false),
            Definition("files_pending_scan", "Files Pending Scan", "Uploaded files waiting for malware scan result.", "count", "file_assets", "near_real_time", "Operations", false),
            Definition("completion_funnel", "Completion Funnel", "Recipients moving from sent, viewed, started, submitted, reviewed, and accepted.", "count", "action_recipients, submissions", "near_real_time", "Product", false),
            Definition("reminder_conversion_rate", "Reminder Conversion Rate", "Reminder attempts followed by recipient submission within seven days.", "percent", "reminder_schedules, submissions", "daily", "Product", false),
            Definition("benchmark_same_plan", "Same-Plan Benchmarking", "Anonymized comparison against workspaces on the same billing plan, hidden until the cohort has enough organizations.", "mixed", "action_recipients, submissions, reminders, submission_files, billing settings", "daily", "Benchmarking", false),
            Definition("reused_previous_submissions", "Previously Submitted Documents Used", "Submission files linked to a previously accepted file asset with recipient consent.", "count", "submission_files", "near_real_time", "Product", false)
        ];
    }

    private static IReadOnlyList<MetricDefinitionResponse> PlatformMetricDefinitions()
    {
        return
        [
            Definition("mrr", "MRR", "Monthly recurring revenue recorded from platform revenue events for the selected period.", "currency", "platform_revenue_events", "daily", "Finance", true),
            Definition("arr", "ARR", "MRR multiplied by 12.", "currency", "platform_revenue_events", "daily", "Finance", true),
            Definition("monthly_active_organizations", "Monthly Active Organizations", "Organizations that sent at least one checklist in the selected period.", "count", "usage_events", "daily", "Product", true),
            Definition("completed_checklists", "Completed Checklists", "Accepted submissions in the selected period.", "count", "submissions", "near_real_time", "Product", true),
            Definition("second_checklist_rate", "Second-Checklist Rate", "Active organizations that sent at least two checklists in the selected period divided by active organizations.", "percent", "usage_events", "daily", "Product", true),
            Definition("logo_retention_90_day", "90-Day Logo Retention", "Organizations older than 90 days with at least one checklist sent in the selected period.", "percent", "organizations, usage_events", "daily", "Finance", true),
            Definition("activation_rate", "Activation Rate", "New organizations that sent a first checklist divided by new organizations in the selected period.", "percent", "organizations, usage_events", "daily", "Growth", true),
            Definition("retention_cohorts", "Retention Cohorts", "Organizations grouped by signup month and measured by later active checklist-sending months.", "percent", "organizations, usage_events", "daily", "Retention", true),
            Definition("unit_economics", "Unit Economics", "Revenue per active organization, cost placeholders, and contribution-margin-ready fields.", "currency", "platform_revenue_events, settings", "daily", "Finance", true),
            Definition("template_conversion_rate", "Template Conversion Rate", "Accepted submissions divided by sent checklists grouped by template.", "percent", "templates, actions, submissions", "daily", "Product", true),
            Definition("requirement_completion_rate", "Requirement Completion Rate", "Requirements completed divided by requirements presented, grouped by requirement type and key.", "percent", "requirements, submissions", "daily", "Product", true),
            Definition("benchmark_checklists_sent_per_org", "Benchmark Checklists Sent Per Organization", "Organization checklist volume compared against same-plan platform medians. Industry cohorts require structured organization vertical data.", "count", "action_recipients, organizations, billing settings", "daily", "Benchmarking", true),
            Definition("estimated_time_saved_minutes", "Estimated Time Saved", "Directional estimate derived from completed requests, reused files, and avoided reminder work.", "minutes", "submissions, submission_files, reminders", "daily", "Executive", true),
            Definition("email_delivery_success_rate", "Email Delivery Success Rate", "Notification delivery rows marked delivered divided by all notification delivery rows for the period.", "percent", "notification_deliveries", "near_real_time", "Operations", false)
        ];
    }

    private static InvestorReportResponse ToInvestorReportResponse(InvestorReport report)
    {
        return new InvestorReportResponse(
            report.Id,
            report.ReportType,
            report.Period,
            report.IncludeSections,
            report.Status,
            report.ResultJson,
            report.RequestedByStaffId,
            report.CreatedAt,
            report.UpdatedAt,
            report.CompletedAt);
    }

    private static AnalyticsPeriod ResolveReportPeriod(string period, DateTimeOffset now)
    {
        var trimmed = period.Trim();
        if (DateOnly.TryParseExact(trimmed + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            var start = new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, TimeSpan.Zero);
            return new AnalyticsPeriod(start, start.AddMonths(1).AddTicks(-1));
        }

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            var start = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
            return new AnalyticsPeriod(start, start.AddDays(1).AddTicks(-1));
        }

        var utcNow = now.ToUniversalTime();
        var fallbackStart = new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return new AnalyticsPeriod(fallbackStart, fallbackStart.AddMonths(1).AddTicks(-1));
    }

    private static MetricDefinitionResponse Definition(
        string key,
        string name,
        string description,
        string unit,
        string dataSource,
        string refreshFrequency,
        string owner,
        bool investorVisible)
    {
        return new MetricDefinitionResponse(
            key,
            name,
            description,
            Formula: description,
            unit,
            dataSource,
            refreshFrequency,
            owner,
            Version: "2026-07-16",
            EffectiveFrom: new DateOnly(2026, 7, 16),
            IsInvestorVisible: investorVisible);
    }

    private static bool TryResolvePeriod(
        DateTimeOffset? from,
        DateTimeOffset? to,
        DateTimeOffset now,
        out AnalyticsPeriod period,
        out IResult? problem,
        int? defaultDays = null)
    {
        var utcNow = now.ToUniversalTime();
        var effectiveTo = (to ?? utcNow).ToUniversalTime();
        var effectiveFrom = from?.ToUniversalTime()
            ?? (defaultDays.HasValue
                ? effectiveTo.AddDays(-defaultDays.Value)
                : new DateTimeOffset(effectiveTo.Year, effectiveTo.Month, 1, 0, 0, 0, TimeSpan.Zero));

        if (effectiveFrom > effectiveTo)
        {
            period = new AnalyticsPeriod(effectiveFrom, effectiveTo);
            problem = EndpointHelpers.Problem("validation_failed", "from must be earlier than or equal to to.", StatusCodes.Status422UnprocessableEntity);
            return false;
        }

        period = new AnalyticsPeriod(effectiveFrom, effectiveTo);
        problem = null;
        return true;
    }

    private static decimal? Rate(decimal numerator, decimal denominator)
    {
        return denominator <= 0 ? null : Math.Round(numerator / denominator * 100, 2);
    }

    private static decimal? MedianMinutes(IEnumerable<TimeSpan> durations)
    {
        var values = durations
            .Where(item => item >= TimeSpan.Zero)
            .Select(item => (decimal)item.TotalMinutes)
            .OrderBy(item => item)
            .ToList();
        if (values.Count == 0)
        {
            return null;
        }

        var middle = values.Count / 2;
        return values.Count % 2 == 1
            ? Math.Round(values[middle], 2)
            : Math.Round((values[middle - 1] + values[middle]) / 2, 2);
    }

    private static decimal? Percentile(IReadOnlyList<decimal> sortedValues, int percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        var bounded = Math.Clamp(percentile, 0, 100);
        var rank = (bounded / 100m) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return Math.Round(sortedValues[lower], 2);
        }

        var weight = rank - lower;
        return Math.Round(sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * weight, 2);
    }

    private static string NormalizeInterval(string? interval)
    {
        return (interval ?? "month").Trim().ToLowerInvariant() switch
        {
            "day" => "day",
            "week" => "week",
            _ => "month"
        };
    }

    private static IReadOnlyList<string> PeriodKeys(DateTimeOffset from, DateTimeOffset to, string interval)
    {
        var keys = new List<string>();
        var cursor = PeriodStart(from, interval);
        while (cursor <= to)
        {
            keys.Add(PeriodKey(cursor, interval));
            cursor = interval switch
            {
                "day" => cursor.AddDays(1),
                "week" => cursor.AddDays(7),
                _ => cursor.AddMonths(1)
            };
        }

        return keys;
    }

    private static DateTimeOffset PeriodStart(DateTimeOffset value, string interval)
    {
        var utc = value.ToUniversalTime();
        return interval switch
        {
            "day" => new DateTimeOffset(utc.Year, utc.Month, utc.Day, 0, 0, 0, TimeSpan.Zero),
            "week" => new DateTimeOffset(ISOWeek.ToDateTime(ISOWeek.GetYear(utc.UtcDateTime), ISOWeek.GetWeekOfYear(utc.UtcDateTime), DayOfWeek.Monday), TimeSpan.Zero),
            _ => new DateTimeOffset(utc.Year, utc.Month, 1, 0, 0, 0, TimeSpan.Zero)
        };
    }

    private static string PeriodKey(DateTimeOffset value, string interval)
    {
        var utc = value.ToUniversalTime();
        return interval switch
        {
            "day" => utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "week" => $"{ISOWeek.GetYear(utc.UtcDateTime):0000}-W{ISOWeek.GetWeekOfYear(utc.UtcDateTime):00}",
            _ => utc.ToString("yyyy-MM", CultureInfo.InvariantCulture)
        };
    }

    private static (DateTimeOffset From, DateTimeOffset To) PeriodWindow(string key, string interval)
    {
        if (interval == "day")
        {
            var day = DateOnly.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var start = new DateTimeOffset(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);
            return (start, start.AddDays(1));
        }

        if (interval == "week")
        {
            var parts = key.Split("-W", StringSplitOptions.None);
            var start = new DateTimeOffset(ISOWeek.ToDateTime(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), DayOfWeek.Monday), TimeSpan.Zero);
            return (start, start.AddDays(7));
        }

        var monthParts = key.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var month = new DateTimeOffset(
            int.Parse(monthParts[0], CultureInfo.InvariantCulture),
            int.Parse(monthParts[1], CultureInfo.InvariantCulture),
            1,
            0,
            0,
            0,
            TimeSpan.Zero);
        return (month, month.AddMonths(1));
    }

    private sealed record BenchmarkPlanCodes(
        string DefaultPlanCode,
        IReadOnlyDictionary<Guid, string> PlansByOrganization);

    private sealed record OrganizationBenchmarkRow(
        Guid OrganizationId,
        string PlanCode,
        int SentRecipients,
        int ViewedRecipients,
        int StartedRecipients,
        int SubmittedRecipients,
        int AcceptedSubmissions,
        int ReminderedRecipients,
        int SubmissionFiles,
        int ReusedSubmissionFiles,
        int PendingScans,
        decimal? CompletionRate,
        decimal? FirstOpenRate,
        decimal? MedianCompletionMinutes,
        decimal? MedianFirstOpenMinutes,
        decimal? DocumentReuseRate,
        decimal? ReminderCoverageRate)
    {
        public static OrganizationBenchmarkRow Empty(Guid organizationId, string planCode)
        {
            return new OrganizationBenchmarkRow(
                organizationId,
                planCode,
                SentRecipients: 0,
                ViewedRecipients: 0,
                StartedRecipients: 0,
                SubmittedRecipients: 0,
                AcceptedSubmissions: 0,
                ReminderedRecipients: 0,
                SubmissionFiles: 0,
                ReusedSubmissionFiles: 0,
                PendingScans: 0,
                CompletionRate: null,
                FirstOpenRate: null,
                MedianCompletionMinutes: null,
                MedianFirstOpenMinutes: null,
                DocumentReuseRate: null,
                ReminderCoverageRate: null);
        }
    }

    private sealed record PlatformAccess(PlatformStaff? Staff, IResult? Problem);
    private sealed record PlatformStats(
        PlatformBusinessAnalytics Business,
        PlatformProductAnalytics Product,
        PlatformRetentionAnalytics Retention,
        PlatformOperationsSummary Operations);

    private sealed record RecurringRevenueSnapshot(decimal Mrr, int PayingOrganizations);
}

public sealed record AnalyticsPeriod(DateTimeOffset From, DateTimeOffset To);

public sealed record OrganizationOverviewAnalyticsResponse(
    AnalyticsPeriod Period,
    OrganizationOverviewSummary Summary,
    IReadOnlyList<OrganizationNeedsAttentionItem> NeedsAttention,
    IReadOnlyList<DueDateAnalyticsItem> UpcomingDueDates,
    IReadOnlyList<RecentSubmissionAnalyticsItem> RecentSubmissions,
    IReadOnlyList<ExpiringDocumentAnalyticsItem> ExpiringDocuments);

public sealed record OrganizationOverviewSummary(
    int ActiveChecklists,
    int AwaitingRecipient,
    int InProgress,
    int Overdue,
    int Submitted);

public sealed record OrganizationNeedsAttentionItem(
    Guid ChecklistId,
    Guid RecipientId,
    string RecipientName,
    string RecipientEmail,
    string ChecklistTitle,
    int MissingRequiredItems,
    DateTimeOffset? DueAt,
    int DaysOverdue,
    DateTimeOffset? LastActivityAt,
    string RecommendedAction);

public sealed record DueDateAnalyticsItem(Guid ChecklistId, string ChecklistTitle, DateTimeOffset DueAt, ChecklistActionStatus Status);

public sealed record RecentSubmissionAnalyticsItem(
    Guid SubmissionId,
    Guid ChecklistId,
    string ChecklistTitle,
    Guid RecipientId,
    string RecipientName,
    string RecipientEmail,
    SubmissionStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ReviewedAt);

public sealed record ExpiringDocumentAnalyticsItem(
    Guid FileAssetId,
    string? DocumentName,
    DateTimeOffset ExpiresAt,
    string? RecipientName,
    string? RecipientEmail);

public sealed record CompletionAnalyticsResponse(
    AnalyticsPeriod Period,
    CompletionFunnel Funnel,
    CompletionRates Rates,
    CompletionPerformance Performance);

public sealed record CompletionFunnel(int Sent, int Delivered, int Viewed, int Started, int Submitted, int Accepted);

public sealed record CompletionRates(
    decimal? DeliveryRate,
    decimal? ViewRate,
    decimal? StartRate,
    decimal? SubmissionRate,
    decimal? AcceptanceRate);

public sealed record CompletionPerformance(
    decimal? MedianCompletionMinutes,
    decimal? CompletedBeforeDueRate,
    decimal? FirstAttemptAcceptanceRate);

public sealed record PagedAnalyticsResponse<T>(IReadOnlyList<T> Items, PaginationResponse Pagination);

public sealed record PaginationResponse(int Page, int PageSize, int TotalItems);

public sealed record TemplateAnalyticsItem(
    Guid? TemplateId,
    string TemplateName,
    int Sent,
    int Submitted,
    decimal? CompletionRate,
    decimal? MedianCompletionMinutes,
    decimal? AverageReminderCount,
    decimal? FirstAttemptAcceptanceRate,
    RequirementHighlight? TopMissingRequirement,
    RequirementHighlight? TopRejectedRequirement,
    RequirementHighlight? DropOffRequirement);

public sealed record RequirementHighlight(Guid? RequirementId, string Label, int Count);

public sealed record RequirementAnalyticsItem(
    Guid? TemplateRequirementId,
    string RequirementKey,
    string Label,
    RequirementType Type,
    int PresentedCount,
    int CompletedCount,
    decimal? CompletionRate,
    decimal? AverageCompletionMinutes,
    decimal? ValidationErrorRate,
    decimal? UploadFailureRate,
    decimal? CorrectionRequestRate,
    decimal? FileRejectionRate,
    int EstimatedDropOffCount);

public sealed record ReminderAnalyticsResponse(
    AnalyticsPeriod Period,
    ReminderAnalyticsSummary Summary,
    IReadOnlyList<ReminderTimingPerformance> TimingPerformance);

public sealed record ReminderAnalyticsSummary(
    int AutomaticSent,
    int ManualSent,
    decimal? CompletionWithin24HoursRate,
    decimal? CompletionWithin72HoursRate,
    decimal? AverageRemindersPerCompletion);

public sealed record ReminderTimingPerformance(
    string Timing,
    int Sent,
    int CompletedWithin24Hours,
    decimal? ConversionRate);

public sealed record RecipientExperienceAnalyticsResponse(
    AnalyticsPeriod Period,
    decimal? MobileShare,
    decimal? DesktopShare,
    decimal? SaveAndReturnRate,
    decimal? AverageSessionsPerSubmission,
    decimal? MedianRecipientSessionDurationMinutes,
    decimal? UploadFailureRate,
    decimal? SubmissionRetryRate,
    decimal? TurnstileFailureRate,
    int? LinkRecoveryRequests,
    decimal? RecipientSatisfaction);

public sealed record ComplianceAnalyticsResponse(
    AnalyticsPeriod Period,
    int DocumentsExpiring30Days,
    int DocumentsExpiring60Days,
    int DocumentsExpiring90Days,
    int ExpiredDocuments,
    int MissingRequiredDocuments,
    int PendingReviews,
    int FilesPendingScan,
    int FilesQuarantined,
    int RetentionDeletionsUpcoming,
    int ReusedPreviousSubmissions);

public sealed record UsageCurrentResponse(
    OrganizationBillingState Billing,
    BillingPlan Plan,
    OrganizationUsageSnapshot Usage,
    int? ApiRequests,
    int? WebhookDeliveries,
    int? AiCredits);

public sealed record MetricDefinitionResponse(
    string Key,
    string Name,
    string Description,
    string Formula,
    string Unit,
    string DataSource,
    string RefreshFrequency,
    string Owner,
    string Version,
    DateOnly EffectiveFrom,
    bool IsInvestorVisible);

public sealed record PlatformAnalyticsOverviewResponse(
    AnalyticsPeriod Period,
    PlatformBusinessAnalytics Business,
    PlatformProductAnalytics Product,
    PlatformRetentionAnalytics Retention,
    PlatformOperationsSummary Operations);

public sealed record PlatformBusinessAnalytics(
    decimal Mrr,
    decimal Arr,
    int PayingOrganizations,
    int MonthlyActiveOrganizations,
    int NewOrganizations);

public sealed record PlatformProductAnalytics(
    decimal ChecklistsSent,
    int ChecklistsCompleted,
    decimal? CompletionRate,
    decimal? CompletedPerActiveOrganization,
    decimal? MedianCompletionMinutes,
    decimal? SecondChecklistRate);

public sealed record PlatformRetentionAnalytics(
    decimal? LogoRetention90Day,
    decimal? GrossRevenueRetention,
    decimal? NetRevenueRetention);

public sealed record PlatformOperationsSummary(
    decimal? EmailDeliverySuccessRate,
    decimal? WebhookSuccessRate,
    decimal? FileScanSuccessRate,
    int? OpenIncidents);

public sealed record PlatformGrowthAnalyticsResponse(
    AnalyticsPeriod Period,
    string Interval,
    IReadOnlyList<PlatformGrowthSeriesItem> Series);

public sealed record PlatformGrowthSeriesItem(
    string Period,
    int NewOrganizations,
    int ActivatedOrganizations,
    int? PayingOrganizations,
    int ActiveOrganizations,
    int CompletedChecklists);

public sealed record PlatformRevenueAnalyticsResponse(
    AnalyticsPeriod Period,
    decimal? OpeningMrr,
    decimal NewMrr,
    decimal ExpansionMrr,
    decimal? ReactivationMrr,
    decimal ContractionMrr,
    decimal ChurnedMrr,
    decimal ClosingMrr,
    decimal Arr,
    decimal? Arpa,
    decimal? TrialToPaidConversionRate,
    IReadOnlyList<RevenueByCurrency> RevenueByCurrency);

public sealed record PlatformOperationsAnalyticsResponse(
    AnalyticsPeriod Period,
    decimal? ApiUptimeRate,
    decimal? ApiErrorRate,
    decimal? P50LatencyMs,
    decimal? P95LatencyMs,
    decimal? P99LatencyMs,
    int? JobBacklog,
    int? FailedJobs,
    decimal? EmailDeliverySuccessRate,
    decimal? EmailBounceRate,
    decimal? UploadFailureRate,
    decimal? FileScanSuccessRate,
    decimal? FileScanFailureRate,
    decimal? WebhookDeliverySuccessRate,
    bool DatabaseHealthy,
    long StorageBytes,
    int? TurnstileFailureCount,
    int? RateLimitTriggerCount);

public sealed record PlatformSecurityAnalyticsResponse(
    AnalyticsPeriod Period,
    int? FailedOrganizationLogins,
    int SuspiciousRecipientLinkActivity,
    int? InvalidTokenAttempts,
    int? RevokedLinkAttempts,
    int? RateLimitTriggers,
    int MalwareDetections,
    int QuarantinedFiles,
    int? CrossTenantAuthorizationDenials,
    int? ApiKeyFailures,
    int? WebhookSignatureFailures,
    int AdministrativeRoleChanges,
    int DataExports,
    int DeletionRequests);

public sealed record InvestorSummaryResponse(
    DateTimeOffset AsOf,
    InvestorBusinessSummary Business,
    InvestorProductSummary Product,
    InvestorRetentionSummary Retention,
    InvestorGrowthSummary Growth);

public sealed record InvestorBusinessSummary(
    decimal Mrr,
    decimal Arr,
    decimal? MrrGrowthPercent,
    int PayingOrganizations,
    decimal? AverageRevenuePerOrganization,
    decimal? GrossMarginPercent,
    decimal? MonthlyBurn,
    decimal? RunwayMonths);

public sealed record InvestorProductSummary(
    int MonthlyActiveOrganizations,
    int CompletedChecklists,
    decimal? CompletedPerActiveOrganization,
    decimal? CompletionRate,
    decimal? MedianCompletionMinutes,
    decimal? SecondChecklistRate);

public sealed record InvestorRetentionSummary(
    decimal? LogoRetention90Day,
    decimal? GrossRevenueRetention,
    decimal? NetRevenueRetention,
    decimal? MonthlyLogoChurn);

public sealed record InvestorGrowthSummary(
    int NewActivatedOrganizations,
    decimal? TrialToPaidRate,
    decimal? QualifiedPipelineValue,
    decimal? AverageSalesCycleDays,
    decimal? AverageContractValue);

public sealed record TrackAnalyticsEventRequest(
    string EventName,
    Guid? ChecklistId,
    Guid? TemplateId,
    Guid? RecipientId,
    string? SessionId,
    JsonElement? Properties,
    DateTimeOffset? OccurredAt);

public sealed record TrackAnalyticsEventResponse(Guid Id, bool Accepted);

public sealed record AnalyticsEventResponse(
    Guid Id,
    string EventName,
    Guid? OrganizationId,
    Guid? ChecklistId,
    Guid? TemplateId,
    Guid? RecipientId,
    Guid? UserId,
    string? SessionId,
    string PropertiesJson,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt);

public sealed record PlatformActivationAnalyticsResponse(
    AnalyticsPeriod Period,
    ActivationFunnel Funnel,
    ActivationRates Rates);

public sealed record ActivationFunnel(
    int SignedUp,
    int OrganizationConfigured,
    int TemplateSelectedOrCreated,
    int FirstChecklistCreated,
    int FirstChecklistSent,
    int FirstChecklistViewed,
    int FirstChecklistCompleted,
    int SecondChecklistSent,
    int Paid);

public sealed record ActivationRates(
    decimal? SignupToFirstChecklistCreated,
    decimal? SignupToFirstChecklistSent,
    decimal? SignupToFirstSubmission,
    decimal? ActivationWithinPeriod,
    decimal? SecondChecklistRate,
    decimal? TrialToPaidConversion);

public sealed record PlatformEngagementAnalyticsResponse(
    AnalyticsPeriod Period,
    int ActiveOrganizations,
    decimal? ChecklistsPerActiveOrganization,
    decimal? TemplatesPerOrganization,
    decimal? UniqueRecipientsPerOrganization,
    int ActiveOrganizationUsers,
    decimal? AutoReminderAdoptionRate,
    decimal? ApiAdoptionRate,
    decimal? WebhookAdoptionRate,
    decimal? CustomTemplateAdoptionRate,
    decimal? RenewalFeatureAdoptionRate,
    decimal? PreviousDocumentReuseAdoptionRate);

public sealed record RetentionCohortAnalyticsResponse(
    string CohortBy,
    string Activity,
    AnalyticsPeriod Period,
    IReadOnlyList<RetentionCohortItem> Cohorts);

public sealed record RetentionCohortItem(
    string Cohort,
    int StartingOrganizations,
    IReadOnlyDictionary<string, decimal?> Retention);

public sealed record UnitEconomicsAnalyticsResponse(
    AnalyticsPeriod Period,
    decimal? InfrastructureCost,
    decimal? CostPerChecklistSent,
    decimal? CostPerCompletedChecklist,
    decimal? CostPerThousandChecklists,
    long StorageBytes,
    decimal? StorageCost,
    decimal? EmailCost,
    decimal? MalwareScanningCost,
    decimal? AiProcessingCost,
    decimal? GrossMarginPercent,
    decimal? Cac,
    decimal? PaybackPeriodMonths,
    decimal? Ltv,
    decimal? SupportCostPerOrganization,
    decimal ChecklistsSent,
    int CompletedChecklists);

public sealed record SegmentAnalyticsResponse(
    AnalyticsPeriod Period,
    IReadOnlyList<SegmentAnalyticsItem> Industries,
    IReadOnlyList<TemplateIntelligenceItem> Templates);

public sealed record SegmentAnalyticsItem(
    string Segment,
    int Organizations,
    int? PayingOrganizations,
    decimal? Mrr,
    decimal? CompletionRate,
    decimal? MedianCompletionMinutes,
    decimal? RetentionRate,
    decimal? AverageChecklistVolume,
    decimal? AverageContractValue,
    decimal? SupportBurden);

public sealed record TemplateIntelligenceItem(
    string TemplateName,
    int Usage,
    int Completed,
    decimal? CompletionRate,
    decimal? RepeatUsageRate,
    decimal? ConversionToPaidRate,
    IReadOnlyList<string> IndustriesUsingTemplate,
    decimal? RetentionCorrelation);

public sealed record TimeSavedAnalyticsResponse(
    AnalyticsPeriod Period,
    int CompletedChecklists,
    int ReusedDocuments,
    decimal EstimatedMinutesSaved,
    decimal EstimatedHoursSaved,
    string Methodology);

public sealed record PredictiveInsightsResponse(
    AnalyticsPeriod Period,
    IReadOnlyList<PredictiveInsightItem> Insights);

public sealed record PredictiveInsightItem(
    string Key,
    string Title,
    string Severity,
    string Summary,
    string RecommendedAction,
    decimal Confidence);

public sealed record BenchmarkAnalyticsResponse(
    AnalyticsPeriod Period,
    BenchmarkItem ChecklistsSentPerOrganization,
    IReadOnlyList<PlatformBenchmarkMetric> Metrics,
    IReadOnlyList<PlatformBenchmarkCohort> Cohorts,
    IReadOnlyList<BenchmarkDelayDriver> DelayDrivers,
    string Notes);

public sealed record BenchmarkItem(
    string Key,
    string Label,
    decimal? P50,
    decimal? P75,
    decimal? P90,
    string Unit);

public sealed record OrganizationBenchmarkAnalyticsResponse(
    AnalyticsPeriod Period,
    string Cohort,
    string CohortLabel,
    int CohortSampleSize,
    int MinimumCohortSize,
    bool BenchmarksAvailable,
    IReadOnlyList<OrganizationBenchmarkMetric> Metrics,
    IReadOnlyList<BenchmarkDelayDriver> DelayDrivers,
    IReadOnlyList<BenchmarkInsight> Insights,
    string PrivacyNote);

public sealed record OrganizationBenchmarkMetric(
    string Key,
    string Label,
    string Unit,
    decimal? OrganizationValue,
    decimal? BenchmarkMedian,
    decimal? BenchmarkP75,
    decimal? BenchmarkP90,
    decimal? DeltaFromMedian,
    string Direction,
    bool HigherIsBetter,
    bool Suppressed);

public sealed record PlatformBenchmarkMetric(
    string Key,
    string Label,
    string Unit,
    int OrganizationsWithValue,
    decimal? P50,
    decimal? P75,
    decimal? P90,
    bool HigherIsBetter);

public sealed record PlatformBenchmarkCohort(
    string CohortType,
    string CohortKey,
    string CohortLabel,
    int SampleSize,
    IReadOnlyList<PlatformBenchmarkMetric> Metrics);

public sealed record BenchmarkDelayDriver(
    string Key,
    string Label,
    int Value,
    string Unit,
    string RecommendedAction);

public sealed record BenchmarkInsight(
    string Key,
    string Title,
    string Severity,
    string Summary,
    string RecommendedAction);

public sealed record SystemHealthAnalyticsResponse(
    DateTimeOffset AsOf,
    string Status,
    bool DatabaseHealthy,
    bool ApiHealthy,
    int PendingFileScans,
    int FailedNotificationDeliveries,
    int PendingWebhookDeliveries,
    long StorageBytes,
    int? OpenIncidents);

public sealed record InvestorTrendResponse<T>(
    AnalyticsPeriod Period,
    string Interval,
    IReadOnlyList<T> Series);

public sealed record InvestorRevenueTrendItem(string Period, decimal Mrr, decimal Arr);

public sealed record InvestorProductUsageTrendItem(
    string Period,
    decimal ChecklistsSent,
    int CompletedChecklists,
    decimal? CompletionRate);

public sealed record InvestorMilestonesResponse(
    DateTimeOffset AsOf,
    IReadOnlyList<InvestorMilestoneItem> Items);

public sealed record InvestorMilestoneItem(
    string Key,
    string Label,
    decimal Value,
    decimal? Target,
    string Status);

public sealed record CreateInvestorReportRequest(
    string ReportType,
    string Period,
    IReadOnlyList<string>? IncludeSections);

public sealed record InvestorReportResponse(
    Guid Id,
    string ReportType,
    string Period,
    IReadOnlyList<string> IncludeSections,
    InvestorReportStatus Status,
    string ResultJson,
    Guid RequestedByStaffId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);
