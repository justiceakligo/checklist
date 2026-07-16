using Atlas.Domain.Common;
using Atlas.Domain.Enums;
using System.Net;

namespace Atlas.Domain.Entities;

public sealed class Submission : Entity
{
    public Guid ActionId { get; set; }
    public Guid ActionRecipientId { get; set; }
    public int VersionNumber { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Submitted;
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset? DeclarationAcceptedAt { get; set; }
    public IPAddress? DeclarationIpAddress { get; set; }
    public byte[] ContentHash { get; set; } = Array.Empty<byte>();

    public ChecklistAction? Action { get; set; }
    public ActionRecipient? ActionRecipient { get; set; }
    public AppUser? ReviewedByUser { get; set; }
    public ICollection<SubmissionResponseEntry> Responses { get; } = new List<SubmissionResponseEntry>();
    public ICollection<SubmissionFile> Files { get; } = new List<SubmissionFile>();
}

public sealed class SubmissionResponseEntry : Entity
{
    public Guid SubmissionId { get; set; }
    public Guid RequirementId { get; set; }
    public string? ValueJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Submission? Submission { get; set; }
    public Requirement? Requirement { get; set; }
}

public sealed class SubmissionFile : Entity
{
    public Guid SubmissionId { get; set; }
    public Guid RequirementId { get; set; }
    public Guid FileAssetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Submission? Submission { get; set; }
    public Requirement? Requirement { get; set; }
    public FileAsset? FileAsset { get; set; }
}
