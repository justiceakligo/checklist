namespace Atlas.Application.Billing;

public interface IEntitlementService
{
    Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken);

    Task<OrganizationEntitlementSnapshot> GetOrganizationEntitlementsAsync(
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<EntitlementCheckResult> CanSendChecklistAsync(
        Guid organizationId,
        DateTimeOffset now,
        int additionalChecklists,
        CancellationToken cancellationToken);

    Task<EntitlementCheckResult> CanStoreFileAsync(
        Guid organizationId,
        DateTimeOffset now,
        long additionalBytes,
        CancellationToken cancellationToken);

    Task<EntitlementCheckResult> HasFeatureAsync(
        Guid organizationId,
        DateTimeOffset now,
        string feature,
        CancellationToken cancellationToken);
}

public sealed record BillingPlan(
    string Code,
    string Name,
    string Description,
    int? MonthlyPriceCents,
    int? AnnualPriceCents,
    string Currency,
    int? MonthlyChecklistLimit,
    long? StorageBytes,
    bool CustomPricing,
    PlanFeatures Features);

public sealed record PlanFeatures(
    bool TeamWorkspace,
    bool TemplateLibrary,
    bool ReqaraBranding,
    bool CustomBranding,
    bool AutomaticReminders,
    bool ApiAndWebhooks,
    bool CustomWorkflows,
    bool PrioritySupport,
    bool Sso,
    bool CustomRetention,
    bool SecurityReview,
    bool Dpa,
    bool DedicatedOnboarding);

public sealed record OrganizationBillingState(
    string PlanCode,
    string BillingCycle,
    string Status,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    string Provider = "manual",
    string? StripeCustomerId = null,
    string? StripeSubscriptionId = null,
    bool CancelAtPeriodEnd = false);

public sealed record OrganizationUsageSnapshot(
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    int ChecklistsSent,
    int? MonthlyChecklistLimit,
    int? ChecklistsRemaining,
    long StorageBytesUsed,
    long? StorageBytesLimit,
    long? StorageBytesRemaining);

public sealed record OrganizationEntitlementSnapshot(
    BillingPlan Plan,
    OrganizationBillingState Billing,
    OrganizationUsageSnapshot Usage);

public sealed record EntitlementCheckResult(
    bool Allowed,
    string Code,
    string Message,
    OrganizationEntitlementSnapshot Snapshot);
