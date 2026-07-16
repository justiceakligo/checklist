# Project Atlas Frontend API Implementation Guide

This guide covers the customer dashboard, recipient checklist portal, public interest intake, and organization-level administration endpoints.

Platform-operator endpoints are intentionally documented separately in [PLATFORM_ADMIN_API_IMPLEMENTATION.md](PLATFORM_ADMIN_API_IMPLEMENTATION.md).

## Client Basics

Base API origin:

```text
Production: https://api.reqara.com
Production health: https://api.reqara.com/v1/health
Local: https://localhost:<port> or http://localhost:<port>
```

Recommended frontend environment variable:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
```

Current recipient-link frontend origin in the database:

```text
app.baseUrl=https://atlaschecklist.lovable.app
```

`app.baseUrl` is not the API origin. The backend uses it only when generating recipient email links like `/c/{token}`. Platform admins can CRUD it through platform settings, and organization admins can override it through organization admin settings.

Current production email sender settings in the database:

```text
email.provider=resend
Email:Resend.From=requests@reqara.com
Email:Resend.FromName=Reqara
```

All JSON examples use the actual wire format expected by the API:

- JSON property names are `camelCase`.
- Enum values are strings, for example `"Active"` or `"Text"`.
- Dates are ISO 8601 strings.
- Send `Content-Type: application/json` on JSON requests.
- Send `Accept: application/json` on API requests.
- Browser calls that use cookies must include `credentials: "include"`.

Dashboard auth uses an HttpOnly cookie named `__Host-atlas_dashboard`.

Recipient auth uses an HttpOnly cookie named `__Host-atlas_recipient`.

Organization-scoped dashboard requests should include `X-Atlas-Organization-Id` when the signed-in user belongs to multiple organizations.

Server-to-server calls can use `X-Atlas-Key` instead of the dashboard cookie. Do not put API keys in browser code.

```ts
const API_BASE_URL = import.meta.env.VITE_ATLAS_API_BASE_URL ?? "https://api.reqara.com";

await fetch(`${API_BASE_URL}/v1/actions`, {
  method: "GET",
  credentials: "include",
  headers: {
    "Accept": "application/json",
    "X-Atlas-Organization-Id": currentOrganizationId
  }
});
```

Cross-origin note: dashboard and recipient auth are cookie-based. The production API currently allows any browser origin with credentials so the frontend can integrate quickly. Tighten this to the deployed frontend/admin origins before final production hardening.

## Error Shape

Most errors use RFC7807-style problem details.

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Template name and title are required.",
  "status": 422,
  "code": "validation_failed"
}
```

Common status codes:

- `401`: not signed in, missing tenant, expired recipient session, or invalid credentials.
- `403`: signed in but missing permission, wrong actor type, or OTP still required.
- `404`: resource not found in the current tenant.
- `409`: state conflict, duplicate slug/email, stale autosave version, already submitted, or idempotency conflict.
- `410`: expired or inactive recipient link.
- `413`: upload exceeds configured size.
- `422`: request validation failed.
- `503`: email/storage/provider failure.

## Enums

Use these string values in requests and UI state mapping.

```text
OrganizationStatus: Active, Suspended, Closed
OrganizationUserRole: Owner, Admin, Member, Viewer
MembershipStatus: Invited, Active, Disabled
TemplateStatus: Draft, Published, Archived
RequirementType: Text, LongText, Number, Date, Boolean, SingleSelect, MultiSelect, File, Signature, Email, Phone
ChecklistActionStatus: Draft, Sent, InProgress, Submitted, Completed, Cancelled, Expired
ActionRecipientStatus: Pending, Viewed, Started, Submitted, Cancelled, Expired
SubmissionStatus: Submitted, Accepted, ChangesRequested
FileScanStatus: Pending, Clean, Rejected
AdminSettingScope: System, Organization
```

## Recommended App Areas

Dashboard:

- Sign up, login, logout, session restore.
- Organization switcher and organization settings.
- Template list/create/publish.
- Checklist action list/detail/create/send/cancel/remind/timeline.
- Submission inbox, detail, accept, request changes.
- Developer API keys.
- Organization-level admin settings.

