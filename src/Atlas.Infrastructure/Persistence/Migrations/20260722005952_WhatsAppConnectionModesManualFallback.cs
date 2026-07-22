using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WhatsAppConnectionModesManualFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_whatsapp_connections_org_phone_number",
                table: "whatsapp_connections");

            migrationBuilder.AlterColumn<string>(
                name: "waba_id",
                table: "whatsapp_connections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number_id",
                table: "whatsapp_connections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AddColumn<string>(
                name: "business_portfolio_id",
                table: "whatsapp_connections",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "connected_at",
                table: "whatsapp_connections",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embedded_signup_session_id",
                table: "whatsapp_connections",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_account_id",
                table: "whatsapp_connections",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "history_sync_requested",
                table: "whatsapp_connections",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "mode",
                table: "whatsapp_connections",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.CreateIndex(
                name: "ix_whatsapp_connections_org_mode_status",
                table: "whatsapp_connections",
                columns: new[] { "organization_id", "mode", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_whatsapp_connections_org_phone_number",
                table: "whatsapp_connections",
                columns: new[] { "organization_id", "phone_number_id" },
                unique: true,
                filter: "phone_number_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_whatsapp_connections_org_mode_status",
                table: "whatsapp_connections");

            migrationBuilder.DropIndex(
                name: "uq_whatsapp_connections_org_phone_number",
                table: "whatsapp_connections");

            migrationBuilder.DropColumn(
                name: "business_portfolio_id",
                table: "whatsapp_connections");

            migrationBuilder.DropColumn(
                name: "connected_at",
                table: "whatsapp_connections");

            migrationBuilder.DropColumn(
                name: "embedded_signup_session_id",
                table: "whatsapp_connections");

            migrationBuilder.DropColumn(
                name: "external_account_id",
                table: "whatsapp_connections");

            migrationBuilder.DropColumn(
                name: "history_sync_requested",
                table: "whatsapp_connections");

            migrationBuilder.DropColumn(
                name: "mode",
                table: "whatsapp_connections");

            migrationBuilder.AlterColumn<string>(
                name: "waba_id",
                table: "whatsapp_connections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone_number_id",
                table: "whatsapp_connections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_whatsapp_connections_org_phone_number",
                table: "whatsapp_connections",
                columns: new[] { "organization_id", "phone_number_id" },
                unique: true);
        }
    }
}
