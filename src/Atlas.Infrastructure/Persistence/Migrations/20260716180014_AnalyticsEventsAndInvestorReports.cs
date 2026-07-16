using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalyticsEventsAndInvestorReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytics_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checklist_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    properties_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analytics_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_analytics_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "investor_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    period = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    include_sections = table.Column<string[]>(type: "text[]", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    requested_by_staff_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_investor_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_investor_reports_platform_staff_requested_by_staff_id",
                        column: x => x.requested_by_staff_id,
                        principalTable: "platform_staff",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_checklist",
                table: "analytics_events",
                column: "checklist_id");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_name_occurred",
                table: "analytics_events",
                columns: new[] { "event_name", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_org_occurred",
                table: "analytics_events",
                columns: new[] { "organization_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_recipient",
                table: "analytics_events",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_analytics_events_template",
                table: "analytics_events",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_investor_reports_staff_created",
                table: "investor_reports",
                columns: new[] { "requested_by_staff_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_investor_reports_type_period",
                table: "investor_reports",
                columns: new[] { "report_type", "period" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_events");

            migrationBuilder.DropTable(
                name: "investor_reports");
        }
    }
}