Recipient portal:

- Open `/c/{token}` from email.
- OTP screen when required.
- Checklist form with autosave.
- File upload using signed DigitalOcean Spaces URLs.
- Review and submit.
- Receipt page.

Marketing/public:

- Prospective organization interest form.

## Health

### GET `/v1/health`

Use for deployment checks and simple frontend diagnostics.

Full production URL:

```text
GET https://api.reqara.com/v1/health
```

Response:

```json
{
  "status": "ok",
  "service": "atlas-api"
}
```

## Dashboard Auth And Organization Flow

### Signup Flow

UI scenario:

1. User fills account and organization fields.
2. Submit `POST /v1/auth/signup`.
3. API creates the user, organization, owner membership, and signs in via cookie.
4. Store returned `organizationId` as the current organization context.
5. Route to dashboard.

### POST `/v1/auth/signup`

Request:

```json
{
  "email": "ollie@northstarstaffing.com",
  "password": "correct-horse-battery-staple",
  "fullName": "Ollie Ed",
  "organizationName": "Northstar Staffing",
  "organizationSlug": "northstar-staffing",
  "timezone": "America/Toronto",
  "defaultLanguage": "en"
}
```

Response `201`:

```json
{
  "userId": "11111111-1111-1111-1111-111111111111",
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "organizationSlug": "northstar-staffing",
  "role": "Owner"
}
```

Common errors:

- `409 email_in_use`
- `409 slug_in_use`
- `422 validation_failed`

### Login Flow

UI scenario:

1. User submits email/password.
2. Call `POST /v1/auth/login`.
3. If the user belongs to multiple organizations, show an organization switcher using `organizations`.
4. Save `currentOrganizationId` in frontend state.
5. Include `X-Atlas-Organization-Id` on tenant-scoped dashboard calls.

### POST `/v1/auth/login`

Request:

```json
{
  "email": "ollie@northstarstaffing.com",
  "password": "correct-horse-battery-staple",
  "organizationId": "22222222-2222-2222-2222-222222222222"
}
```

Response:

```json
{
  "userId": "11111111-1111-1111-1111-111111111111",
  "email": "ollie@northstarstaffing.com",
  "fullName": "Ollie Ed",
  "organizations": [
    {
      "organizationId": "22222222-2222-2222-2222-222222222222",
      "name": "Northstar Staffing",
      "slug": "northstar-staffing",
      "role": "Owner",
      "status": "Active"
    }
  ],
  "currentOrganizationId": "22222222-2222-2222-2222-222222222222"
}
```

### POST `/v1/auth/logout`

Request body: none.

Response: `204 No Content`.

### GET `/v1/me`

Use on app boot to restore the session.

Response:

```json
{
  "userId": "11111111-1111-1111-1111-111111111111",
  "email": "ollie@northstarstaffing.com",
  "fullName": "Ollie Ed",
  "organizations": [
    {
      "organizationId": "22222222-2222-2222-2222-222222222222",
      "name": "Northstar Staffing",
      "slug": "northstar-staffing",
      "role": "Owner",
      "status": "Active"
    }
  ],
  "currentOrganizationId": "22222222-2222-2222-2222-222222222222"
}
```

## Organization Settings

### Organization Profile Flow

UI scenario:

1. Load `GET /v1/organizations/{id}`.
2. Let owners/admins edit branding, privacy statement, timezone, language, and retention.
3. Save via `PATCH /v1/organizations/{id}`.

### GET `/v1/organizations/{id}`

Headers:

```text
X-Atlas-Organization-Id: 22222222-2222-2222-2222-222222222222
```

Response:

```json
{
  "id": "22222222-2222-2222-2222-222222222222",
  "name": "Northstar Staffing",
  "slug": "northstar-staffing",
  "status": "Active",
  "timezone": "America/Toronto",
  "defaultLanguage": "en",
  "accentColor": "#111827",
  "privacyStatement": "We use your documents only for onboarding.",
  "retentionDays": 365,
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T18:10:00Z"
}
```

