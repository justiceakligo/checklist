using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class SubmissionPackage : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ActionId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid? ActionRecipientId { get; set; }
    public string PackageReference { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public SubmissionPackageStatus Status { get; set; } = SubmissionPackageStatus.Preparing;
    public DateTimeOffset AcceptedAt { get; set; }
    public Guid AcceptedByUserId { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? ContentHash { get; set; }
    public Guid? ReportFileAssetId { get; set; }
    public Guid? BundleFileAssetId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public Guid? PreviousPackageId { get; set; }
    public Guid? SupersededByPackageId { get; set; }
    public string? RevisionReason { get; set; }
    public Guid? OwnerUserId { get; set; }
    public Guid? AssignedTeamId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
    public long RowVersion { get; set; } = 1;

    public Organization? Organization { get; set; }
    public ChecklistAction? Action { get; set; }
    public Submission? Submission { get; set; }
    public ActionRecipient? ActionRecipient { get; set; }
    public AppUser? AcceptedByUser { get; set; }
    public AppUser? OwnerUser { get; set; }
    public FileAsset? ReportFileAsset { get; set; }
    public FileAsset? BundleFileAsset { get; set; }
    public SubmissionPackage? PreviousPackage { get; set; }
    public SubmissionPackage? SupersededByPackage { get; set; }
    public ICollection<DeliveryJob> DeliveryJobs { get; } = new List<DeliveryJob>();
    public ICollection<ManualHandoff> ManualHandoffs { get; } = new List<ManualHandoff>();
}

public sealed class Destination : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DestinationType Type { get; set; }
    public DestinationStatus Status { get; set; } = DestinationStatus.Active;
    public string ConfigurationJson { get; set; } = "{}";
    public string? ConfigurationEncrypted { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public string? LastValidationStatus { get; set; }

    public Organization? Organization { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ICollection<TemplateRoutingRule> RoutingRules { get; } = new List<TemplateRoutingRule>();
    public ICollection<DeliveryJob> DeliveryJobs { get; } = new List<DeliveryJob>();
}

public sealed class TemplateRoutingRule : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid? TemplateId { get; set; }
    public Guid DestinationId { get; set; }
    public RoutingTrigger Trigger { get; set; } = RoutingTrigger.ManualOnly;
    public int Sequence { get; set; }
    public bool IsRequired { get; set; }
    public bool IsAutomatic { get; set; }
    public string ConfigurationOverridesJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public Template? Template { get; set; }
    public Destination? Destination { get; set; }
}

public sealed class DeliveryJob : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid SubmissionPackageId { get; set; }
    public Guid? DestinationId { get; set; }
    public DeliveryJobStatus Status { get; set; } = DeliveryJobStatus.Queued;
    public int AttemptCount { get; set; }
    public int MaximumAttempts { get; set; } = 5;
    public Guid? RequestedByUserId { get; set; }
    public string TriggeredBy { get; set; } = "manual";
    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? ExternalReference { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public SubmissionPackage? SubmissionPackage { get; set; }
    public Destination? Destination { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public ICollection<DeliveryAttempt> Attempts { get; } = new List<DeliveryAttempt>();
}

public sealed class DeliveryAttempt : Entity
{
    public Guid DeliveryJobId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public DeliveryAttemptStatus Status { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ProviderReference { get; set; }
    public string? ResponseSummary { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public long? DurationMilliseconds { get; set; }

    public DeliveryJob? DeliveryJob { get; set; }
}

public sealed class ManualHandoff : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid SubmissionPackageId { get; set; }
    public string DestinationLabel { get; set; } = string.Empty;
    public Guid CompletedByUserId { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public Guid? EvidenceFileAssetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public SubmissionPackage? SubmissionPackage { get; set; }
    public AppUser? CompletedByUser { get; set; }
    public FileAsset? EvidenceFileAsset { get; set; }
}
