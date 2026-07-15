using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    full_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_staff", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    event_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    event_data = table.Column<string>(type: "jsonb", nullable: false),
                    ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_audit_events_platform_staff_staff_id",
                        column: x => x.staff_id,
                        principalTable: "platform_staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "platform_organization_interests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    contact_email = table.Column<string>(type: "citext", nullable: false),
                    contact_phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    expected_volume = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    assigned_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_organization_interests", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_organization_interests_organizations_approved_orga",
                        column: x => x.approved_organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_platform_organization_interests_platform_staff_assigned_sta",
                        column: x => x.assigned_staff_id,
                        principalTable: "platform_staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "platform_revenue_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    source = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    period_start = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    period_end = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: false),
                    recorded_by_staff_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_revenue_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_revenue_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_platform_revenue_events_platform_staff_recorded_by_staff_id",
                        column: x => x.recorded_by_staff_id,
                        principalTable: "platform_staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_created",
                table: "platform_audit_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_staff_created",
                table: "platform_audit_events",
                columns: new[] { "staff_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_interests_contact_email",
                table: "platform_organization_interests",
                column: "contact_email");

            migrationBuilder.CreateIndex(
                name: "ix_platform_interests_status_created",
                table: "platform_organization_interests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_platform_organization_interests_approved_organization_id",
                table: "platform_organization_interests",
                column: "approved_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_organization_interests_assigned_staff_id",
                table: "platform_organization_interests",
                column: "assigned_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_revenue_events_recorded_by_staff_id",
                table: "platform_revenue_events",
                column: "recorded_by_staff_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_revenue_external_reference",
                table: "platform_revenue_events",
                column: "external_reference");

            migrationBuilder.CreateIndex(
                name: "ix_platform_revenue_occurred",
                table: "platform_revenue_events",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_revenue_org_occurred",
                table: "platform_revenue_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "uq_platform_staff_email",
                table: "platform_staff",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_audit_events");

            migrationBuilder.DropTable(
                name: "platform_organization_interests");

            migrationBuilder.DropTable(
                name: "platform_revenue_events");

            migrationBuilder.DropTable(
                name: "platform_staff");
        }
    }
}
