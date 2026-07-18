using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubmissionPackagesAndRouting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "destinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false),
                    configuration_encrypted = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_validation_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_destinations", x => x.id);
                    table.ForeignKey(
                        name: "fk_destinations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_destinations_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    package_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    report_file_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bundle_file_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    previous_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    superseded_by_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submission_packages", x => x.id);
                    table.ForeignKey(
                        name: "fk_submission_packages_action_recipients_action_recipient_id",
                        column: x => x.action_recipient_id,
                        principalTable: "action_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_submission_packages_actions_action_id",
                        column: x => x.action_id,
                        principalTable: "actions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_packages_file_assets_bundle_file_asset_id",
                        column: x => x.bundle_file_asset_id,
                        principalTable: "file_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_submission_packages_file_assets_report_file_asset_id",
                        column: x => x.report_file_asset_id,
                        principalTable: "file_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_submission_packages_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_packages_submission_packages_previous_package_id",
                        column: x => x.previous_package_id,
                        principalTable: "submission_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_submission_packages_submission_packages_superseded_by_packa",
                        column: x => x.superseded_by_package_id,
                        principalTable: "submission_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_submission_packages_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_packages_users_accepted_by_user_id",
                        column: x => x.accepted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submission_packages_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "template_routing_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger = table.Column<short>(type: "smallint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_automatic = table.Column<bool>(type: "boolean", nullable: false),
                    configuration_overrides_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_routing_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_template_routing_rules_destinations_destination_id",
                        column: x => x.destination_id,
                        principalTable: "destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_routing_rules_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_template_routing_rules_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    maximum_attempts = table.Column<int>(type: "integer", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    triggered_by = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    next_retry_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_jobs_destinations_destination_id",
                        column: x => x.destination_id,
                        principalTable: "destinations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_delivery_jobs_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_delivery_jobs_submission_packages_submission_package_id",
                        column: x => x.submission_package_id,
                        principalTable: "submission_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_delivery_jobs_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "manual_handoffs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    evidence_file_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manual_handoffs", x => x.id);
                    table.ForeignKey(
                        name: "fk_manual_handoffs_file_assets_evidence_file_asset_id",
                        column: x => x.evidence_file_asset_id,
                        principalTable: "file_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_manual_handoffs_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_manual_handoffs_submission_packages_submission_package_id",
                        column: x => x.submission_package_id,
                        principalTable: "submission_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_manual_handoffs_users_completed_by_user_id",
                        column: x => x.completed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    response_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    duration_milliseconds = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_delivery_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_delivery_attempts_delivery_jobs_delivery_job_id",
                        column: x => x.delivery_job_id,
                        principalTable: "delivery_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("49da403e-f91d-4f17-bdb0-ef52117edeb4"),
                column: "value_json",
                value: "{\n  \"plans\": [\n    {\n      \"code\": \"free\",\n      \"name\": \"Free\",\n      \"description\": \"Try Reqara on real requests.\",\n      \"monthlyPriceCents\": 0,\n      \"annualPriceCents\": 0,\n      \"currency\": \"CAD\",\n      \"monthlyChecklistLimit\": 10,\n      \"storageBytes\": 524288000,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": true,\n        \"customBranding\": false,\n        \"automaticReminders\": false,\n        \"apiAndWebhooks\": false,\n        \"customWorkflows\": false,\n        \"prioritySupport\": false,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"starter\",\n      \"name\": \"Starter\",\n      \"description\": \"For solo pros and small teams.\",\n      \"monthlyPriceCents\": 3900,\n      \"annualPriceCents\": 39000,\n      \"currency\": \"CAD\",\n      \"monthlyChecklistLimit\": 100,\n      \"storageBytes\": 5368709120,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": false,\n        \"customWorkflows\": false,\n        \"prioritySupport\": false,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"business\",\n      \"name\": \"Business\",\n      \"description\": \"For teams sending every day.\",\n      \"monthlyPriceCents\": 9900,\n      \"annualPriceCents\": 99000,\n      \"currency\": \"CAD\",\n      \"monthlyChecklistLimit\": 500,\n      \"storageBytes\": 26843545600,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": true,\n        \"customWorkflows\": true,\n        \"prioritySupport\": true,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"scale\",\n      \"name\": \"Scale\",\n      \"description\": \"For regulated and high-volume use.\",\n      \"monthlyPriceCents\": null,\n      \"annualPriceCents\": null,\n      \"currency\": \"CAD\",\n      \"monthlyChecklistLimit\": null,\n      \"storageBytes\": null,\n      \"customPricing\": true,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": true,\n        \"customWorkflows\": true,\n        \"prioritySupport\": true,\n        \"sso\": true,\n        \"customRetention\": true,\n        \"securityReview\": true,\n        \"dpa\": true,\n        \"dedicatedOnboarding\": true\n      }\n    }\n  ]\n}");

            migrationBuilder.CreateIndex(
                name: "uq_delivery_attempts_job_attempt",
                table: "delivery_attempts",
                columns: new[] { "delivery_job_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_jobs_destination_id",
                table: "delivery_jobs",
                column: "destination_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_jobs_org_status",
                table: "delivery_jobs",
                columns: new[] { "organization_id", "status", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_jobs_requested_by_user_id",
                table: "delivery_jobs",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_delivery_jobs_submission_package_id",
                table: "delivery_jobs",
                column: "submission_package_id");

            migrationBuilder.CreateIndex(
                name: "uq_delivery_jobs_org_idempotency",
                table: "delivery_jobs",
                columns: new[] { "organization_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_destinations_created_by_user_id",
                table: "destinations",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_destinations_org_name",
                table: "destinations",
                columns: new[] { "organization_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_destinations_org_type_active",
                table: "destinations",
                columns: new[] { "organization_id", "type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_manual_handoffs_completed_by_user_id",
                table: "manual_handoffs",
                column: "completed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_manual_handoffs_evidence_file_asset_id",
                table: "manual_handoffs",
                column: "evidence_file_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_manual_handoffs_package",
                table: "manual_handoffs",
                columns: new[] { "organization_id", "submission_package_id", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_manual_handoffs_submission_package_id",
                table: "manual_handoffs",
                column: "submission_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_accepted_by_user_id",
                table: "submission_packages",
                column: "accepted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_action_recipient_id",
                table: "submission_packages",
                column: "action_recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_action_version",
                table: "submission_packages",
                columns: new[] { "action_id", "version_number" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_bundle_file_asset_id",
                table: "submission_packages",
                column: "bundle_file_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_org_status",
                table: "submission_packages",
                columns: new[] { "organization_id", "status", "accepted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_owner_user_id",
                table: "submission_packages",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_previous_package_id",
                table: "submission_packages",
                column: "previous_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_report_file_asset_id",
                table: "submission_packages",
                column: "report_file_asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_packages_superseded_by_package_id",
                table: "submission_packages",
                column: "superseded_by_package_id");

            migrationBuilder.CreateIndex(
                name: "uq_submission_packages_reference",
                table: "submission_packages",
                column: "package_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_submission_packages_submission",
                table: "submission_packages",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_routing_rules_destination_id",
                table: "template_routing_rules",
                column: "destination_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_routing_rules_template_id",
                table: "template_routing_rules",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_routing_rules_template_order",
                table: "template_routing_rules",
                columns: new[] { "organization_id", "template_id", "sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts");

            migrationBuilder.DropTable(
                name: "manual_handoffs");

            migrationBuilder.DropTable(
                name: "template_routing_rules");

            migrationBuilder.DropTable(
                name: "delivery_jobs");

            migrationBuilder.DropTable(
                name: "destinations");

            migrationBuilder.DropTable(
                name: "submission_packages");

            migrationBuilder.UpdateData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("49da403e-f91d-4f17-bdb0-ef52117edeb4"),
                column: "value_json",
                value: "{\n  \"plans\": [\n    {\n      \"code\": \"free\",\n      \"name\": \"Free\",\n      \"description\": \"Try Reqara on real requests.\",\n      \"monthlyPriceCents\": 0,\n      \"annualPriceCents\": 0,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": 10,\n      \"storageBytes\": 524288000,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": true,\n        \"customBranding\": false,\n        \"automaticReminders\": false,\n        \"apiAndWebhooks\": false,\n        \"customWorkflows\": false,\n        \"prioritySupport\": false,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"starter\",\n      \"name\": \"Starter\",\n      \"description\": \"For solo pros and small teams.\",\n      \"monthlyPriceCents\": 3900,\n      \"annualPriceCents\": 39000,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": 100,\n      \"storageBytes\": 5368709120,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": false,\n        \"customWorkflows\": false,\n        \"prioritySupport\": false,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"business\",\n      \"name\": \"Business\",\n      \"description\": \"For teams sending every day.\",\n      \"monthlyPriceCents\": 9900,\n      \"annualPriceCents\": 99000,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": 500,\n      \"storageBytes\": 26843545600,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": true,\n        \"customWorkflows\": true,\n        \"prioritySupport\": true,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"scale\",\n      \"name\": \"Scale\",\n      \"description\": \"For regulated and high-volume use.\",\n      \"monthlyPriceCents\": null,\n      \"annualPriceCents\": null,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": null,\n      \"storageBytes\": null,\n      \"customPricing\": true,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": true,\n        \"customWorkflows\": true,\n        \"prioritySupport\": true,\n        \"sso\": true,\n        \"customRetention\": true,\n        \"securityReview\": true,\n        \"dpa\": true,\n        \"dedicatedOnboarding\": true\n      }\n    }\n  ]\n}");
        }
    }
}
