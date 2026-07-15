using Atlas.Application.Abstractions;
using Atlas.Api.Endpoints;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Atlas.Api.Filters;

public static class TenantContextMiddleware
{
    private const string OrganizationHeader = "X-Atlas-Organization-Id";
    private const string ApiKeyHeader = "X-Atlas-Key";
    public const string RecipientSessionCookie = "__Host-atlas_recipient";

    public static IApplicationBuilder UseAtlasTenantContext(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var accessor = context.RequestServices.GetRequiredService<ITenantContextAccessor>();
            var dbContext = context.RequestServices.GetRequiredService<AtlasDbContext>();
            var secretHasher = context.RequestServices.GetRequiredService<ISecretHasher>();
            var clock = context.RequestServices.GetRequiredService<IAtlasClock>();
            accessor.ClearTenant();

            if (await TrySetApiTenantAsync(context, accessor, dbContext, secretHasher, clock, next))
            {
                return;
            }

            if (await TrySetUserTenantAsync(context, accessor, dbContext, clock, next))
            {
                return;
            }

            if (await TrySetRecipientTenantAsync(context, accessor, dbContext, secretHasher, clock, next))
            {
                return;
            }

            await next(context);
        });
    }

    private static async Task<bool> TrySetApiTenantAsync(
        HttpContext context,
        ITenantContextAccessor accessor,
        AtlasDbContext dbContext,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        RequestDelegate next)
    {
        if (!context.Request.Headers.TryGetValue(ApiKeyHeader, out var values))
        {
            return false;
        }

        var apiKey = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var prefix = ExtractApiKeyPrefix(apiKey);
        if (prefix is null)
        {
            return false;
        }

        var candidates = await dbContext.ApiKeys
            .IgnoreQueryFilters()
            .Where(key => key.KeyPrefix == prefix && key.RevokedAt == null)
            .ToListAsync(context.RequestAborted);

        var matched = candidates.FirstOrDefault(candidate =>
            (candidate.ExpiresAt is null || candidate.ExpiresAt > clock.UtcNow)
            && secretHasher.VerifySecret(apiKey, candidate.SecretHash));

        if (matched is null)
        {
            return false;
        }

        matched.LastUsedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(context.RequestAborted);

        accessor.SetApiTenant(matched.OrganizationId, matched.Id, matched.Scopes);
        await next(context);
        return true;
    }

    private static async Task<bool> TrySetUserTenantAsync(
        HttpContext context,
        ITenantContextAccessor accessor,
        AtlasDbContext dbContext,
        IAtlasClock clock,
        RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (context.User.FindFirstValue(PlatformEndpoints.ActorKindClaim) == "platform")
        {
            return false;
        }

        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return false;
        }

        var memberships = await dbContext.OrganizationUsers
            .IgnoreQueryFilters()
            .Where(membership => membership.UserId == userId
                && membership.Status == MembershipStatus.Active
                && membership.Organization != null
                && membership.Organization.Status == OrganizationStatus.Active
                && membership.Organization.DeletedAt == null)
            .ToListAsync(context.RequestAborted);

        if (memberships.Count == 0)
        {
            return false;
        }

        Guid? requestedOrganizationId = null;
        if (context.Request.Headers.TryGetValue(OrganizationHeader, out var values)
            && Guid.TryParse(values.FirstOrDefault(), out var parsedOrganizationId))
        {
            requestedOrganizationId = parsedOrganizationId;
        }

        var membership = requestedOrganizationId.HasValue
            ? memberships.FirstOrDefault(item => item.OrganizationId == requestedOrganizationId.Value)
            : memberships.OrderBy(item => item.CreatedAt).First();

        if (membership is null)
        {
            return false;
        }

        var user = await dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.Id == userId, context.RequestAborted);
        if (user is not null)
        {
            user.LastLoginAt ??= clock.UtcNow;
            await dbContext.SaveChangesAsync(context.RequestAborted);
        }

        accessor.SetUserTenant(
            membership.OrganizationId,
            userId,
            user?.Email ?? userId.ToString(),
            ScopesForRole(membership.Role));
        await next(context);
        return true;
    }

    private static async Task<bool> TrySetRecipientTenantAsync(
        HttpContext context,
        ITenantContextAccessor accessor,
        AtlasDbContext dbContext,
        ISecretHasher secretHasher,
        IAtlasClock clock,
        RequestDelegate next)
    {
        if (!context.Request.Cookies.TryGetValue(RecipientSessionCookie, out var sessionToken)
            || string.IsNullOrWhiteSpace(sessionToken))
        {
            return false;
        }

        var sessions = await dbContext.RecipientAccessSessions
            .IgnoreQueryFilters()
            .Include(session => session.ActionRecipient)
            .ThenInclude(recipient => recipient!.Action)
            .Where(session => session.RevokedAt == null && session.ExpiresAt > clock.UtcNow)
            .ToListAsync(context.RequestAborted);

        var session = sessions.FirstOrDefault(candidate =>
            secretHasher.VerifySecret(sessionToken, candidate.SessionTokenHash));

        if (session?.ActionRecipient?.Action is null
            || session.ActionRecipient.Action.DeletedAt is not null
            || session.ActionRecipient.Action.Status is ChecklistActionStatus.Cancelled or ChecklistActionStatus.Expired)
        {
            return false;
        }

        session.LastSeenAt = clock.UtcNow;
        session.ActionRecipient.LastActivityAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(context.RequestAborted);

        accessor.SetRecipientTenant(
            session.ActionRecipient.Action.OrganizationId,
            session.ActionRecipientId,
            session.ActionRecipient.Email);
        context.Items["AtlasRecipientSessionId"] = session.Id;
        context.Items["AtlasRecipientSessionOtpVerified"] = session.OtpVerified;

        await next(context);
        return true;
    }

    private static string? ExtractApiKeyPrefix(string apiKey)
    {
        const string livePrefix = "atl_live_";
        const string testPrefix = "atl_test_";
        var prefixLength = apiKey.StartsWith(livePrefix, StringComparison.Ordinal)
            ? livePrefix.Length
            : apiKey.StartsWith(testPrefix, StringComparison.Ordinal)
                ? testPrefix.Length
                : 0;

        if (prefixLength == 0)
        {
            return null;
        }

        var remaining = apiKey[prefixLength..];
        var separatorIndex = remaining.IndexOf('_', StringComparison.Ordinal);
        return separatorIndex <= 0 ? null : apiKey[..(prefixLength + separatorIndex)];
    }

    private static string[] ScopesForRole(OrganizationUserRole role)
    {
        return role switch
        {
            OrganizationUserRole.Owner => ["dashboard:*", "admin:*", "developer:*", "files:*"],
            OrganizationUserRole.Admin => ["dashboard:*", "admin:*", "developer:*", "files:*"],
            OrganizationUserRole.Member => ["dashboard:*", "files:*"],
            OrganizationUserRole.Viewer => ["dashboard:read", "files:read"],
            _ => []
        };
    }
}
