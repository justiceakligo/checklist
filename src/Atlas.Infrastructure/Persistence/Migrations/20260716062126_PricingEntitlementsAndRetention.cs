using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Atlas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PricingEntitlementsAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "declaration_accepted_at",
                table: "submissions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<IPAddress>(
                name: "declaration_ip_address",
                table: "submissions",
                type: "inet",
                nullable: true);

            migrationBuilder.InsertData(
                table: "admin_settings",
                columns: new[] { "id", "category", "created_at", "is_secret", "key", "organization_id", "scope", "updated_at", "updated_by_user_id", "value_json" },
                values: new object[,]
                {
                    { new Guid("2c3051c8-b7d3-4e8a-bd6f-48d34de91201"), "billing", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "defaultPlanCode", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "\"free\"" },
                    { new Guid("49da403e-f91d-4f17-bdb0-ef52117edeb4"), "billing", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "plans", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "{\n  \"plans\": [\n    {\n      \"code\": \"free\",\n      \"name\": \"Free\",\n      \"description\": \"Try Reqara on real requests.\",\n      \"monthlyPriceCents\": 0,\n      \"annualPriceCents\": 0,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": 10,\n      \"storageBytes\": 524288000,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": true,\n        \"customBranding\": false,\n        \"automaticReminders\": false,\n        \"apiAndWebhooks\": false,\n        \"customWorkflows\": false,\n        \"prioritySupport\": false,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"starter\",\n      \"name\": \"Starter\",\n      \"description\": \"For solo pros and small teams.\",\n      \"monthlyPriceCents\": 3900,\n      \"annualPriceCents\": 39000,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": 100,\n      \"storageBytes\": 5368709120,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": false,\n        \"customWorkflows\": false,\n        \"prioritySupport\": false,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"business\",\n      \"name\": \"Business\",\n      \"description\": \"For teams sending every day.\",\n      \"monthlyPriceCents\": 9900,\n      \"annualPriceCents\": 99000,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": 500,\n      \"storageBytes\": 26843545600,\n      \"customPricing\": false,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": true,\n        \"customWorkflows\": true,\n        \"prioritySupport\": true,\n        \"sso\": false,\n        \"customRetention\": false,\n        \"securityReview\": false,\n        \"dpa\": false,\n        \"dedicatedOnboarding\": false\n      }\n    },\n    {\n      \"code\": \"scale\",\n      \"name\": \"Scale\",\n      \"description\": \"For regulated and high-volume use.\",\n      \"monthlyPriceCents\": null,\n      \"annualPriceCents\": null,\n      \"currency\": \"USD\",\n      \"monthlyChecklistLimit\": null,\n      \"storageBytes\": null,\n      \"customPricing\": true,\n      \"features\": {\n        \"teamWorkspace\": true,\n        \"templateLibrary\": true,\n        \"reqaraBranding\": false,\n        \"customBranding\": true,\n        \"automaticReminders\": true,\n        \"apiAndWebhooks\": true,\n        \"customWorkflows\": true,\n        \"prioritySupport\": true,\n        \"sso\": true,\n        \"customRetention\": true,\n        \"securityReview\": true,\n        \"dpa\": true,\n        \"dedicatedOnboarding\": true\n      }\n    }\n  ]\n}" },
                    { new Guid("4d0cf604-c972-49dd-965f-50dd5d0638c2"), "retention", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "purgeIntervalMinutes", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "60" },
                    { new Guid("4f7ea49d-6d39-4ae7-8d73-6283d2598131"), "reminders", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "dispatchIntervalSeconds", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "60" },
                    { new Guid("cb7399c6-3275-45d6-a5e8-f59d923208f2"), "developer", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "apiKeyDefaultDays", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "180" },
                    { new Guid("e2b64e8a-7b31-443b-84d8-5a8539db9eed"), "reminders", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "beforeDueDays", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "3" },
                    { new Guid("ed4541b0-14bf-4a5a-b74c-19f18ce1223e"), "reminders", new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), false, "overdueDays", null, (short)1, new DateTimeOffset(new DateTime(2026, 7, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "1" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("2c3051c8-b7d3-4e8a-bd6f-48d34de91201"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("49da403e-f91d-4f17-bdb0-ef52117edeb4"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("4d0cf604-c972-49dd-965f-50dd5d0638c2"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("4f7ea49d-6d39-4ae7-8d73-6283d2598131"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("cb7399c6-3275-45d6-a5e8-f59d923208f2"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("e2b64e8a-7b31-443b-84d8-5a8539db9eed"));

            migrationBuilder.DeleteData(
                table: "admin_settings",
                keyColumn: "id",
                keyValue: new Guid("ed4541b0-14bf-4a5a-b74c-19f18ce1223e"));

            migrationBuilder.DropColumn(
                name: "declaration_accepted_at",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "declaration_ip_address",
                table: "submissions");
        }
    }
}
