using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class FileAsset : Entity, ITenantOwned, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Guid? ActionId { get; set; }
    public Guid? ActionRecipientId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string? Extension { get; set; }
    public long SizeBytes { get; set; }
    public byte[]? Sha256Hash { get; set; }
    public FileScanStatus ScanStatus { get; set; } = FileScanStatus.Pending;
    public string? ScanEngine { get; set; }
    public DateTimeOffset? ScanCompletedAt { get; set; }
    public DateTimeOffset? RetentionUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    public Organization? Organization { get; set; }
    public ChecklistAction? Action { get; set; }
    public ActionRecipient? ActionRecipient { get; set; }
}

public sealed class ReminderSchedule : Entity
{
    public Guid ActionRecipientId { get; set; }
    public DateTimeOffset TriggerAt { get; set; }
    public ReminderType Type { get; set; }
    public ReminderStatus Status { get; set; } = ReminderStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ActionRecipient? ActionRecipient { get; set; }
}

public sealed class NotificationDelivery : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid? ActionRecipientId { get; set; }
    public DeliveryChannel Channel { get; set; } = DeliveryChannel.Email;
    public string TemplateKey { get; set; } = string.Empty;
    public string? ProviderMessageId { get; set; }
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Queued;
    public string? ErrorCode { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public ActionRecipient? ActionRecipient { get; set; }
}