### PATCH `/v1/organizations/{id}`

Request:

```json
{
  "name": "Northstar Staffing",
  "timezone": "America/Toronto",
  "defaultLanguage": "en",
  "accentColor": "#0f172a",
  "privacyStatement": "We use your documents only to process your request.",
  "retentionDays": 365
}
```

Response: same shape as `GET /v1/organizations/{id}`.

## Organization Admin Settings

These are organization-admin settings, not platform-admin settings.

Use them for configurable product values such as upload limits, token grace days, email display settings, and app base URL.

### GET `/v1/admin/settings?organizationId={id}`

Requires dashboard user with `admin:*` scope.

Response:

```json
{
  "items": [
    {
      "id": "33333333-3333-3333-3333-333333333333",
      "organizationId": null,
      "scope": "System",
      "category": "files",
      "key": "maxUploadBytes",
      "valueJson": "10485760",
      "isSecret": false,
      "updatedAt": "2026-07-15T18:00:00Z"
    },
    {
      "id": "44444444-4444-4444-4444-444444444444",
      "organizationId": "22222222-2222-2222-2222-222222222222",
      "scope": "Organization",
      "category": "app",
      "key": "baseUrl",
      "valueJson": "\"https://atlaschecklist.lovable.app\"",
      "isSecret": false,
      "updatedAt": "2026-07-15T18:05:00Z"
    }
  ]
}
```

### PUT `/v1/admin/settings/{category}/{key}`

Request:

```json
{
  "scope": "Organization",
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "value": "https://atlaschecklist.lovable.app",
  "isSecret": false,
  "updatedByUserId": "11111111-1111-1111-1111-111111111111"
}
```

Example route:

```text
PUT /v1/admin/settings/app/baseUrl
```

Response:

```json
{
  "id": "44444444-4444-4444-4444-444444444444",
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "scope": "Organization",
  "category": "app",
  "key": "baseUrl",
  "valueJson": "\"https://atlaschecklist.lovable.app\"",
  "isSecret": false,
  "updatedAt": "2026-07-15T19:00:00Z"
}
```

## Developer API Keys

API keys are for backend integrations. Show the raw `secret` once after creation and never expect it again from the API.

### GET `/v1/api-keys`

Response:

```json
{
  "items": [
    {
      "id": "55555555-5555-5555-5555-555555555555",
      "name": "Production integration",
      "keyPrefix": "atl_live_ab12cd34",
      "scopes": ["actions:*", "files:*"],
      "lastUsedAt": null,
      "expiresAt": null,
      "revokedAt": null,
      "createdAt": "2026-07-15T18:00:00Z"
    }
  ]
}
```

### POST `/v1/api-keys`

Requires `developer:*`.

Request:

```json
{
  "name": "Production integration",
  "scopes": ["actions:*", "files:*"],
  "expiresAt": null
}
```

Response `201`:

```json
{
  "id": "55555555-5555-5555-5555-555555555555",
  "name": "Production integration",
  "keyPrefix": "atl_live_ab12cd34",
  "secret": "atl_live_ab12cd34_Wr0w...",
  "scopes": ["actions:*", "files:*"],
  "expiresAt": null,
  "createdAt": "2026-07-15T18:00:00Z"
}
```

### DELETE `/v1/api-keys/{id}`

Revokes the key.

Response: `204 No Content`.

## Templates

### Template Builder Flow

UI scenario:

1. Load templates with `GET /v1/templates`.
2. Create a draft template with requirements using `POST /v1/templates`.
3. Publish the latest version with `POST /v1/templates/{id}/publish`.
4. Use published templates when creating checklist actions.

Current backend note: there is no template detail or template-version edit endpoint yet. The current frontend can list, create, and publish templates.

### GET `/v1/templates`

Response:

