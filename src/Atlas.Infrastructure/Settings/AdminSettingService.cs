using Atlas.Application.Abstractions;
using Atlas.Application.Settings;
using Atlas.Domain.Entities;
using Atlas.Domain.Enums;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Settings;

public sealed class AdminSettingService(AtlasDbContext dbContext, IAtlasClock clock) : IAdminSettingService
{
    public async Task<IReadOnlyList<AdminSettingValue>> ListAsync(
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.AdminSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(setting => setting.OrganizationId == null);

        if (organizationId.HasValue)
        {
            query = query.Concat(dbContext.AdminSettings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(setting => setting.OrganizationId == organizationId.Value));
        }

        return await query
            .OrderBy(setting => setting.Category)
            .ThenBy(setting => setting.Key)
            .Select(setting => ToValue(setting))
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminSettingValue?> GetAsync(
        Guid? organizationId,
        string category,
        string key,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var query = dbContext.AdminSettings.IgnoreQueryFilters().AsNoTracking();

        if (organizationId.HasValue)
        {
            var organizationSetting = await query
                .Where(setting => setting.OrganizationId == organizationId.Value
                    && setting.Category == category
                    && setting.Key == key)
                .Select(setting => ToValue(setting))
                .FirstOrDefaultAsync(cancellationToken);

            if (organizationSetting is not null)
            {
                return organizationSetting;
            }
        }

        return await query
            .Where(setting => setting.OrganizationId == null
                && setting.Category == category
                && setting.Key == key)
            .Select(setting => ToValue(setting))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AdminSettingValue> UpsertAsync(
        UpsertAdminSettingRequest request,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var setting = await dbContext.AdminSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(existing => existing.OrganizationId == request.OrganizationId
                && existing.Category == request.Category
                && existing.Key == request.Key,
                cancellationToken);

        if (setting is null)
        {
            setting = new AdminSetting
            {
                OrganizationId = request.OrganizationId,
                Scope = request.Scope,
                Category = request.Category,
                Key = request.Key,
                CreatedAt = clock.UtcNow
            };
            dbContext.AdminSettings.Add(setting);
        }

        setting.Scope = request.Scope;
        setting.ValueJson = request.ValueJson;
        setting.IsSecret = request.IsSecret;
        setting.UpdatedAt = clock.UtcNow;
        setting.UpdatedByUserId = request.UpdatedByUserId;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToValue(setting);
    }

    private static AdminSettingValue ToValue(AdminSetting setting)
    {
        return new AdminSettingValue(
            setting.Id,
            setting.OrganizationId,
            setting.Scope,
            setting.Category,
            setting.Key,
            setting.IsSecret ? "\"***\"" : setting.ValueJson,
            setting.IsSecret,
            setting.UpdatedAt);
    }

    private static void ValidateRequest(UpsertAdminSettingRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Category);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ValueJson);

        if (request.Scope == AdminSettingScope.System && request.OrganizationId.HasValue)
        {
            throw new ArgumentException("System settings cannot have an organization id.", nameof(request));
        }

        if (request.Scope == AdminSettingScope.Organization && !request.OrganizationId.HasValue)
        {
            throw new ArgumentException("Organization settings require an organization id.", nameof(request));
        }
    }
}
