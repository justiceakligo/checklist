using System.Net;
using System.Text.Json;
using Atlas.Application.Abstractions;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Email;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Tests.Infrastructure;

public sealed class ResendEmailServiceTests
{
    [Fact]
    public async Task Send_uses_verified_sender_settings_with_message_reply_to()
    {
        var tenant = new TestTenantContext();
        await using var dbContext = CreateContext(tenant);
        dbContext.AdminSettings.AddRange(
            Setting("email", "provider", "\"resend\""),
            Setting("Email:Resend", "ApiKey", "\"re_test\""),
            Setting("Email:Resend", "From", "\"requests@projectatlas.app\""),
            Setting("Email:Resend", "FromName", "\"Project Atlas\""),
            Setting("Email", "ReplyTo", "\"support@ryvepool.com\""));
        await dbContext.SaveChangesAsync();

        var handler = new RecordingHandler();
        var service = new ResendEmailService(
            new TestHttpClientFactory(new HttpClient(handler)),
            dbContext,
            new ConfigurationBuilder().Build(),
            NullLogger<ResendEmailService>.Instance);

        var result = await service.SendAsync(
            "recipient@example.com",
            "Your Project Atlas access code",
            "123456",
            "<p>123456</p>",
            "fallback@example.com",
            "Northstar Staffing via Project Atlas",
            CancellationToken.None,
            new Dictionary<string, string> { ["X-Atlas-Email-Type"] = "recipient-invitation" },
            "ollie@northstarstaffing.com");

        Assert.True(result.Sent);
        Assert.Equal("email_123", result.MessageId);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("re_test", handler.AuthorizationParameter);

        using var payload = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("Northstar Staffing via Project Atlas <requests@projectatlas.app>", payload.RootElement.GetProperty("from").GetString());
        Assert.Equal("recipient@example.com", payload.RootElement.GetProperty("to")[0].GetString());
        Assert.Equal("ollie@northstarstaffing.com", payload.RootElement.GetProperty("reply_to").GetString());
        Assert.Equal("recipient-invitation", payload.RootElement.GetProperty("headers").GetProperty("X-Atlas-Email-Type").GetString());
    }

    private static AdminSetting Setting(string category, string key, string valueJson)
    {
        return new AdminSetting
        {
            Scope = AdminSettingScope.System,
            Category = category,
            Key = key,
            ValueJson = valueJson,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static AtlasDbContext CreateContext(TestTenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<AtlasDbContext>()
            .UseInMemoryDatabase("atlas-email-" + Guid.NewGuid())
            .Options;

        return new AtlasDbContext(options, tenantContext, new TestClock());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"id\":\"email_123\"}")
            };
        }
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? OrganizationId => null;
        public string? ActorId => "test";
        public string? ActorType => "user";
        public Guid? UserId => Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        public Guid? RecipientId => null;
        public IReadOnlySet<string> Scopes => new HashSet<string>(["dashboard:*"], StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TestClock : IAtlasClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    }
}