```json
{
  "items": [
    {
      "id": "66666666-6666-6666-6666-666666666666",
      "name": "Employee onboarding",
      "category": "HR",
      "description": "Collect identity and payroll documents.",
      "status": "Published",
      "currentVersionId": "77777777-7777-7777-7777-777777777777",
      "createdAt": "2026-07-15T18:00:00Z",
      "updatedAt": "2026-07-15T18:00:00Z"
    }
  ]
}
```

### POST `/v1/templates`

Request:

```json
{
  "name": "Employee onboarding",
  "category": "HR",
  "description": "Collect identity and payroll documents.",
  "title": "Onboarding documents",
  "instructions": "Please complete each item carefully.",
  "settings": {
    "allowPartialSave": true
  },
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": [
    {
      "key": "legal_name",
      "type": "Text",
      "label": "Legal name",
      "description": "Enter your full legal name.",
      "required": true,
      "displayOrder": 1,
      "configuration": {},
      "validation": {
        "minLength": 2
      },
      "condition": null
    },
    {
      "key": "government_id",
      "type": "File",
      "label": "Government ID",
      "description": "Upload a clear image or PDF.",
      "required": true,
      "displayOrder": 2,
      "configuration": {
        "acceptedMimeTypes": ["image/jpeg", "image/png", "application/pdf"]
      },
      "validation": {},
      "condition": null
    }
  ]
}
```

Response `201`:

```json
{
  "id": "66666666-6666-6666-6666-666666666666",
  "name": "Employee onboarding",
  "category": "HR",
  "description": "Collect identity and payroll documents.",
  "status": "Draft",
  "currentVersionId": null,
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T18:00:00Z"
}
```

### POST `/v1/templates/{id}/publish`

Request body: none.

Response:

```json
{
  "id": "66666666-6666-6666-6666-666666666666",
  "name": "Employee onboarding",
  "category": "HR",
  "description": "Collect identity and payroll documents.",
  "status": "Published",
  "currentVersionId": "77777777-7777-7777-7777-777777777777",
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T18:00:00Z"
}
```

## Checklist Actions

### Checklist Creation Flow

UI scenario:

1. Sender chooses a template or builds custom requirements.
2. Sender enters recipient, due date, and whether OTP is required.
3. Call `POST /v1/actions` with an `Idempotency-Key`.
4. If `sendImmediately` is `true`, the API emails the recipient and returns status `"Sent"`.
5. If `sendImmediately` is `false`, show draft detail and a "Send" button.

### GET `/v1/actions`

Response:

```json
{
  "items": [
    {
      "id": "88888888-8888-8888-8888-888888888888",
      "publicReference": "act_5fd67eb7d9c2a001e3a4",
      "title": "Onboarding - Jamie Chen",
      "status": "Sent",
      "dueAt": "2026-08-15T00:00:00Z",
      "expiresAt": "2026-09-14T00:00:00Z",
      "recipientUrl": null,
      "createdAt": "2026-07-15T18:00:00Z"
    }
  ]
}
```

### GET `/v1/actions/{id}`

Response:

```json
{
  "id": "88888888-8888-8888-8888-888888888888",
  "publicReference": "act_5fd67eb7d9c2a001e3a4",
  "title": "Onboarding - Jamie Chen",
  "description": "Collect onboarding documents.",
  "status": "InProgress",
  "dueAt": "2026-08-15T00:00:00Z",
  "expiresAt": "2026-09-14T00:00:00Z",
  "sentAt": "2026-07-15T18:05:00Z",
  "completedAt": null,
  "recipients": [
    {
      "id": "99999999-9999-9999-9999-999999999999",
      "name": "Jamie Chen",
      "email": "jamie.chen@example.com",
      "phone": null,
      "status": "Started",
      "otpRequired": true,
      "firstViewedAt": "2026-07-15T18:10:00Z",
      "startedAt": "2026-07-15T18:12:00Z",
      "submittedAt": null
    }
  ],
  "requirements": [
    {
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "key": "legal_name",
      "type": "Text",
      "label": "Legal name",
      "required": true,
      "displayOrder": 1
    }
  ],
  "createdAt": "2026-07-15T18:00:00Z",
  "updatedAt": "2026-07-15T18:12:00Z"
}
```

