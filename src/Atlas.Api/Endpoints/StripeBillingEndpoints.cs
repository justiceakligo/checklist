using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

internal static class StripeBillingEndpoints
{
    private const string StripeApiBaseUrl = "https://api.stripe.com/v1";
    private const string StripeCategory = "stripe";

    public static void MapStripeBillingEndpoints(this RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/billing").WithTags("Billing");

        group.MapGet("/config", async (
            AtlasDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var stripe = await ReadStripeSettingsAsync(dbContext, cancellationToken);
            return Results.Ok(new StripeBillingConfigResponse(
                "stripe",
                stripe.Enabled,
                stripe.PublishableKey,
                ["free", "starter", "business"],
                "scale"));
        });

        group.MapPost("/checkout-sessions", async (
            CreateStripeCheckoutSessionRequest request,
            AtlasDbContext dbContext,
            IEntitlementService entitlements,
            IAdminSettingService adminSettings,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (tenantContext.ActorType != "user")
            {
                return EndpointHelpers.Problem("dashboard_user_required", "A dashboard user session is required to start billing checkout.", StatusCodes.Status403Forbidden);
            }

            var stripe = await ReadStripeSettingsAsync(dbContext, cancellationToken);
            if (!stripe.Enabled || string.IsNullOrWhiteSpace(stripe.SecretKey))
            {
                return EndpointHelpers.Problem("billing_not_configured", "Stripe billing is not configured.", StatusCodes.Status503ServiceUnavailable);
            }

            var plans = await entitlements.ListPlansAsync(cancellationToken);
            var planCode = NormalizePlanCode(request.PlanCode);
            var plan = plans.FirstOrDefault(item => string.Equals(item.Code, planCode, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                return EndpointHelpers.Problem("plan_not_found", "Billing plan was not found.", StatusCodes.Status404NotFound);
            }

            if (plan.Code == "free")
            {
                return EndpointHelpers.Problem("checkout_not_required", "The Free plan does not require Stripe checkout.", StatusCodes.Status422UnprocessableEntity);
            }

            if (plan.CustomPricing)
            {
                return EndpointHelpers.Problem("contact_sales_required", "This plan uses custom pricing and cannot be purchased through self-serve checkout.", StatusCodes.Status422UnprocessableEntity);
            }

            var billingCycle = NormalizeBillingCycle(request.BillingCycle);
            var amountCents = billingCycle == "annual" ? plan.AnnualPriceCents : plan.MonthlyPriceCents;
            if (amountCents is null or <= 0)
            {
                return EndpointHelpers.Problem("plan_not_purchasable", "This plan does not have a configured Stripe checkout price.", StatusCodes.Status422UnprocessableEntity);
            }

            var organization = await dbContext.Organizations.IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == organizationId && item.DeletedAt == null, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("organization_not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            var userEmail = await ResolveBillingEmailAsync(dbContext, organizationId, tenantContext.UserId, cancellationToken);
            var currentBilling = await ReadOrganizationBillingStateAsync(dbContext, organizationId, cancellationToken);
            if (IsSameActiveSubscription(currentBilling, plan.Code, billingCycle))
            {
                return Results.Problem(
                    title: $"This workspace is already subscribed to {plan.Name}.",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    type: "https://docs.atlas.example/errors/already_subscribed",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "already_subscribed",
                        ["message"] = $"This workspace is already on the {plan.Name} {billingCycle} plan.",
                        ["planCode"] = plan.Code,
                        ["billingCycle"] = billingCycle,
                        ["billing"] = currentBilling
                    });
            }

            var appBaseUrl = await EndpointHelpers.BuildAppBaseUrlAsync(adminSettings, configuration, organizationId, httpContext, cancellationToken);
            var successUrl = NormalizeCheckoutSuccessUrl(request.SuccessUrl)
                ?? $"{appBaseUrl.TrimEnd('/')}/billing/success?session_id={{CHECKOUT_SESSION_ID}}";
            var cancelUrl = NormalizeReturnUrl(request.CancelUrl)
                ?? $"{appBaseUrl.TrimEnd('/')}/pricing";

            var urlProblem = ValidateFrontendReturnUrl(successUrl, "successUrl")
                ?? ValidateFrontendReturnUrl(cancelUrl, "cancelUrl");
            if (urlProblem is not null)
            {
                return urlProblem;
            }

            var values = new List<KeyValuePair<string, string>>
            {
                new("mode", "subscription"),
                new("success_url", successUrl),
                new("cancel_url", cancelUrl),
                new("client_reference_id", organizationId.ToString()),
                new("allow_promotion_codes", "true"),
                new("metadata[organization_id]", organizationId.ToString()),
                new("metadata[organization_name]", organization.Name),
                new("metadata[plan_code]", plan.Code),
                new("metadata[billing_cycle]", billingCycle),
                new("subscription_data[metadata][organization_id]", organizationId.ToString()),
                new("subscription_data[metadata][organization_name]", organization.Name),
                new("subscription_data[metadata][plan_code]", plan.Code),
                new("subscription_data[metadata][billing_cycle]", billingCycle),
                new("line_items[0][quantity]", "1"),
                new("line_items[0][price_data][currency]", plan.Currency.ToLowerInvariant()),
                new("line_items[0][price_data][unit_amount]", amountCents.Value.ToString(CultureInfo.InvariantCulture)),
                new("line_items[0][price_data][recurring][interval]", billingCycle == "annual" ? "year" : "month"),
                new("line_items[0][price_data][product_data][name]", $"Reqara {plan.Name}"),
                new("line_items[0][price_data][product_data][metadata][plan_code]", plan.Code)
            };

            if (!string.IsNullOrWhiteSpace(currentBilling.StripeCustomerId))
            {
                values.Add(new("customer", currentBilling.StripeCustomerId));
            }
            else if (!string.IsNullOrWhiteSpace(userEmail))
            {
                values.Add(new("customer_email", userEmail));
            }

            var stripeResponse = await PostStripeFormAsync(
                httpClientFactory,
                stripe.SecretKey,
                "checkout/sessions",
                values,
                cancellationToken);
            if (stripeResponse.Problem is not null)
            {
                return stripeResponse.Problem;
            }

            using var document = JsonDocument.Parse(stripeResponse.Body!);
            var root = document.RootElement;
            var sessionId = GetString(root, "id");
            var checkoutUrl = GetString(root, "url");
            var expiresAt = GetUnixTime(root, "expires_at");
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(checkoutUrl))
            {
                return EndpointHelpers.Problem("stripe_response_invalid", "Stripe did not return a usable checkout session.", StatusCodes.Status502BadGateway);
            }

            return Results.Created($"/v1/billing/checkout-sessions/{sessionId}", new StripeCheckoutSessionResponse(
                sessionId,
                checkoutUrl,
                expiresAt));
        });

        group.MapGet("/checkout-sessions/{sessionId}", async (
            string sessionId,
            AtlasDbContext dbContext,
            IAtlasClock clock,
            IHttpClientFactory httpClientFactory,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (tenantContext.ActorType != "user")
            {
                return EndpointHelpers.Problem("dashboard_user_required", "A dashboard user session is required to verify billing checkout.", StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(sessionId) || !sessionId.StartsWith("cs_", StringComparison.Ordinal))
            {
                return EndpointHelpers.Problem("validation_failed", "sessionId must be a Stripe Checkout Session ID.", StatusCodes.Status422UnprocessableEntity);
            }

            var stripe = await ReadStripeSettingsAsync(dbContext, cancellationToken);
            if (!stripe.Enabled || string.IsNullOrWhiteSpace(stripe.SecretKey))
            {
                return EndpointHelpers.Problem("billing_not_configured", "Stripe billing is not configured.", StatusCodes.Status503ServiceUnavailable);
            }

            var stripeResponse = await GetStripeAsync(
                httpClientFactory,
                stripe.SecretKey,
                $"checkout/sessions/{Uri.EscapeDataString(sessionId)}",
                [
                    new("expand[]", "subscription"),
                    new("expand[]", "customer"),
                    new("expand[]", "line_items")
                ],
                cancellationToken);
            if (stripeResponse.Problem is not null)
            {
                return stripeResponse.Problem;
            }

            using var document = JsonDocument.Parse(stripeResponse.Body!);
            var session = document.RootElement.Clone();
            var sessionOrganizationId = GetSessionOrganizationId(session);
            if (sessionOrganizationId != organizationId)
            {
                return EndpointHelpers.Problem("checkout_session_not_found", "Checkout session was not found for this organization.", StatusCodes.Status404NotFound);
            }

            var paymentStatus = GetString(session, "payment_status");
            var status = GetString(session, "status");
            var mode = GetString(session, "mode");
            OrganizationBillingState billing;
            if (string.Equals(mode, "subscription", StringComparison.OrdinalIgnoreCase)
                && string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase)
                && string.Equals(paymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            {
                billing = await ApplyCheckoutSessionAsync(dbContext, clock, session, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                billing = await ReadOrganizationBillingStateAsync(dbContext, organizationId, cancellationToken);
            }

            return Results.Ok(new StripeCheckoutSessionStatusResponse(
                sessionId,
                status,
                paymentStatus,
                mode,
                GetMetadataString(session, "plan_code"),
                NormalizeBillingCycle(GetMetadataString(session, "billing_cycle")),
                GetLong(session, "amount_total"),
                GetString(session, "currency")?.ToUpperInvariant(),
                GetIdOrString(session, "customer"),
                GetIdOrString(session, "subscription"),
                GetNestedObjectString(session, "subscription", "status"),
                billing));
        });

        group.MapPost("/customer-portal-sessions", async (
            CreateStripeCustomerPortalSessionRequest request,
            AtlasDbContext dbContext,
            IAdminSettingService adminSettings,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (tenantContext.ActorType != "user")
            {
                return EndpointHelpers.Problem("dashboard_user_required", "A dashboard user session is required to manage billing.", StatusCodes.Status403Forbidden);
            }

            var stripe = await ReadStripeSettingsAsync(dbContext, cancellationToken);
            if (!stripe.Enabled || string.IsNullOrWhiteSpace(stripe.SecretKey))
            {
                return EndpointHelpers.Problem("billing_not_configured", "Stripe billing is not configured.", StatusCodes.Status503ServiceUnavailable);
            }

            var billing = await ReadOrganizationBillingStateAsync(dbContext, organizationId, cancellationToken);
            if (string.IsNullOrWhiteSpace(billing.StripeCustomerId))
            {
                return EndpointHelpers.Problem("stripe_customer_missing", "This organization does not have a Stripe customer yet.", StatusCodes.Status409Conflict);
            }

            var appBaseUrl = await EndpointHelpers.BuildAppBaseUrlAsync(adminSettings, configuration, organizationId, httpContext, cancellationToken);
            var returnUrl = NormalizeReturnUrl(request.ReturnUrl)
                ?? await ReadSettingStringAsync(dbContext, null, StripeCategory, "CustomerPortalReturnUrl", cancellationToken)
                ?? $"{appBaseUrl.TrimEnd('/')}/dashboard/billing";
            var urlProblem = ValidateFrontendReturnUrl(returnUrl, "returnUrl");
            if (urlProblem is not null)
            {
                return urlProblem;
            }

            var stripeResponse = await PostStripeFormAsync(
                httpClientFactory,
                stripe.SecretKey,
                "billing_portal/sessions",
                [
                    new("customer", billing.StripeCustomerId),
                    new("return_url", returnUrl)
                ],
                cancellationToken);
            if (stripeResponse.Problem is not null)
            {
                return stripeResponse.Problem;
            }

            using var document = JsonDocument.Parse(stripeResponse.Body!);
            var portalUrl = GetString(document.RootElement, "url");
            if (string.IsNullOrWhiteSpace(portalUrl))
            {
                return EndpointHelpers.Problem("stripe_response_invalid", "Stripe did not return a usable customer portal URL.", StatusCodes.Status502BadGateway);
            }

            return Results.Ok(new StripeCustomerPortalSessionResponse(portalUrl));
        });

        group.MapPost("/webhook/stripe", async (
            AtlasDbContext dbContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var stripe = await ReadStripeSettingsAsync(dbContext, cancellationToken);
            if (string.IsNullOrWhiteSpace(stripe.WebhookSigningSecret))
            {
                return EndpointHelpers.Problem("stripe_webhook_not_configured", "Stripe webhook signing secret is not configured.", StatusCodes.Status503ServiceUnavailable);
            }

            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            var payload = await reader.ReadToEndAsync(cancellationToken);
            if (!VerifyStripeSignature(
                payload,
                httpContext.Request.Headers["Stripe-Signature"].FirstOrDefault(),
                stripe.WebhookSigningSecret,
                clock.UtcNow,
                out var signatureProblem))
            {
                return signatureProblem!;
            }

            StripeWebhookEvent stripeEvent;
            try
            {
                stripeEvent = ParseStripeWebhookEvent(payload);
            }
            catch (JsonException)
            {
                return EndpointHelpers.Problem("invalid_webhook_payload", "Stripe webhook payload must be valid JSON.", StatusCodes.Status400BadRequest);
            }

            var processed = await ProcessStripeWebhookEventAsync(dbContext, clock, stripeEvent, cancellationToken);
            return Results.Ok(new StripeWebhookResponse(stripeEvent.Id, stripeEvent.Type, processed));
        });
    }

    private static async Task<StripeSettings> ReadStripeSettingsAsync(
        AtlasDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var publishableKey = await ReadSettingStringAsync(dbContext, null, StripeCategory, "PublishableKey", cancellationToken)
            ?? await ReadSettingStringAsync(dbContext, null, StripeCategory, "publishableKey", cancellationToken);
        var secretKey = await ReadSettingStringAsync(dbContext, null, StripeCategory, "SecretKey", cancellationToken)
            ?? await ReadSettingStringAsync(dbContext, null, StripeCategory, "secretKey", cancellationToken);
        var webhookSigningSecret = await ReadSettingStringAsync(dbContext, null, StripeCategory, "WebhookSigningSecret", cancellationToken)
            ?? await ReadSettingStringAsync(dbContext, null, StripeCategory, "webhookSigningSecret", cancellationToken);
        var enabledJson = await ReadRawSettingJsonAsync(dbContext, null, StripeCategory, "Enabled", cancellationToken)
            ?? await ReadRawSettingJsonAsync(dbContext, null, StripeCategory, "enabled", cancellationToken);
        var enabled = EndpointHelpers.ReadBoolSetting(enabledJson, !string.IsNullOrWhiteSpace(publishableKey) && !string.IsNullOrWhiteSpace(secretKey));

        return new StripeSettings(
            enabled,
            publishableKey,
            secretKey,
            webhookSigningSecret);
    }

    private static async Task<string?> ReadSettingStringAsync(
        AtlasDbContext dbContext,
        Guid? organizationId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        return EndpointHelpers.ReadStringSetting(await ReadRawSettingJsonAsync(dbContext, organizationId, category, key, cancellationToken));
    }

    private static async Task<string?> ReadRawSettingJsonAsync(
        AtlasDbContext dbContext,
        Guid? organizationId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        if (organizationId.HasValue)
        {
            var organizationValue = await dbContext.AdminSettings.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.OrganizationId == organizationId.Value
                    && item.Category == category
                    && item.Key == key)
                .Select(item => item.ValueJson)
                .FirstOrDefaultAsync(cancellationToken);
            if (organizationValue is not null)
            {
                return organizationValue;
            }
        }

        return await dbContext.AdminSettings.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.OrganizationId == null
                && item.Category == category
                && item.Key == key)
            .Select(item => item.ValueJson)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<OrganizationBillingState> ReadOrganizationBillingStateAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var raw = await ReadRawSettingJsonAsync(dbContext, organizationId, "billing", "plan", cancellationToken)
            ?? await ReadRawSettingJsonAsync(dbContext, organizationId, "billing", "planCode", cancellationToken);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return new OrganizationBillingState("free", "monthly", "active", null, null);
        }

        if (EndpointHelpers.ReadStringSetting(raw) is { } planCode
            && !raw.TrimStart().StartsWith('{'))
        {
            return new OrganizationBillingState(planCode, "monthly", "active", null, null);
        }

        try
        {
            return JsonSerializer.Deserialize<OrganizationBillingState>(raw, EndpointHelpers.JsonOptions)
                ?? new OrganizationBillingState("free", "monthly", "active", null, null);
        }
        catch (JsonException)
        {
            return new OrganizationBillingState("free", "monthly", "active", null, null);
        }
    }

    private static async Task UpsertOrganizationBillingStateAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        Guid organizationId,
        OrganizationBillingState state,
        CancellationToken cancellationToken)
    {
        var setting = await dbContext.AdminSettings.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.OrganizationId == organizationId
                && item.Category == "billing"
                && item.Key == "plan",
                cancellationToken);

        if (setting is null)
        {
            setting = new AdminSetting
            {
                OrganizationId = organizationId,
                Scope = AdminSettingScope.Organization,
                Category = "billing",
                Key = "plan",
                CreatedAt = clock.UtcNow
            };
            dbContext.AdminSettings.Add(setting);
        }

        setting.Scope = AdminSettingScope.Organization;
        setting.ValueJson = JsonSerializer.Serialize(state, EndpointHelpers.JsonOptions);
        setting.IsSecret = false;
        setting.UpdatedAt = clock.UtcNow;
    }

    private static async Task<string?> ResolveBillingEmailAsync(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (userId.HasValue)
        {
            var email = await dbContext.Users.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(item => item.Id == userId.Value)
                .Select(item => item.Email)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(email))
            {
                return email;
            }
        }

        return await dbContext.OrganizationUsers.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.User)
            .Where(item => item.OrganizationId == organizationId && item.Status == MembershipStatus.Active)
            .OrderBy(item => item.Role == OrganizationUserRole.Owner ? 0 : 1)
            .ThenBy(item => item.CreatedAt)
            .Select(item => item.User!.Email)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task<StripeFormResponse> PostStripeFormAsync(
        IHttpClientFactory httpClientFactory,
        string secretKey,
        string path,
        IReadOnlyList<KeyValuePair<string, string>> values,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{StripeApiBaseUrl}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Headers.Add("Stripe-Version", "2026-02-25.clover");
        request.Content = new FormUrlEncodedContent(values);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new StripeFormResponse(body, null);
        }

        var message = ReadStripeErrorMessage(body) ?? "Stripe request failed.";
        return new StripeFormResponse(
            null,
            EndpointHelpers.Problem(
                "stripe_request_failed",
                message,
                StatusCodes.Status502BadGateway,
                $"Stripe returned HTTP {(int)response.StatusCode}."));
    }

    private static async Task<StripeFormResponse> GetStripeAsync(
        IHttpClientFactory httpClientFactory,
        string secretKey,
        string path,
        IReadOnlyList<KeyValuePair<string, string>> query,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var queryString = query.Count == 0
            ? string.Empty
            : "?" + string.Join("&", query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{StripeApiBaseUrl}/{path}{queryString}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secretKey);
        request.Headers.Add("Stripe-Version", "2026-02-25.clover");

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new StripeFormResponse(body, null);
        }

        var message = ReadStripeErrorMessage(body) ?? "Stripe request failed.";
        return new StripeFormResponse(
            null,
            EndpointHelpers.Problem(
                "stripe_request_failed",
                message,
                StatusCodes.Status502BadGateway,
                $"Stripe returned HTTP {(int)response.StatusCode}."));
    }

    private static string? ReadStripeErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                ? GetString(error, "message")
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool VerifyStripeSignature(
        string payload,
        string? signatureHeader,
        string signingSecret,
        DateTimeOffset now,
        out IResult? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            problem = EndpointHelpers.Problem("stripe_signature_missing", "Stripe-Signature header is required.", StatusCodes.Status400BadRequest);
            return false;
        }

        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = part[..separator];
            var value = part[(separator + 1)..];
            if (key == "t" && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }
            else if (key == "v1")
            {
                signatures.Add(value);
            }
        }

        if (timestamp is null || signatures.Count == 0)
        {
            problem = EndpointHelpers.Problem("stripe_signature_invalid", "Stripe signature header is invalid.", StatusCodes.Status400BadRequest);
            return false;
        }

        var signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (Math.Abs((now - signedAt).TotalMinutes) > 5)
        {
            problem = EndpointHelpers.Problem("stripe_signature_expired", "Stripe signature timestamp is outside the allowed tolerance.", StatusCodes.Status400BadRequest);
            return false;
        }

        var signedPayload = $"{timestamp}.{payload}";
        var expectedBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(signingSecret),
            Encoding.UTF8.GetBytes(signedPayload));
        var expectedHex = Convert.ToHexString(expectedBytes).ToLowerInvariant();
        var expectedHexBytes = Encoding.UTF8.GetBytes(expectedHex);

        foreach (var signature in signatures)
        {
            var signatureBytes = Encoding.UTF8.GetBytes(signature.ToLowerInvariant());
            if (signatureBytes.Length == expectedHexBytes.Length
                && CryptographicOperations.FixedTimeEquals(signatureBytes, expectedHexBytes))
            {
                return true;
            }
        }

        problem = EndpointHelpers.Problem("stripe_signature_invalid", "Stripe webhook signature is invalid.", StatusCodes.Status400BadRequest);
        return false;
    }

    private static StripeWebhookEvent ParseStripeWebhookEvent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement.Clone();
        var id = GetString(root, "id") ?? string.Empty;
        var type = GetString(root, "type") ?? string.Empty;
        var dataObject = root.TryGetProperty("data", out var data)
            && data.TryGetProperty("object", out var nested)
            ? nested.Clone()
            : default;

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(type) || dataObject.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Stripe event is missing id, type or data.object.");
        }

        return new StripeWebhookEvent(id, type, dataObject);
    }

    private static async Task<bool> ProcessStripeWebhookEventAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        StripeWebhookEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await ApplyCheckoutSessionCompletedAsync(dbContext, clock, stripeEvent, cancellationToken);
                return true;

            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                await ApplySubscriptionEventAsync(dbContext, clock, stripeEvent, cancellationToken);
                return true;

            case "invoice.payment_succeeded":
                await ApplyInvoicePaymentSucceededAsync(dbContext, clock, stripeEvent, cancellationToken);
                return true;

            case "invoice.payment_failed":
                await ApplyInvoicePaymentFailedAsync(dbContext, clock, stripeEvent, cancellationToken);
                return true;

            case "charge.refunded":
                await ApplyChargeRefundedAsync(dbContext, clock, stripeEvent, cancellationToken);
                return true;

            case "payment_intent.succeeded":
            case "payment_intent.payment_failed":
            case "invoice.finalized":
            case "invoice.paid":
            case "customer.created":
            case "customer.updated":
            case "customer.deleted":
                return false;

            default:
                return false;
        }
    }

    private static async Task ApplyCheckoutSessionCompletedAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        StripeWebhookEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        await ApplyCheckoutSessionAsync(dbContext, clock, stripeEvent.Object, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<OrganizationBillingState> ApplyCheckoutSessionAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        JsonElement session,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(GetString(session, "mode"), "subscription", StringComparison.OrdinalIgnoreCase))
        {
            return new OrganizationBillingState("free", "monthly", "active", null, null);
        }

        var customerId = GetIdOrString(session, "customer");
        var subscriptionId = GetIdOrString(session, "subscription");
        var organizationId = GetSessionOrganizationId(session)
            ?? await FindOrganizationIdByStripeIdsAsync(
                dbContext,
                customerId,
                subscriptionId,
                cancellationToken);
        if (organizationId is null)
        {
            return new OrganizationBillingState("free", "monthly", "active", null, null);
        }

        var current = await ReadOrganizationBillingStateAsync(dbContext, organizationId.Value, cancellationToken);
        var subscriptionStatus = GetNestedObjectString(session, "subscription", "status");
        var isPaid = string.Equals(GetString(session, "payment_status"), "paid", StringComparison.OrdinalIgnoreCase);
        var state = current with
        {
            PlanCode = GetMetadataString(session, "plan_code") ?? current.PlanCode,
            BillingCycle = NormalizeBillingCycle(GetMetadataString(session, "billing_cycle") ?? current.BillingCycle),
            Status = isPaid ? NormalizeStripeBillingStatus(subscriptionStatus) : "incomplete",
            CurrentPeriodStart = GetNestedObjectUnixTime(session, "subscription", "current_period_start") ?? current.CurrentPeriodStart,
            CurrentPeriodEnd = GetNestedObjectUnixTime(session, "subscription", "current_period_end") ?? current.CurrentPeriodEnd,
            Provider = "stripe",
            StripeCustomerId = customerId ?? current.StripeCustomerId,
            StripeSubscriptionId = subscriptionId ?? current.StripeSubscriptionId,
            CancelAtPeriodEnd = GetNestedObjectBoolean(session, "subscription", "cancel_at_period_end") ?? current.CancelAtPeriodEnd
        };

        await UpsertOrganizationBillingStateAsync(dbContext, clock, organizationId.Value, state, cancellationToken);
        return state;
    }

    private static async Task ApplySubscriptionEventAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        StripeWebhookEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        var subscription = stripeEvent.Object;
        var subscriptionId = GetString(subscription, "id");
        var customerId = GetString(subscription, "customer");
        var organizationId = GetMetadataGuid(subscription, "organization_id")
            ?? await FindOrganizationIdByStripeIdsAsync(dbContext, customerId, subscriptionId, cancellationToken);
        if (organizationId is null)
        {
            return;
        }

        var current = await ReadOrganizationBillingStateAsync(dbContext, organizationId.Value, cancellationToken);
        var stripeStatus = stripeEvent.Type == "customer.subscription.deleted"
            ? "canceled"
            : GetString(subscription, "status") ?? current.Status;
        var nextPlanCode = stripeEvent.Type == "customer.subscription.deleted"
            ? "free"
            : GetMetadataString(subscription, "plan_code") ?? current.PlanCode;
        var nextBillingCycle = stripeEvent.Type == "customer.subscription.deleted"
            ? current.BillingCycle
            : NormalizeBillingCycle(GetMetadataString(subscription, "billing_cycle") ?? current.BillingCycle);

        var state = current with
        {
            PlanCode = nextPlanCode,
            BillingCycle = nextBillingCycle,
            Status = stripeStatus.Trim().ToLowerInvariant(),
            CurrentPeriodStart = stripeEvent.Type == "customer.subscription.deleted" ? null : GetUnixTime(subscription, "current_period_start") ?? current.CurrentPeriodStart,
            CurrentPeriodEnd = stripeEvent.Type == "customer.subscription.deleted" ? null : GetUnixTime(subscription, "current_period_end") ?? current.CurrentPeriodEnd,
            Provider = "stripe",
            StripeCustomerId = customerId ?? current.StripeCustomerId,
            StripeSubscriptionId = subscriptionId ?? current.StripeSubscriptionId,
            CancelAtPeriodEnd = GetBoolean(subscription, "cancel_at_period_end") ?? current.CancelAtPeriodEnd
        };

        await UpsertOrganizationBillingStateAsync(dbContext, clock, organizationId.Value, state, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ApplyInvoicePaymentSucceededAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        StripeWebhookEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Object;
        var subscriptionId = GetString(invoice, "subscription");
        var customerId = GetString(invoice, "customer");
        var organizationId = GetMetadataGuid(invoice, "organization_id")
            ?? await FindOrganizationIdByStripeIdsAsync(dbContext, customerId, subscriptionId, cancellationToken);
        if (organizationId is null)
        {
            return;
        }

        var current = await ReadOrganizationBillingStateAsync(dbContext, organizationId.Value, cancellationToken);
        var state = current with
        {
            Status = "active",
            Provider = "stripe",
            StripeCustomerId = customerId ?? current.StripeCustomerId,
            StripeSubscriptionId = subscriptionId ?? current.StripeSubscriptionId
        };
        await UpsertOrganizationBillingStateAsync(dbContext, clock, organizationId.Value, state, cancellationToken);

        var amountPaid = GetLong(invoice, "amount_paid").GetValueOrDefault();
        if (amountPaid > 0)
        {
            await AddRevenueEventIfMissingAsync(
                dbContext,
                clock,
                stripeEvent.Id,
                organizationId.Value,
                PlatformRevenueEventType.Subscription,
                amountPaid / 100m,
                GetString(invoice, "currency")?.ToUpperInvariant() ?? "USD",
                GetString(invoice, "id"),
                GetUnixTime(invoice, "period_start"),
                GetUnixTime(invoice, "period_end"),
                stripeEvent.Type,
                invoice,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ApplyInvoicePaymentFailedAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        StripeWebhookEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        var invoice = stripeEvent.Object;
        var subscriptionId = GetString(invoice, "subscription");
        var customerId = GetString(invoice, "customer");
        var organizationId = GetMetadataGuid(invoice, "organization_id")
            ?? await FindOrganizationIdByStripeIdsAsync(dbContext, customerId, subscriptionId, cancellationToken);
        if (organizationId is null)
        {
            return;
        }

        var current = await ReadOrganizationBillingStateAsync(dbContext, organizationId.Value, cancellationToken);
        var state = current with
        {
            Status = "past_due",
            Provider = "stripe",
            StripeCustomerId = customerId ?? current.StripeCustomerId,
            StripeSubscriptionId = subscriptionId ?? current.StripeSubscriptionId
        };

        await UpsertOrganizationBillingStateAsync(dbContext, clock, organizationId.Value, state, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task ApplyChargeRefundedAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        StripeWebhookEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        var charge = stripeEvent.Object;
        var customerId = GetString(charge, "customer");
        var organizationId = GetMetadataGuid(charge, "organization_id")
            ?? await FindOrganizationIdByStripeIdsAsync(dbContext, customerId, null, cancellationToken);
        if (organizationId is null)
        {
            return;
        }

        var amountRefunded = GetLong(charge, "amount_refunded").GetValueOrDefault();
        if (amountRefunded <= 0)
        {
            return;
        }

        await AddRevenueEventIfMissingAsync(
            dbContext,
            clock,
            stripeEvent.Id,
            organizationId.Value,
            PlatformRevenueEventType.Refund,
            -(amountRefunded / 100m),
            GetString(charge, "currency")?.ToUpperInvariant() ?? "USD",
            GetString(charge, "id"),
            null,
            null,
            stripeEvent.Type,
            charge,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task AddRevenueEventIfMissingAsync(
        AtlasDbContext dbContext,
        IAtlasClock clock,
        string stripeEventId,
        Guid organizationId,
        PlatformRevenueEventType type,
        decimal amount,
        string currency,
        string? providerObjectId,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        string eventType,
        JsonElement providerObject,
        CancellationToken cancellationToken)
    {
        var externalReference = $"stripe:{stripeEventId}";
        var exists = await dbContext.PlatformRevenueEvents.IgnoreQueryFilters()
            .AnyAsync(item => item.ExternalReference == externalReference, cancellationToken);
        if (exists)
        {
            return;
        }

        dbContext.PlatformRevenueEvents.Add(new PlatformRevenueEvent
        {
            OrganizationId = organizationId,
            Type = type,
            Amount = amount,
            Currency = currency.Length == 3 ? currency : "USD",
            Source = "stripe",
            ExternalReference = externalReference,
            OccurredAt = clock.UtcNow,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            MetadataJson = JsonSerializer.Serialize(new
            {
                stripeEventId,
                eventType,
                providerObjectId,
                stripeObjectType = GetString(providerObject, "object")
            }, EndpointHelpers.JsonOptions)
        });
    }

    private static async Task<Guid?> FindOrganizationIdByStripeIdsAsync(
        AtlasDbContext dbContext,
        string? customerId,
        string? subscriptionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId) && string.IsNullOrWhiteSpace(subscriptionId))
        {
            return null;
        }

        var settings = await dbContext.AdminSettings.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.OrganizationId.HasValue
                && item.Category == "billing"
                && item.Key == "plan")
            .ToListAsync(cancellationToken);

        foreach (var setting in settings)
        {
            OrganizationBillingState? state;
            try
            {
                state = JsonSerializer.Deserialize<OrganizationBillingState>(setting.ValueJson, EndpointHelpers.JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (state is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(customerId)
                && string.Equals(state.StripeCustomerId, customerId, StringComparison.Ordinal))
            {
                return setting.OrganizationId;
            }

            if (!string.IsNullOrWhiteSpace(subscriptionId)
                && string.Equals(state.StripeSubscriptionId, subscriptionId, StringComparison.Ordinal))
            {
                return setting.OrganizationId;
            }
        }

        return null;
    }

    private static string NormalizePlanCode(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static string NormalizeBillingCycle(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "annual" or "yearly" or "year"
            ? "annual"
            : "monthly";
    }

    private static string NormalizeStripeBillingStatus(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "active" or "trialing" or "past_due" or "unpaid" or "canceled" or "incomplete"
            ? normalized
            : "active";
    }

    private static bool IsSameActiveSubscription(
        OrganizationBillingState currentBilling,
        string requestedPlanCode,
        string requestedBillingCycle)
    {
        return IsActiveSubscriptionStatus(currentBilling.Status)
            && string.Equals(currentBilling.Provider, "stripe", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(currentBilling.StripeCustomerId)
            && !string.IsNullOrWhiteSpace(currentBilling.StripeSubscriptionId)
            && string.Equals(currentBilling.PlanCode, requestedPlanCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeBillingCycle(currentBilling.BillingCycle), requestedBillingCycle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActiveSubscriptionStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() is "active" or "trialing" or "past_due";
    }

    private static string? NormalizeReturnUrl(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeCheckoutSuccessUrl(string? value)
    {
        var url = NormalizeReturnUrl(value);
        if (url is null || url.Contains("{CHECKOUT_SESSION_ID}", StringComparison.Ordinal))
        {
            return url;
        }

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}session_id={{CHECKOUT_SESSION_ID}}";
    }

    private static IResult? ValidateFrontendReturnUrl(string value, string fieldName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return EndpointHelpers.Problem("validation_failed", $"{fieldName} must be an absolute URL.", StatusCodes.Status422UnprocessableEntity);
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttp
            && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return EndpointHelpers.Problem("validation_failed", $"{fieldName} must use HTTPS outside localhost.", StatusCodes.Status422UnprocessableEntity);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? GetIdOrString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Object
            ? GetString(value, "id")
            : GetString(element, propertyName);
    }

    private static string? GetNestedObjectString(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
                ? GetString(nested, nestedPropertyName)
                : null;
    }

    private static bool? GetNestedObjectBoolean(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
                ? GetBoolean(nested, nestedPropertyName)
                : null;
    }

    private static bool? GetBoolean(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
    }

    private static DateTimeOffset? GetUnixTime(JsonElement element, string propertyName)
    {
        var value = GetLong(element, propertyName);
        return value.HasValue ? DateTimeOffset.FromUnixTimeSeconds(value.Value) : null;
    }

    private static DateTimeOffset? GetNestedObjectUnixTime(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
                ? GetUnixTime(nested, nestedPropertyName)
                : null;
    }

    private static string? GetMetadataString(JsonElement element, string key)
    {
        return GetMetadataStringAt(element, key)
            ?? GetNestedMetadataString(element, "subscription_details", key)
            ?? GetNestedMetadataString(element, "parent", "subscription_details", key)
            ?? GetFirstLineMetadataString(element, key);
    }

    private static string? GetMetadataStringAt(JsonElement element, string key)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("metadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string? GetNestedMetadataString(JsonElement element, string propertyName, string key)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var nested)
                ? GetMetadataStringAt(nested, key)
                : null;
    }

    private static string? GetNestedMetadataString(JsonElement element, string propertyName, string nestedPropertyName, string key)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var nested)
            && nested.ValueKind == JsonValueKind.Object
            && nested.TryGetProperty(nestedPropertyName, out var secondNested)
                ? GetMetadataStringAt(secondNested, key)
                : null;
    }

    private static string? GetFirstLineMetadataString(JsonElement element, string key)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("lines", out var lines)
            || lines.ValueKind != JsonValueKind.Object
            || !lines.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        return GetMetadataStringAt(data[0], key);
    }

    private static Guid? GetMetadataGuid(JsonElement element, string key)
    {
        return Guid.TryParse(GetMetadataString(element, key), out var parsed) ? parsed : null;
    }

    private static Guid? GetSessionOrganizationId(JsonElement session)
    {
        if (GetMetadataGuid(session, "organization_id") is { } metadataOrganizationId)
        {
            return metadataOrganizationId;
        }

        return Guid.TryParse(GetString(session, "client_reference_id"), out var parsed) ? parsed : null;
    }

    private sealed record StripeSettings(
        bool Enabled,
        string? PublishableKey,
        string? SecretKey,
        string? WebhookSigningSecret);

    private sealed record StripeFormResponse(string? Body, IResult? Problem);

    private sealed record StripeWebhookEvent(string Id, string Type, JsonElement Object);
}

public sealed record StripeBillingConfigResponse(
    string Provider,
    bool Enabled,
    string? PublishableKey,
    IReadOnlyList<string> SelfServePlans,
    string ContactSalesPlan);

public sealed record CreateStripeCheckoutSessionRequest(
    string PlanCode,
    string? BillingCycle,
    string? SuccessUrl,
    string? CancelUrl);

public sealed record StripeCheckoutSessionResponse(
    string SessionId,
    string CheckoutUrl,
    DateTimeOffset? ExpiresAt);

public sealed record StripeCheckoutSessionStatusResponse(
    string SessionId,
    string? Status,
    string? PaymentStatus,
    string? Mode,
    string? PlanCode,
    string BillingCycle,
    long? AmountTotal,
    string? Currency,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    string? StripeSubscriptionStatus,
    OrganizationBillingState Billing);

public sealed record CreateStripeCustomerPortalSessionRequest(string? ReturnUrl);

public sealed record StripeCustomerPortalSessionResponse(string PortalUrl);

public sealed record StripeWebhookResponse(string EventId, string EventType, bool Processed);
