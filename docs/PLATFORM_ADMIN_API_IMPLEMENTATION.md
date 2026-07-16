# Reqara Platform Admin API Implementation Guide

This guide covers the platform-operator console: platform staff login, staff management, global settings, customer organization management, prospective organization leads, revenue events, metrics, and audit logs.

Customer dashboard and recipient portal endpoints are documented in [FRONTEND_API_IMPLEMENTATION.md](FRONTEND_API_IMPLEMENTATION.md).

Frontend droplet deployment is documented separately in [FRONTEND_DROPLET_DEPLOYMENT_GUIDE.md](FRONTEND_DROPLET_DEPLOYMENT_GUIDE.md).

## Client Basics

Base API origin:

```text
Production: https://api.reqara.com
Production health: https://api.reqara.com/v1/health
Local: https://localhost:<port> or http://localhost:<port>
```

Recommended platform-admin environment variable:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
```

Current production settings in the database:

```text
email.provider=resend
Email:Resend.From=requests@reqara.com
Email:Resend.FromName=Reqara
app.baseUrl=https://reqara.com
```

All JSON examples use the actual wire format expected by the API:

- JSON property names are `camelCase`.
- Enum values are strings.
- Dates are ISO 8601 strings.
- Send `Content-Type: application/json` on JSON requests.
- Send `Accept: application/json` on API requests.
- Browser calls must include `credentials: "include"`.

Platform auth uses the same underlying HttpOnly cookie name as dashboard auth: `__Host-atlas_dashboard`.

Implementation recommendation: run the platform admin console on a separate subdomain or tell admins that logging into platform admin replaces their organization-dashboard session in the same browser.

Cross-origin note: platform auth is cookie-based. The production API currently allows any browser origin with credentials so the admin app can integrate quickly. Tighten this to the deployed frontend/admin origins before final production hardening.

Production health check:

```ts
const API_BASE_URL = import.meta.env.VITE_ATLAS_API_BASE_URL ?? "https://api.reqara.com";

const response = await fetch(`${API_BASE_URL}/v1/health`, {
  headers: { "Accept": "application/json" }
});
```

## Error Shape

Most errors use RFC7807-style problem details.

```json
{
  "type": "https://docs.atlas.example/errors/platform_auth_required",
  "title": "Platform staff authentication is required.",
  "status": 401,
  "code": "platform_auth_required"
}
```

Common platform-admin errors:

- `401 platform_auth_required`
- `401 invalid_credentials`
- `401 invalid_bootstrap_key`
- `403 forbidden`
- `404 not_found`
- `409 platform_staff_exists`
- `409 email_in_use`
- `409 last_owner`
- `409 slug_in_use`
- `409 setting_exists`
- `422 validation_failed`
- `422 invalid_assignee`
- `422 invalid_organization`
- `503 bootstrap_not_configured`

## Platform Enums

```text
PlatformStaffRole: Owner, Admin, Support, Finance
PlatformStaffStatus: Active, Disabled
OrganizationStatus: Active, Suspended, Closed
OrganizationInterestStatus: New, Qualified, Approved, Rejected, Archived
PlatformRevenueEventType: Subscription, Usage, Credit, Refund, Adjustment
AdminSettingScope: System, Organization
```

## Role Matrix

Owner/Admin:

- Staff CRUD.
- Settings CRUD, including viewing secret values when requested.
- Organization CRUD and approval.
- Lead CRUD and approval.
- Revenue CRUD.
- Metrics.
- Audit log.

Support:

- Organization list/detail.
- Lead list/detail/create/update.
- Metrics.

Finance:

- Revenue event CRUD.
- Metrics.

## Platform Admin UI Areas

Recommended navigation:

- Login
- Staff
- Settings
- Organizations
- Interested Organizations
- Revenue
- Metrics
- Audit Log

Recommended global state:

```ts
type PlatformSession = {
  staffId: string;
  email: string;
  fullName: string;
  role: "Owner" | "Admin" | "Support" | "Finance";
  status: "Active" | "Disabled";
};
```

## Bootstrap And Auth

### First Owner Bootstrap Flow

Operational scenario:

1. Retrieve the production bootstrap key from the server-only production config.
2. Open a private bootstrap screen.
3. Submit `POST https://api.reqara.com/v1/platform/bootstrap`.
4. API creates the first `Owner`, signs them in, and refuses future bootstrap once any platform staff exists.
5. Remove or rotate the bootstrap key after the owner exists.