### POST `/v1/actions`

Recommended header:

```text
Idempotency-Key: create-action-<uuid>
```

Request:

```json
{
  "templateId": "66666666-6666-6666-6666-666666666666",
  "title": "Onboarding - Jamie Chen",
  "description": "Collect onboarding documents.",
  "recipient": {
    "name": "Jamie Chen",
    "email": "jamie.chen@example.com",
    "phone": null,
    "otpRequired": true
  },
  "dueAt": "2026-08-15T00:00:00Z",
  "expiresAt": null,
  "settings": {
    "priority": "normal"
  },
  "sendImmediately": true,
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": []
}
```

Custom action without a template:

```json
{
  "templateId": null,
  "title": "Vendor setup",
  "description": "Collect vendor details.",
  "recipient": {
    "name": "Pat Doe",
    "email": "pat@example.com",
    "phone": "+14165550199",
    "otpRequired": false
  },
  "dueAt": "2026-08-01T00:00:00Z",
  "expiresAt": null,
  "settings": {},
  "sendImmediately": false,
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": [
    {
      "key": "w9",
      "type": "File",
      "label": "W-9",
      "description": "Upload signed W-9.",
      "required": true,
      "displayOrder": 1,
      "configuration": {},
      "validation": {},
      "condition": null
    }
  ]
}
```

Response `201`:

```json
{
  "id": "88888888-8888-8888-8888-888888888888",
  "publicReference": "act_5fd67eb7d9c2a001e3a4",
  "title": "Onboarding - Jamie Chen",
  "status": "Sent",
  "dueAt": "2026-08-15T00:00:00Z",
  "expiresAt": "2026-09-14T00:00:00Z",
  "recipientUrl": "https://atlaschecklist.lovable.app/c/9f3kV2qP_hN8mL0aZxYtR7wQeUcJbGdKsMoXnBvIrAs",
  "createdAt": "2026-07-15T18:00:00Z"
}
```

Security note: do not log or display the raw `recipientUrl` except in intentional sender UI. If copied for support, show only a token prefix.

### POST `/v1/actions/{id}/send`

Mints fresh recipient tokens, invalidates prior links, and sends invitation email.

Request body: none.

Response `202`:

```json
{
  "sent": 1
}
```

### POST `/v1/actions/{id}/cancel`

Request body: none.

Response:

```json
{
  "id": "88888888-8888-8888-8888-888888888888",
  "status": "Cancelled",
  "cancelledAt": "2026-07-15T19:00:00Z"
}
```

### POST `/v1/actions/{id}/reminders`

Sends reminder or overdue emails to pending recipients. The API reuses the same structure as invitation emails and rotates tokens for the reminded recipients.

Request body: none.

Response `202`:

```json
{
  "sent": 1
}
```

### GET `/v1/actions/{id}/timeline`

Response:

```json
{
  "items": [
    {
      "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "eventType": "action.sent",
      "actorType": "User",
      "actorId": "ollie@northstarstaffing.com",
      "eventData": "{\"id\":\"88888888-8888-8888-8888-888888888888\"}",
      "createdAt": "2026-07-15T18:05:00Z"
    }
  ]
}
```

## Dashboard File Uploads

Most recipient uploads happen through the recipient endpoints. Dashboard file endpoints are useful for organization-managed files and scanner callbacks.

### Dashboard Upload Flow

1. Call `POST /v1/files/upload-intents`.
2. Upload the binary directly to the returned `uploadUrl` with the returned headers.
3. Call `POST /v1/files/{fileId}/complete`.
4. A scanner or trusted workflow calls `POST /v1/files/{fileId}/scan-result`.
5. Download only after scan status is `"Clean"`.

### POST `/v1/files/upload-intents`

Request:

```json
{
  "requirementId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "actionId": "88888888-8888-8888-8888-888888888888",
  "actionRecipientId": "99999999-9999-9999-9999-999999999999",
  "fileName": "passport.pdf",
  "sizeBytes": 245760,
  "mimeType": "application/pdf"
}
```

