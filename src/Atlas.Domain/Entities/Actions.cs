using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class ChecklistAction : Entity, ITenantOwned, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public string? ClientReference { get; set; }
    public string PublicReference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ChecklistActionStatus Status { get; set; } = ChecklistActionStatus.Draft;
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public Organization? Organization { get; set; }
    public Template? Template { get; set; }
    public TemplateVersion? TemplateVersion { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ICollection<ActionRecipient> Recipients { get; } = new List<ActionRecipient>();
    public ICollection<Requirement> Requirements { get; } = new List<Requirement>();
    public ICollection<Submission> Submissions { get; } = new List<Submission>();
    public ICollection<AuditEvent> AuditEvents { get; } = new List<AuditEvent>();
}

public sealed class ActionRecipient : Entity
{
    public Guid ActionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public ActionRecipientStatus Status { get; set; } = ActionRecipientStatus.Pending;
    public byte[] AccessTokenHash { get; set; } = Array.Empty<byte>();
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public bool OtpRequired { get; set; }
    public byte[]? OtpCodeHash { get; set; }
    public DateTimeOffset? OtpExpiresAt { get; set; }
    public DateTimeOffset? OtpVerifiedAt { get; set; }
    public int OtpAttemptCount { get; set; }
    public DateTimeOffset? FirstViewedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ChecklistAction? Action { get; set; }
    public ICollection<DraftResponse> DraftResponses { get; } = new List<DraftResponse>();
    public ICollection<Submission> Submissions { get; } = new List<Submission>();
    public ICollection<ReminderSchedule> ReminderSchedules { get; } = new List<ReminderSchedule>();
    public ICollection<RecipientAccessSession> AccessSessions { get; } = new List<RecipientAccessSession>();
}

public sealed class RecipientAccessSession : Entity
{
    public Guid ActionRecipientId { get; set; }
    public byte[] SessionTokenHash { get; set; } = Array.Empty<byte>();
    public bool OtpVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;

    public ActionRecipient? ActionRecipient { get; set; }
}

public sealed class Requirement : Entity
{
    public Guid ActionId { get; set; }
    public Guid? SourceTemplateRequirementId { get; set; }
    public string Key { get; set; } = string.Empty;
    public RequirementType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string ValidationJson { get; set; } = "{}";
    public string? ConditionJson { get; set; }

    public ChecklistAction? Action { get; set; }
    public TemplateRequirement? SourceTemplateRequirement { get; set; }
    public ICollection<DraftResponse> DraftResponses { get; } = new List<DraftResponse>();
    public ICollection<SubmissionResponseEntry> SubmissionResponses { get; } = new List<SubmissionResponseEntry>();
}

public sealed class DraftResponse : Entity
{
    public Guid ActionRecipientId { get; set; }
    public Guid RequirementId { get; set; }
    public string? ValueJson { get; set; }
    public int Version { get; set; } = 1;
    public Guid? UpdatedBySession { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ActionRecipient? ActionRecipient { get; set; }
    public Requirement? Requirement { get; set; }
}
