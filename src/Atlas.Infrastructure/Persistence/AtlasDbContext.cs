using Atlas.Application.Abstractions;
using Atlas.Domain.Common;
using Atlas.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence;

public sealed class AtlasDbContext(
    DbContextOptions<AtlasDbContext> options,
    ITenantContext tenantContext,
    IAtlasClock clock) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<TemplateRequirement> TemplateRequirements => Set<TemplateRequirement>();
    public DbSet<ChecklistAction> Actions => Set<ChecklistAction>();
    public DbSet<ActionRecipient> ActionRecipients => Set<ActionRecipient>();
    public DbSet<RecipientAccessSession> RecipientAccessSessions => Set<RecipientAccessSession>();
    public DbSet<Requirement> Requirements => Set<Requirement>();
    public DbSet<DraftResponse> DraftResponses => Set<DraftResponse>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionResponseEntry> SubmissionResponses => Set<SubmissionResponseEntry>();
    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();
    public DbSet<FileAsset> FileAssets => Set<FileAsset>();
    public DbSet<ReminderSchedule> ReminderSchedules => Set<ReminderSchedule>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AdminSetting> AdminSettings => Set<AdminSetting>();
    public DbSet<PlatformStaff> PlatformStaff => Set<PlatformStaff>();
    public DbSet<PlatformOrganizationInterest> PlatformOrganizationInterests => Set<PlatformOrganizationInterest>();
    public DbSet<PlatformRevenueEvent> PlatformRevenueEvents => Set<PlatformRevenueEvent>();
    public DbSet<PlatformAuditEvent> PlatformAuditEvents => Set<PlatformAuditEvent>();

    private Guid? CurrentOrganizationId => tenantContext.OrganizationId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        ConfigureOrganizations(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureTemplates(modelBuilder);
        ConfigureActions(modelBuilder);
        ConfigureSubmissions(modelBuilder);
        ConfigureFilesAndNotifications(modelBuilder);
        ConfigureDeveloperAndAudit(modelBuilder);
        ConfigureAdminSettings(modelBuilder);
        ConfigurePlatform(modelBuilder);
        ConfigureQueryFilters(modelBuilder);
        ConfigureIds(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private static void ConfigureOrganizations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations");
            entity.HasIndex(e => e.Slug).IsUnique().HasDatabaseName("uq_organizations_slug");
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.Timezone).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DefaultLanguage).HasMaxLength(12).IsRequired();
            entity.Property(e => e.AccentColor).HasMaxLength(7);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.LogoFile)
                .WithMany()
                .HasForeignKey(e => e.LogoFileId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("uq_users_email");
            entity.Property(e => e.Email).HasColumnType("citext").IsRequired();
            entity.Property(e => e.FullName).HasMaxLength(160).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.EmailVerifiedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastLoginAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<OrganizationUser>(entity =>
        {
            entity.ToTable("organization_users");
            entity.HasIndex(e => new { e.OrganizationId, e.UserId })
                .IsUnique()
                .HasDatabaseName("uq_organization_users_org_user");
            entity.Property(e => e.Role).HasConversion<short>().IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.InvitedAt).HasColumnType("timestamptz");
            entity.Property(e => e.JoinedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.Members)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.User)
                .WithMany(e => e.Memberships)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTemplates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Template>(entity =>
        {
            entity.ToTable("templates");
            entity.HasIndex(e => new { e.OrganizationId, e.Name })
                .IsUnique()
                .HasDatabaseName("uq_templates_org_name");
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(80);
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.Templates)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CurrentVersion)
                .WithMany()
                .HasForeignKey(e => e.CurrentVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TemplateVersion>(entity =>
        {
            entity.ToTable("template_versions");
            entity.HasIndex(e => new { e.TemplateId, e.VersionNumber })
                .IsUnique()
                .HasDatabaseName("uq_template_versions_template_version");
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.SettingsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.PublishedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Template)
                .WithMany(e => e.Versions)
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TemplateRequirement>(entity =>
        {
            entity.ToTable("template_requirements");
            entity.HasIndex(e => new { e.TemplateVersionId, e.Key })
                .IsUnique()
                .HasDatabaseName("uq_template_requirements_version_key");
            entity.HasIndex(e => new { e.TemplateVersionId, e.DisplayOrder })
                .HasDatabaseName("ix_template_requirements_version_order");
            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Type).HasConversion<short>().IsRequired();
            entity.Property(e => e.Label).HasMaxLength(300).IsRequired();
            entity.Property(e => e.ConfigurationJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ValidationJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ConditionJson).HasColumnType("jsonb");
            entity.HasOne(e => e.TemplateVersion)
                .WithMany(e => e.Requirements)
                .HasForeignKey(e => e.TemplateVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureActions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChecklistAction>(entity =>
        {
            entity.ToTable("actions", table =>
                table.HasCheckConstraint("ck_actions_due_after_created", "due_at IS NULL OR due_at >= created_at"));
            entity.HasIndex(e => e.PublicReference).IsUnique().HasDatabaseName("uq_actions_public_reference");
            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.CreatedAt })
                .HasDatabaseName("ix_actions_org_status");
            entity.HasIndex(e => new { e.OrganizationId, e.DueAt })
                .HasDatabaseName("ix_actions_org_due")
                .HasFilter("deleted_at IS NULL AND due_at IS NOT NULL AND status IN (2, 3)");
            entity.Property(e => e.PublicReference).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(240).IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.SettingsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.DueAt).HasColumnType("timestamptz");
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.SentAt).HasColumnType("timestamptz");
            entity.Property(e => e.CompletedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CancelledAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.Actions)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Template)
                .WithMany()
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.TemplateVersion)
                .WithMany()
                .HasForeignKey(e => e.TemplateVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ActionRecipient>(entity =>
        {
            entity.ToTable("action_recipients");
            entity.HasIndex(e => e.AccessTokenHash).IsUnique().HasDatabaseName("ix_recipients_token_hash");
            entity.HasIndex(e => new { e.ActionId, e.Status }).HasDatabaseName("ix_recipients_action_status");
            entity.Property(e => e.Name).HasMaxLength(180).IsRequired();
            entity.Property(e => e.Email).HasColumnType("citext").IsRequired();
            entity.Property(e => e.Phone).HasMaxLength(40);
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.AccessTokenHash).IsRequired();
            entity.Property(e => e.TokenExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.OtpExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.OtpVerifiedAt).HasColumnType("timestamptz");
            entity.Property(e => e.FirstViewedAt).HasColumnType("timestamptz");
            entity.Property(e => e.StartedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastActivityAt).HasColumnType("timestamptz");
            entity.Property(e => e.SubmittedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Action)
                .WithMany(e => e.Recipients)
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecipientAccessSession>(entity =>
        {
            entity.ToTable("recipient_access_sessions");
            entity.HasIndex(e => e.SessionTokenHash)
                .IsUnique()
                .HasDatabaseName("uq_recipient_access_sessions_token_hash");
            entity.HasIndex(e => new { e.ActionRecipientId, e.ExpiresAt })
                .HasDatabaseName("ix_recipient_access_sessions_recipient_expiry");
            entity.Property(e => e.SessionTokenHash).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.RevokedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastSeenAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.ActionRecipient)
                .WithMany(e => e.AccessSessions)
                .HasForeignKey(e => e.ActionRecipientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Requirement>(entity =>
        {
            entity.ToTable("requirements");
            entity.HasIndex(e => new { e.ActionId, e.DisplayOrder }).HasDatabaseName("ix_requirements_action_order");
            entity.HasIndex(e => new { e.ActionId, e.Key })
                .IsUnique()
                .HasDatabaseName("uq_requirements_action_key");
            entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Type).HasConversion<short>().IsRequired();
            entity.Property(e => e.Label).HasMaxLength(300).IsRequired();
            entity.Property(e => e.ConfigurationJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ValidationJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ConditionJson).HasColumnType("jsonb");
            entity.HasOne(e => e.Action)
                .WithMany(e => e.Requirements)
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.SourceTemplateRequirement)
                .WithMany()
                .HasForeignKey(e => e.SourceTemplateRequirementId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DraftResponse>(entity =>
        {
            entity.ToTable("draft_responses");
            entity.HasIndex(e => new { e.ActionRecipientId, e.RequirementId })
                .IsUnique()
                .HasDatabaseName("uq_draft_recipient_requirement");
            entity.Property(e => e.ValueJson).HasColumnType("jsonb");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.ActionRecipient)
                .WithMany(e => e.DraftResponses)
                .HasForeignKey(e => e.ActionRecipientId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Requirement)
                .WithMany(e => e.DraftResponses)
                .HasForeignKey(e => e.RequirementId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSubmissions(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Submission>(entity =>
        {
            entity.ToTable("submissions");
            entity.HasIndex(e => new { e.ActionId, e.VersionNumber })
                .HasDatabaseName("ix_submissions_action_version");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.SubmittedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ReviewedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ContentHash).IsRequired();
            entity.HasOne(e => e.Action)
                .WithMany(e => e.Submissions)
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ActionRecipient)
                .WithMany(e => e.Submissions)
                .HasForeignKey(e => e.ActionRecipientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ReviewedByUser)
                .WithMany()
                .HasForeignKey(e => e.ReviewedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SubmissionResponseEntry>(entity =>
        {
            entity.ToTable("submission_responses");
            entity.Property(e => e.ValueJson).HasColumnType("jsonb");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Submission)
                .WithMany(e => e.Responses)
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Requirement)
                .WithMany(e => e.SubmissionResponses)
                .HasForeignKey(e => e.RequirementId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SubmissionFile>(entity =>
        {
            entity.ToTable("submission_files");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Submission)
                .WithMany(e => e.Files)
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Requirement)
                .WithMany()
                .HasForeignKey(e => e.RequirementId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.FileAsset)
                .WithMany()
                .HasForeignKey(e => e.FileAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureFilesAndNotifications(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileAsset>(entity =>
        {
            entity.ToTable("file_assets");
            entity.HasIndex(e => e.StorageKey).IsUnique().HasDatabaseName("uq_file_assets_storage_key");
            entity.HasIndex(e => new { e.OrganizationId, e.CreatedAt }).HasDatabaseName("ix_files_org_created");
            entity.HasIndex(e => new { e.ScanStatus, e.CreatedAt }).HasDatabaseName("ix_files_scan_status");
            entity.Property(e => e.StorageKey).HasMaxLength(500).IsRequired();
            entity.Property(e => e.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.MimeType).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Extension).HasMaxLength(20);
            entity.Property(e => e.ScanStatus).HasConversion<short>().IsRequired();
            entity.Property(e => e.ScanEngine).HasMaxLength(100);
            entity.Property(e => e.ScanCompletedAt).HasColumnType("timestamptz");
            entity.Property(e => e.RetentionUntil).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeletedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.Files)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Action)
                .WithMany()
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ActionRecipient)
                .WithMany()
                .HasForeignKey(e => e.ActionRecipientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReminderSchedule>(entity =>
        {
            entity.ToTable("reminder_schedules");
            entity.HasIndex(e => new { e.Status, e.TriggerAt }).HasDatabaseName("ix_reminders_due");
            entity.Property(e => e.TriggerAt).HasColumnType("timestamptz");
            entity.Property(e => e.Type).HasConversion<short>().IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.SentAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.ActionRecipient)
                .WithMany(e => e.ReminderSchedules)
                .HasForeignKey(e => e.ActionRecipientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("notification_deliveries");
            entity.Property(e => e.Channel).HasConversion<short>().IsRequired();
            entity.Property(e => e.TemplateKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProviderMessageId).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.ErrorCode).HasMaxLength(100);
            entity.Property(e => e.SentAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeliveredAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ActionRecipient)
                .WithMany()
                .HasForeignKey(e => e.ActionRecipientId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureDeveloperAndAudit(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasIndex(e => new { e.OrganizationId, e.CreatedAt }).HasDatabaseName("ix_audit_org_created");
            entity.HasIndex(e => new { e.ActionId, e.CreatedAt }).HasDatabaseName("ix_audit_action_created");
            entity.Property(e => e.ActorType).HasConversion<short>().IsRequired();
            entity.Property(e => e.ActorId).HasMaxLength(100);
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EventData).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.IpAddress).HasColumnType("inet");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.AuditEvents)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Action)
                .WithMany(e => e.AuditEvents)
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.ToTable("api_keys");
            entity.HasIndex(e => new { e.OrganizationId, e.KeyPrefix }).HasDatabaseName("ix_api_keys_org_prefix");
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.KeyPrefix).HasMaxLength(16).IsRequired();
            entity.Property(e => e.SecretHash).IsRequired();
            entity.Property(e => e.LastUsedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.RevokedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.ApiKeys)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WebhookEndpoint>(entity =>
        {
            entity.ToTable("webhook_endpoints");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.WebhookEndpoints)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.ToTable("webhook_deliveries");
            entity.HasIndex(e => new { e.Status, e.NextAttemptAt }).HasDatabaseName("ix_webhook_retry");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CompletedAt).HasColumnType("timestamptz");
            entity.Property(e => e.NextAttemptAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.WebhookEndpoint)
                .WithMany(e => e.Deliveries)
                .HasForeignKey(e => e.WebhookEndpointId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UsageEvent>(entity =>
        {
            entity.ToTable("usage_events");
            entity.HasIndex(e => e.IdempotencyKey).IsUnique().HasDatabaseName("uq_usage_events_idempotency_key");
            entity.HasIndex(e => new { e.OrganizationId, e.OccurredAt }).HasDatabaseName("ix_usage_org_period");
            entity.Property(e => e.EventType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Quantity).HasPrecision(18, 4);
            entity.Property(e => e.Unit).HasMaxLength(32).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.OccurredAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany(e => e.UsageEvents)
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Action)
                .WithMany()
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasIndex(e => new { e.OrganizationId, e.Key })
                .IsUnique()
                .HasDatabaseName("uq_idempotency_records_org_key");
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_idempotency_records_expiry");
            entity.Property(e => e.Key).HasMaxLength(120).IsRequired();
            entity.Property(e => e.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ResponseJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureAdminSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminSetting>(entity =>
        {
            entity.ToTable("admin_settings");
            entity.HasIndex(e => new { e.OrganizationId, e.Category, e.Key })
                .IsUnique()
                .HasDatabaseName("uq_admin_settings_org_category_key")
                .HasFilter("organization_id IS NOT NULL");
            entity.HasIndex(e => new { e.Category, e.Key })
                .IsUnique()
                .HasDatabaseName("uq_admin_settings_global_category_key")
                .HasFilter("organization_id IS NULL");
            entity.Property(e => e.Scope).HasConversion<short>().IsRequired();
            entity.Property(e => e.Category).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Key).HasMaxLength(160).IsRequired();
            entity.Property(e => e.ValueJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.UpdatedByUser)
                .WithMany()
                .HasForeignKey(e => e.UpdatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            var seededAt = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);
            entity.HasData(
                new AdminSetting
                {
                    Id = Guid.Parse("a573c60e-6d96-4204-a451-4c4c3f66ac31"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "files",
                    Key = "maxUploadBytes",
                    ValueJson = "10485760",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("c17b8e86-e95f-4d19-a375-17672ce7c390"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "files",
                    Key = "uploadUrlMinutes",
                    ValueJson = "10",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("cc13c863-f4e5-4c34-9029-9d77b82d713b"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "files",
                    Key = "downloadUrlMinutes",
                    ValueJson = "5",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("25ff9a4f-61f3-4460-a92e-5b0784154b2f"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "security",
                    Key = "recipientTokenDays",
                    ValueJson = "30",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("2af49998-5f6e-4d9d-a36f-91e8d4b4f9f0"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "security",
                    Key = "recipientTokenGraceDays",
                    ValueJson = "30",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("8ddca1a6-85ca-4f55-802b-3506d681e4ed"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "security",
                    Key = "otpMinutes",
                    ValueJson = "10",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("8c967ee1-9233-4edc-aa96-961ea6eca16e"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "retention",
                    Key = "defaultRetentionDays",
                    ValueJson = "365",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("75fd9ff9-7be9-40a8-a30f-b24b201cdf49"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "email",
                    Key = "provider",
                    ValueJson = "\"resend\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("b64009db-2539-4745-9a44-808105d10db3"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "Email:Resend",
                    Key = "From",
                    ValueJson = "\"requests@projectatlas.app\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("6505a485-c0d9-4d29-8f00-7509f8a4bfd9"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "Email:Resend",
                    Key = "FromName",
                    ValueJson = "\"Project Atlas\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("c64f642f-a12a-4138-a963-178e6f072ea5"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "Email",
                    Key = "ReplyTo",
                    ValueJson = "\"support@projectatlas.app\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("3cfbb448-a95f-4d94-89b8-d15b81a8bace"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "app",
                    Key = "baseUrl",
                    ValueJson = "\"https://atlaschecklist.lovable.app\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("55228888-0a9a-43c3-8760-387a48ad864e"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "DigitalOceanSpaces",
                    Key = "ServiceUrl",
                    ValueJson = "\"\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("a6783782-f779-42ce-afde-f2d857fc19af"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "DigitalOceanSpaces",
                    Key = "Region",
                    ValueJson = "\"nyc3\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("1f56f6c0-d0b9-4a3d-88d9-9a736e9cf1ad"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "DigitalOceanSpaces",
                    Key = "BucketName",
                    ValueJson = "\"\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("c8bf7e7c-a030-4481-8ab2-10dac8984d92"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "DigitalOceanSpaces",
                    Key = "AccessKey",
                    ValueJson = "\"\"",
                    IsSecret = true,
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("e3a282d6-c69d-40a3-a3f8-77528257be18"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "DigitalOceanSpaces",
                    Key = "SecretKey",
                    ValueJson = "\"\"",
                    IsSecret = true,
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("b99bd470-792f-4dfd-8e91-bcbdf566bd17"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "DigitalOceanSpaces",
                    Key = "QuarantinePrefix",
                    ValueJson = "\"quarantine\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("fb40c9fd-25de-4c6b-83eb-f249c5a64e8f"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "DigitalOceanSpaces",
                    Key = "ForcePathStyle",
                    ValueJson = "false",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                });
        });
    }

    private static void ConfigurePlatform(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlatformStaff>(entity =>
        {
            entity.ToTable("platform_staff");
            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("uq_platform_staff_email");
            entity.Property(e => e.Email).HasColumnType("citext").IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.FullName).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Role).HasConversion<short>().IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastLoginAt).HasColumnType("timestamptz");
            entity.Property(e => e.DisabledAt).HasColumnType("timestamptz");
        });

        modelBuilder.Entity<PlatformOrganizationInterest>(entity =>
        {
            entity.ToTable("platform_organization_interests");
            entity.HasIndex(e => new { e.Status, e.CreatedAt }).HasDatabaseName("ix_platform_interests_status_created");
            entity.HasIndex(e => e.ContactEmail).HasDatabaseName("ix_platform_interests_contact_email");
            entity.Property(e => e.OrganizationName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ContactName).HasMaxLength(160).IsRequired();
            entity.Property(e => e.ContactEmail).HasColumnType("citext").IsRequired();
            entity.Property(e => e.ContactPhone).HasMaxLength(40);
            entity.Property(e => e.Source).HasMaxLength(80);
            entity.Property(e => e.Region).HasMaxLength(80);
            entity.Property(e => e.ExpectedVolume).HasMaxLength(120);
            entity.Property(e => e.Message).HasMaxLength(4000);
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(4000);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ApprovedAt).HasColumnType("timestamptz");
            entity.Property(e => e.RejectedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.AssignedStaff)
                .WithMany()
                .HasForeignKey(e => e.AssignedStaffId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ApprovedOrganization)
                .WithMany()
                .HasForeignKey(e => e.ApprovedOrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlatformRevenueEvent>(entity =>
        {
            entity.ToTable("platform_revenue_events");
            entity.HasIndex(e => new { e.OrganizationId, e.OccurredAt }).HasDatabaseName("ix_platform_revenue_org_occurred");
            entity.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_platform_revenue_occurred");
            entity.HasIndex(e => e.ExternalReference).HasDatabaseName("ix_platform_revenue_external_reference");
            entity.Property(e => e.Type).HasConversion<short>().IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Currency).HasMaxLength(3).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(80).IsRequired();
            entity.Property(e => e.ExternalReference).HasMaxLength(200);
            entity.Property(e => e.OccurredAt).HasColumnType("timestamptz");
            entity.Property(e => e.PeriodStart).HasColumnType("timestamptz");
            entity.Property(e => e.PeriodEnd).HasColumnType("timestamptz");
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.RecordedByStaff)
                .WithMany()
                .HasForeignKey(e => e.RecordedByStaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlatformAuditEvent>(entity =>
        {
            entity.ToTable("platform_audit_events");
            entity.HasIndex(e => new { e.StaffId, e.CreatedAt }).HasDatabaseName("ix_platform_audit_staff_created");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_platform_audit_created");
            entity.Property(e => e.EventType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.EventData).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.IpAddress).HasColumnType("inet");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Staff)
                .WithMany()
                .HasForeignKey(e => e.StaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private void ConfigureQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Organization>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.Id == CurrentOrganizationId
                && e.DeletedAt == null);
        modelBuilder.Entity<OrganizationUser>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<Template>()
            .HasQueryFilter(e => e.DeletedAt == null
                && (e.OrganizationId == null
                    || e.OrganizationId == CurrentOrganizationId));
        modelBuilder.Entity<TemplateVersion>()
            .HasQueryFilter(e => e.Template != null
                && e.Template.DeletedAt == null
                && (e.Template.OrganizationId == null
                    || e.Template.OrganizationId == CurrentOrganizationId));
        modelBuilder.Entity<TemplateRequirement>()
            .HasQueryFilter(e => e.TemplateVersion != null
                && e.TemplateVersion.Template != null
                && e.TemplateVersion.Template.DeletedAt == null
                && (e.TemplateVersion.Template.OrganizationId == null
                    || e.TemplateVersion.Template.OrganizationId == CurrentOrganizationId));
        modelBuilder.Entity<ChecklistAction>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.OrganizationId == CurrentOrganizationId
                && e.DeletedAt == null);
        modelBuilder.Entity<ActionRecipient>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.Action != null
                && e.Action.OrganizationId == CurrentOrganizationId
                && e.Action.DeletedAt == null);
        modelBuilder.Entity<RecipientAccessSession>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.ActionRecipient != null
                && e.ActionRecipient.Action != null
                && e.ActionRecipient.Action.OrganizationId == CurrentOrganizationId
                && e.ActionRecipient.Action.DeletedAt == null);
        modelBuilder.Entity<Requirement>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.Action != null
                && e.Action.OrganizationId == CurrentOrganizationId
                && e.Action.DeletedAt == null);
        modelBuilder.Entity<DraftResponse>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.ActionRecipient != null
                && e.ActionRecipient.Action != null
                && e.ActionRecipient.Action.OrganizationId == CurrentOrganizationId
                && e.ActionRecipient.Action.DeletedAt == null);
        modelBuilder.Entity<Submission>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.Action != null
                && e.Action.OrganizationId == CurrentOrganizationId
                && e.Action.DeletedAt == null);
        modelBuilder.Entity<SubmissionResponseEntry>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.Submission != null
                && e.Submission.Action != null
                && e.Submission.Action.OrganizationId == CurrentOrganizationId
                && e.Submission.Action.DeletedAt == null);
        modelBuilder.Entity<SubmissionFile>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.Submission != null
                && e.Submission.Action != null
                && e.Submission.Action.OrganizationId == CurrentOrganizationId
                && e.Submission.Action.DeletedAt == null);
        modelBuilder.Entity<FileAsset>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.OrganizationId == CurrentOrganizationId
                && e.DeletedAt == null);
        modelBuilder.Entity<ReminderSchedule>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.ActionRecipient != null
                && e.ActionRecipient.Action != null
                && e.ActionRecipient.Action.OrganizationId == CurrentOrganizationId
                && e.ActionRecipient.Action.DeletedAt == null);
        modelBuilder.Entity<NotificationDelivery>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<AuditEvent>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<ApiKey>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<WebhookEndpoint>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<WebhookDelivery>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.WebhookEndpoint != null
                && e.WebhookEndpoint.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<UsageEvent>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<IdempotencyRecord>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<AdminSetting>()
            .HasQueryFilter(e => e.OrganizationId == null
                || e.OrganizationId == CurrentOrganizationId);
    }

    private static void ConfigureIds(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(Entity.Id))
                .ValueGeneratedNever();
        }
    }

    private void ApplyTimestamps()
    {
        var now = clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                switch (entry.Entity)
                {
                    case Organization organization:
                        organization.CreatedAt = organization.CreatedAt == default ? now : organization.CreatedAt;
                        organization.UpdatedAt = now;
                        break;
                    case Template template:
                        template.CreatedAt = template.CreatedAt == default ? now : template.CreatedAt;
                        template.UpdatedAt = now;
                        break;
                    case ChecklistAction action:
                        action.CreatedAt = action.CreatedAt == default ? now : action.CreatedAt;
                        action.UpdatedAt = now;
                        break;
                    case AdminSetting setting:
                        setting.CreatedAt = setting.CreatedAt == default ? now : setting.CreatedAt;
                        setting.UpdatedAt = now;
                        break;
                    case PlatformStaff staff:
                        staff.CreatedAt = staff.CreatedAt == default ? now : staff.CreatedAt;
                        staff.UpdatedAt = now;
                        break;
                    case PlatformOrganizationInterest interest:
                        interest.CreatedAt = interest.CreatedAt == default ? now : interest.CreatedAt;
                        interest.UpdatedAt = now;
                        break;
                    case PlatformRevenueEvent revenueEvent:
                        revenueEvent.CreatedAt = revenueEvent.CreatedAt == default ? now : revenueEvent.CreatedAt;
                        revenueEvent.UpdatedAt = now;
                        break;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                switch (entry.Entity)
                {
                    case Organization organization:
                        organization.UpdatedAt = now;
                        break;
                    case Template template:
                        template.UpdatedAt = now;
                        break;
                    case ChecklistAction action:
                        action.UpdatedAt = now;
                        break;
                    case AdminSetting setting:
                        setting.UpdatedAt = now;
                        break;
                    case PlatformStaff staff:
                        staff.UpdatedAt = now;
                        break;
                    case PlatformOrganizationInterest interest:
                        interest.UpdatedAt = now;
                        break;
                    case PlatformRevenueEvent revenueEvent:
                        revenueEvent.UpdatedAt = now;
                        break;
                }
            }
        }
    }
}