Response `201`:

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "storageKey": "quarantine/22222222-2222-2222-2222-222222222222/cccccccc-cccc-cccc-cccc-cccccccccccc/passport.pdf",
  "uploadUrl": "https://nyc3.digitaloceanspaces.com/...",
  "headers": {
    "Content-Type": "application/pdf"
  },
  "expiresAt": "2026-07-15T18:10:00Z"
}
```

Direct upload to Spaces:

```ts
await fetch(uploadIntent.uploadUrl, {
  method: "PUT",
  headers: uploadIntent.headers,
  body: file
});
```

### POST `/v1/files/{id}/complete`

Request body: none.

Response:

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "scanStatus": "Pending",
  "scanCompletedAt": null
}
```

### POST `/v1/files/{id}/scan-result`

Requires `files:*`. Intended for trusted admin/scanner integration, not normal recipient UI.

Request:

```json
{
  "scanStatus": "Clean",
  "scanEngine": "clamav"
}
```

Response:

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "scanStatus": "Clean",
  "scanCompletedAt": "2026-07-15T18:11:00Z"
}
```

### GET `/v1/files/{id}/download-url`

Response:

```json
{
  "downloadUrl": "https://nyc3.digitaloceanspaces.com/...",
  "expiresAt": "2026-07-15T18:16:00Z"
}
```

## Submissions Dashboard

### Submission Review Flow

1. Reviewer opens submissions inbox with `GET /v1/submissions`.
2. Reviewer opens detail with `GET /v1/submissions/{id}`.
3. For files, call `GET /v1/files/{fileId}/download-url`.
4. Reviewer accepts with `POST /v1/submissions/{id}/accept` or requests changes with `POST /v1/submissions/{id}/request-changes`.

### GET `/v1/submissions`

Response:

```json
{
  "items": [
    {
      "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
      "actionId": "88888888-8888-8888-8888-888888888888",
      "actionRecipientId": "99999999-9999-9999-9999-999999999999",
      "versionNumber": 1,
      "status": "Submitted",
      "submittedAt": "2026-07-16T14:30:00Z",
      "reviewedAt": null
    }
  ]
}
```

### GET `/v1/submissions/{id}`

Response:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "actionId": "88888888-8888-8888-8888-888888888888",
  "actionRecipientId": "99999999-9999-9999-9999-999999999999",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-16T14:30:00Z",
  "reviewedAt": null,
  "reviewedByUserId": null,
  "reviewComment": null,
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924...",
  "responses": [
    {
      "requirementId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "value": "Jamie Chen"
    }
  ],
  "files": [
    {
      "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      "fileAssetId": "cccccccc-cccc-cccc-cccc-cccccccccccc"
    }
  ]
}
```

### POST `/v1/submissions/{id}/accept`

Request:

```json
{
  "comment": "All documents accepted."
}
```

Response:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "actionId": "88888888-8888-8888-8888-888888888888",
  "actionRecipientId": "99999999-9999-9999-9999-999999999999",
  "versionNumber": 1,
  "status": "Accepted",
  "submittedAt": "2026-07-16T14:30:00Z",
  "reviewedAt": "2026-07-16T15:00:00Z"
}
```

### POST `/v1/submissions/{id}/request-changes`

Request:

```json
{
  "comment": "Please upload a clearer copy of your ID."
}
```

Response:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "actionId": "88888888-8888-8888-8888-888888888888",
  "actionRecipientId": "99999999-9999-9999-9999-999999999999",
  "versionNumber": 1,
  "status": "ChangesRequested",
  "submittedAt": "2026-07-16T14:30:00Z",
  "reviewedAt": "2026-07-16T15:00:00Z"
}
```

## Recipient Portal

### Recipient Link Flow

UI scenario:

1. Recipient opens an email link like `/c/{token}`.
2. Frontend calls `GET https://api.reqara.com/c/{token}`.
3. API validates the token, creates the `__Host-atlas_recipient` cookie, and returns whether OTP is required.
4. If `otpRequired` is true and `otpVerified` is false, show OTP screen.
5. After OTP verification, load `GET /v1/recipient/checklist`.

