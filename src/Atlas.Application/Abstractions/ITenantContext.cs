namespace Atlas.Application.Abstractions;

public interface ITenantContext
{
    Guid? OrganizationId { get; }
    string? ActorId { get; }
    string? ActorType { get; }
    Guid? UserId { get; }
    Guid? RecipientId { get; }
    IReadOnlySet<string> Scopes { get; }
    bool HasTenant => OrganizationId.HasValue;
}

public interface ITenantContextAccessor : ITenantContext
{
    void SetUserTenant(Guid organizationId, Guid userId, string? actorId = null, IEnumerable<string>? scopes = null);
    void SetApiTenant(Guid organizationId, Guid apiKeyId, IEnumerable<string> scopes);
    void SetRecipientTenant(Guid organizationId, Guid recipientId, string? actorId = null);
    void ClearTenant();
}
