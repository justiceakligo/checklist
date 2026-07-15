using Atlas.Application.Abstractions;

namespace Atlas.Infrastructure;

public sealed class TenantContextAccessor : ITenantContextAccessor
{
    public Guid? OrganizationId { get; private set; }
    public string? ActorId { get; private set; }
    public string? ActorType { get; private set; }
    public Guid? UserId { get; private set; }
    public Guid? RecipientId { get; private set; }
    public IReadOnlySet<string> Scopes { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public void SetUserTenant(Guid organizationId, Guid userId, string? actorId = null, IEnumerable<string>? scopes = null)
    {
        OrganizationId = organizationId;
        UserId = userId;
        RecipientId = null;
        ActorType = "user";
        ActorId = actorId ?? userId.ToString();
        Scopes = new HashSet<string>(scopes ?? ["dashboard:*"], StringComparer.OrdinalIgnoreCase);
    }

    public void SetApiTenant(Guid organizationId, Guid apiKeyId, IEnumerable<string> scopes)
    {
        OrganizationId = organizationId;
        UserId = null;
        RecipientId = null;
        ActorType = "api";
        ActorId = apiKeyId.ToString();
        Scopes = new HashSet<string>(scopes, StringComparer.OrdinalIgnoreCase);
    }

    public void SetRecipientTenant(Guid organizationId, Guid recipientId, string? actorId = null)
    {
        OrganizationId = organizationId;
        UserId = null;
        RecipientId = recipientId;
        ActorType = "recipient";
        ActorId = actorId ?? recipientId.ToString();
        Scopes = new HashSet<string>(["recipient:*"], StringComparer.OrdinalIgnoreCase);
    }

    public void ClearTenant()
    {
        OrganizationId = null;
        ActorId = null;
        ActorType = null;
        UserId = null;
        RecipientId = null;
        Scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