Security requirement for frontend hosting: serve `/c/{token}` HTML with `Referrer-Policy: no-referrer` so the token does not leak through outbound referrer headers.

### GET `/c/{token}` or `/r/{token}`

Request body: none.

Response:

```json
{
  "otpRequired": true,
  "otpVerified": false,
  "sessionExpiresAt": "2026-07-15T22:00:00Z",
  "organizationName": "Northstar Staffing",
  "checklistTitle": "Onboarding - Jamie Chen"
}
```

Errors:

- `404 not_found`: invalid or rotated token.
- `410 expired_link`: token expired.
- `410 inactive_link`: action cancelled or expired.
- `503 otp_email_failed`: OTP email could not be sent.

### POST `/v1/recipient/access/verify`

Request:

```json
{
  "code": "123456"
}
```

Response:

```json
{
  "verified": true
}
```

Errors:

- `422 invalid_otp`
- `410 otp_expired`
- `429 otp_locked`

### GET `/v1/recipient/checklist`

Response:

```json
{
  "organization": {
    "name": "Northstar Staffing",
    "slug": "northstar-staffing",
    "accentColor": "#0f172a",
    "privacyStatement": "We use your documents only to process your request.",
    "logoFileId": null
  },
  "checklist": {
    "id": "88888888-8888-8888-8888-888888888888",
    "publicReference": "act_5fd67eb7d9c2a001e3a4",
    "title": "Onboarding - Jamie Chen",
    "description": "Collect onboarding documents.",
    "status": "InProgress",
    "dueAt": "2026-08-15T00:00:00Z",
    "expiresAt": "2026-09-14T00:00:00Z"
  },
  "requirements": [
    {
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "key": "legal_name",
      "type": "Text",
      "label": "Legal name",
      "description": "Enter your full legal name.",
      "required": true,
      "displayOrder": 1,
      "configuration": {},
      "validation": {
        "minLength": 2
      },
      "condition": null
    },
    {
      "id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      "key": "government_id",
      "type": "File",
      "label": "Government ID",
      "description": "Upload a clear image or PDF.",
      "required": true,
      "displayOrder": 2,
      "configuration": {},
      "validation": {},
      "condition": null
    }
  ],
  "drafts": [
    {
      "requirementId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "value": "Jamie Chen",
      "version": 2,
      "updatedAt": "2026-07-15T18:20:00Z"
    }
  ]
}
```

### Autosave Flow

UI scenario:

1. Keep a local `version` per requirement from `drafts`.
2. On debounced field save, call `PATCH /v1/recipient/responses/{requirementId}` with `expectedVersion`.
3. Replace the local version with the returned `version`.
4. On `409 concurrency_conflict`, reload the checklist and ask the user to resolve.

### PATCH `/v1/recipient/responses/{requirementId}`

Request:

```json
{
  "value": "Jamie Chen",
  "expectedVersion": 2
}
```

Response:

```json
{
  "requirementId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "version": 3,
  "updatedAt": "2026-07-15T18:25:00Z"
}
```

Use JSON values that match requirement type:

```json
{ "value": true, "expectedVersion": 1 }
```

```json
{ "value": ["A", "B"], "expectedVersion": 4 }
```

### Recipient File Upload Flow

1. File requirement selected.
2. Call `POST /v1/recipient/uploads`.
3. PUT file bytes to returned Spaces `uploadUrl` with returned headers.
4. Call `POST /v1/recipient/uploads/{fileId}/complete`.
5. Show scan status. Submission can include the file only after it is `"Clean"`.

### POST `/v1/recipient/uploads`

Request:

```json
{
  "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  "fileName": "government-id.pdf",
  "sizeBytes": 245760,
  "mimeType": "application/pdf"
}
```

