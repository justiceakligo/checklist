using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecurityRecipientAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "otp_attempt_count",
                table: "action_recipients",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "otp_code_hash",
                table: "action_recipients",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "otp_expires_at",
                table: "action_recipients",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "otp_verified_at",
                table: "action_recipients",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    response_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_idempotency_records_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recipient_access_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    otp_verified = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recipient_access_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_recipient_access_sessions_action_recipients_action_recipien",
                        column: x => x.action_recipient_id,
                        principalTable: "action_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_records_expiry",
                table: "idempotency_records",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "uq_idempotency_records_org_key",
                table: "idempotency_records",
                columns: new[] { "organization_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipient_access_sessions_recipient_expiry",
                table: "recipient_access_sessions",
                columns: new[] { "action_recipient_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "uq_recipient_access_sessions_token_hash",
                table: "recipient_access_sessions",
                column: "session_token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "recipient_access_sessions");

            migrationBuilder.DropColumn(
                name: "otp_attempt_count",
                table: "action_recipients");

            migrationBuilder.DropColumn(
                name: "otp_code_hash",
                table: "action_recipients");

            migrationBuilder.DropColumn(
                name: "otp_expires_at",
                table: "action_recipients");

            migrationBuilder.DropColumn(
                name: "otp_verified_at",
                table: "action_recipients");
        }
    }
}
