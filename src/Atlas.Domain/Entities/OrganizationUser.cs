using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class OrganizationUser : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationUserRole Role { get; set; } = OrganizationUserRole.Member;
    public MembershipStatus Status { get; set; } = MembershipStatus.Invited;
    public DateTimeOffset? InvitedAt { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public AppUser? User { get; set; }
}