Response `201`:

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "storageKey": "quarantine/22222222-2222-2222-2222-222222222222/cccccccc-cccc-cccc-cccc-cccccccccccc/government-id.pdf",
  "uploadUrl": "https://nyc3.digitaloceanspaces.com/...",
  "headers": {
    "Content-Type": "application/pdf"
  },
  "expiresAt": "2026-07-15T18:35:00Z"
}
```

### POST `/v1/recipient/uploads/{fileId}/complete`

Request body: none.

Response:

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "scanStatus": "Pending",
  "scanCompletedAt": null
}
```

### DELETE `/v1/recipient/uploads/{fileId}`

Deletes the object from storage and marks it deleted.

Response: `204 No Content`.

### POST `/v1/recipient/return-link`

Use when a recipient has a valid session and wants a return-link email queued. Current backend queues the delivery record.

Request body: none.

Response `202`:

```json
{
  "queued": true
}
```

### Recipient Submit Flow

UI scenario:

1. Validate required non-file fields client-side.
2. Ensure file requirements have uploaded files that have passed scanning.
3. Show declaration checkbox.
4. Submit with `POST /v1/recipient/submit` and an `Idempotency-Key`.
5. Route to receipt screen.

### POST `/v1/recipient/submit`

Recommended header:

```text
Idempotency-Key: recipient-submit-<uuid>
```

Request:

```json
{
  "declarationAccepted": true,
  "files": [
    {
      "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc"
    }
  ]
}
```

Response `201`:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-16T14:30:00Z",
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924..."
}
```

Validation error with missing required fields:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Validation failed",
  "status": 422,
  "code": "validation_failed",
  "errors": {
    "requirements": ["legal_name"]
  }
}
```

### GET `/v1/recipient/receipt`

Response:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-16T14:30:00Z",
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924..."
}
```

## Public Interest Intake

Use this on a marketing site or "Request access" page. It does not require auth.

### POST `/v1/public/interests`

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
  "message": "We want to use Project Atlas for onboarding.",
  "notes": null,
  "status": null,
  "assignedStaffId": null
}
```

Response `202`:

```json
{
  "id": "ffffffff-ffff-ffff-ffff-ffffffffffff",
  "status": "New"
}
```

Frontend note: public callers cannot set internal status, internal notes, or assignee. The backend always creates public interests as `"New"`.

## Implementation Checklist

- Always call dashboard and recipient APIs with `credentials: "include"`.
- Store only organization IDs and UI state in local storage. Do not store raw cookies, API keys, checklist raw tokens, or signed URLs.
- Treat signed upload/download URLs as short-lived.
- Use `Idempotency-Key` for action creation and recipient submission.
- For `/c/{token}` pages, set `Referrer-Policy: no-referrer`.
- Hide raw checklist links in normal support logs. Show only the first 6 token characters if needed.
- On `401`, route dashboard users to login and recipients back to the original link screen.
- On `403 otp_required`, show the OTP form.
- On `409 concurrency_conflict`, refresh recipient checklist drafts before saving again.
- On `503 email_send_failed` or `otp_email_failed`, show a retry option and support fallback.

## Current Backend Gaps To Plan Around

These are not frontend blockers for the documented endpoints, but they matter for a polished production UI.

- Template management currently supports list, create, and publish only. There is no template detail, update, archive, or version-edit endpoint yet.
- Checklist creation currently accepts one recipient. The database can model multiple recipients, but the create endpoint takes a single `recipient`.
- Organization member management is not exposed yet. Signup creates an owner; platform lead approval can create an owner; dashboard invite/edit/remove member endpoints are still needed.
- Recipient file status polling is not exposed yet. The recipient receives status from upload completion, but there is no `GET /v1/recipient/uploads/{fileId}` endpoint to poll scanner completion.
- `POST /v1/recipient/return-link` queues a delivery record, but the current backend does not yet send that return-link email.
- Requesting changes on a submission updates review state, but it does not automatically send the recipient an email. Use checklist reminders until a dedicated changes-requested email endpoint exists.
- Dashboard list endpoints do not currently expose pagination. Add pagination before high-volume production use.
