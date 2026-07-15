using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FrontendChecklistInviteEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE admin_settings
                SET value_json = '"Project Atlas"'
                WHERE id = '6505a485-c0d9-4d29-8f00-7509f8a4bfd9'
                    AND organization_id IS NULL
                    AND value_json = '"RyvePool"';

                UPDATE admin_settings
                SET value_json = '"requests@projectatlas.app"'
                WHERE id = 'b64009db-2539-4745-9a44-808105d10db3'
                    AND organization_id IS NULL
                    AND value_json = '"no-reply@ryverental.info"';

                UPDATE admin_settings
                SET value_json = '"support@projectatlas.app"'
                WHERE id = 'c64f642f-a12a-4138-a963-178e6f072ea5'
                    AND organization_id IS NULL
                    AND value_json = '"support@ryvepool.com"';

                INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
                VALUES
                    ('2af49998-5f6e-4d9d-a36f-91e8d4b4f9f0', 'security', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'recipientTokenGraceDays', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '30'),
                    ('3cfbb448-a95f-4d94-89b8-d15b81a8bace', 'app', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'baseUrl', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"https://atlaschecklist.lovable.app"')
                ON CONFLICT (category, key) WHERE organization_id IS NULL DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM admin_settings
                WHERE id IN ('2af49998-5f6e-4d9d-a36f-91e8d4b4f9f0', '3cfbb448-a95f-4d94-89b8-d15b81a8bace');

                UPDATE admin_settings
                SET value_json = '"RyvePool"'
                WHERE id = '6505a485-c0d9-4d29-8f00-7509f8a4bfd9'
                    AND organization_id IS NULL
                    AND value_json = '"Project Atlas"';

                UPDATE admin_settings
                SET value_json = '"no-reply@ryverental.info"'
                WHERE id = 'b64009db-2539-4745-9a44-808105d10db3'
                    AND organization_id IS NULL
                    AND value_json = '"requests@projectatlas.app"';

                UPDATE admin_settings
                SET value_json = '"support@ryvepool.com"'
                WHERE id = 'c64f642f-a12a-4138-a963-178e6f072ea5'
                    AND organization_id IS NULL
                    AND value_json = '"support@projectatlas.app"';
                """);
        }
    }
}
