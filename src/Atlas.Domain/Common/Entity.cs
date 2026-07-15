namespace Atlas.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public interface ITenantOwned
{
    Guid OrganizationId { get; set; }
}

public interface ISoftDelete
{
    DateTimeOffset? DeletedAt { get; set; }
}