### POST `/v1/platform/bootstrap`

Request:

```json
{
  "bootstrapKey": "replace-with-production-secret",
  "email": "owner@projectatlas.app",
  "password": "correct-horse-battery-staple",
  "fullName": "Platform Owner"
}
```

Response `201`:

```json
{
  "id": "10000000-0000-0000-0000-000000000001",
  "email": "owner@projectatlas.app",
  "fullName": "Platform Owner",
  "role": "Owner",
  "status": "Active",
  "lastLoginAt": null,
  "disabledAt": null,
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T18:00:00Z"
}
```

### Platform Login Flow

UI scenario:

1. Admin enters email/password.
2. Call `POST /v1/platform/auth/login`.
3. Store returned staff profile in state.
4. Use role to show/hide navigation.
5. On app boot, call `GET /v1/platform/me`.

### POST `/v1/platform/auth/login`

Request:

```json
{
  "email": "owner@projectatlas.app",
  "password": "correct-horse-battery-staple"
}
```

Response:

```json
{
  "id": "10000000-0000-0000-0000-000000000001",
  "email": "owner@projectatlas.app",
  "fullName": "Platform Owner",
  "role": "Owner",
  "status": "Active",
  "lastLoginAt": "2026-07-15T18:30:00Z",
  "disabledAt": null,
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T18:00:00Z"
}
```

### POST `/v1/platform/auth/logout`

Request body: none.

Response: `204 No Content`.

### GET `/v1/platform/me`

Response:

```json
{
  "id": "10000000-0000-0000-0000-000000000001",
  "email": "owner@projectatlas.app",
  "fullName": "Platform Owner",
  "role": "Owner",
  "status": "Active",
  "lastLoginAt": "2026-07-15T18:30:00Z",
  "disabledAt": null,
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T18:00:00Z"
}
```

## Staff Management

### Staff UI Flow

1. Owner/Admin opens Staff.
2. Load `GET /v1/platform/staff`.
3. Create support or finance users with temporary passwords.
4. Edit role/status/full name as needed.
5. Use delete action to disable a staff account.

Safety behavior: the backend prevents disabling or demoting the last active `Owner`.

### GET `/v1/platform/staff?status=Active`

Query parameters:

- `status`: optional `Active` or `Disabled`.

Response:

```json
{
  "items": [
    {
      "id": "10000000-0000-0000-0000-000000000001",
      "email": "owner@projectatlas.app",
      "fullName": "Platform Owner",
      "role": "Owner",
      "status": "Active",
      "lastLoginAt": "2026-07-15T18:30:00Z",
      "disabledAt": null,
      "createdAt": "2026-07-15T18:00:00Z",
      "updatedAt": "2026-07-15T18:00:00Z"
    }
  ]
}
```

### POST `/v1/platform/staff`

Defaults if omitted:

- `role`: `Support`
- `status`: `Active`

Request:

```json
{
  "email": "support@reqara.com",
  "password": "temporary-password",
  "fullName": "Support Staff",
  "role": "Support",
  "status": "Active"
}
```

Response `201`:

```json
{
  "id": "10000000-0000-0000-0000-000000000002",
  "email": "support@reqara.com",
  "fullName": "Support Staff",
  "role": "Support",
  "status": "Active",
  "lastLoginAt": null,
  "disabledAt": null,
  "createdAt": "2026-07-15T19:00:00Z",
  "updatedAt": "2026-07-15T19:00:00Z"
}
```

### GET `/v1/platform/staff/{id}`

Response: one `PlatformStaffResponse`, same shape as above.

### PUT `/v1/platform/staff/{id}`

All fields are optional. Send only changed fields.

Request:

```json
{
  "email": "support@reqara.com",
  "password": null,
  "fullName": "Senior Support Staff",
  "role": "Support",
  "status": "Active"
}
```

Response:

```json
{
  "id": "10000000-0000-0000-0000-000000000002",
  "email": "support@reqara.com",
  "fullName": "Senior Support Staff",
  "role": "Support",
  "status": "Active",
  "lastLoginAt": null,
  "disabledAt": null,
  "createdAt": "2026-07-15T19:00:00Z",
  "updatedAt": "2026-07-15T19:05:00Z"
}
```

### DELETE `/v1/platform/staff/{id}`

Disables the staff member.

Response: `204 No Content`.

## Platform Settings

### Settings UI Flow

1. Owner/Admin opens Settings.
2. Load all with `GET /v1/platform/settings`.
3. Filter by organization or category in the UI.
4. Use `includeSecrets=true` only in a deliberate "reveal secret" action.
5. Upsert via `POST /v1/platform/settings`.
6. Edit via `PUT /v1/platform/settings/{id}`.
7. Delete only after confirmation.

Secret behavior: secret setting values return `"\"***\""` unless an Owner/Admin requests `includeSecrets=true` or `includeSecret=true`.

DigitalOcean Spaces settings are managed here. The storage service reads system settings first and falls back to environment/appsettings values when a DB value is blank.

Use this category and keys:

```text
Category: DigitalOceanSpaces
Keys:
- ServiceUrl
- Region
- BucketName
- AccessKey
- SecretKey
- QuarantinePrefix
- ForcePathStyle

Mark as secret:
- AccessKey
- SecretKey
```

### GET `/v1/platform/settings`

Query parameters:

- `organizationId`: optional.
- `category`: optional exact category.
- `includeSecrets`: optional boolean.

Example:

```text
GET /v1/platform/settings?category=Email:Resend&includeSecrets=false
```

Response:

```json
{
  "items": [
    {
      "id": "20000000-0000-0000-0000-000000000001",
      "organizationId": null,
      "scope": "System",
      "category": "Email:Resend",
      "key": "ApiKey",
      "valueJson": "\"***\"",
      "isSecret": true,
      "updatedAt": "2026-07-15T18:00:00Z"
    }
  ]
}
```

### POST `/v1/platform/settings`

Creates or updates a setting by scope, category, key, and organization.

Request:

```json
{
  "organizationId": null,
  "scope": "System",
  "category": "Email:Resend",
  "key": "ApiKey",
  "value": "re_123",
  "isSecret": true
}
```

Response:

```json
{
  "id": "20000000-0000-0000-0000-000000000001",
  "organizationId": null,
  "scope": "System",
  "category": "Email:Resend",
  "key": "ApiKey",
  "valueJson": "\"***\"",
  "isSecret": true,
  "updatedAt": "2026-07-15T19:00:00Z"
}
```

Organization setting example:

```json
{
  "organizationId": "30000000-0000-0000-0000-000000000001",
  "scope": "Organization",
  "category": "files",
  "key": "maxUploadBytes",
  "value": 20971520,
  "isSecret": false
}
```

### GET `/v1/platform/settings/{id}?includeSecret=true`

Response:

```json
{
  "id": "20000000-0000-0000-0000-000000000001",
  "organizationId": null,
  "scope": "System",
  "category": "Email:Resend",
  "key": "ApiKey",
  "valueJson": "\"re_123\"",
  "isSecret": true,
  "updatedAt": "2026-07-15T19:00:00Z"
}
```

### PUT `/v1/platform/settings/{id}`

All fields are optional. If changing scope to `Organization`, include `organizationId`.

Request:

```json
{
  "organizationId": null,
  "scope": "System",
  "category": "Email:Resend",
  "key": "ApiKey",
  "value": "re_456",
  "isSecret": true
}
```

Response: one `PlatformSettingResponse`, same shape as above with secret masked unless included.

### DELETE `/v1/platform/settings/{id}`

Response: `204 No Content`.

## Organizations

### Organization Operations UI Flow

1. Support/Admin opens Organizations.
2. Load `GET /v1/platform/organizations?q=&status=`.
3. Open detail for counts and revenue summary.
4. Owner/Admin can create, edit, approve/reactivate, suspend, or close organizations.
5. Closing uses `DELETE`; it sets status to `Closed` and preserves records.

### GET `/v1/platform/organizations?q=northstar&status=Active`

