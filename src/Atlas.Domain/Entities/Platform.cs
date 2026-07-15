using Atlas.Domain.Common;
using Atlas.Domain.Enums;
using System.Net;

namespace Atlas.Domain.Entities;

public sealed class PlatformStaff : Entity
{
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string FullName { get; set; } = string.Empty;
    public PlatformStaffRole Role { get; set; } = PlatformStaffRole.Support;
    public PlatformStaffStatus Status { get; set; } = PlatformStaffStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
}

public sealed class PlatformOrganizationInterest : Entity
{
    public string OrganizationName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string? Source { get; set; }
    public string? Region { get; set; }
    public string? ExpectedVolume { get; set; }
    public string? Message { get; set; }
    public OrganizationInterestStatus Status { get; set; } = OrganizationInterestStatus.New;
    public Guid? AssignedStaffId { get; set; }
    public Guid? ApprovedOrganizationId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }

    public PlatformStaff? AssignedStaff { get; set; }
    public Organization? ApprovedOrganization { get; set; }
}

public sealed class PlatformRevenueEvent : Entity
{
    public Guid? OrganizationId { get; set; }
    public PlatformRevenueEventType Type { get; set; } = PlatformRevenueEventType.Subscription;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Source { get; set; } = "manual";
    public string? ExternalReference { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PeriodStart { get; set; }
    public DateTimeOffset? PeriodEnd { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public Guid? RecordedByStaffId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public PlatformStaff? RecordedByStaff { get; set; }
}

public sealed class PlatformAuditEvent : Entity
{
    public Guid? StaffId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = "{}";
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public PlatformStaff? Staff { get; set; }
}
