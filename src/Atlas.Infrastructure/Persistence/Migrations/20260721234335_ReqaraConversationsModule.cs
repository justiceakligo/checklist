using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReqaraConversationsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "whatsapp_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone_number_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    waba_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    display_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    secret_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    webhook_verify_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_validated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_inbound_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_outbound_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_whatsapp_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_whatsapp_connections_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    normalized_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    display_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    profile_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contacts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contacts_whats_app_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "conversation_message_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    language = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    provider_template_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    components_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_message_templates", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_message_templates_whatsapp_connections_connect",
                        column: x => x.connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "conversation_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_event_id = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    processing_status = table.Column<short>(type: "smallint", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_webhook_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_webhook_events_whats_app_connections_connectio",
                        column: x => x.connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    campaign_ref = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    won_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    lost_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                    table.ForeignKey(
                        name: "fk_leads_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_leads_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_inbound_message_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_outbound_message_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    unread_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversations_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversations_users_assigned_user_id",
                        column: x => x.assigned_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_conversations_whats_app_connections_connection_id",
                        column: x => x.connection_id,
                        principalTable: "whatsapp_connections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    actor_type = table.Column<short>(type: "smallint", nullable: false),
                    actor_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_activities_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_assignments_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_assignments_users_assigned_by_user_id",
                        column: x => x.assigned_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_conversation_assignments_users_from_user_id",
                        column: x => x.from_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_conversation_assignments_users_to_user_id",
                        column: x => x.to_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "conversation_follow_ups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_follow_ups", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_follow_ups_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_follow_ups_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_internal_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_internal_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_internal_notes_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_internal_notes_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "conversation_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    link_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    linked_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_links_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "conversation_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    direction = table.Column<short>(type: "smallint", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    body = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_conversation_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_conversation_messages_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contacts_connection_id",
                table: "contacts",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "uq_contacts_org_phone",
                table: "contacts",
                columns: new[] { "organization_id", "normalized_phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_activities_conversation_created",
                table: "conversation_activities",
                columns: new[] { "organization_id", "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_activities_conversation_id",
                table: "conversation_activities",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_assignments_assigned_by_user_id",
                table: "conversation_assignments",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_assignments_conversation_assigned",
                table: "conversation_assignments",
                columns: new[] { "organization_id", "conversation_id", "assigned_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_assignments_conversation_id",
                table: "conversation_assignments",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_assignments_from_user_id",
                table: "conversation_assignments",
                column: "from_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_assignments_to_user_id",
                table: "conversation_assignments",
                column: "to_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_follow_ups_conversation_id",
                table: "conversation_follow_ups",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_follow_ups_owner_user_id",
                table: "conversation_follow_ups",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_followups_conversation_status",
                table: "conversation_follow_ups",
                columns: new[] { "organization_id", "conversation_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_followups_owner_due",
                table: "conversation_follow_ups",
                columns: new[] { "organization_id", "owner_user_id", "due_at" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_internal_notes_author_user_id",
                table: "conversation_internal_notes",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_internal_notes_conversation_id",
                table: "conversation_internal_notes",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_notes_conversation_created",
                table: "conversation_internal_notes",
                columns: new[] { "organization_id", "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_links_conversation_id",
                table: "conversation_links",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_links_conversation_type",
                table: "conversation_links",
                columns: new[] { "organization_id", "conversation_id", "link_type" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_message_templates_connection_id",
                table: "conversation_message_templates",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "uq_conversation_templates_org_name_language",
                table: "conversation_message_templates",
                columns: new[] { "organization_id", "name", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation_created",
                table: "conversation_messages",
                columns: new[] { "organization_id", "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_conversation_id",
                table: "conversation_messages",
                column: "conversation_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_messages_created_by_user_id",
                table: "conversation_messages",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_conversation_messages_org_provider_id",
                table: "conversation_messages",
                columns: new[] { "organization_id", "provider_message_id" },
                unique: true,
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_webhook_events_connection_id",
                table: "conversation_webhook_events",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversation_webhook_events_org_received",
                table: "conversation_webhook_events",
                columns: new[] { "organization_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "uq_conversation_webhook_events_provider_event",
                table: "conversation_webhook_events",
                column: "provider_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_assigned_user_id",
                table: "conversations",
                column: "assigned_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_connection_id",
                table: "conversations",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_contact_id",
                table: "conversations",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_lead_id",
                table: "conversations",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_conversations_org_assignee_last_message",
                table: "conversations",
                columns: new[] { "organization_id", "assigned_user_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_org_connection_contact",
                table: "conversations",
                columns: new[] { "organization_id", "connection_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_org_status_last_message",
                table: "conversations",
                columns: new[] { "organization_id", "status", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_contact_id",
                table: "leads",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_leads_org_contact",
                table: "leads",
                columns: new[] { "organization_id", "contact_id" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_org_status_updated",
                table: "leads",
                columns: new[] { "organization_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_connections_org_status",
                table: "whatsapp_connections",
                columns: new[] { "organization_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_whatsapp_connections_org_phone_number",
                table: "whatsapp_connections",
                columns: new[] { "organization_id", "phone_number_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_activities");

            migrationBuilder.DropTable(
                name: "conversation_assignments");

            migrationBuilder.DropTable(
                name: "conversation_follow_ups");

            migrationBuilder.DropTable(
                name: "conversation_internal_notes");

            migrationBuilder.DropTable(
                name: "conversation_links");

            migrationBuilder.DropTable(
                name: "conversation_message_templates");

            migrationBuilder.DropTable(
                name: "conversation_messages");

            migrationBuilder.DropTable(
                name: "conversation_webhook_events");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "leads");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "whatsapp_connections");
        }
    }
}