Response:

```json
{
  "items": [
    {
      "id": "30000000-0000-0000-0000-000000000001",
      "name": "Northstar Staffing",
      "slug": "northstar-staffing",
      "status": "Active",
      "timezone": "America/Toronto",
      "defaultLanguage": "en",
      "retentionDays": 365,
      "createdAt": "2026-07-15T18:00:00Z",
      "updatedAt": "2026-07-15T18:00:00Z",
      "deletedAt": null,
      "memberCount": 3,
      "actionCount": 18,
      "submissionCount": 12,
      "revenueAllTime": [
        {
          "currency": "USD",
          "amount": 499.00
        }
      ]
    }
  ]
}
```

### POST `/v1/platform/organizations`

Defaults:

- `status`: `Active`
- `timezone`: `UTC`
- `defaultLanguage`: `en`
- `retentionDays`: `365`

Slug rules:

- 2 to 100 characters.
- Lowercase letters, numbers, and hyphens only.
- Cannot start or end with a hyphen.
- Cannot contain consecutive hyphens.

Request:

```json
{
  "name": "Northstar Staffing",
  "slug": "northstar-staffing",
  "status": "Active",
  "timezone": "America/Toronto",
  "defaultLanguage": "en",
  "retentionDays": 365
}
```

Response `201`: one platform organization response.

### GET `/v1/platform/organizations/{id}`

Response: one platform organization response.

### PATCH `/v1/platform/organizations/{id}`

Request:

```json
{
  "name": "Northstar Staffing",
  "status": "Suspended",
  "timezone": "America/Toronto",
  "defaultLanguage": "en",
  "accentColor": "#0f172a",
  "privacyStatement": "We use submitted files only to process requests.",
  "retentionDays": 365
}
```

Response: one platform organization response.

### POST `/v1/platform/organizations/{id}/approve`

Use to reactivate or approve an existing organization by setting it to `Active` and clearing `deletedAt`.

Request body: none.

Response: one platform organization response.

### DELETE `/v1/platform/organizations/{id}`

Closes the organization.

Response: `204 No Content`.

## Interested Organizations

### Lead Management UI Flow

1. Marketing site submits `POST /v1/public/interests`.
2. Support opens Interested Organizations.
3. Load `GET /v1/platform/interests`.
4. Assign support staff, mark as `Qualified`, add notes.
5. Owner/Admin approves a lead.
6. Approval can either mark the lead approved or create a new organization and owner membership.

### Public Marketing Endpoint: POST `/v1/public/interests`

No auth required. Public callers always create `New` leads.

Request:

```json
{
  "organizationName": "Northstar Staffing",
  "contactName": "Ollie Ed",
  "contactEmail": "ollie@northstarstaffing.com",
  "contactPhone": "+14165550100",
  "source": "website",
  "region": "Canada",
  "expectedVolume": "50 checklists/month",
  "message": "We want to use Reqara for onboarding.",
  "notes": null,
  "status": null,
  "assignedStaffId": null
}
```

Response `202`:

```json
{
  "id": "40000000-0000-0000-0000-000000000001",
  "status": "New"
}
```

### GET `/v1/platform/interests`

Query parameters:

- `status`: optional.
- `assignedStaffId`: optional.
- `q`: optional text search across organization name, contact name, and contact email.

Response:

```json
{
  "items": [
    {
      "id": "40000000-0000-0000-0000-000000000001",
      "organizationName": "Northstar Staffing",
      "contactName": "Ollie Ed",
      "contactEmail": "ollie@northstarstaffing.com",
      "contactPhone": "+14165550100",
      "source": "website",
      "region": "Canada",
      "expectedVolume": "50 checklists/month",
      "message": "We want to use Reqara for onboarding.",
      "status": "New",
      "assignedStaffId": null,
      "approvedOrganizationId": null,
      "notes": null,
      "createdAt": "2026-07-15T18:00:00Z",
      "updatedAt": "2026-07-15T18:00:00Z",
      "approvedAt": null,
      "rejectedAt": null
    }
  ]
}
```

### POST `/v1/platform/interests`

Support/admin-created lead. Can set initial status, notes, and assignee.

Request:

