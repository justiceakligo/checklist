using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class Organization : Entity, ISoftDelete
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public OrganizationStatus Status { get; set; } = OrganizationStatus.Active;
    public string Timezone { get; set; } = "UTC";
    public string DefaultLanguage { get; set; } = "en";
    public Guid? LogoFileId { get; set; }
    public string? AccentColor { get; set; }
    public string? PrivacyStatement { get; set; }
    public int RetentionDays { get; set; } = 365;
    public DeveloperAccessStatus DeveloperAccessStatus { get; set; } = DeveloperAccessStatus.SandboxOnly;
    public DateTimeOffset? DeveloperProductionRequestedAt { get; set; }
    public DateTimeOffset? DeveloperProductionApprovedAt { get; set; }
    public DateTimeOffset? DeveloperProductionRejectedAt { get; set; }
    public string? DeveloperProductionNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    public FileAsset? LogoFile { get; set; }
    public ICollection<OrganizationUser> Members { get; } = new List<OrganizationUser>();
    public ICollection<Template> Templates { get; } = new List<Template>();
    public ICollection<ChecklistAction> Actions { get; } = new List<ChecklistAction>();
    public ICollection<FileAsset> Files { get; } = new List<FileAsset>();
    public ICollection<ApiKey> ApiKeys { get; } = new List<ApiKey>();
    public ICollection<WebhookEndpoint> WebhookEndpoints { get; } = new List<WebhookEndpoint>();
    public ICollection<AuditEvent> AuditEvents { get; } = new List<AuditEvent>();
    public ICollection<UsageEvent> UsageEvents { get; } = new List<UsageEvent>();
}
