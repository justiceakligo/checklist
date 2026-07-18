using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Billing;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Tests.Infrastructure;

public sealed class EntitlementServiceTests
{
    [Fact]
    public async Task Business_plan_allows_advanced_package_destinations()
    {
        var organizationId = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationId };
        await using var dbContext = CreateContext(tenant);
        var settings = new AdminSettingService(dbContext, new TestClock());
        await settings.UpsertAsync(
            new UpsertAdminSettingRequest(
                organizationId,
                AdminSettingScope.Organization,
                "billing",
                "plan",
                "\"business\"",
                false,
                null),
            CancellationToken.None);
        var service = new EntitlementService(dbContext, settings);

        Assert.True((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "sftp_destination", CancellationToken.None)).Allowed);
        Assert.True((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "sharepoint_destination", CancellationToken.None)).Allowed);
        Assert.True((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "onedrive_destination", CancellationToken.None)).Allowed);
        Assert.True((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "google_drive_destination", CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task Starter_plan_blocks_advanced_package_destinations()
    {
        var organizationId = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationId };
        await using var dbContext = CreateContext(tenant);
        var settings = new AdminSettingService(dbContext, new TestClock());
        await settings.UpsertAsync(
            new UpsertAdminSettingRequest(
                organizationId,
                AdminSettingScope.Organization,
                "billing",
                "plan",
                "\"starter\"",
                false,
                null),
            CancellationToken.None);
        var service = new EntitlementService(dbContext, settings);

        Assert.False((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "sftp_destination", CancellationToken.None)).Allowed);
        Assert.False((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "sharepoint_destination", CancellationToken.None)).Allowed);
        Assert.False((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "onedrive_destination", CancellationToken.None)).Allowed);
        Assert.False((await service.HasFeatureAsync(organizationId, new TestClock().UtcNow, "google_drive_destination", CancellationToken.None)).Allowed);
    }

    private static AtlasDbContext CreateContext(TestTenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<AtlasDbContext>()
            .UseInMemoryDatabase("atlas-entitlements-" + Guid.NewGuid())
            .Options;

        return new AtlasDbContext(options, tenantContext, new TestClock());
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? OrganizationId { get; set; }
        public string? ActorId => "test";
        public string? ActorType => "user";
        public Guid? UserId => Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        public Guid? RecipientId => null;
        public IReadOnlySet<string> Scopes => new HashSet<string>(["dashboard:*", "admin:*"], StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TestClock : IAtlasClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    }
}
