using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Settings;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Billing;

public sealed class EntitlementService(
    AtlasDbContext dbContext,
    IAdminSettingService settings) : IEntitlementService
{
    private const string DefaultPlanCode = "free";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<BillingPlan>> ListPlansAsync(CancellationToken cancellationToken)
    {
        var configured = await settings.GetAsync(null, "billing", "plans", cancellationToken);
        return ParsePlans(configured?.ValueJson);
    }

    public async Task<OrganizationEntitlementSnapshot> GetOrganizationEntitlementsAsync(
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var plans = await ListPlansAsync(cancellationToken);
        var billing = await ResolveBillingStateAsync(organizationId, plans, cancellationToken);
        var effectivePlanCode = IsEntitledBillingStatus(billing.Status) ? billing.PlanCode : DefaultPlanCode;
        var plan = plans.FirstOrDefault(item => string.Equals(item.Code, effectivePlanCode, StringComparison.OrdinalIgnoreCase))
            ?? plans.First(item => item.Code == DefaultPlanCode);
        var usage = await BuildUsageSnapshotAsync(organizationId, plan, now, cancellationToken);
        return new OrganizationEntitlementSnapshot(plan, billing, usage);
    }

    public async Task<EntitlementCheckResult> CanSendChecklistAsync(
        Guid organizationId,
        DateTimeOffset now,
        int additionalChecklists,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetOrganizationEntitlementsAsync(organizationId, now, cancellationToken);
        if (snapshot.Plan.MonthlyChecklistLimit is null)
        {
            return Allowed(snapshot);
        }

        var projected = snapshot.Usage.ChecklistsSent + Math.Max(0, additionalChecklists);
        return projected <= snapshot.Plan.MonthlyChecklistLimit.Value
            ? Allowed(snapshot)
            : new EntitlementCheckResult(
                false,
                "monthly_checklist_limit_exceeded",
                $"This organization has reached the {snapshot.Plan.Name} monthly checklist limit.",
                snapshot);
    }

    public async Task<EntitlementCheckResult> CanStoreFileAsync(
        Guid organizationId,
        DateTimeOffset now,
        long additionalBytes,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetOrganizationEntitlementsAsync(organizationId, now, cancellationToken);
        if (snapshot.Plan.StorageBytes is null)
        {
            return Allowed(snapshot);
        }

        var projected = snapshot.Usage.StorageBytesUsed + Math.Max(0, additionalBytes);
        return projected <= snapshot.Plan.StorageBytes.Value
            ? Allowed(snapshot)
            : new EntitlementCheckResult(
                false,
                "storage_limit_exceeded",
                $"This organization has reached the {snapshot.Plan.Name} storage limit.",
                snapshot);
    }

    public async Task<EntitlementCheckResult> HasFeatureAsync(
        Guid organizationId,
        DateTimeOffset now,
        string feature,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetOrganizationEntitlementsAsync(organizationId, now, cancellationToken);
        return IsFeatureEnabled(snapshot.Plan.Features, feature)
            ? Allowed(snapshot)
            : new EntitlementCheckResult(
                false,
                "feature_not_available",
                $"The {feature} feature is not included in the current plan.",
                snapshot);
    }

    private static EntitlementCheckResult Allowed(OrganizationEntitlementSnapshot snapshot)
    {
        return new EntitlementCheckResult(true, "allowed", "Allowed by current plan.", snapshot);
    }

    private async Task<OrganizationBillingState> ResolveBillingStateAsync(
        Guid organizationId,
        IReadOnlyList<BillingPlan> plans,
        CancellationToken cancellationToken)
    {
        var defaultCode = ReadString((await settings.GetAsync(null, "billing", "defaultPlanCode", cancellationToken))?.ValueJson)
            ?? DefaultPlanCode;
        var configured = await settings.GetAsync(organizationId, "billing", "plan", cancellationToken)
            ?? await settings.GetAsync(organizationId, "billing", "planCode", cancellationToken);

        if (configured is null)
        {
            return NormalizeBillingState(
                new OrganizationBillingState(defaultCode, "monthly", "active", null, null),
                plans,
                defaultCode);
        }

        var raw = configured.ValueJson;
        if (ReadString(raw) is { } planCode)
        {
            return NormalizeBillingState(
                new OrganizationBillingState(planCode, "monthly", "active", null, null),
                plans,
                defaultCode);
        }

        try
        {
            var state = JsonSerializer.Deserialize<OrganizationBillingState>(raw, JsonOptions);
            if (state is not null)
            {
                return NormalizeBillingState(state, plans, defaultCode);
            }
        }
        catch (JsonException)
        {
        }

        return NormalizeBillingState(
            new OrganizationBillingState(defaultCode, "monthly", "active", null, null),
            plans,
            defaultCode);
    }

    private static OrganizationBillingState NormalizeBillingState(
        OrganizationBillingState state,
        IReadOnlyCollection<BillingPlan> plans,
        string defaultCode)
    {
        var planCode = string.IsNullOrWhiteSpace(state.PlanCode) ? defaultCode : state.PlanCode.Trim().ToLowerInvariant();
        if (plans.All(item => !string.Equals(item.Code, planCode, StringComparison.OrdinalIgnoreCase)))
        {
            planCode = DefaultPlanCode;
        }

        return state with
        {
            PlanCode = planCode,
            BillingCycle = string.IsNullOrWhiteSpace(state.BillingCycle) ? "monthly" : state.BillingCycle.Trim().ToLowerInvariant(),
            Status = string.IsNullOrWhiteSpace(state.Status) ? "active" : state.Status.Trim().ToLowerInvariant(),
            Provider = string.IsNullOrWhiteSpace(state.Provider) ? "manual" : state.Provider.Trim().ToLowerInvariant()
        };
    }

    private static bool IsEntitledBillingStatus(string status)
    {
        return status.Trim().ToLowerInvariant() is "active" or "trialing" or "past_due";
    }

    private async Task<OrganizationUsageSnapshot> BuildUsageSnapshotAsync(
        Guid organizationId,
        BillingPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var utcNow = now.ToUniversalTime();
        var periodStart = new DateTimeOffset(utcNow.Year, utcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = periodStart.AddMonths(1);
        var checklistsSent = await dbContext.UsageEvents.IgnoreQueryFilters()
            .Where(item => item.OrganizationId == organizationId
                && item.EventType == "action_sent"
                && item.OccurredAt >= periodStart
                && item.OccurredAt < periodEnd)
            .SumAsync(item => (decimal?)item.Quantity, cancellationToken) ?? 0;
        var storageUsed = await dbContext.FileAssets.IgnoreQueryFilters()
            .Where(item => item.OrganizationId == organizationId && item.DeletedAt == null)
            .SumAsync(item => (long?)item.SizeBytes, cancellationToken) ?? 0;

        return new OrganizationUsageSnapshot(
            periodStart,
            periodEnd,
            (int)checklistsSent,
            plan.MonthlyChecklistLimit,
            plan.MonthlyChecklistLimit is null ? null : Math.Max(0, plan.MonthlyChecklistLimit.Value - (int)checklistsSent),
            storageUsed,
            plan.StorageBytes,
            plan.StorageBytes is null ? null : Math.Max(0, plan.StorageBytes.Value - storageUsed));
    }

    private static IReadOnlyList<BillingPlan> ParsePlans(string? valueJson)
    {
        if (!string.IsNullOrWhiteSpace(valueJson))
        {
            try
            {
                using var document = JsonDocument.Parse(valueJson);
                var root = document.RootElement;
                var plansElement = root.ValueKind == JsonValueKind.Array
                    ? root
                    : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("plans", out var nested)
                        ? nested
                        : default;
                if (plansElement.ValueKind == JsonValueKind.Array)
                {
                    var plans = JsonSerializer.Deserialize<IReadOnlyList<BillingPlan>>(plansElement.GetRawText(), JsonOptions);
                    if (plans is { Count: > 0 })
                    {
                        return plans
                            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
                            .Select(item => item with { Code = item.Code.Trim().ToLowerInvariant() })
                            .ToList();
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return DefaultPlans();
    }

    private static string? ReadString(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(valueJson, JsonOptions);
            return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsFeatureEnabled(PlanFeatures features, string feature)
    {
        return feature.Trim().ToLowerInvariant() switch
        {
            "team_workspace" => features.TeamWorkspace,
            "template_library" => features.TemplateLibrary,
            "reqara_branding" => features.ReqaraBranding,
            "custom_branding" => features.CustomBranding,
            "automatic_reminders" => features.AutomaticReminders,
            "api_and_webhooks" => features.ApiAndWebhooks,
            "custom_workflows" => features.CustomWorkflows,
            "priority_support" => features.PrioritySupport,
            "sso" => features.Sso,
            "custom_retention" => features.CustomRetention,
            "security_review" => features.SecurityReview,
            "dpa" => features.Dpa,
            "dedicated_onboarding" => features.DedicatedOnboarding,
            "package_report" => features.TeamWorkspace,
            "package_zip_export" => features.TeamWorkspace,
            "manual_handoff" => features.TeamWorkspace,
            "email_destination" => features.TeamWorkspace,
            "webhook_destination" => features.ApiAndWebhooks,
            "api_access" => features.ApiAndWebhooks,
            "api_pull_destination" => features.ApiAndWebhooks,
            "custom_destinations" => features.CustomWorkflows,
            "automatic_routing" => features.CustomWorkflows,
            "sharepoint_destination" => features.CustomWorkflows,
            "onedrive_destination" => features.CustomWorkflows,
            "google_drive_destination" => features.CustomWorkflows,
            "sftp_destination" => features.CustomWorkflows,
            "delivery_history_retention" => features.TeamWorkspace,
            "advanced_audit" => features.TeamWorkspace,
            _ => false
        };
    }

    private static IReadOnlyList<BillingPlan> DefaultPlans()
    {
        return
        [
            new BillingPlan(
                "free",
                "Free",
                "Try Reqara on real requests.",
                0,
                0,
                "CAD",
                10,
                500L * 1024 * 1024,
                false,
                new PlanFeatures(true, true, true, false, false, false, false, false, false, false, false, false, false)),
            new BillingPlan(
                "starter",
                "Starter",
                "For solo pros and small teams.",
                3900,
                39000,
                "CAD",
                100,
                5L * 1024 * 1024 * 1024,
                false,
                new PlanFeatures(true, true, false, true, true, false, false, false, false, false, false, false, false)),
            new BillingPlan(
                "business",
                "Business",
                "For teams sending every day.",
                9900,
                99000,
                "CAD",
                500,
                25L * 1024 * 1024 * 1024,
                false,
                new PlanFeatures(true, true, false, true, true, true, true, true, false, false, false, false, false)),
            new BillingPlan(
                "scale",
                "Scale",
                "For regulated and high-volume use.",
                null,
                null,
                "CAD",
                null,
                null,
                true,
                new PlanFeatures(true, true, false, true, true, true, true, true, true, true, true, true, true))
        ];
    }
}
