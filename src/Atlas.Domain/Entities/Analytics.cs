using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class AnalyticsEvent : Entity
{
    public string EventName { get; set; } = string.Empty;
    public Guid? OrganizationId { get; set; }
    public Guid? ChecklistId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? RecipientId { get; set; }
    public Guid? UserId { get; set; }
    public string? SessionId { get; set; }
    public string PropertiesJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
}

public sealed class InvestorReport : Entity
{
    public string ReportType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public string[] IncludeSections { get; set; } = [];
    public InvestorReportStatus Status { get; set; } = InvestorReportStatus.Completed;
    public string ResultJson { get; set; } = "{}";
    public Guid RequestedByStaffId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public PlatformStaff? RequestedByStaff { get; set; }
}
