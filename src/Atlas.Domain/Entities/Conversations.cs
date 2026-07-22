using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class WhatsAppConnection : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public WhatsAppConnectionMode Mode { get; set; } = WhatsAppConnectionMode.CloudApi;
    public string? PhoneNumberId { get; set; }
    public string? WabaId { get; set; }
    public string DisplayNumber { get; set; } = string.Empty;
    public WhatsAppConnectionStatus Status { get; set; } = WhatsAppConnectionStatus.PendingVerification;
    public string? SecretReference { get; set; }
    public byte[]? WebhookVerifyTokenHash { get; set; }
    public string? BusinessPortfolioId { get; set; }
    public string? EmbeddedSignupSessionId { get; set; }
    public string? ExternalAccountId { get; set; }
    public bool HistorySyncRequested { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    public DateTimeOffset? LastValidatedAt { get; set; }
    public DateTimeOffset? LastInboundAt { get; set; }
    public DateTimeOffset? LastOutboundAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public ICollection<Contact> Contacts { get; } = new List<Contact>();
    public ICollection<Conversation> Conversations { get; } = new List<Conversation>();
    public ICollection<MessageTemplate> MessageTemplates { get; } = new List<MessageTemplate>();
}

public sealed class Contact : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string NormalizedPhone { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string ProfileJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public WhatsAppConnection? Connection { get; set; }
    public ICollection<Lead> Leads { get; } = new List<Lead>();
    public ICollection<Conversation> Conversations { get; } = new List<Conversation>();
}

public sealed class Lead : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ContactId { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.New;
    public string Source { get; set; } = "whatsapp";
    public string? CampaignRef { get; set; }
    public DateTimeOffset? WonAt { get; set; }
    public string? LostReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public Contact? Contact { get; set; }
    public ICollection<Conversation> Conversations { get; } = new List<Conversation>();
}

public sealed class Conversation : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid ContactId { get; set; }
    public Guid LeadId { get; set; }
    public Guid? AssignedUserId { get; set; }
    public Guid? AssignedTeamId { get; set; }
    public ConversationStatus Status { get; set; } = ConversationStatus.Open;
    public DateTimeOffset? LastMessageAt { get; set; }
    public DateTimeOffset? LastInboundMessageAt { get; set; }
    public DateTimeOffset? LastOutboundMessageAt { get; set; }
    public int UnreadCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedAt { get; set; }

    public Organization? Organization { get; set; }
    public WhatsAppConnection? Connection { get; set; }
    public Contact? Contact { get; set; }
    public Lead? Lead { get; set; }
    public AppUser? AssignedUser { get; set; }
    public ICollection<ConversationMessage> Messages { get; } = new List<ConversationMessage>();
    public ICollection<ConversationAssignment> Assignments { get; } = new List<ConversationAssignment>();
    public ICollection<InternalNote> InternalNotes { get; } = new List<InternalNote>();
    public ICollection<FollowUp> FollowUps { get; } = new List<FollowUp>();
    public ICollection<ConversationActivity> Activities { get; } = new List<ConversationActivity>();
    public ICollection<ConversationLink> Links { get; } = new List<ConversationLink>();
}

public sealed class ConversationMessage : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public string? ProviderMessageId { get; set; }
    public ConversationMessageDirection Direction { get; set; }
    public ConversationMessageType Type { get; set; } = ConversationMessageType.Text;
    public string? Body { get; set; }
    public ConversationMessageStatus Status { get; set; } = ConversationMessageStatus.Pending;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? FailureCode { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Conversation? Conversation { get; set; }
    public AppUser? CreatedByUser { get; set; }
}

public sealed class ConversationAssignment : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid? ToUserId { get; set; }
    public Guid? AssignedTeamId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset AssignedAt { get; set; } = DateTimeOffset.UtcNow;

    public Conversation? Conversation { get; set; }
    public AppUser? FromUser { get; set; }
    public AppUser? ToUser { get; set; }
    public AppUser? AssignedByUser { get; set; }
}

public sealed class InternalNote : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Conversation? Conversation { get; set; }
    public AppUser? AuthorUser { get; set; }
}

public sealed class FollowUp : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public ConversationFollowUpStatus Status { get; set; } = ConversationFollowUpStatus.Pending;
    public string? Note { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Conversation? Conversation { get; set; }
    public AppUser? OwnerUser { get; set; }
}

public sealed class ConversationWebhookEvent : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string ProviderEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public ConversationWebhookEventStatus ProcessingStatus { get; set; } = ConversationWebhookEventStatus.Pending;
    public string PayloadJson { get; set; } = "{}";
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }

    public WhatsAppConnection? Connection { get; set; }
}

public sealed class ConversationActivity : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public string Type { get; set; } = string.Empty;
    public ActorType ActorType { get; set; } = ActorType.System;
    public string? ActorId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Conversation? Conversation { get; set; }
}

public sealed class MessageTemplate : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid? ConnectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Category { get; set; } = string.Empty;
    public ConversationTemplateStatus Status { get; set; } = ConversationTemplateStatus.Pending;
    public string? ProviderTemplateId { get; set; }
    public string ComponentsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public WhatsAppConnection? Connection { get; set; }
}

public sealed class ConversationLink : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid ConversationId { get; set; }
    public string LinkType { get; set; } = string.Empty;
    public Guid? LinkedEntityId { get; set; }
    public string? ExternalReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Conversation? Conversation { get; set; }
}
