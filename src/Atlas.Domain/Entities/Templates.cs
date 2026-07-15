using Atlas.Domain.Common;
using Atlas.Domain.Enums;

namespace Atlas.Domain.Entities;

public sealed class Template : Entity, ISoftDelete
{
    public Guid? OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public TemplateStatus Status { get; set; } = TemplateStatus.Draft;
    public Guid? CurrentVersionId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }

    public Organization? Organization { get; set; }
    public TemplateVersion? CurrentVersion { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ICollection<TemplateVersion> Versions { get; } = new List<TemplateVersion>();
}

public sealed class TemplateVersion : Entity
{
    public Guid TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Template? Template { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public ICollection<TemplateRequirement> Requirements { get; } = new List<TemplateRequirement>();
}

public sealed class TemplateRequirement : Entity
{
    public Guid TemplateVersionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public RequirementType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public string ConfigurationJson { get; set; } = "{}";
    public string ValidationJson { get; set; } = "{}";
    public string? ConditionJson { get; set; }

    public TemplateVersion? TemplateVersion { get; set; }
}
