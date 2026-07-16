using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreviouslySubmittedDocumentsAndActionDedupe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "document_expires_at",
                table: "submission_files",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_name",
                table: "submission_files",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_previously_submitted",
                table: "submission_files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "reuse_confirmed_at",
                table: "submission_files",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<IPAddress>(
                name: "reuse_consent_ip_address",
                table: "submission_files",
                type: "inet",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reuse_consent_text",
                table: "submission_files",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_submission_file_id",
                table: "submission_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_submission_id",
                table: "submission_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "client_reference",
                table: "actions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.InsertData(
                table: "admin_settings",
                columns: new[] { "id", "category", "created_at", "is_secret", "key", "organization_id", "scope", "updated_at", "updated_by_user_id", "value_json" },
                values: new object[,]
                {
                    { new Guid("2399ef93-7023-43a8-901e-b326ca953d29"), "documentReuse", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "enabled", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "true" },
                    { new Guid("24801c38-0bfe-4404-aecb-7b2ce479706a"), "actions", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "duplicateWindowMinutes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "10" },
                    { new Guid("426602a2-af96-45fd-a44e-f98038f9383d"), "documentReuse", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "reuseExpiredDocuments", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "false" },
                    { new Guid("a54fb28b-f38c-47c2-80f4-aae9943ca573"), "documentReuse", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "maximumAgeDays", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "365" },
                    { new Guid("b49d89e2-6874-481b-9325-f7f2ed11f2d1"), "documentReuse", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "requireRecipientConfirmation", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "true" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_submission_files_source_file",
                table: "submission_files",
                column: "source_submission_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_submission_files_source_submission",
                table: "submission_files",
                column: "source_submission_id");

            migrationBuilder.CreateIndex(
                name: "uq_actions_org_client_reference",
                table: "actions",
                columns: new[] { "organization_id", "client_reference" },
                unique: true,
                filter: "client_reference IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_submission_files_submission_files_source_submission_file_id",
                table: "submission_files",
                column: "source_submission_file_id",
                principalTable: "submission_files",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_submission_files_submissions_source_submission_id",
                table: "submission_files",
                column: "source_submission_id",
                principalTable: "submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_submission_files_submission_files_source_submission_file_id",
                table: "submission_files");

            migrationBuilder.DropForeignKey(
                name: "fk_submission_files_submissions_source_submission_id",
                table: "submission_files");

            migrationBuilder.DropIndex(
                name: "ix_submission_files_source_file",
                table: "submission_files");

            migrationBuilder.DropIndex(
                name: "ix_submission_files_source_submission",
                table: "submission_files");

            migrationBuilder.DropIndex(
                name: "uq_actions_org_client_reference",
                table: "actions");

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("2399ef93-7023-43a8-901e-b326ca953d29"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("24801c38-0bfe-4404-aecb-7b2ce479706a"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("426602a2-af96-45fd-a44e-f98038f9383d"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("a54fb28b-f38c-47c2-80f4-aae9943ca573"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("b49d89e2-6874-481b-9325-f7f2ed11f2d1"));

            migrationBuilder.DropColumn(
                name: "document_expires_at",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "document_name",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "is_previously_submitted",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "reuse_confirmed_at",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "reuse_consent_ip_address",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "reuse_consent_text",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "source_submission_file_id",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "source_submission_id",
                table: "submission_files");

            migrationBuilder.DropColumn(
                name: "client_reference",
                table: "actions");
        }
    }
}
