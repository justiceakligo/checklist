using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DigitalOceanSpacesAdminSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "admin_settings",
                columns: new[] { "id", "category", "created_at", "is_secret", "key", "organization_id", "scope", "updated_at", "updated_by_user_id", "value_json" },
                values: new object[,]
                {
                    { new Guid("1f56f6c0-d0b9-4a3d-88d9-9a736e9cf1ad"), "DigitalOceanSpaces", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "BucketName", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"\"" },
                    { new Guid("55228888-0a9a-43c3-8760-387a48ad864e"), "DigitalOceanSpaces", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "ServiceUrl", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"\"" },
                    { new Guid("a6783782-f779-42ce-afde-f2d857fc19af"), "DigitalOceanSpaces", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "Region", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"nyc3\"" },
                    { new Guid("b99bd470-792f-4dfd-8e91-bcbdf566bd17"), "DigitalOceanSpaces", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "QuarantinePrefix", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"quarantine\"" },
                    { new Guid("c8bf7e7c-a030-4481-8ab2-10dac8984d92"), "DigitalOceanSpaces", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "AccessKey", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"\"" },
                    { new Guid("e3a282d6-c69d-40a3-a3f8-77528257be18"), "DigitalOceanSpaces", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "SecretKey", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"\"" },
                    { new Guid("fb40c9fd-25de-4c6b-83eb-f249c5a64e8f"), "DigitalOceanSpaces", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "ForcePathStyle", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "false" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("1f56f6c0-d0b9-4a3d-88d9-9a736e9cf1ad"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("55228888-0a9a-43c3-8760-387a48ad864e"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("a6783782-f779-42ce-afde-f2d857fc19af"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("b99bd470-792f-4dfd-8e91-bcbdf566bd17"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("c8bf7e7c-a030-4481-8ab2-10dac8984d92"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("e3a282d6-c69d-40a3-a3f8-77528257be18"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("fb40c9fd-25de-4c6b-83eb-f249c5a64e8f"));
        }
    }
}
