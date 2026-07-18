using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrueZipPackageExportSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "admin_settings",
                columns: new[] { "id", "category", "created_at", "is_secret", "key", "organization_id", "scope", "updated_at", "updated_by_user_id", "value_json" },
                values: new object[,]
                {
                    { new Guid("57a3ce93-90dd-42a5-8717-b36e5e1390d7"), "packageExport", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "maxFileCount", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "100" },
                    { new Guid("c4b4d3cc-23e4-4421-bc99-639f7978038d"), "packageExport", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "maxZipBytes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "262144000" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("57a3ce93-90dd-42a5-8717-b36e5e1390d7"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("c4b4d3cc-23e4-4421-bc99-639f7978038d"));
        }
    }
}
