using Atlas.Domain.Common;

namespace Atlas.Domain.Entities;

public sealed class AppUser : Entity
{
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string FullName { get; set; } = string.Empty;
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public bool MfaEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    public ICollection<OrganizationUser> Memberships { get; } = new List<OrganizationUser>();
}
