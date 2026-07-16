using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Atlas.Application.Email;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Email;

public sealed class ResendEmailService(
    IHttpClientFactory httpClientFactory,
    AtlasDbContext dbContext,
    IConfiguration configuration,
    ILogger<ResendEmailService> logger) : IEmailService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string DefaultEmailLogoUrl = "https://api.reqara.com/brand/reqara-email-logo.png";

    public async Task<EmailSendResult> SendAsync(
        string toEmail,
        string subject,
        string? textBody,
        string? htmlBody,
        string fromEmail,
        string? fromName = null,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headers = null,
        string? replyToEmail = null)
    {
        var provider = await GetSettingAsync("email", "provider", cancellationToken)
            ?? configuration["Email:Provider"]
            ?? configuration["EMAIL_PROVIDER"]
            ?? "smtp";

        if (!provider.Equals("resend", StringComparison.OrdinalIgnoreCase))
        {
            return new EmailSendResult("failed", Error: $"unsupported_provider:{provider}");
        }

        var apiKey = await GetSettingAsync("Email:Resend", "ApiKey", cancellationToken)
            ?? configuration["Email:Resend:ApiKey"]
            ?? configuration["RESEND_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new EmailSendResult("failed", Error: "missing_resend_api_key");
        }

        var configuredFromEmail = await GetSettingAsync("Email:Resend", "From", cancellationToken)
            ?? configuration["Email:Resend:From"]
            ?? configuration["EMAIL_FROM"]
            ?? fromEmail;
        var configuredFromName = fromName
            ?? await GetSettingAsync("Email:Resend", "FromName", cancellationToken)
            ?? configuration["Email:Resend:FromName"]
            ?? configuration["EMAIL_FROM_NAME"];
        var replyTo = replyToEmail
            ?? await GetSettingAsync("Email", "ReplyTo", cancellationToken)
            ?? configuration["Email:ReplyTo"]
            ?? configuration["EMAIL_REPLY_TO"]
            ?? "support@example.com";
        var logoUrl = await GetSettingAsync("Email:Brand", "LogoUrl", cancellationToken)
            ?? configuration["Email:Brand:LogoUrl"]
            ?? configuration["EMAIL_BRAND_LOGO_URL"];
        var effectiveHtmlBody = string.IsNullOrWhiteSpace(logoUrl)
            ? htmlBody
            : htmlBody?.Replace(DefaultEmailLogoUrl, logoUrl.Trim(), StringComparison.Ordinal);

        var from = string.IsNullOrWhiteSpace(configuredFromName)
            ? configuredFromEmail
            : $"{configuredFromName} <{configuredFromEmail}>";

        var payload = new
        {
            from,
            to = new[] { toEmail },
            subject,
            text = textBody,
            html = effectiveHtmlBody ?? textBody,
            reply_to = replyTo,
            headers = headers is { Count: > 0 } ? headers : null
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        var client = httpClientFactory.CreateClient(nameof(ResendEmailService));
        var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Resend send failed: {StatusCode} {Body}", (int)response.StatusCode, body);
            return new EmailSendResult("failed", Error: $"resend_error:{(int)response.StatusCode}");
        }

        using var json = JsonDocument.Parse(body);
        var messageId = json.RootElement.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

        return new EmailSendResult("sent", messageId);
    }

    private async Task<string?> GetSettingAsync(string category, string key, CancellationToken cancellationToken)
    {
        var setting = await dbContext.AdminSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.OrganizationId == null && item.Category == category && item.Key == key)
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(setting?.ValueJson))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<JsonElement>(setting.ValueJson, JsonOptions);
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : value.GetRawText().Trim();
        }
        catch (JsonException)
        {
            return setting.ValueJson.Trim();
        }
    }
}
