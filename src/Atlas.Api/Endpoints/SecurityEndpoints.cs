using System.Security.Claims;
using System.Text.Json;
using Atlas.Application.Abstractions;
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

        MapApiKeys(v1);
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

            var keys = await dbContext.ApiKeys.AsNoTracking()
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new ApiKeyMetadataResponse(
                    item.Id,
                    item.Name,
                    item.KeyPrefix,
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
            ISecretHasher secretHasher,
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

            if (string.IsNullOrWhiteSpace(request.Name) || request.Scopes.Count == 0)
            {
                return EndpointHelpers.Problem("validation_failed", "Name and at least one scope are required.", StatusCodes.Status422UnprocessableEntity);
            }

            var secret = EndpointHelpers.NewApiKey(out var keyPrefix);
            var apiKey = new ApiKey
            {
                OrganizationId = organizationId,
                Name = request.Name.Trim(),
                KeyPrefix = keyPrefix,
                SecretHash = secretHasher.HashSecret(secret),
                Scopes = request.Scopes.Select(scope => scope.Trim()).Where(scope => scope.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ExpiresAt = request.ExpiresAt,
                CreatedAt = clock.UtcNow
            };

            dbContext.ApiKeys.Add(apiKey);
            AddAudit(dbContext, organizationId, null, tenantContext, "api_key.created", new { apiKey.Id, apiKey.Name, apiKey.KeyPrefix }, httpContext);
            await dbContext.SaveChangesAsync(cancellationToken);

            return Results.Created($"/v1/api-keys/{apiKey.Id}", new ApiKeyCreatedResponse(
                apiKey.Id,
                apiKey.Name,
                apiKey.KeyPrefix,
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

public sealed record CreateApiKeyRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);

public sealed record ApiKeyCreatedResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    string Secret,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record ApiKeyMetadataResponse(
    Guid Id,
    string Name,
    string KeyPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt);
