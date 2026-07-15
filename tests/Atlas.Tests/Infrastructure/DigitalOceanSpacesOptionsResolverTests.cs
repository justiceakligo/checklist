using Atlas.Application.Abstractions;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atlas.Tests.Infrastructure;

public sealed class DigitalOceanSpacesOptionsResolverTests
{
    [Fact]
    public async Task ResolveAsync_uses_database_settings_before_configuration_fallback()
    {
        await using var dbContext = CreateContext();
        dbContext.AdminSettings.AddRange(
            Setting("ServiceUrl", "\"https://tor1.digitaloceanspaces.com\""),
            Setting("Region", "\"tor1\""),
            Setting("BucketName", "\"reqara-bucket\""),
            Setting("AccessKey", "\"db-access-key\"", isSecret: true),
            Setting("SecretKey", "\"db-secret-key\"", isSecret: true),
            Setting("QuarantinePrefix", "\"incoming\""),
            Setting("ForcePathStyle", "true"));
        await dbContext.SaveChangesAsync();

        var resolver = new DigitalOceanSpacesOptionsResolver(
            dbContext,
            Options.Create(new DigitalOceanSpacesOptions
            {
                ServiceUrl = "https://nyc3.digitaloceanspaces.com",
                Region = "nyc3",
                BucketName = "fallback-bucket",
                AccessKey = "fallback-access",
                SecretKey = "fallback-secret",
                QuarantinePrefix = "quarantine"
            }));

        var options = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal("https://tor1.digitaloceanspaces.com", options.ServiceUrl);
        Assert.Equal("tor1", options.Region);
        Assert.Equal("reqara-bucket", options.BucketName);
        Assert.Equal("db-access-key", options.AccessKey);
        Assert.Equal("db-secret-key", options.SecretKey);
        Assert.Equal("incoming", options.QuarantinePrefix);
        Assert.True(options.ForcePathStyle);
    }

    [Fact]
    public async Task ResolveAsync_uses_configuration_fallback_for_blank_database_values()
    {
        await using var dbContext = CreateContext();
        dbContext.AdminSettings.AddRange(
            Setting("BucketName", "\"\""),
            Setting("AccessKey", "\"\"", isSecret: true));
        await dbContext.SaveChangesAsync();

        var resolver = new DigitalOceanSpacesOptionsResolver(
            dbContext,
            Options.Create(new DigitalOceanSpacesOptions
            {
                ServiceUrl = "https://nyc3.digitaloceanspaces.com",
                Region = "nyc3",
                BucketName = "fallback-bucket",
                AccessKey = "fallback-access",
                SecretKey = "fallback-secret",
                QuarantinePrefix = "quarantine"
            }));

        var options = await resolver.ResolveAsync(CancellationToken.None);

        Assert.Equal("fallback-bucket", options.BucketName);
        Assert.Equal("fallback-access", options.AccessKey);
        Assert.Equal("fallback-secret", options.SecretKey);
    }

    private static AdminSetting Setting(string key, string valueJson, bool isSecret = false)
    {
        return new AdminSetting
        {
            Scope = AdminSettingScope.System,
            Category = DigitalOceanSpacesOptions.SectionName,
            Key = key,
            ValueJson = valueJson,
            IsSecret = isSecret,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static AtlasDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AtlasDbContext>()
            .UseInMemoryDatabase("atlas-spaces-options-" + Guid.NewGuid())
            .Options;

        return new AtlasDbContext(options, new TestTenantContext(), new TestClock());
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? OrganizationId => null;
        public string? ActorId => "test";
        public string? ActorType => "user";
        public Guid? UserId => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        public Guid? RecipientId => null;
        public IReadOnlySet<string> Scopes => new HashSet<string>(["dashboard:*"], StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TestClock : IAtlasClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    }
}
