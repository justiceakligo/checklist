using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicContactTurnstileSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "admin_settings",
                columns: new[] { "id", "category", "created_at", "is_secret", "key", "organization_id", "scope", "updated_at", "updated_by_user_id", "value_json" },
                values: new object[,]
                {
                    { new Guid("050eb9f7-7445-4e7f-ad37-0c7e9f835257"), "publicContact", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "toEmail", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"hello@nextronyx.com\"" },
                    { new Guid("395f0214-a89e-4497-85f4-6904a4463cd6"), "turnstile", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "secretKey", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"\"" },
                    { new Guid("9dd99ff5-55cb-4f8b-b18a-1b39a437b195"), "turnstile", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "siteKey", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"0x4AAAAAAD3PDppjZsCXGkx4\"" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("050eb9f7-7445-4e7f-ad37-0c7e9f835257"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("395f0214-a89e-4497-85f4-6904a4463cd6"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("9dd99ff5-55cb-4f8b-b18a-1b39a437b195"));
        }
    }
}
