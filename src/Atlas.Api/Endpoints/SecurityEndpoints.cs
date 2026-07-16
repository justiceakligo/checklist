using System.Security.Claims;
using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Api.Endpoints;

public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapAtlasSecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1");
        var auth = v1.MapGroup("/auth").WithTags("Auth & org");

        auth.MapPost("/login", async (
            LoginRequest request,
            AtlasDbContext dbContext,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return EndpointHelpers.Problem("validation_failed", "Email and password are required.", StatusCodes.Status422UnprocessableEntity);
            }

            var email = request.Email.Trim().ToLowerInvariant();
            var user = await dbContext.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Email == email, cancellationToken);
            if (user?.PasswordHash is null)
            {
                return EndpointHelpers.Problem("invalid_credentials", "Invalid email or password.", StatusCodes.Status401Unauthorized);
            }

            var passwordResult = new PasswordHasher<AppUser>().VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (passwordResult == PasswordVerificationResult.Failed)
            {
                return EndpointHelpers.Problem("invalid_credentials", "Invalid email or password.", StatusCodes.Status401Unauthorized);
            }

            var memberships = await dbContext.OrganizationUsers.IgnoreQueryFilters()
                .Include(item => item.Organization)
                .Where(item => item.UserId == user.Id
                    && item.Status == MembershipStatus.Active
                    && item.Organization != null
                    && item.Organization.Status == OrganizationStatus.Active
                    && item.Organization.DeletedAt == null)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            if (memberships.Count == 0)
            {
                return EndpointHelpers.Problem("no_active_membership", "User has no active organization membership.", StatusCodes.Status403Forbidden);
            }

            user.LastLoginAt = clock.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await SignInDashboardUserAsync(httpContext, user);

            return Results.Ok(new MeResponse(
                user.Id,
                user.Email,
                user.FullName,
                memberships.Select(ToOrganizationMembership).ToList(),
                request.OrganizationId ?? memberships[0].OrganizationId));
        });

        auth.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        v1.MapGet("/me", async (
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (tenantContext.UserId is null)
            {
                return EndpointHelpers.Problem("authentication_required", "Dashboard authentication is required.", StatusCodes.Status401Unauthorized);
            }

            var user = await dbContext.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == tenantContext.UserId.Value, cancellationToken);
            if (user is null)
            {
                await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return EndpointHelpers.Problem("authentication_required", "Dashboard authentication is required.", StatusCodes.Status401Unauthorized);
            }

            var memberships = await dbContext.OrganizationUsers.IgnoreQueryFilters()
                .Include(item => item.Organization)
                .Where(item => item.UserId == user.Id && item.Status == MembershipStatus.Active)
                .OrderBy(item => item.CreatedAt)
                .ToListAsync(cancellationToken);

            return Results.Ok(new MeResponse(
                user.Id,
                user.Email,
                user.FullName,
                memberships.Select(ToOrganizationMembership).ToList(),
                tenantContext.OrganizationId));
        }).WithTags("Auth & org");

        MapDeveloperReference(v1);
        MapDeveloperAccess(v1);
        MapSandbox(v1);
        MapApiKeys(v1);
        MapWebhooks(v1);
        var developer = v1.MapGroup("/developer");
        MapApiKeys(developer);
        MapWebhooks(developer);
        return app;
    }

    private static void MapApiKeys(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/api-keys").WithTags("Developers");

        group.MapGet("", async (
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

            var keys = await dbContext.ApiKeys.AsNoTracking()
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new ApiKeyMetadataResponse(
                    item.Id,
                    item.Name,
                    item.KeyPrefix,
                    item.Environment,
                    item.Scopes,
                    item.LastUsedAt,
                    item.ExpiresAt,
                    item.RevokedAt,
                    item.CreatedAt))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { items = keys });
        });

        group.MapPost("", async (
            CreateApiKeyRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IAdminSettingService settings,
            ISecretHasher secretHasher,
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var environment = request.Environment ?? ApiKeyEnvironment.Sandbox;
            if (!Enum.IsDefined(environment))
            {
                return EndpointHelpers.Problem("validation_failed", "API key environment is invalid.", StatusCodes.Status422UnprocessableEntity);
            }

            if (environment == ApiKeyEnvironment.Production)
            {
                var productionAllowed = await CanUseProductionDeveloperAccessAsync(dbContext, entitlements, organizationId, clock.UtcNow, cancellationToken);
                if (!productionAllowed.Allowed)
                {
                    return productionAllowed.Problem!;
                }
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return EndpointHelpers.Problem("validation_failed", "Name is required.", StatusCodes.Status422UnprocessableEntity);
            }

            var secret = EndpointHelpers.NewApiKey(environment, out var keyPrefix);
            var apiKeyDefaultDays = EndpointHelpers.ReadPositiveIntSetting(
                (await settings.GetAsync(organizationId, "developer", "apiKeyDefaultDays", cancellationToken))?.ValueJson,
                180);
            var apiKey = new ApiKey
            {
                OrganizationId = organizationId,
                Name = request.Name.Trim(),
                KeyPrefix = keyPrefix,
                SecretHash = secretHasher.HashSecret(secret),
                Scopes = NormalizeApiKeyScopes(request.Scopes, environment),
                Environment = environment,
                ExpiresAt = request.ExpiresAt ?? clock.UtcNow.AddDays(apiKeyDefaultDays),
                CreatedAt = clock.UtcNow
            };

            dbContext.ApiKeys.Add(apiKey);
            AddAudit(dbContext, organizationId, null, tenantContext, "api_key.created", new { apiKey.Id, apiKey.Name, apiKey.KeyPrefix, apiKey.Environment }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/v1/api-keys/{apiKey.Id}", new ApiKeyCreatedResponse(
                apiKey.Id,
                apiKey.Name,
                apiKey.KeyPrefix,
                apiKey.Environment,
                secret,
                apiKey.Scopes,
                apiKey.ExpiresAt,
                apiKey.CreatedAt));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var apiKey = await dbContext.ApiKeys.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (apiKey is null)
            {
                return EndpointHelpers.Problem("not_found", "API key was not found.", StatusCodes.Status404NotFound);
            }

            apiKey.RevokedAt ??= clock.UtcNow;
            AddAudit(dbContext, organizationId, null, tenantContext, "api_key.revoked", new { apiKey.Id, apiKey.Name, apiKey.KeyPrefix }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapDeveloperAccess(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/developer/access").WithTags("Developers");

        group.MapGet("", async (
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            var organization = await dbContext.Organizations
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "api_and_webhooks", cancellationToken);
            return Results.Ok(ToDeveloperAccessResponse(organization, feature));
        });

        group.MapPost("/production-request", async (
            DeveloperProductionAccessRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            IAtlasClock clock,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var organization = await dbContext.Organizations.FirstOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
            if (organization is null)
            {
                return EndpointHelpers.Problem("not_found", "Organization was not found.", StatusCodes.Status404NotFound);
            }

            if (organization.DeveloperAccessStatus == DeveloperAccessStatus.ProductionApproved)
            {
                var approvedFeature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "api_and_webhooks", cancellationToken);
                return Results.Ok(ToDeveloperAccessResponse(organization, approvedFeature));
            }

            organization.DeveloperAccessStatus = DeveloperAccessStatus.ProductionRequested;
            organization.DeveloperProductionRequestedAt = clock.UtcNow;
            organization.DeveloperProductionRejectedAt = null;
            organization.DeveloperProductionNotes = request.Message?.Trim();
            AddAudit(dbContext, organizationId, null, tenantContext, "developer.production_access_requested", new { request.UseCase, request.ExpectedVolume }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            var feature = await entitlements.HasFeatureAsync(organizationId, clock.UtcNow, "api_and_webhooks", cancellationToken);
            return Results.Accepted($"/v1/developer/access", ToDeveloperAccessResponse(organization, feature));
        });
    }

    private static void MapSandbox(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/sandbox").WithTags("Sandbox");

        group.MapGet("/status", (ITenantContext tenantContext) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }
            if (tenantContext.ActorType == "api" && !EndpointHelpers.HasScope(tenantContext, "sandbox:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Sandbox scope is required.", StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
            {
                organizationId,
                environment = "sandbox",
                dataPersistence = "none",
                message = "Sandbox endpoints validate request shape and return realistic sample responses without touching production checklist or file data."
            });
        });

        group.MapGet("/templates", (ITenantContext tenantContext) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out _, out var problem))
            {
                return problem!;
            }
            if (tenantContext.ActorType == "api" && !EndpointHelpers.HasScope(tenantContext, "sandbox:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Sandbox scope is required.", StatusCodes.Status403Forbidden);
            }

            return Results.Ok(new
        {
            items = new[]
            {
                new
                {
                    id = "sandbox-template-vendor-insurance",
                    name = "Vendor Insurance Renewal",
                    category = "Property Management",
                    requirements = new[]
                    {
                        new { key = "insurance_certificate", type = "File", label = "Insurance certificate", required = true },
                        new { key = "expiry_date", type = "Date", label = "Policy expiry date", required = true },
                        new { key = "notes", type = "LongText", label = "Notes", required = false }
                    }
                }
            }
        });
        });

        group.MapPost("/checklists", (SandboxChecklistRequest request, ITenantContext tenantContext) =>
        {
            if (!EndpointHelpers.TryGetDashboardTenant(tenantContext, out var organizationId, out var problem))
            {
                return problem!;
            }
            if (tenantContext.ActorType == "api" && !EndpointHelpers.HasScope(tenantContext, "sandbox:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Sandbox scope is required.", StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.RecipientEmail))
            {
                return EndpointHelpers.Problem("validation_failed", "Title and recipient email are required.", StatusCodes.Status422UnprocessableEntity);
            }

            var id = Guid.NewGuid();
            return Results.Created($"/v1/sandbox/checklists/{id}", new SandboxChecklistResponse(
                id,
                organizationId,
                request.Title.Trim(),
                request.RecipientEmail.Trim().ToLowerInvariant(),
                "sandbox",
                "validated",
                "No production action, recipient token, email, or file record was created."));
        });
    }

    private static DeveloperAccessResponse ToDeveloperAccessResponse(
        Organization organization,
        EntitlementCheckResult? apiFeature)
    {
        var planAllowsProduction = apiFeature?.Allowed ?? true;
        return new DeveloperAccessResponse(
            organization.DeveloperAccessStatus,
            true,
            planAllowsProduction && organization.DeveloperAccessStatus == DeveloperAccessStatus.ProductionApproved,
            planAllowsProduction,
            organization.DeveloperProductionRequestedAt,
            organization.DeveloperProductionApprovedAt,
            organization.DeveloperProductionRejectedAt,
            organization.DeveloperProductionNotes);
    }

    private static void MapDeveloperReference(RouteGroupBuilder v1)
    {
        v1.MapGet("/developer/reference", (HttpContext httpContext) =>
        {
            var origin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
            return Results.Ok(new DeveloperReferenceResponse(
                $"{origin}/v1",
                $"{origin}/openapi/v1.json",
                new[]
                {
                    new DeveloperEndpointGroup(
                        "Authentication",
                        "Use an API key in X-Atlas-Key for server-to-server calls. Browser dashboard auth uses HttpOnly cookies.",
                        new[]
                        {
                            new DeveloperEndpoint("GET", "/v1/developer/access", "Read sandbox and production developer access state."),
                            new DeveloperEndpoint("POST", "/v1/developer/access/production-request", "Request platform approval for production API access."),
                            new DeveloperEndpoint("POST", "/v1/developer/api-keys", "Create a sandbox key immediately, or a production key after Business/Scale entitlement and platform approval."),
                            new DeveloperEndpoint("GET", "/v1/developer/api-keys", "List API key metadata. Secrets are never returned after creation."),
                            new DeveloperEndpoint("DELETE", "/v1/developer/api-keys/{id}", "Revoke an API key.")
                        }),
                    new DeveloperEndpointGroup(
                        "Sandbox",
                        "Validate integration payloads without creating production recipients, tokens, emails, files, or submissions.",
                        new[]
                        {
                            new DeveloperEndpoint("GET", "/v1/sandbox/status", "Read sandbox status."),
                            new DeveloperEndpoint("GET", "/v1/sandbox/templates", "List sample sandbox templates."),
                            new DeveloperEndpoint("POST", "/v1/sandbox/checklists", "Validate a sandbox checklist payload.")
                        }),
                    new DeveloperEndpointGroup(
                        "Webhooks",
                        "Manage outbound webhook endpoints. Delivery history is available per endpoint.",
                        new[]
                        {
                            new DeveloperEndpoint("POST", "/v1/developer/webhooks", "Create a webhook endpoint."),
                            new DeveloperEndpoint("GET", "/v1/developer/webhooks", "List webhook endpoints."),
                            new DeveloperEndpoint("PATCH", "/v1/developer/webhooks/{id}", "Update URL, events, or status."),
                            new DeveloperEndpoint("POST", "/v1/developer/webhooks/{id}/rotate-secret", "Rotate the signing secret and show it once."),
                            new DeveloperEndpoint("GET", "/v1/developer/webhooks/{id}/deliveries", "List delivery attempts.")
                        }),
                    new DeveloperEndpointGroup(
                        "Checklist API",
                        "API keys can create and send secure checklist requests when their scopes allow it.",
                        new[]
                        {
                            new DeveloperEndpoint("GET", "/v1/templates?query=insurance", "Search real workflow templates."),
                            new DeveloperEndpoint("POST", "/v1/actions", "Create a checklist request."),
                            new DeveloperEndpoint("POST", "/v1/actions/{id}/send", "Send or resend a checklist request."),
                            new DeveloperEndpoint("GET", "/v1/actions/{id}/timeline", "Read audit timeline events.")
                        })
                },
                new DeveloperCodeSamples(
                    """
                    curl -X POST "https://api.reqara.com/v1/sandbox/checklists" \
                      -H "Content-Type: application/json" \
                      -H "X-Atlas-Key: atl_test_xxxx_secret" \
                      -d '{"title":"Vendor Insurance Renewal","recipientEmail":"jamie@example.com","templateId":"sandbox-template-vendor-insurance","payload":{"insurance_certificate":"sample.pdf"}}'
                    """,
                    """
                    const res = await fetch("https://api.reqara.com/v1/sandbox/checklists", {
                      method: "POST",
                      headers: {
                        "Content-Type": "application/json",
                        "X-Atlas-Key": process.env.REQARA_SANDBOX_KEY
                      },
                      body: JSON.stringify(payload)
                    });
                    const data = await res.json();
                    """)));
        }).WithTags("Developers");
    }

    private static void MapWebhooks(RouteGroupBuilder v1)
    {
        var group = v1.MapGroup("/webhooks").WithTags("Developers");

        group.MapGet("", async (
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var webhooks = await dbContext.WebhookEndpoints.AsNoTracking()
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => ToWebhookResponse(item))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = webhooks });
        });

        group.MapPost("", async (
            CreateWebhookRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            ISecretHasher secretHasher,
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var productionAllowed = await CanUseProductionDeveloperAccessAsync(dbContext, entitlements, organizationId, clock.UtcNow, cancellationToken);
            if (!productionAllowed.Allowed)
            {
                return productionAllowed.Problem!;
            }

            var validation = ValidateWebhook(request.Url, request.EventTypes);
            if (validation is not null)
            {
                return validation;
            }

            var secret = EndpointHelpers.NewOpaqueToken();
            var webhook = new WebhookEndpoint
            {
                OrganizationId = organizationId,
                Url = request.Url.Trim(),
                SecretCiphertext = secretHasher.HashSecret(secret),
                EventTypes = NormalizeWebhookEvents(request.EventTypes),
                Status = request.Status ?? WebhookEndpointStatus.Active,
                CreatedAt = clock.UtcNow
            };
            dbContext.WebhookEndpoints.Add(webhook);
            AddAudit(dbContext, organizationId, null, tenantContext, "webhook.created", new { webhook.Id, webhook.Url, webhook.EventTypes }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/v1/webhooks/{webhook.Id}", new WebhookCreatedResponse(
                webhook.Id,
                webhook.Url,
                webhook.EventTypes,
                webhook.Status,
                secret,
                webhook.CreatedAt));
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var webhook = await dbContext.WebhookEndpoints.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            return webhook is null
                ? EndpointHelpers.Problem("not_found", "Webhook endpoint was not found.", StatusCodes.Status404NotFound)
                : Results.Ok(ToWebhookResponse(webhook));
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateWebhookRequest request,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var productionAllowed = await CanUseProductionDeveloperAccessAsync(dbContext, entitlements, organizationId, clock.UtcNow, cancellationToken);
            if (!productionAllowed.Allowed)
            {
                return productionAllowed.Problem!;
            }

            var webhook = await dbContext.WebhookEndpoints.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (webhook is null)
            {
                return EndpointHelpers.Problem("not_found", "Webhook endpoint was not found.", StatusCodes.Status404NotFound);
            }

            var nextUrl = string.IsNullOrWhiteSpace(request.Url) ? webhook.Url : request.Url.Trim();
            var nextEvents = request.EventTypes is { Count: > 0 } ? request.EventTypes : webhook.EventTypes;
            var validation = ValidateWebhook(nextUrl, nextEvents);
            if (validation is not null)
            {
                return validation;
            }

            webhook.Url = nextUrl;
            webhook.EventTypes = NormalizeWebhookEvents(nextEvents);
            webhook.Status = request.Status ?? webhook.Status;
            AddAudit(dbContext, organizationId, null, tenantContext, "webhook.updated", new { webhook.Id, webhook.Url, webhook.EventTypes, webhook.Status }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToWebhookResponse(webhook));
        });

        group.MapPost("/{id:guid}/rotate-secret", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
            IEntitlementService entitlements,
            ISecretHasher secretHasher,
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var productionAllowed = await CanUseProductionDeveloperAccessAsync(dbContext, entitlements, organizationId, clock.UtcNow, cancellationToken);
            if (!productionAllowed.Allowed)
            {
                return productionAllowed.Problem!;
            }

            var webhook = await dbContext.WebhookEndpoints.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (webhook is null)
            {
                return EndpointHelpers.Problem("not_found", "Webhook endpoint was not found.", StatusCodes.Status404NotFound);
            }

            var secret = EndpointHelpers.NewOpaqueToken();
            webhook.SecretCiphertext = secretHasher.HashSecret(secret);
            AddAudit(dbContext, organizationId, null, tenantContext, "webhook.secret_rotated", new { webhook.Id }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new WebhookSecretResponse(webhook.Id, secret));
        });

        group.MapDelete("/{id:guid}", async (
            Guid id,
            AtlasDbContext dbContext,
            ITenantContext tenantContext,
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var webhook = await dbContext.WebhookEndpoints.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (webhook is null)
            {
                return EndpointHelpers.Problem("not_found", "Webhook endpoint was not found.", StatusCodes.Status404NotFound);
            }

            webhook.Status = WebhookEndpointStatus.Disabled;
            AddAudit(dbContext, organizationId, null, tenantContext, "webhook.disabled", new { webhook.Id, webhook.Url }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/{id:guid}/deliveries", async (
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

            if (!EndpointHelpers.HasScope(tenantContext, "developer:*"))
            {
                return EndpointHelpers.Problem("forbidden", "Developer administration scope is required.", StatusCodes.Status403Forbidden);
            }

            var exists = await dbContext.WebhookEndpoints.AnyAsync(item => item.Id == id, cancellationToken);
            if (!exists)
            {
                return EndpointHelpers.Problem("not_found", "Webhook endpoint was not found.", StatusCodes.Status404NotFound);
            }

            var deliveries = await dbContext.WebhookDeliveries.AsNoTracking()
                .Where(item => item.WebhookEndpointId == id)
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new WebhookDeliveryResponse(
                    item.Id,
                    item.WebhookEndpointId,
                    item.EventId,
                    item.AttemptNumber,
                    item.Status,
                    item.ResponseStatus,
                    item.ResponseExcerpt,
                    item.NextAttemptAt,
                    item.CreatedAt,
                    item.CompletedAt))
                .ToListAsync(cancellationToken);
            return Results.Ok(new { items = deliveries });
        });
    }

    public static async Task SignInDashboardUserAsync(HttpContext httpContext, AppUser user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Email)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    private static WebhookResponse ToWebhookResponse(WebhookEndpoint webhook)
    {
        return new WebhookResponse(
            webhook.Id,
            webhook.Url,
            webhook.EventTypes,
            webhook.Status,
            webhook.CreatedAt);
    }

    private static async Task<ProductionDeveloperAccessCheck> CanUseProductionDeveloperAccessAsync(
        AtlasDbContext dbContext,
        IEntitlementService entitlements,
        Guid organizationId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var feature = await entitlements.HasFeatureAsync(organizationId, now, "api_and_webhooks", cancellationToken);
        if (!feature.Allowed)
        {
            return new ProductionDeveloperAccessCheck(false, EndpointHelpers.EntitlementProblem(feature));
        }

        var organization = await dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
        if (organization?.DeveloperAccessStatus != DeveloperAccessStatus.ProductionApproved)
        {
            return new ProductionDeveloperAccessCheck(
                false,
                EndpointHelpers.Problem(
                    "production_developer_access_required",
                    "Production developer access must be approved by platform admin before creating production API keys or webhooks.",
                    StatusCodes.Status403Forbidden));
        }

        return new ProductionDeveloperAccessCheck(true, null);
    }

    private static string[] NormalizeApiKeyScopes(
        IReadOnlyList<string>? requestedScopes,
        ApiKeyEnvironment environment)
    {
        string[] defaults = environment == ApiKeyEnvironment.Sandbox
            ? ["sandbox:*"]
            : ["templates:read", "actions:write", "files:write"];
        var source = requestedScopes is { Count: > 0 } ? requestedScopes : defaults;
        var scopes = source
            .Select(scope => scope.Trim())
            .Where(scope => scope.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (environment != ApiKeyEnvironment.Sandbox)
        {
            return scopes.Length == 0 ? defaults : scopes;
        }

        var sandboxScopes = scopes
            .Where(scope => scope.Equals("sandbox:*", StringComparison.OrdinalIgnoreCase)
                || scope.StartsWith("sandbox:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return sandboxScopes.Length == 0 ? defaults : sandboxScopes;
    }

    private static IResult? ValidateWebhook(string? url, IReadOnlyList<string>? eventTypes)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            return EndpointHelpers.Problem("validation_failed", "Webhook URL must be an absolute HTTPS URL.", StatusCodes.Status422UnprocessableEntity);
        }

        if (eventTypes is null || eventTypes.Count == 0 || NormalizeWebhookEvents(eventTypes).Length == 0)
        {
            return EndpointHelpers.Problem("validation_failed", "At least one webhook event type is required.", StatusCodes.Status422UnprocessableEntity);
        }

        return null;
    }

    private static string[] NormalizeWebhookEvents(IReadOnlyList<string> eventTypes)
    {
        return eventTypes
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record ProductionDeveloperAccessCheck(bool Allowed, IResult? Problem);

    public static void AddAudit(
        AtlasDbContext dbContext,
        Guid organizationId,
        Guid? actionId,
        ITenantContext tenantContext,
        string eventType,
        object eventData,
        HttpContext httpContext)
    {
        var actorType = tenantContext.ActorType switch
        {
            "user" => ActorType.User,
            "recipient" => ActorType.Recipient,
            "api" => ActorType.Api,
            _ => ActorType.System
        };

        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            ActionId = actionId,
            ActorType = actorType,
            ActorId = tenantContext.ActorId,
            EventType = eventType,
            EventData = JsonSerializer.Serialize(eventData, EndpointHelpers.JsonOptions),
            IpAddress = httpContext.Connection.RemoteIpAddress,
            UserAgent = httpContext.Request.Headers.UserAgent.FirstOrDefault(),
            CorrelationId = Guid.TryParse(httpContext.TraceIdentifier, out var traceId) ? traceId : null
        });
    }

    private static OrganizationMembershipResponse ToOrganizationMembership(OrganizationUser membership)
    {
        return new OrganizationMembershipResponse(
            membership.OrganizationId,
            membership.Organization?.Name ?? string.Empty,
            membership.Organization?.Slug ?? string.Empty,
            membership.Role,
            membership.Status);
    }
}

