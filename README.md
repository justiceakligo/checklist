# Project Atlas Checklist API

Production-shaped ASP.NET Core API for Project Atlas: tenant-scoped checklists, versioned templates, recipient access with email OTPs, private file intake through DigitalOcean Spaces, append-only submissions/audit/usage records, and database-backed admin configuration.

## What Is Built

- `src/Atlas.Domain`: entities and enums from the database specification.
- `src/Atlas.Application`: tenant, settings, secret hashing, email, and object-storage contracts.
- `src/Atlas.Infrastructure`: EF Core/Npgsql persistence, tenant query filters, admin settings service, Resend email service, DigitalOcean Spaces signed URL service, and migrations.
- `src/Atlas.Api`: `/v1` API endpoints for auth, organizations, admin settings, templates, actions, recipient checklist sessions, submissions, signed file upload/download URLs, file scan results, platform administration, Swagger in development, security headers, and RFC7807-style errors.
- `tests/Atlas.Tests`: focused tests for tenant filtering, admin settings, and secret hashing.

## Local Development

The local development connection string is in `src/Atlas.Api/appsettings.Development.json`:

```json
"Postgres": "Host=localhost;Port=5432;Database=atlaschecklist;Username=postgres;Password=Fafa@post1"
```

Create the database if it does not exist, then apply the migration:

```powershell
createdb -h localhost -p 5432 -U postgres atlaschecklist
dotnet ef database update --project src\Atlas.Infrastructure\Atlas.Infrastructure.csproj --startup-project src\Atlas.Api\Atlas.Api.csproj
dotnet run --project src\Atlas.Api\Atlas.Api.csproj
```

Bootstrap an owner and organization with `POST /v1/auth/signup`; the API signs the user into the dashboard cookie. `X-Atlas-Organization-Id` only selects one of the signed-in user's memberships. For server-to-server access, create API keys with `/v1/api-keys` and send them as `X-Atlas-Key`.

## Platform Administration

Platform administration is separate from organization dashboard auth. Set a one-time secret in `PlatformAdmin__BootstrapKey`, then create the first platform owner with:

```http
POST /v1/platform/bootstrap
Content-Type: application/json

{
  "bootstrapKey": "...",
  "email": "owner@projectatlas.app",
  "password": "...",
  "fullName": "Platform Owner"
}
```

After bootstrap, platform staff sign in with `POST /v1/platform/auth/login`. The bootstrap route refuses to create another owner once any platform staff account exists.

Platform roles:

- `Owner` and `Admin`: manage staff, all settings, organizations, leads, revenue events, metrics, and audit logs.
- `Support`: manage organizations and organization-interest leads, and view platform metrics.
- `Finance`: manage revenue events and view platform metrics.

Main platform routes:

- `/v1/platform/staff`: CRUD platform staff. Deletes disable accounts and protect the last active owner.
- `/v1/platform/settings`: CRUD global and organization settings, with secret values masked unless an owner/admin requests them.
- `/v1/platform/organizations`: CRUD organizations, approve/reactivate accounts, and see counts plus revenue totals.
- `/v1/platform/interests`: CRUD prospective organizations and approve leads into active organizations.
- `/v1/platform/revenue-events`: CRUD manually recorded subscription, usage, credit, refund, and adjustment events.
- `/v1/platform/metrics`: platform-wide organization, lead, checklist, submission, file, email, usage, and revenue metrics.
- `/v1/platform/audit`: recent platform-admin audit events.

Public prospective-customer intake is available at `POST /v1/public/interests`.

## Configuration

Admin-configurable product values live in `admin_settings`, not appsettings. The migration seeds initial system defaults for:

- `files.maxUploadBytes`
- `files.uploadUrlMinutes`
- `files.downloadUrlMinutes`
- `security.recipientTokenDays`
- `security.recipientTokenGraceDays`
- `security.otpMinutes`
- `retention.defaultRetentionDays`
- `app.baseUrl`
- `email.provider`
- `Email:Resend.From`
- `Email:Resend.FromName`
- `Email.ReplyTo`
- `DigitalOceanSpaces.ServiceUrl`
- `DigitalOceanSpaces.Region`
- `DigitalOceanSpaces.BucketName`
- `DigitalOceanSpaces.AccessKey`
- `DigitalOceanSpaces.SecretKey`
- `DigitalOceanSpaces.QuarantinePrefix`
- `DigitalOceanSpaces.ForcePathStyle`

The Resend API key and DigitalOcean Spaces access keys are intentionally seeded as blank/absent secret values. Configure them as secret environment variables or as platform admin settings.

Resend API key:

```text
RESEND_API_KEY
```

or:

```http
PUT /v1/admin/settings/Email:Resend/ApiKey
Content-Type: application/json

{
  "scope": "System",
  "value": "re_...",
  "isSecret": true
}
```

DigitalOcean Spaces settings are read from system admin settings first, then fall back to environment/appsettings values. Use platform admin settings CRUD for:

```text
Category: DigitalOceanSpaces
Keys: ServiceUrl, Region, BucketName, AccessKey, SecretKey, QuarantinePrefix, ForcePathStyle
Secret keys: AccessKey, SecretKey
```

Recipient emails use `app.baseUrl` or `APP_BASE_URL` and link to `/c/{token}`. The token is generated from 32 CSPRNG bytes, sent only in the email/creation response, and stored only as a hash. The API also accepts `/c/{token}` and `/r/{token}` for recipient session resolution. Opening the link creates a short-lived recipient session; if `otpRequired` is true, the API generates a fresh six-digit OTP, hashes it in the database, sends it through Resend, and requires `/v1/recipient/access/verify` before checklist access.

Initial invitations and manual reminders are sent through Resend. Since the raw checklist token is not stored, later `/send` and reminder calls mint a fresh token, update the stored hash, and invalidate the previous link.

Production appsettings only contains empty placeholders. Set these as DigitalOcean App Platform environment variables:

```text
ConnectionStrings__Postgres
DigitalOceanSpaces__ServiceUrl
DigitalOceanSpaces__Region
DigitalOceanSpaces__BucketName
DigitalOceanSpaces__AccessKey
DigitalOceanSpaces__SecretKey
DigitalOceanSpaces__QuarantinePrefix
Security__HashPepper
PlatformAdmin__BootstrapKey
RESEND_API_KEY
APP_BASE_URL
EMAIL_FROM
EMAIL_REPLY_TO
```

## DigitalOcean

- `Dockerfile` builds and runs the API on port `8080`.
- `deploy/digitalocean/app.yaml` is a starting App Platform spec. Replace `github.repo` and the `${...}` placeholders before applying it with `doctl apps create --spec deploy/digitalocean/app.yaml`.
- Keep the Spaces bucket private. The API issues short-lived presigned PUT URLs into the `quarantine/` prefix and only issues download URLs for files marked `Clean`.
- Run EF migrations as an explicit deployment step or job. The API does not auto-migrate on startup.

An idempotent SQL script is generated at `deploy/postgres/initial-atlas-schema.sql`.
