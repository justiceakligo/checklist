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
    public DbSet<UserAuthToken> UserAuthTokens => Set<UserAuthToken>();
    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();
    public DbSet<OrganizationTeam> OrganizationTeams => Set<OrganizationTeam>();
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
    public DbSet<SubmissionPackage> SubmissionPackages => Set<SubmissionPackage>();
    public DbSet<Destination> Destinations => Set<Destination>();
    public DbSet<TemplateRoutingRule> TemplateRoutingRules => Set<TemplateRoutingRule>();
    public DbSet<DeliveryJob> DeliveryJobs => Set<DeliveryJob>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<ManualHandoff> ManualHandoffs => Set<ManualHandoff>();
    public DbSet<PlatformStaff> PlatformStaff => Set<PlatformStaff>();
    public DbSet<PlatformOrganizationInterest> PlatformOrganizationInterests => Set<PlatformOrganizationInterest>();
    public DbSet<PlatformRevenueEvent> PlatformRevenueEvents => Set<PlatformRevenueEvent>();
    public DbSet<PlatformAuditEvent> PlatformAuditEvents => Set<PlatformAuditEvent>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<InvestorReport> InvestorReports => Set<InvestorReport>();
    public DbSet<WhatsAppConnection> WhatsAppConnections => Set<WhatsAppConnection>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();
    public DbSet<ConversationAssignment> ConversationAssignments => Set<ConversationAssignment>();
    public DbSet<InternalNote> InternalNotes => Set<InternalNote>();
    public DbSet<FollowUp> FollowUps => Set<FollowUp>();
    public DbSet<ConversationWebhookEvent> ConversationWebhookEvents => Set<ConversationWebhookEvent>();
    public DbSet<ConversationActivity> ConversationActivities => Set<ConversationActivity>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<ConversationLink> ConversationLinks => Set<ConversationLink>();

    private Guid? CurrentOrganizationId => tenantContext.OrganizationId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        ConfigureOrganizations(modelBuilder);
        ConfigureUsers(modelBuilder);
        ConfigureTeams(modelBuilder);
        ConfigureTemplates(modelBuilder);
        ConfigureActions(modelBuilder);
        ConfigureSubmissions(modelBuilder);
        ConfigureFilesAndNotifications(modelBuilder);
        ConfigureDeveloperAndAudit(modelBuilder);
        ConfigureAdminSettings(modelBuilder);
        ConfigurePackagesAndRouting(modelBuilder);
        ConfigurePlatform(modelBuilder);
        ConfigureAnalytics(modelBuilder);
        ConfigureConversations(modelBuilder);
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
            entity.Property(e => e.DeveloperAccessStatus).HasConversion<short>().IsRequired();
            entity.Property(e => e.DeveloperProductionRequestedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeveloperProductionApprovedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeveloperProductionRejectedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeveloperProductionNotes).HasMaxLength(1000);
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

        modelBuilder.Entity<UserAuthToken>(entity =>
        {
            entity.ToTable("user_auth_tokens");
            entity.HasIndex(e => e.TokenHash)
                .IsUnique()
                .HasDatabaseName("uq_user_auth_tokens_token_hash");
            entity.HasIndex(e => new { e.UserId, e.Purpose, e.ExpiresAt })
                .HasDatabaseName("ix_user_auth_tokens_user_purpose_expiry");
            entity.Property(e => e.Purpose).HasConversion<short>().IsRequired();
            entity.Property(e => e.TokenHash).IsRequired();
            entity.Property(e => e.Email).HasColumnType("citext").IsRequired();
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.ConsumedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedIpAddress).HasColumnType("inet");
            entity.Property(e => e.ConsumedIpAddress).HasColumnType("inet");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.User)
                .WithMany(e => e.AuthTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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

    private static void ConfigureTeams(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationTeam>(entity =>
        {
            entity.ToTable("organization_teams");
            entity.HasIndex(e => new { e.OrganizationId, e.Name })
                .IsUnique()
                .HasDatabaseName("uq_organization_teams_org_name");
            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.Name })
                .HasDatabaseName("ix_organization_teams_org_status_name");
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
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
            entity.HasIndex(e => new { e.OrganizationId, e.ClientReference })
                .IsUnique()
                .HasDatabaseName("uq_actions_org_client_reference")
                .HasFilter("client_reference IS NOT NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.CreatedAt })
                .HasDatabaseName("ix_actions_org_status");
            entity.HasIndex(e => new { e.OrganizationId, e.DueAt })
                .HasDatabaseName("ix_actions_org_due")
                .HasFilter("deleted_at IS NULL AND due_at IS NOT NULL AND status IN (2, 3)");
            entity.Property(e => e.PublicReference).HasMaxLength(32).IsRequired();
            entity.Property(e => e.ClientReference).HasMaxLength(160);
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
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
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
            entity.Property(e => e.DeclarationAcceptedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeclarationIpAddress).HasColumnType("inet");
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
            entity.HasIndex(e => e.SourceSubmissionId).HasDatabaseName("ix_submission_files_source_submission");
            entity.HasIndex(e => e.SourceSubmissionFileId).HasDatabaseName("ix_submission_files_source_file");
            entity.Property(e => e.DocumentName).HasMaxLength(255);
            entity.Property(e => e.ReuseConsentText).HasMaxLength(500);
            entity.Property(e => e.ReuseConsentIpAddress).HasColumnType("inet");
            entity.Property(e => e.ReuseConfirmedAt).HasColumnType("timestamptz");
            entity.Property(e => e.DocumentExpiresAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Submission)
                .WithMany(e => e.Files)
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.SourceSubmission)
                .WithMany()
                .HasForeignKey(e => e.SourceSubmissionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.SourceSubmissionFile)
                .WithMany()
                .HasForeignKey(e => e.SourceSubmissionFileId)
                .OnDelete(DeleteBehavior.SetNull);
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
            entity.Property(e => e.KeyPrefix).HasMaxLength(32).IsRequired();
            entity.Property(e => e.SecretHash).IsRequired();
            entity.Property(e => e.Environment).HasConversion<short>().IsRequired();
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
            var billingPlansJson =
                """
                {
                  "plans": [
                    {
                      "code": "free",
                      "name": "Free",
                      "description": "Try Reqara on real requests.",
                      "monthlyPriceCents": 0,
                      "annualPriceCents": 0,
                      "currency": "CAD",
                      "monthlyChecklistLimit": 10,
                      "storageBytes": 524288000,
                      "customPricing": false,
                      "features": {
                        "teamWorkspace": true,
                        "templateLibrary": true,
                        "reqaraBranding": true,
                        "customBranding": false,
                        "automaticReminders": false,
                        "apiAndWebhooks": false,
                        "customWorkflows": false,
                        "prioritySupport": false,
                        "sso": false,
                        "customRetention": false,
                        "securityReview": false,
                        "dpa": false,
                        "dedicatedOnboarding": false
                      }
                    },
                    {
                      "code": "starter",
                      "name": "Starter",
                      "description": "For solo pros and small teams.",
                      "monthlyPriceCents": 3900,
                      "annualPriceCents": 39000,
                      "currency": "CAD",
                      "monthlyChecklistLimit": 100,
                      "storageBytes": 5368709120,
                      "customPricing": false,
                      "features": {
                        "teamWorkspace": true,
                        "templateLibrary": true,
                        "reqaraBranding": false,
                        "customBranding": true,
                        "automaticReminders": true,
                        "apiAndWebhooks": false,
                        "customWorkflows": false,
                        "prioritySupport": false,
                        "sso": false,
                        "customRetention": false,
                        "securityReview": false,
                        "dpa": false,
                        "dedicatedOnboarding": false
                      }
                    },
                    {
                      "code": "business",
                      "name": "Business",
                      "description": "For teams sending every day.",
                      "monthlyPriceCents": 9900,
                      "annualPriceCents": 99000,
                      "currency": "CAD",
                      "monthlyChecklistLimit": 500,
                      "storageBytes": 26843545600,
                      "customPricing": false,
                      "features": {
                        "teamWorkspace": true,
                        "templateLibrary": true,
                        "reqaraBranding": false,
                        "customBranding": true,
                        "automaticReminders": true,
                        "apiAndWebhooks": true,
                        "customWorkflows": true,
                        "prioritySupport": true,
                        "sso": false,
                        "customRetention": false,
                        "securityReview": false,
                        "dpa": false,
                        "dedicatedOnboarding": false
                      }
                    },
                    {
                      "code": "scale",
                      "name": "Scale",
                      "description": "For regulated and high-volume use.",
                      "monthlyPriceCents": null,
                      "annualPriceCents": null,
                      "currency": "CAD",
                      "monthlyChecklistLimit": null,
                      "storageBytes": null,
                      "customPricing": true,
                      "features": {
                        "teamWorkspace": true,
                        "templateLibrary": true,
                        "reqaraBranding": false,
                        "customBranding": true,
                        "automaticReminders": true,
                        "apiAndWebhooks": true,
                        "customWorkflows": true,
                        "prioritySupport": true,
                        "sso": true,
                        "customRetention": true,
                        "securityReview": true,
                        "dpa": true,
                        "dedicatedOnboarding": true
                      }
                    }
                  ]
                }
                """;
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
                    Id = Guid.Parse("9dd99ff5-55cb-4f8b-b18a-1b39a437b195"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "turnstile",
                    Key = "siteKey",
                    ValueJson = "\"0x4AAAAAAD3PDppjZsCXGkx4\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("395f0214-a89e-4497-85f4-6904a4463cd6"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "turnstile",
                    Key = "secretKey",
                    ValueJson = "\"\"",
                    IsSecret = true,
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("050eb9f7-7445-4e7f-ad37-0c7e9f835257"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "publicContact",
                    Key = "toEmail",
                    ValueJson = "\"hello@nextronyx.com\"",
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
                    ValueJson = "\"requests@reqara.com\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("6505a485-c0d9-4d29-8f00-7509f8a4bfd9"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "Email:Resend",
                    Key = "FromName",
                    ValueJson = "\"Reqara\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("c64f642f-a12a-4138-a963-178e6f072ea5"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "Email",
                    Key = "ReplyTo",
                    ValueJson = "\"support@reqara.com\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("3cfbb448-a95f-4d94-89b8-d15b81a8bace"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "app",
                    Key = "baseUrl",
                    ValueJson = "\"https://reqara.com\"",
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
                },
                new AdminSetting
                {
                    Id = Guid.Parse("2c3051c8-b7d3-4e8a-bd6f-48d34de91201"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "billing",
                    Key = "defaultPlanCode",
                    ValueJson = "\"free\"",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("49da403e-f91d-4f17-bdb0-ef52117edeb4"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "billing",
                    Key = "plans",
                    ValueJson = billingPlansJson,
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("e2b64e8a-7b31-443b-84d8-5a8539db9eed"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "reminders",
                    Key = "beforeDueDays",
                    ValueJson = "3",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("ed4541b0-14bf-4a5a-b74c-19f18ce1223e"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "reminders",
                    Key = "overdueDays",
                    ValueJson = "1",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("4f7ea49d-6d39-4ae7-8d73-6283d2598131"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "reminders",
                    Key = "dispatchIntervalSeconds",
                    ValueJson = "60",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("4d0cf604-c972-49dd-965f-50dd5d0638c2"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "retention",
                    Key = "purgeIntervalMinutes",
                    ValueJson = "60",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("2399ef93-7023-43a8-901e-b326ca953d29"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "documentReuse",
                    Key = "enabled",
                    ValueJson = "true",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("a54fb28b-f38c-47c2-80f4-aae9943ca573"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "documentReuse",
                    Key = "maximumAgeDays",
                    ValueJson = "365",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("b49d89e2-6874-481b-9325-f7f2ed11f2d1"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "documentReuse",
                    Key = "requireRecipientConfirmation",
                    ValueJson = "true",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("426602a2-af96-45fd-a44e-f98038f9383d"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "documentReuse",
                    Key = "reuseExpiredDocuments",
                    ValueJson = "false",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("57a3ce93-90dd-42a5-8717-b36e5e1390d7"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "packageExport",
                    Key = "maxFileCount",
                    ValueJson = "100",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("c4b4d3cc-23e4-4421-bc99-639f7978038d"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "packageExport",
                    Key = "maxZipBytes",
                    ValueJson = "262144000",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("24801c38-0bfe-4404-aecb-7b2ce479706a"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "actions",
                    Key = "duplicateWindowMinutes",
                    ValueJson = "10",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("38a0758f-080a-4f7f-8ee3-b95e37b61f66"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "auth",
                    Key = "emailVerificationMinutes",
                    ValueJson = "1440",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("216922b7-3c5d-4fdd-a4fb-f26878f378f1"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "auth",
                    Key = "passwordResetMinutes",
                    ValueJson = "30",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("abf05d8e-6077-4615-8c35-c661fa414cd2"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "recipientOtp",
                    Key = "defaultRequired",
                    ValueJson = "false",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                },
                new AdminSetting
                {
                    Id = Guid.Parse("cb7399c6-3275-45d6-a5e8-f59d923208f2"),
                    Scope = Domain.Enums.AdminSettingScope.System,
                    Category = "developer",
                    Key = "apiKeyDefaultDays",
                    ValueJson = "180",
                    CreatedAt = seededAt,
                    UpdatedAt = seededAt
                });
        });
    }

    private static void ConfigurePackagesAndRouting(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SubmissionPackage>(entity =>
        {
            entity.ToTable("submission_packages");
            entity.HasIndex(e => e.PackageReference).IsUnique().HasDatabaseName("uq_submission_packages_reference");
            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.AcceptedAt }).HasDatabaseName("ix_submission_packages_org_status");
            entity.HasIndex(e => new { e.ActionId, e.VersionNumber }).HasDatabaseName("ix_submission_packages_action_version");
            entity.HasIndex(e => e.SubmissionId).IsUnique().HasDatabaseName("uq_submission_packages_submission");
            entity.Property(e => e.PackageReference).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.AcceptedAt).HasColumnType("timestamptz");
            entity.Property(e => e.GeneratedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ContentHash).HasMaxLength(128);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.RevisionReason).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ArchivedAt).HasColumnType("timestamptz");
            entity.Property(e => e.RowVersion).IsConcurrencyToken();
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Action)
                .WithMany()
                .HasForeignKey(e => e.ActionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Submission)
                .WithMany()
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ActionRecipient)
                .WithMany()
                .HasForeignKey(e => e.ActionRecipientId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.AcceptedByUser)
                .WithMany()
                .HasForeignKey(e => e.AcceptedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OwnerUser)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ReportFileAsset)
                .WithMany()
                .HasForeignKey(e => e.ReportFileAssetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.BundleFileAsset)
                .WithMany()
                .HasForeignKey(e => e.BundleFileAssetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.PreviousPackage)
                .WithMany()
                .HasForeignKey(e => e.PreviousPackageId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.SupersededByPackage)
                .WithMany()
                .HasForeignKey(e => e.SupersededByPackageId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Destination>(entity =>
        {
            entity.ToTable("destinations");
            entity.HasIndex(e => new { e.OrganizationId, e.Type, e.IsActive }).HasDatabaseName("ix_destinations_org_type_active");
            entity.HasIndex(e => new { e.OrganizationId, e.Name }).HasDatabaseName("ix_destinations_org_name");
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Type).HasConversion<short>().IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.ConfigurationJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.ConfigurationEncrypted);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastValidatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastValidationStatus).HasMaxLength(30);
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TemplateRoutingRule>(entity =>
        {
            entity.ToTable("template_routing_rules");
            entity.HasIndex(e => new { e.OrganizationId, e.TemplateId, e.Sequence }).HasDatabaseName("ix_template_routing_rules_template_order");
            entity.Property(e => e.Trigger).HasConversion<short>().IsRequired();
            entity.Property(e => e.ConfigurationOverridesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Template)
                .WithMany()
                .HasForeignKey(e => e.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Destination)
                .WithMany(e => e.RoutingRules)
                .HasForeignKey(e => e.DestinationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeliveryJob>(entity =>
        {
            entity.ToTable("delivery_jobs");
            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.QueuedAt }).HasDatabaseName("ix_delivery_jobs_org_status");
            entity.HasIndex(e => new { e.OrganizationId, e.IdempotencyKey }).IsUnique().HasDatabaseName("uq_delivery_jobs_org_idempotency");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.TriggeredBy).HasMaxLength(60).IsRequired();
            entity.Property(e => e.QueuedAt).HasColumnType("timestamptz");
            entity.Property(e => e.StartedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CompletedAt).HasColumnType("timestamptz");
            entity.Property(e => e.NextRetryAt).HasColumnType("timestamptz");
            entity.Property(e => e.FailureCode).HasMaxLength(100);
            entity.Property(e => e.FailureMessage).HasMaxLength(1000);
            entity.Property(e => e.ExternalReference).HasMaxLength(200);
            entity.Property(e => e.IdempotencyKey).HasMaxLength(160).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SubmissionPackage)
                .WithMany(e => e.DeliveryJobs)
                .HasForeignKey(e => e.SubmissionPackageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Destination)
                .WithMany(e => e.DeliveryJobs)
                .HasForeignKey(e => e.DestinationId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.RequestedByUser)
                .WithMany()
                .HasForeignKey(e => e.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DeliveryAttempt>(entity =>
        {
            entity.ToTable("delivery_attempts");
            entity.HasIndex(e => new { e.DeliveryJobId, e.AttemptNumber }).IsUnique().HasDatabaseName("uq_delivery_attempts_job_attempt");
            entity.Property(e => e.StartedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CompletedAt).HasColumnType("timestamptz");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.ProviderReference).HasMaxLength(200);
            entity.Property(e => e.ResponseSummary).HasMaxLength(2000);
            entity.Property(e => e.FailureCode).HasMaxLength(100);
            entity.Property(e => e.FailureMessage).HasMaxLength(1000);
            entity.HasOne(e => e.DeliveryJob)
                .WithMany(e => e.Attempts)
                .HasForeignKey(e => e.DeliveryJobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ManualHandoff>(entity =>
        {
            entity.ToTable("manual_handoffs");
            entity.HasIndex(e => new { e.OrganizationId, e.SubmissionPackageId, e.CompletedAt }).HasDatabaseName("ix_manual_handoffs_package");
            entity.Property(e => e.DestinationLabel).HasMaxLength(120).IsRequired();
            entity.Property(e => e.CompletedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ExternalReference).HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SubmissionPackage)
                .WithMany(e => e.ManualHandoffs)
                .HasForeignKey(e => e.SubmissionPackageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CompletedByUser)
                .WithMany()
                .HasForeignKey(e => e.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EvidenceFileAsset)
                .WithMany()
                .HasForeignKey(e => e.EvidenceFileAssetId)
                .OnDelete(DeleteBehavior.SetNull);
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

    private static void ConfigureAnalytics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.ToTable("analytics_events");
            entity.HasIndex(e => new { e.OrganizationId, e.OccurredAt }).HasDatabaseName("ix_analytics_events_org_occurred");
            entity.HasIndex(e => new { e.EventName, e.OccurredAt }).HasDatabaseName("ix_analytics_events_name_occurred");
            entity.HasIndex(e => e.ChecklistId).HasDatabaseName("ix_analytics_events_checklist");
            entity.HasIndex(e => e.TemplateId).HasDatabaseName("ix_analytics_events_template");
            entity.HasIndex(e => e.RecipientId).HasDatabaseName("ix_analytics_events_recipient");
            entity.Property(e => e.EventName).HasMaxLength(160).IsRequired();
            entity.Property(e => e.SessionId).HasMaxLength(160);
            entity.Property(e => e.PropertiesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.OccurredAt).HasColumnType("timestamptz");
            entity.Property(e => e.ReceivedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<InvestorReport>(entity =>
        {
            entity.ToTable("investor_reports");
            entity.HasIndex(e => new { e.ReportType, e.Period }).HasDatabaseName("ix_investor_reports_type_period");
            entity.HasIndex(e => new { e.RequestedByStaffId, e.CreatedAt }).HasDatabaseName("ix_investor_reports_staff_created");
            entity.Property(e => e.ReportType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Period).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.ResultJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CompletedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.RequestedByStaff)
                .WithMany()
                .HasForeignKey(e => e.RequestedByStaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureConversations(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WhatsAppConnection>(entity =>
        {
            entity.ToTable("whatsapp_connections");
            entity.HasIndex(e => new { e.OrganizationId, e.PhoneNumberId })
                .IsUnique()
                .HasDatabaseName("uq_whatsapp_connections_org_phone_number");
            entity.HasIndex(e => new { e.OrganizationId, e.Status })
                .HasDatabaseName("ix_whatsapp_connections_org_status");
            entity.Property(e => e.PhoneNumberId).HasMaxLength(80).IsRequired();
            entity.Property(e => e.WabaId).HasMaxLength(80).IsRequired();
            entity.Property(e => e.DisplayNumber).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.SecretReference).HasMaxLength(300);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.VerifiedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastValidatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastInboundAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastOutboundAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contacts");
            entity.HasIndex(e => new { e.OrganizationId, e.NormalizedPhone })
                .IsUnique()
                .HasDatabaseName("uq_contacts_org_phone");
            entity.Property(e => e.NormalizedPhone).HasMaxLength(40).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(180);
            entity.Property(e => e.ProfileJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Connection)
                .WithMany(e => e.Contacts)
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("leads");
            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.UpdatedAt })
                .HasDatabaseName("ix_leads_org_status_updated");
            entity.HasIndex(e => new { e.OrganizationId, e.ContactId })
                .HasDatabaseName("ix_leads_org_contact");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.Source).HasMaxLength(80).IsRequired();
            entity.Property(e => e.CampaignRef).HasMaxLength(160);
            entity.Property(e => e.LostReason).HasMaxLength(500);
            entity.Property(e => e.WonAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Contact)
                .WithMany(e => e.Leads)
                .HasForeignKey(e => e.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasIndex(e => new { e.OrganizationId, e.AssignedUserId, e.LastMessageAt })
                .HasDatabaseName("ix_conversations_org_assignee_last_message");
            entity.HasIndex(e => new { e.OrganizationId, e.Status, e.LastMessageAt })
                .HasDatabaseName("ix_conversations_org_status_last_message");
            entity.HasIndex(e => new { e.OrganizationId, e.ConnectionId, e.ContactId })
                .HasDatabaseName("ix_conversations_org_connection_contact");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.LastMessageAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastInboundMessageAt).HasColumnType("timestamptz");
            entity.Property(e => e.LastOutboundMessageAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ClosedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Organization)
                .WithMany()
                .HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Connection)
                .WithMany(e => e.Conversations)
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Contact)
                .WithMany(e => e.Conversations)
                .HasForeignKey(e => e.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Lead)
                .WithMany(e => e.Conversations)
                .HasForeignKey(e => e.LeadId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.AssignedUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConversationMessage>(entity =>
        {
            entity.ToTable("conversation_messages");
            entity.HasIndex(e => new { e.OrganizationId, e.ProviderMessageId })
                .IsUnique()
                .HasDatabaseName("uq_conversation_messages_org_provider_id")
                .HasFilter("provider_message_id IS NOT NULL");
            entity.HasIndex(e => new { e.OrganizationId, e.ConversationId, e.CreatedAt })
                .HasDatabaseName("ix_conversation_messages_conversation_created");
            entity.Property(e => e.ProviderMessageId).HasMaxLength(160);
            entity.Property(e => e.Direction).HasConversion<short>().IsRequired();
            entity.Property(e => e.Type).HasConversion<short>().IsRequired();
            entity.Property(e => e.Body).HasMaxLength(8000);
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.SentAt).HasColumnType("timestamptz");
            entity.Property(e => e.DeliveredAt).HasColumnType("timestamptz");
            entity.Property(e => e.ReadAt).HasColumnType("timestamptz");
            entity.Property(e => e.FailedAt).HasColumnType("timestamptz");
            entity.Property(e => e.FailureCode).HasMaxLength(100);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Conversation)
                .WithMany(e => e.Messages)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConversationAssignment>(entity =>
        {
            entity.ToTable("conversation_assignments");
            entity.HasIndex(e => new { e.OrganizationId, e.ConversationId, e.AssignedAt })
                .HasDatabaseName("ix_conversation_assignments_conversation_assigned");
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.AssignedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Conversation)
                .WithMany(e => e.Assignments)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FromUser)
                .WithMany()
                .HasForeignKey(e => e.FromUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ToUser)
                .WithMany()
                .HasForeignKey(e => e.ToUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.AssignedByUser)
                .WithMany()
                .HasForeignKey(e => e.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InternalNote>(entity =>
        {
            entity.ToTable("conversation_internal_notes");
            entity.HasIndex(e => new { e.OrganizationId, e.ConversationId, e.CreatedAt })
                .HasDatabaseName("ix_conversation_notes_conversation_created");
            entity.Property(e => e.Body).HasMaxLength(8000).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Conversation)
                .WithMany(e => e.InternalNotes)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.AuthorUser)
                .WithMany()
                .HasForeignKey(e => e.AuthorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FollowUp>(entity =>
        {
            entity.ToTable("conversation_follow_ups");
            entity.HasIndex(e => new { e.OrganizationId, e.OwnerUserId, e.DueAt })
                .HasDatabaseName("ix_conversation_followups_owner_due")
                .HasFilter("status = 1");
            entity.HasIndex(e => new { e.OrganizationId, e.ConversationId, e.Status })
                .HasDatabaseName("ix_conversation_followups_conversation_status");
            entity.Property(e => e.DueAt).HasColumnType("timestamptz");
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.Note).HasMaxLength(2000);
            entity.Property(e => e.CompletedAt).HasColumnType("timestamptz");
            entity.Property(e => e.CancelledAt).HasColumnType("timestamptz");
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Conversation)
                .WithMany(e => e.FollowUps)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.OwnerUser)
                .WithMany()
                .HasForeignKey(e => e.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConversationWebhookEvent>(entity =>
        {
            entity.ToTable("conversation_webhook_events");
            entity.HasIndex(e => e.ProviderEventId)
                .IsUnique()
                .HasDatabaseName("uq_conversation_webhook_events_provider_event");
            entity.HasIndex(e => new { e.OrganizationId, e.ReceivedAt })
                .HasDatabaseName("ix_conversation_webhook_events_org_received");
            entity.Property(e => e.ProviderEventId).HasMaxLength(180).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.ProcessingStatus).HasConversion<short>().IsRequired();
            entity.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.FailureCode).HasMaxLength(100);
            entity.Property(e => e.FailureMessage).HasMaxLength(1000);
            entity.Property(e => e.ReceivedAt).HasColumnType("timestamptz");
            entity.Property(e => e.ProcessedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Connection)
                .WithMany()
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConversationActivity>(entity =>
        {
            entity.ToTable("conversation_activities");
            entity.HasIndex(e => new { e.OrganizationId, e.ConversationId, e.CreatedAt })
                .HasDatabaseName("ix_conversation_activities_conversation_created");
            entity.Property(e => e.Type).HasMaxLength(120).IsRequired();
            entity.Property(e => e.ActorType).HasConversion<short>().IsRequired();
            entity.Property(e => e.ActorId).HasMaxLength(160);
            entity.Property(e => e.MetadataJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Conversation)
                .WithMany(e => e.Activities)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageTemplate>(entity =>
        {
            entity.ToTable("conversation_message_templates");
            entity.HasIndex(e => new { e.OrganizationId, e.Name, e.Language })
                .IsUnique()
                .HasDatabaseName("uq_conversation_templates_org_name_language");
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Language).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Status).HasConversion<short>().IsRequired();
            entity.Property(e => e.ProviderTemplateId).HasMaxLength(160);
            entity.Property(e => e.ComponentsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.Property(e => e.UpdatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Connection)
                .WithMany(e => e.MessageTemplates)
                .HasForeignKey(e => e.ConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ConversationLink>(entity =>
        {
            entity.ToTable("conversation_links");
            entity.HasIndex(e => new { e.OrganizationId, e.ConversationId, e.LinkType })
                .HasDatabaseName("ix_conversation_links_conversation_type");
            entity.Property(e => e.LinkType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.ExternalReference).HasMaxLength(300);
            entity.Property(e => e.CreatedAt).HasColumnType("timestamptz");
            entity.HasOne(e => e.Conversation)
                .WithMany(e => e.Links)
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
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
        modelBuilder.Entity<OrganizationTeam>()
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
        modelBuilder.Entity<SubmissionPackage>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<Destination>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<TemplateRoutingRule>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<DeliveryJob>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<DeliveryAttempt>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue
                && e.DeliveryJob != null
                && e.DeliveryJob.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<ManualHandoff>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<WhatsAppConnection>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<Contact>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<Lead>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<Conversation>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<ConversationMessage>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<ConversationAssignment>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<InternalNote>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<FollowUp>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<ConversationWebhookEvent>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<ConversationActivity>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<MessageTemplate>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<ConversationLink>()
            .HasQueryFilter(e => CurrentOrganizationId.HasValue && e.OrganizationId == CurrentOrganizationId);
        modelBuilder.Entity<AnalyticsEvent>()
            .HasQueryFilter(e => e.OrganizationId == CurrentOrganizationId);
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
                    case OrganizationTeam team:
                        team.CreatedAt = team.CreatedAt == default ? now : team.CreatedAt;
                        team.UpdatedAt = now;
                        break;
                    case AdminSetting setting:
                        setting.CreatedAt = setting.CreatedAt == default ? now : setting.CreatedAt;
                        setting.UpdatedAt = now;
                        break;
                    case SubmissionPackage package:
                        package.CreatedAt = package.CreatedAt == default ? now : package.CreatedAt;
                        package.UpdatedAt = now;
                        package.RowVersion = package.RowVersion <= 0 ? 1 : package.RowVersion;
                        break;
                    case Destination destination:
                        destination.CreatedAt = destination.CreatedAt == default ? now : destination.CreatedAt;
                        destination.UpdatedAt = now;
                        break;
                    case TemplateRoutingRule routingRule:
                        routingRule.CreatedAt = routingRule.CreatedAt == default ? now : routingRule.CreatedAt;
                        routingRule.UpdatedAt = now;
                        break;
                    case DeliveryJob deliveryJob:
                        deliveryJob.CreatedAt = deliveryJob.CreatedAt == default ? now : deliveryJob.CreatedAt;
                        deliveryJob.UpdatedAt = now;
                        deliveryJob.QueuedAt = deliveryJob.QueuedAt == default ? now : deliveryJob.QueuedAt;
                        break;
                    case ManualHandoff manualHandoff:
                        manualHandoff.CreatedAt = manualHandoff.CreatedAt == default ? now : manualHandoff.CreatedAt;
                        manualHandoff.CompletedAt = manualHandoff.CompletedAt == default ? now : manualHandoff.CompletedAt;
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
                    case AnalyticsEvent analyticsEvent:
                        analyticsEvent.ReceivedAt = analyticsEvent.ReceivedAt == default ? now : analyticsEvent.ReceivedAt;
                        analyticsEvent.OccurredAt = analyticsEvent.OccurredAt == default ? now : analyticsEvent.OccurredAt;
                        break;
                    case InvestorReport investorReport:
                        investorReport.CreatedAt = investorReport.CreatedAt == default ? now : investorReport.CreatedAt;
                        investorReport.UpdatedAt = now;
                        break;
                    case WhatsAppConnection connection:
                        connection.CreatedAt = connection.CreatedAt == default ? now : connection.CreatedAt;
                        connection.UpdatedAt = now;
                        break;
                    case Contact contact:
                        contact.CreatedAt = contact.CreatedAt == default ? now : contact.CreatedAt;
                        contact.UpdatedAt = now;
                        break;
                    case Lead lead:
                        lead.CreatedAt = lead.CreatedAt == default ? now : lead.CreatedAt;
                        lead.UpdatedAt = now;
                        break;
                    case Conversation conversation:
                        conversation.CreatedAt = conversation.CreatedAt == default ? now : conversation.CreatedAt;
                        conversation.UpdatedAt = now;
                        break;
                    case ConversationMessage message:
                        message.CreatedAt = message.CreatedAt == default ? now : message.CreatedAt;
                        message.UpdatedAt = now;
                        break;
                    case ConversationAssignment assignment:
                        assignment.AssignedAt = assignment.AssignedAt == default ? now : assignment.AssignedAt;
                        break;
                    case InternalNote note:
                        note.CreatedAt = note.CreatedAt == default ? now : note.CreatedAt;
                        break;
                    case FollowUp followUp:
                        followUp.CreatedAt = followUp.CreatedAt == default ? now : followUp.CreatedAt;
                        followUp.UpdatedAt = now;
                        break;
                    case ConversationWebhookEvent webhookEvent:
                        webhookEvent.ReceivedAt = webhookEvent.ReceivedAt == default ? now : webhookEvent.ReceivedAt;
                        break;
                    case ConversationActivity activity:
                        activity.CreatedAt = activity.CreatedAt == default ? now : activity.CreatedAt;
                        break;
                    case MessageTemplate messageTemplate:
                        messageTemplate.CreatedAt = messageTemplate.CreatedAt == default ? now : messageTemplate.CreatedAt;
                        messageTemplate.UpdatedAt = now;
                        break;
                    case ConversationLink link:
                        link.CreatedAt = link.CreatedAt == default ? now : link.CreatedAt;
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
                    case OrganizationTeam team:
                        team.UpdatedAt = now;
                        break;
                    case AdminSetting setting:
                        setting.UpdatedAt = now;
                        break;
                    case SubmissionPackage package:
                        package.UpdatedAt = now;
                        package.RowVersion++;
                        break;
                    case Destination destination:
                        destination.UpdatedAt = now;
                        break;
                    case TemplateRoutingRule routingRule:
                        routingRule.UpdatedAt = now;
                        break;
                    case DeliveryJob deliveryJob:
                        deliveryJob.UpdatedAt = now;
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
                    case InvestorReport investorReport:
                        investorReport.UpdatedAt = now;
                        break;
                    case WhatsAppConnection connection:
                        connection.UpdatedAt = now;
                        break;
                    case Contact contact:
                        contact.UpdatedAt = now;
                        break;
                    case Lead lead:
                        lead.UpdatedAt = now;
                        break;
                    case Conversation conversation:
                        conversation.UpdatedAt = now;
                        break;
                    case ConversationMessage message:
                        message.UpdatedAt = now;
                        break;
                    case FollowUp followUp:
                        followUp.UpdatedAt = now;
                        break;
                    case MessageTemplate messageTemplate:
                        messageTemplate.UpdatedAt = now;
                        break;
                }
            }
        }
    }
}
