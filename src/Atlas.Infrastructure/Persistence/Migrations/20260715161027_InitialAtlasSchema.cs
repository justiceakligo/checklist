using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAtlasSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    full_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    mfa_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "action_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    access_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    token_expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    otp_required = table.Column<bool>(type: "boolean", nullable: false),
                    first_viewed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_action_recipients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reminder_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_schedules", x => x.id);
                    table.ForeignKey(
                        name: "fk_reminder_schedules_action_recipients_action_recipient_id",
                        column: x => x.action_recipient_id,
                        principalTable: "action_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    public_reference = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actions", x => x.id);
                    table.CheckConstraint("ck_actions_due_after_created", "due_at IS NULL OR due_at >= created_at");
                    table.ForeignKey(
                        name: "fk_actions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_comment = table.Column<string>(type: "text", nullable: true),
                    content_hash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_submissions_action_recipients_action_recipient_id",
                        column: x => x.action_recipient_id,
                        principalTable: "action_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_actions_action_id",
                        column: x => x.action_id,
                        principalTable: "actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_submissions_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "admin_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_secret = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_admin_settings_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    key_prefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    secret_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    scopes = table.Column<string[]>(type: "text[]", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_type = table.Column<short>(type: "smallint", nullable: false),
                    actor_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_data = table.Column<string>(type: "jsonb", nullable: false),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_events_actions_action_id",
                        column: x => x.action_id,
                        principalTable: "actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "draft_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    updated_by_session = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_draft_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_draft_responses_action_recipients_action_recipient_id",
                        column: x => x.action_recipient_id,
                        principalTable: "action_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "file_assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action_recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    scan_status = table.Column<short>(type: "smallint", nullable: false),
                    scan_engine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scan_completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    retention_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_assets", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_assets_action_recipients_action_recipient_id",
                        column: x => x.action_recipient_id,
                        principalTable: "action_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_assets_actions_action_id",
                        column: x => x.action_id,
                        principalTable: "actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    default_language = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accent_color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    privacy_statement = table.Column<string>(type: "text", nullable: true),
                    retention_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                    table.ForeignKey(
                        name: "fk_organizations_file_assets_logo_file_id",
                        column: x => x.logo_file_id,
                        principalTable: "file_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<short>(type: "smallint", nullable: false),
                    template_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_action_recipients_action_recipient_",
                        column: x => x.action_recipient_id,
                        principalTable: "action_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_notification_deliveries_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    invited_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organization_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_organization_users_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_organization_users_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "usage_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_events_actions_action_id",
                        column: x => x.action_id,
                        principalTable: "actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_usage_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    event_types = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoints", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_excerpt = table.Column<string>(type: "text", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_webhook_endpoints_webhook_endpoint_id",
                        column: x => x.webhook_endpoint_id,
                        principalTable: "webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_template_requirement_id = table.Column<Guid>(type: "uuid", nullable: true),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false),
                    validation_json = table.Column<string>(type: "jsonb", nullable: false),
                    condition_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requirements", x => x.id);
                    table.ForeignKey(
                        name: "fk_requirements_actions_action_id",
                        column: x => x.action_id,
                        principalTable: "actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_files_file_assets_file_asset_id",
                        column: x => x.file_asset_id,
                        principalTable: "file_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_files_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_files_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "submission_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requirement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_responses_requirements_requirement_id",
                        column: x => x.requirement_id,
                        principalTable: "requirements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_responses_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_requirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false),
                    validation_json = table.Column<string>(type: "jsonb", nullable: false),
                    condition_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_requirements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "template_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    instructions = table.Column<string>(type: "text", nullable: true),
                    settings_json = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_template_versions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_templates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_templates_template_versions_current_version_id",
                        column: x => x.current_version_id,
                        principalTable: "template_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_templates_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "admin_settings",
                columns: new[] { "id", "category", "created_at", "is_secret", "key", "organization_id", "scope", "updated_at", "updated_by_user_id", "value_json" },
                values: new object[,]
                {
                    { new Guid("25ff9a4f-61f3-4460-a92e-5b0784154b2f"), "security", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "recipientTokenDays", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "30" },
                    { new Guid("8c967ee1-9233-4edc-aa96-961ea6eca16e"), "retention", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "defaultRetentionDays", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "365" },
                    { new Guid("a573c60e-6d96-4204-a451-4c4c3f66ac31"), "files", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "maxUploadBytes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "10485760" },
                    { new Guid("c17b8e86-e95f-4d19-a375-17672ce7c390"), "files", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "uploadUrlMinutes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "10" },
                    { new Guid("cc13c863-f4e5-4c34-9029-9d77b82d713b"), "files", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "downloadUrlMinutes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "5" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_recipients_action_status",
                table: "action_recipients",
                columns: new[] { "action_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_recipients_token_hash",
                table: "action_recipients",
                column: "access_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actions_created_by_user_id",
                table: "actions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_actions_org_due",
                table: "actions",
                columns: new[] { "organization_id", "due_at" },
                filter: "deleted_at IS NULL AND due_at IS NOT NULL AND status IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_actions_org_status",
                table: "actions",
                columns: new[] { "organization_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_actions_template_id",
                table: "actions",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_actions_template_version_id",
                table: "actions",
                column: "template_version_id");

            migrationBuilder.CreateIndex(
                name: "uq_actions_public_reference",
                table: "actions",
                column: "public_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_admin_settings_updated_by_user_id",
                table: "admin_settings",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_admin_settings_global_category_key",
                table: "admin_settings",
                columns: new[] { "category", "key" },
                unique: true,
                filter: "organization_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_admin_settings_org_category_key",
                table: "admin_settings",
                columns: new[] { "organization_id", "category", "key" },
                unique: true,
                filter: "organization_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_org_prefix",
                table: "api_keys",
                columns: new[] { "organization_id", "key_prefix" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_action_created",
                table: "audit_events",
                columns: new[] { "action_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_org_created",
                table: "audit_events",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_draft_responses_requirement_id",
                table: "draft_responses",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "uq_draft_recipient_requirement",
                table: "draft_responses",
                columns: new[] { "action_recipient_id", "requirement_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_file_assets_action_id",
                table: "file_assets",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_assets_action_recipient_id",
                table: "file_assets",
                column: "action_recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_files_org_created",
                table: "file_assets",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_files_scan_status",
                table: "file_assets",
                columns: new[] { "scan_status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_file_assets_storage_key",
                table: "file_assets",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_action_recipient_id",
                table: "notification_deliveries",
                column: "action_recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_organization_id",
                table: "notification_deliveries",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_organization_users_user_id",
                table: "organization_users",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_organization_users_org_user",
                table: "organization_users",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_organizations_logo_file_id",
                table: "organizations",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "uq_organizations_slug",
                table: "organizations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reminder_schedules_action_recipient_id",
                table: "reminder_schedules",
                column: "action_recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminders_due",
                table: "reminder_schedules",
                columns: new[] { "status", "trigger_at" });

            migrationBuilder.CreateIndex(
                name: "ix_requirements_action_order",
                table: "requirements",
                columns: new[] { "action_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ix_requirements_source_template_requirement_id",
                table: "requirements",
                column: "source_template_requirement_id");

            migrationBuilder.CreateIndex(
                name: "uq_requirements_action_key",
                table: "requirements",
                columns: new[] { "action_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submission_files_file_asset_id",
                table: "submission_files",
                column: "file_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_files_requirement_id",
                table: "submission_files",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_files_submission_id",
                table: "submission_files",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_responses_requirement_id",
                table: "submission_responses",
                column: "requirement_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_responses_submission_id",
                table: "submission_responses",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_action_recipient_id",
                table: "submissions",
                column: "action_recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_action_version",
                table: "submissions",
                columns: new[] { "action_id", "version_number" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_reviewed_by_user_id",
                table: "submissions",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_requirements_version_order",
                table: "template_requirements",
                columns: new[] { "template_version_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "uq_template_requirements_version_key",
                table: "template_requirements",
                columns: new[] { "template_version_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_versions_created_by_user_id",
                table: "template_versions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_template_versions_template_version",
                table: "template_versions",
                columns: new[] { "template_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_templates_created_by_user_id",
                table: "templates",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_templates_current_version_id",
                table: "templates",
                column: "current_version_id");

            migrationBuilder.CreateIndex(
                name: "uq_templates_org_name",
                table: "templates",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usage_events_action_id",
                table: "usage_events",
                column: "action_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_org_period",
                table: "usage_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "uq_usage_events_idempotency_key",
                table: "usage_events",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_webhook_endpoint_id",
                table: "webhook_deliveries",
                column: "webhook_endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_retry",
                table: "webhook_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_organization_id",
                table: "webhook_endpoints",
                column: "organization_id");

            migrationBuilder.AddForeignKey(
                name: "fk_action_recipients_actions_action_id",
                table: "action_recipients",
                column: "action_id",
                principalTable: "actions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_actions_organizations_organization_id",
                table: "actions",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_actions_template_versions_template_version_id",
                table: "actions",
                column: "template_version_id",
                principalTable: "template_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_actions_templates_template_id",
                table: "actions",
                column: "template_id",
                principalTable: "templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_admin_settings_organizations_organization_id",
                table: "admin_settings",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_api_keys_organizations_organization_id",
                table: "api_keys",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_audit_events_organizations_organization_id",
                table: "audit_events",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_draft_responses_requirements_requirement_id",
                table: "draft_responses",
                column: "requirement_id",
                principalTable: "requirements",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_file_assets_organizations_organization_id",
                table: "file_assets",
                column: "organization_id",
                principalTable: "organizations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_requirements_template_requirements_source_template_requirem",
                table: "requirements",
                column: "source_template_requirement_id",
                principalTable: "template_requirements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_template_requirements_template_versions_template_version_id",
                table: "template_requirements",
                column: "template_version_id",
                principalTable: "template_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_template_versions_templates_template_id",
                table: "template_versions",
                column: "template_id",
                principalTable: "templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_action_recipients_actions_action_id",
                table: "action_recipients");

            migrationBuilder.DropForeignKey(
                name: "fk_file_assets_actions_action_id",
                table: "file_assets");

            migrationBuilder.DropForeignKey(
                name: "fk_file_assets_organizations_organization_id",
                table: "file_assets");

            migrationBuilder.DropForeignKey(
                name: "fk_templates_organizations_organization_id",
                table: "templates");

            migrationBuilder.DropForeignKey(
                name: "fk_templates_template_versions_current_version_id",
                table: "templates");

            migrationBuilder.DropTable(
                name: "admin_settings");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "draft_responses");

            migrationBuilder.DropTable(
                name: "notification_deliveries");

            migrationBuilder.DropTable(
                name: "organization_users");

            migrationBuilder.DropTable(
                name: "reminder_schedules");

            migrationBuilder.DropTable(
                name: "submission_files");

            migrationBuilder.DropTable(
                name: "submission_responses");

            migrationBuilder.DropTable(
                name: "usage_events");

            migrationBuilder.DropTable(
                name: "webhook_deliveries");

            migrationBuilder.DropTable(
                name: "requirements");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "webhook_endpoints");

            migrationBuilder.DropTable(
                name: "template_requirements");

            migrationBuilder.DropTable(
                name: "actions");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropTable(
                name: "file_assets");

            migrationBuilder.DropTable(
                name: "action_recipients");

            migrationBuilder.DropTable(
                name: "template_versions");

            migrationBuilder.DropTable(
                name: "templates");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
