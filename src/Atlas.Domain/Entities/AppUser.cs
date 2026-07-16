using Atlas.Domain.Common;
using Atlas.Domain.Enums;
using System.Net;

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
    public ICollection<UserAuthToken> AuthTokens { get; } = new List<UserAuthToken>();
}

public sealed class UserAuthToken : Entity
{
    public Guid UserId { get; set; }
    public UserAuthTokenPurpose Purpose { get; set; }
    public byte[] TokenHash { get; set; } = Array.Empty<byte>();
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public IPAddress? CreatedIpAddress { get; set; }
    public IPAddress? ConsumedIpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AppUser? User { get; set; }
}
