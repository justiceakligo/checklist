using Atlas.Domain.Enums;

namespace Atlas.Application.Settings;

public sealed record AdminSettingValue(
    Guid Id,
    Guid? OrganizationId,
    AdminSettingScope Scope,
    string Category,
    string Key,
    string ValueJson,
    bool IsSecret,
    DateTimeOffset UpdatedAt);

public sealed record UpsertAdminSettingRequest(
    Guid? OrganizationId,
    AdminSettingScope Scope,
    string Category,
    string Key,
    string ValueJson,
    bool IsSecret,
    Guid? UpdatedByUserId);

public interface IAdminSettingService
{
    Task<IReadOnlyList<AdminSettingValue>> ListAsync(Guid? organizationId, CancellationToken cancellationToken);
    Task<AdminSettingValue?> GetAsync(Guid? organizationId, string category, string key, CancellationToken cancellationToken);
    Task<AdminSettingValue> UpsertAsync(UpsertAdminSettingRequest request, CancellationToken cancellationToken);
}
