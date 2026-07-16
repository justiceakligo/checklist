using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Application.Billing;
using Atlas.Application.Settings;
using Atlas.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;

namespace Atlas.Api.Endpoints;

internal static class EndpointHelpers
{
    public static bool TryGetTenant(ITenantContext tenantContext, out Guid organizationId, out IResult? problem)
    {
        if (tenantContext.OrganizationId is { } tenantId)
        {
            organizationId = tenantId;
            problem = null;
            return true;
        }

        organizationId = Guid.Empty;
        problem = Problem("tenant_required", "An organization context is required.", StatusCodes.Status401Unauthorized);
        return false;
    }

    public static bool TryGetDashboardTenant(ITenantContext tenantContext, out Guid organizationId, out IResult? problem)
    {
        if (!TryGetTenant(tenantContext, out organizationId, out problem))
        {
            if (tenantContext.ActorType is null)
            {
                problem = Problem(
                    "authentication_required",
                    "Dashboard authentication or an API key is required. Browser calls must include credentials so the dashboard cookie is sent.",
                    StatusCodes.Status401Unauthorized);
            }

            return false;
        }

        if (tenantContext.ActorType is "user" or "api")
        {
            return true;
        }

        problem = Problem("forbidden", "Dashboard or API-key access is required.", StatusCodes.Status403Forbidden);
        return false;
    }

    public static bool TryGetRecipientTenant(ITenantContext tenantContext, out Guid organizationId, out Guid recipientId, out IResult? problem)
    {
        if (tenantContext.OrganizationId is { } tenantId && tenantContext.RecipientId is { } currentRecipientId)
        {
            organizationId = tenantId;
            recipientId = currentRecipientId;
            problem = null;
            return true;
        }

        organizationId = Guid.Empty;
        recipientId = Guid.Empty;
        problem = Problem("recipient_session_required", "A valid recipient session is required.", StatusCodes.Status401Unauthorized);
        return false;
    }

    public static bool HasScope(ITenantContext tenantContext, string scope)
    {
        return tenantContext.Scopes.Contains(scope)
            || tenantContext.Scopes.Contains("*")
            || tenantContext.Scopes.Contains("dashboard:*")
            || tenantContext.Scopes.Contains(scope[..scope.IndexOf(':', StringComparison.Ordinal)] + ":*");
    }

    public static bool IsSandboxApi(ITenantContext tenantContext)
    {
        return tenantContext.ActorType == "api" && tenantContext.Scopes.Contains("env:sandbox");
    }

    public static IResult? RequireProductionApiOrDashboard(ITenantContext tenantContext)
    {
        return IsSandboxApi(tenantContext)
            ? Problem(
                "sandbox_key_not_allowed",
                "Sandbox API keys cannot access production organization data. Use sandbox endpoints or request production API approval.",
                StatusCodes.Status403Forbidden)
            : null;
    }

    public static IResult Problem(string code, string title, int statusCode, string? detail = null)
    {
        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            type: $"https://docs.atlas.example/errors/{code}",
            extensions: new Dictionary<string, object?> { ["code"] = code });
    }

    public static IResult EntitlementProblem(EntitlementCheckResult result)
    {
        return Results.Problem(
            title: result.Message,
            statusCode: StatusCodes.Status402PaymentRequired,
            type: $"https://docs.atlas.example/errors/{result.Code}",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = result.Code,
                ["plan"] = result.Snapshot.Plan,
                ["billing"] = result.Snapshot.Billing,
                ["usage"] = result.Snapshot.Usage
            });
    }

    public static int NormalizePage(int? page)
    {
        return page.GetValueOrDefault(1) < 1 ? 1 : page.GetValueOrDefault(1);
    }

    public static int NormalizePageSize(int? pageSize, int fallback = 25, int max = 100)
    {
        var value = pageSize.GetValueOrDefault(fallback);
        if (value < 1)
        {
            return fallback;
        }

        return Math.Min(value, max);
    }

    public static int PageSkip(int page, int pageSize)
    {
        return (page - 1) * pageSize;
    }

    public static string JsonOrDefault(JsonElement? value, string defaultJson = "{}")
    {
        return value is { ValueKind: not JsonValueKind.Undefined }
            ? value.Value.GetRawText()
            : defaultJson;
    }

    public static string NewPublicReference()
    {
        return "act_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
    }

    public static string NewOpaqueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal);
    }

    public static string TokenLogPrefix(string token)
    {
        return string.IsNullOrEmpty(token)
            ? string.Empty
            : token[..Math.Min(6, token.Length)];
    }

    public static string NewApiKey(out string keyPrefix)
    {
        return NewApiKey(ApiKeyEnvironment.Production, out keyPrefix);
    }

    public static string NewApiKey(ApiKeyEnvironment environment, out string keyPrefix)
    {
        var prefix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var secret = NewOpaqueToken();
        keyPrefix = (environment == ApiKeyEnvironment.Sandbox ? "atl_test_" : "atl_live_") + prefix;
        return $"{keyPrefix}_{secret}";
    }

    public static string NewOtpCode()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        var value = BitConverter.ToUInt32(bytes) % 1_000_000;
        return value.ToString("D6");
    }

    public static string ComputeRequestHash<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static int ReadPositiveIntSetting(string? valueJson, int fallback)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return fallback;
        }

        try
        {
            var value = JsonSerializer.Deserialize<int>(valueJson);
            return value > 0 ? value : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static bool ReadBoolSetting(string? valueJson, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return fallback;
        }

        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(valueJson, JsonOptions);
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                _ => fallback
            };
        }
        catch (JsonException)
        {
            return bool.TryParse(valueJson, out var parsed) ? parsed : fallback;
        }
    }

    public static string? ReadStringSetting(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(valueJson, JsonOptions);
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.GetRawText();
        }
        catch (JsonException)
        {
            return valueJson;
        }
    }

    public static async Task<string> BuildAppBaseUrlAsync(
        IAdminSettingService settings,
        IConfiguration configuration,
        Guid organizationId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var configuredBaseUrl = ReadStringSetting((await settings.GetAsync(organizationId, "app", "baseUrl", cancellationToken))?.ValueJson)
            ?? configuration["App:BaseUrl"]
            ?? configuration["APP_BASE_URL"];
        return string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? $"{httpContext.Request.Scheme}://{httpContext.Request.Host}"
            : configuredBaseUrl.Trim();
    }

    public static async Task<string> BuildRecipientLinkAsync(
        IAdminSettingService settings,
        IConfiguration configuration,
        Guid organizationId,
        HttpContext httpContext,
        string rawToken,
        CancellationToken cancellationToken)
    {
        var baseUrl = await BuildAppBaseUrlAsync(settings, configuration, organizationId, httpContext, cancellationToken);
        return $"{baseUrl.TrimEnd('/')}/c/{rawToken}";
    }
}
