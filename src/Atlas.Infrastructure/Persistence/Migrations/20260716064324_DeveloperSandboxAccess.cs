using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeveloperSandboxAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "developer_access_status",
                table: "organizations",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "developer_production_approved_at",
                table: "organizations",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "developer_production_notes",
                table: "organizations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "developer_production_rejected_at",
                table: "organizations",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "developer_production_requested_at",
                table: "organizations",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "key_prefix",
                table: "api_keys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AddColumn<short>(
                name: "environment",
                table: "api_keys",
                type: "smallint",
                nullable: false,
                defaultValue: (short)2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "developer_access_status",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "developer_production_approved_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "developer_production_notes",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "developer_production_rejected_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "developer_production_requested_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "environment",
                table: "api_keys");

            migrationBuilder.AlterColumn<string>(
                name: "key_prefix",
                table: "api_keys",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }
    }
}