public sealed record LoginRequest(string Email, string Password, Guid? OrganizationId);

public sealed record MeResponse(
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<OrganizationMembershipResponse> Organizations,
    Guid? CurrentOrganizationId);

public sealed record OrganizationMembershipResponse(
    Guid OrganizationId,
    string Name,
    string Slug,
    OrganizationUserRole Role,
    MembershipStatus Status);

public sealed record CreateApiKeyRequest(
    string Name,
    IReadOnlyList<string>? Scopes,
    DateTimeOffset? ExpiresAt,
    ApiKeyEnvironment? Environment);

public sealed record ApiKeyCreatedResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    ApiKeyEnvironment Environment,
    string Secret,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record ApiKeyMetadataResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    ApiKeyEnvironment Environment,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt);

public sealed record DeveloperAccessResponse(
    DeveloperAccessStatus Status,
    bool SandboxAvailable,
    bool ProductionAvailable,
    bool PlanAllowsProduction,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? RejectedAt,
    string? Notes);

public sealed record DeveloperProductionAccessRequest(
    string? UseCase,
    string? ExpectedVolume,
    string? Message);

public sealed record SandboxChecklistRequest(
    string Title,
    string RecipientEmail,
    string? TemplateId,
    JsonElement? Payload);

public sealed record SandboxChecklistResponse(
    Guid Id,
    Guid OrganizationId,
    string Title,
    string RecipientEmail,
    string Environment,
    string Status,
    string Message);

