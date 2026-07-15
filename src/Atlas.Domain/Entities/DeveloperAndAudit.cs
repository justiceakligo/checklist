using Atlas.Domain.Common;
using Atlas.Domain.Enums;
using System.Net;

namespace Atlas.Domain.Entities;

public sealed class AuditEvent : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid? ActionId { get; set; }
    public ActorType ActorType { get; set; } = ActorType.System;
    public string? ActorId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = "{}";
    public IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public ChecklistAction? Action { get; set; }
}

public sealed class ApiKey : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public byte[] SecretHash { get; set; } = Array.Empty<byte>();
    public string[] Scopes { get; set; } = Array.Empty<string>();
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
}

public sealed class WebhookEndpoint : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public string Url { get; set; } = string.Empty;
    public byte[] SecretCiphertext { get; set; } = Array.Empty<byte>();
    public string[] EventTypes { get; set; } = Array.Empty<string>();
    public WebhookEndpointStatus Status { get; set; } = WebhookEndpointStatus.Active;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public ICollection<WebhookDelivery> Deliveries { get; } = new List<WebhookDelivery>();
}

public sealed class WebhookDelivery : Entity
{
    public Guid WebhookEndpointId { get; set; }
    public Guid EventId { get; set; }
    public int AttemptNumber { get; set; }
    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int? ResponseStatus { get; set; }
    public string? ResponseExcerpt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    public WebhookEndpoint? WebhookEndpoint { get; set; }
}

public sealed class UsageEvent : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public Guid? ActionId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization? Organization { get; set; }
    public ChecklistAction? Action { get; set; }
}

public sealed class IdempotencyRecord : Entity, ITenantOwned
{
    public Guid OrganizationId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }

    public Organization? Organization { get; set; }
}

public sealed class AdminSetting : Entity
{
    public Guid? OrganizationId { get; set; }
    public AdminSettingScope Scope { get; set; } = AdminSettingScope.System;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string ValueJson { get; set; } = "{}";
    public bool IsSecret { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedByUserId { get; set; }

    public Organization? Organization { get; set; }
    public AppUser? UpdatedByUser { get; set; }
}