```json
{
  "organizationName": "Acme Payroll",
  "contactName": "Morgan Lane",
  "contactEmail": "morgan@acmepayroll.example",
  "contactPhone": "+16475550100",
  "source": "referral",
  "region": "US",
  "expectedVolume": "200 checklists/month",
  "message": "Interested in compliance checklist automation.",
  "notes": "Asked for enterprise pricing.",
  "status": "Qualified",
  "assignedStaffId": "10000000-0000-0000-0000-000000000002"
}
```

Response `201`: one organization interest response.

### GET `/v1/platform/interests/{id}`

Response: one organization interest response.

### PUT `/v1/platform/interests/{id}`

Request:

```json
{
  "organizationName": "Northstar Staffing",
  "contactName": "Ollie Ed",
  "contactEmail": "ollie@northstarstaffing.com",
  "contactPhone": "+14165550100",
  "source": "website",
  "region": "Canada",
  "expectedVolume": "75 checklists/month",
  "message": "Ready for pilot.",
  "notes": "Demo completed. Wants custom domain.",
  "status": "Qualified",
  "assignedStaffId": "10000000-0000-0000-0000-000000000002"
}
```

Response: one organization interest response.

### POST `/v1/platform/interests/{id}/approve`

Approve without creating an organization:

```json
{
  "createOrganization": false,
  "organizationName": null,
  "organizationSlug": null,
  "ownerEmail": null,
  "ownerFullName": null,
  "ownerPassword": null,
  "timezone": null,
  "defaultLanguage": null
}
```

Approve and create organization plus owner membership:

```json
{
  "createOrganization": true,
  "organizationName": "Northstar Staffing",
  "organizationSlug": "northstar-staffing",
  "ownerEmail": "ollie@northstarstaffing.com",
  "ownerFullName": "Ollie Ed",
  "ownerPassword": "temporary-owner-password",
  "timezone": "America/Toronto",
  "defaultLanguage": "en"
}
```

Response:

```json
{
  "id": "40000000-0000-0000-0000-000000000001",
  "organizationName": "Northstar Staffing",
  "contactName": "Ollie Ed",
  "contactEmail": "ollie@northstarstaffing.com",
  "contactPhone": "+14165550100",
  "source": "website",
  "region": "Canada",
  "expectedVolume": "75 checklists/month",
  "message": "Ready for pilot.",
  "status": "Approved",
  "assignedStaffId": "10000000-0000-0000-0000-000000000002",
  "approvedOrganizationId": "30000000-0000-0000-0000-000000000001",
  "notes": "Demo completed. Wants custom domain.",
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T19:00:00Z",
  "approvedAt": "2026-07-15T19:00:00Z",
  "rejectedAt": null
}
```

### DELETE `/v1/platform/interests/{id}`

Deletes the lead.

Response: `204 No Content`.

## Revenue Events

### Revenue UI Flow

1. Finance opens Revenue.
2. Filter by organization, type, and date range.
3. Add subscription, usage, credit, refund, or adjustment events.
4. Edit mistakes via `PUT`.
5. Delete only after confirmation.

Current backend note: revenue events are manual records. A billing provider integration can call the same endpoints or a future webhook endpoint.

### GET `/v1/platform/revenue-events`

Query parameters:

- `organizationId`: optional.
- `type`: optional `Subscription`, `Usage`, `Credit`, `Refund`, or `Adjustment`.
- `from`: optional ISO date.
- `to`: optional ISO date.

Example:

```text
GET /v1/platform/revenue-events?organizationId=30000000-0000-0000-0000-000000000001&type=Subscription&from=2026-07-01T00:00:00Z&to=2026-07-31T23:59:59Z
```

Response:

```json
{
  "items": [
    {
      "id": "50000000-0000-0000-0000-000000000001",
      "organizationId": "30000000-0000-0000-0000-000000000001",
      "type": "Subscription",
      "amount": 499.00,
      "currency": "USD",
      "source": "manual",
      "externalReference": "INV-1001",
      "occurredAt": "2026-07-15T00:00:00Z",
      "periodStart": "2026-07-01T00:00:00Z",
      "periodEnd": "2026-07-31T23:59:59Z",
      "metadata": {
        "plan": "Growth"
      },
      "recordedByStaffId": "10000000-0000-0000-0000-000000000001",
      "createdAt": "2026-07-15T19:00:00Z",
      "updatedAt": "2026-07-15T19:00:00Z"
    }
  ]
}
```

