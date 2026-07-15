using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Tests.Infrastructure;

public sealed class AdminSettingServiceTests
{
    [Fact]
    public async Task Upsert_masks_secret_values_when_returned()
    {
        var tenant = new TestTenantContext();
        await using var dbContext = CreateContext(tenant);
        var service = new AdminSettingService(dbContext, new TestClock());

        var setting = await service.UpsertAsync(
            new UpsertAdminSettingRequest(
                null,
                AdminSettingScope.System,
                "email",
                "providerApiKey",
                "\"super-secret\"",
                true,
                null),
            CancellationToken.None);

        Assert.True(setting.IsSecret);
        Assert.Equal("\"***\"", setting.ValueJson);

        var stored = await dbContext.AdminSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("\"super-secret\"", stored.ValueJson);
    }

    [Fact]
    public async Task Organization_setting_overrides_global_setting_on_get()
    {
        var organizationId = Guid.NewGuid();
        var tenant = new TestTenantContext { OrganizationId = organizationId };
        await using var dbContext = CreateContext(tenant);
        var service = new AdminSettingService(dbContext, new TestClock());

        await service.UpsertAsync(
            new UpsertAdminSettingRequest(null, AdminSettingScope.System, "files", "uploadUrlMinutes", "10", false, null),
            CancellationToken.None);
        await service.UpsertAsync(
            new UpsertAdminSettingRequest(organizationId, AdminSettingScope.Organization, "files", "uploadUrlMinutes", "3", false, null),
            CancellationToken.None);

        var setting = await service.GetAsync(organizationId, "files", "uploadUrlMinutes", CancellationToken.None);

        Assert.NotNull(setting);
        Assert.Equal("3", setting.ValueJson);
    }

    private static AtlasDbContext CreateContext(TestTenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<AtlasDbContext>()
            .UseInMemoryDatabase("atlas-settings-" + Guid.NewGuid())
            .Options;

        return new AtlasDbContext(options, tenantContext, new TestClock());
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? OrganizationId { get; set; }
        public string? ActorId => "test";
        public string? ActorType => "user";
        public Guid? UserId => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public Guid? RecipientId => null;
        public IReadOnlySet<string> Scopes => new HashSet<string>(["dashboard:*", "admin:*"], StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TestClock : IAtlasClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
    }
}
