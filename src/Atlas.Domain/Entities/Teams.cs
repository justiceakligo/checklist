using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class OrganizationTeam : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public OrganizationTeamStatus Status { get; set; } = OrganizationTeamStatus.Active;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public AppUser? CreatedByUser { get; set; }
}
