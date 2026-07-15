using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmailResendOtpSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
                VALUES
                    ('6505a485-c0d9-4d29-8f00-7509f8a4bfd9', 'Email:Resend', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'FromName', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"RyvePool"'),
                    ('75fd9ff9-7be9-40a8-a30f-b24b201cdf49', 'email', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'provider', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"resend"'),
                    ('8ddca1a6-85ca-4f55-802b-3506d681e4ed', 'security', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'otpMinutes', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '10'),
                    ('b64009db-2539-4745-9a44-808105d10db3', 'Email:Resend', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'From', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"no-reply@ryverental.info"'),
                    ('c64f642f-a12a-4138-a963-178e6f072ea5', 'Email', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'ReplyTo', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"support@ryvepool.com"')
                ON CONFLICT (category, key) WHERE organization_id IS NULL DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("6505a485-c0d9-4d29-8f00-7509f8a4bfd9"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("75fd9ff9-7be9-40a8-a30f-b24b201cdf49"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("8ddca1a6-85ca-4f55-802b-3506d681e4ed"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("b64009db-2539-4745-9a44-808105d10db3"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("c64f642f-a12a-4138-a963-178e6f072ea5"));
        }
    }
}