### POST `/v1/platform/revenue-events`

Rules:

- `type` is required.
- `amount` must be non-zero.
- `currency` must be a 3-letter code.
- `source` is required.
- `occurredAt` is required.
- `organizationId` is optional but must exist if supplied.

Request:

```json
{
  "organizationId": "30000000-0000-0000-0000-000000000001",
  "type": "Subscription",
  "amount": 499.00,
  "currency": "USD",
  "source": "manual",
  "externalReference": "INV-1001",
  "occurredAt": "2026-07-15T00:00:00Z",
  "periodStart": "2026-07-01T00:00:00Z",
  "periodEnd": "2026-07-31T23:59:59Z",
  "metadata": {
    "plan": "Growth"
  }
}
```

Response `201`: one revenue event response.

Credit example:

```json
{
  "organizationId": "30000000-0000-0000-0000-000000000001",
  "type": "Credit",
  "amount": -100.00,
  "currency": "USD",
  "source": "manual",
  "externalReference": "CREDIT-1001",
  "occurredAt": "2026-07-20T00:00:00Z",
  "periodStart": null,
  "periodEnd": null,
  "metadata": {
    "reason": "Pilot discount"
  }
}
```

### GET `/v1/platform/revenue-events/{id}`

Response: one revenue event response.

### PUT `/v1/platform/revenue-events/{id}`

Request:

```json
{
  "organizationId": "30000000-0000-0000-0000-000000000001",
  "type": "Subscription",
  "amount": 599.00,
  "currency": "USD",
  "source": "manual",
  "externalReference": "INV-1001",
  "occurredAt": "2026-07-15T00:00:00Z",
  "periodStart": "2026-07-01T00:00:00Z",
  "periodEnd": "2026-07-31T23:59:59Z",
  "metadata": {
    "plan": "Growth",
    "corrected": true
  }
}
```

Response: one revenue event response.

### DELETE `/v1/platform/revenue-events/{id}`

Response: `204 No Content`.

## Metrics

### Metrics Dashboard Flow

1. Pick date range. Default backend range is last 30 days.
2. Call `GET /v1/platform/metrics`.
3. Render top KPI tiles, revenue by currency, lead status counts, operational exceptions, and usage quantity.

### GET `/v1/platform/metrics?from={iso}&to={iso}`

Response:

```json
{
  "from": "2026-07-01T00:00:00Z",
  "to": "2026-07-31T23:59:59Z",
  "organizationsTotal": 25,
  "organizationsActive": 22,
  "organizationsSuspended": 2,
  "organizationsClosed": 1,
  "activePlatformStaff": 5,
  "interestsNew": 8,
  "interestsQualified": 4,
  "interestsApproved": 12,
  "interestsRejected": 2,
  "checklistsTotal": 640,
  "checklistsInFlight": 91,
  "submissionsTotal": 412,
  "submissionsAccepted": 360,
  "filesPendingScan": 3,
  "failedNotifications": 1,
  "usageQuantityInPeriod": 640,
  "revenueInPeriod": [
    {
      "currency": "USD",
      "amount": 14320.00
    }
  ],
  "revenueAllTime": [
    {
      "currency": "USD",
      "amount": 58200.00
    }
  ]
}
```

Suggested UI:

- KPI cards: active orgs, in-flight checklists, accepted submissions, revenue in period.
- Support alerts: new leads, qualified leads, failed notifications, files pending scan.
- Revenue table grouped by currency.
- Operational funnel: total checklists -> in flight -> submitted -> accepted.

## Audit Log

### Audit UI Flow

1. Owner/Admin opens Audit Log.
2. Filter by staff, event type, and date range.
3. Show latest 250 events.
4. Display `eventData` as formatted JSON when possible.

### GET `/v1/platform/audit`

Query parameters:

- `staffId`: optional.
- `eventType`: optional exact event type, for example `platform.staff_created`.
- `from`: optional.
- `to`: optional.

