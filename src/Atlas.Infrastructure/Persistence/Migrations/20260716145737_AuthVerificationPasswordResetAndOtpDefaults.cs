using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuthVerificationPasswordResetAndOtpDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_auth_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    email = table.Column<string>(type: "citext", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    consumed_ip_address = table.Column<IPAddress>(type: "inet", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_auth_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_auth_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "admin_settings",
                columns: new[] { "id", "category", "created_at", "is_secret", "key", "organization_id", "scope", "updated_at", "updated_by_user_id", "value_json" },
                values: new object[,]
                {
                    { new Guid("216922b7-3c5d-4fdd-a4fb-f26878f378f1"), "auth", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "passwordResetMinutes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "30" },
                    { new Guid("38a0758f-080a-4f7f-8ee3-b95e37b61f66"), "auth", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "emailVerificationMinutes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "1440" },
                    { new Guid("abf05d8e-6077-4615-8c35-c661fa414cd2"), "recipientOtp", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "defaultRequired", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "false" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_auth_tokens_user_purpose_expiry",
                table: "user_auth_tokens",
                columns: new[] { "user_id", "purpose", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "uq_user_auth_tokens_token_hash",
                table: "user_auth_tokens",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_auth_tokens");

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("216922b7-3c5d-4fdd-a4fb-f26878f378f1"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("38a0758f-080a-4f7f-8ee3-b95e37b61f66"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("abf05d8e-6077-4615-8c35-c661fa414cd2"));
        }
    }
}