public sealed record DeveloperReferenceResponse(
    string BaseUrl,
    string OpenApiUrl,
    IReadOnlyList<DeveloperEndpointGroup> EndpointGroups,
    DeveloperCodeSamples Samples);

public sealed record DeveloperEndpointGroup(
    string Name,
    string Description,
    IReadOnlyList<DeveloperEndpoint> Endpoints);

public sealed record DeveloperEndpoint(
    string Method,
    string Path,
    string Description);

public sealed record DeveloperCodeSamples(
    string Curl,
    string TypeScript);

public sealed record CreateWebhookRequest(
    string Url,
    IReadOnlyList<string> EventTypes,
    WebhookEndpointStatus? Status);

public sealed record UpdateWebhookRequest(
    string? Url,
    IReadOnlyList<string>? EventTypes,
    WebhookEndpointStatus? Status);

public sealed record WebhookResponse(
    Guid Id,
    string Url,
    IReadOnlyList<string> EventTypes,
    WebhookEndpointStatus Status,
    DateTimeOffset CreatedAt);

public sealed record WebhookCreatedResponse(
    Guid Id,
    string Url,
    IReadOnlyList<string> EventTypes,
    WebhookEndpointStatus Status,
    string Secret,
    DateTimeOffset CreatedAt);

public sealed record WebhookSecretResponse(Guid Id, string Secret);

public sealed record WebhookDeliveryResponse(
    Guid Id,
    Guid WebhookEndpointId,
    Guid EventId,
    int AttemptNumber,
    WebhookDeliveryStatus Status,
    int? ResponseStatus,
    string? ResponseExcerpt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);