Response:

```json
{
  "items": [
    {
      "id": "60000000-0000-0000-0000-000000000001",
      "staffId": "10000000-0000-0000-0000-000000000001",
      "eventType": "platform.organization_created",
      "eventData": "{\"id\":\"30000000-0000-0000-0000-000000000001\",\"name\":\"Northstar Staffing\",\"slug\":\"northstar-staffing\",\"status\":\"Active\"}",
      "createdAt": "2026-07-15T19:00:00Z"
    }
  ]
}
```

Known event types include:

```text
platform.bootstrap_completed
platform.login
platform.staff_created
platform.staff_updated
platform.staff_disabled
platform.setting_upserted
platform.setting_updated
platform.setting_deleted
platform.organization_created
platform.organization_updated
platform.organization_approved
platform.organization_closed
platform.interest_created
platform.interest_updated
platform.interest_approved
platform.interest_deleted
platform.revenue_event_created
platform.revenue_event_updated
platform.revenue_event_deleted
```

## Recommended Screens And Route Map

Login:

- `POST /v1/platform/auth/login`
- `GET /v1/platform/me`
- `POST /v1/platform/auth/logout`

Staff:

- `GET /v1/platform/staff`
- `POST /v1/platform/staff`
- `GET /v1/platform/staff/{id}`
- `PUT /v1/platform/staff/{id}`
- `DELETE /v1/platform/staff/{id}`

Settings:

- `GET /v1/platform/settings`
- `POST /v1/platform/settings`
- `GET /v1/platform/settings/{id}`
- `PUT /v1/platform/settings/{id}`
- `DELETE /v1/platform/settings/{id}`

Organizations:

- `GET /v1/platform/organizations`
- `POST /v1/platform/organizations`
- `GET /v1/platform/organizations/{id}`
- `PATCH /v1/platform/organizations/{id}`
- `POST /v1/platform/organizations/{id}/approve`
- `DELETE /v1/platform/organizations/{id}`

Interested Organizations:

- `POST /v1/public/interests`
- `GET /v1/platform/interests`
- `POST /v1/platform/interests`
- `GET /v1/platform/interests/{id}`
- `PUT /v1/platform/interests/{id}`
- `POST /v1/platform/interests/{id}/approve`
- `DELETE /v1/platform/interests/{id}`

Revenue:

- `GET /v1/platform/revenue-events`
- `POST /v1/platform/revenue-events`
- `GET /v1/platform/revenue-events/{id}`
- `PUT /v1/platform/revenue-events/{id}`
- `DELETE /v1/platform/revenue-events/{id}`

Metrics and Audit:

- `GET /v1/platform/metrics`
- `GET /v1/platform/audit`

## Frontend Implementation Checklist

- Use a separate platform-admin app shell from the organization dashboard.
- Always use `credentials: "include"`.
- Hide Staff, Settings, Organizations approval, Audit, and destructive actions unless role allows them.
- Treat secret setting reveal as an explicit action, not a default list view.
- Confirm destructive actions: disable staff, delete setting, close organization, delete interest, delete revenue event.
- Prevent last-owner disable/demotion in UI, but rely on backend as source of truth.
- Validate organization slugs client-side before POST/PATCH approval flows.
- Normalize revenue currency to uppercase in UI.
- Render audit `eventData` as JSON if parseable, otherwise as raw text.
- Keep platform admin logs free of passwords, bootstrap keys, setting secrets, API keys, raw checklist links, and signed URLs.

## Current Backend Gaps To Plan Around

These are useful to account for in the platform-admin UI roadmap.

- Platform admin MFA and password reset endpoints are not implemented yet.
- Platform staff CRUD uses password set/update directly; there is no invitation email or forced password-change flow yet.
- List endpoints do not currently expose pagination. Audit returns the latest 250 records; other lists return matching records.
- Revenue events are manual records. Stripe or another billing provider integration is still a separate backend task.
- Support tooling is centered on interested organizations, organization management, audit, and operational metrics. There is no dedicated support-ticket or conversation inbox yet.
- There is no platform-admin impersonation flow. Keep it that way unless strict audit, approval, and visibility controls are added.
