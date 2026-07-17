# Reqara Frontend API Implementation Guide

This file is the customer dashboard, recipient portal, and developer handoff for the production backend.

Platform admin endpoints are documented separately in [PLATFORM_ADMIN_API_IMPLEMENTATION.md](PLATFORM_ADMIN_API_IMPLEMENTATION.md).

Customer analytics, platform analytics, and Investor & Board API flows are documented in [ANALYTICS_AND_INVESTOR_FRONTEND_HANDOFF.md](ANALYTICS_AND_INVESTOR_FRONTEND_HANDOFF.md).

Frontend deployment is documented separately in [FRONTEND_DROPLET_DEPLOYMENT_GUIDE.md](FRONTEND_DROPLET_DEPLOYMENT_GUIDE.md).

Recipient autosave, duplicate-send protection, and the "Previously Submitted" document flow are documented in [PREVIOUSLY_SUBMITTED_DOCUMENTS_FRONTEND_FLOW.md](PREVIOUSLY_SUBMITTED_DOCUMENTS_FRONTEND_FLOW.md).

Auth, password reset, recipient OTP, and pricing semantics are documented in [FRONTEND_AUTH_PRICING_OTP_HANDOFF.md](FRONTEND_AUTH_PRICING_OTP_HANDOFF.md).

Stripe checkout, subscription management, and billing webhooks are documented in [FRONTEND_STRIPE_BILLING_HANDOFF.md](FRONTEND_STRIPE_BILLING_HANDOFF.md).

Recipient submission notification behavior and org notification settings are documented in [FRONTEND_SUBMISSION_NOTIFICATIONS_HANDOFF.md](FRONTEND_SUBMISSION_NOTIFICATIONS_HANDOFF.md).

## Base URLs

```text
API production origin: https://api.reqara.com
API health: https://api.reqara.com/v1/health
Frontend app base URL in DB: https://reqara.com
OpenAPI JSON: https://api.reqara.com/openapi/v1.json
Developer reference: https://api.reqara.com/v1/developer/reference
```

Use:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
VITE_TURNSTILE_SITE_KEY=0x4AAAAAAD3PDppjZsCXGkx4
```

`app.baseUrl` is not the API origin. The backend uses it for recipient links in emails: `https://reqara.com/c/{token}`. Platform admins can CRUD it in platform settings. Organization admins can override it with organization admin settings.

## Client Rules

- JSON uses `camelCase`.
- Enum values are strings, for example `"Active"`, `"Text"`, `"Production"`.
- Dashboard and recipient browser calls must use `credentials: "include"`.
- Dashboard auth cookie: `__Host-atlas_dashboard`.
- Recipient auth cookie: `__Host-atlas_recipient`.
- Include `X-Atlas-Organization-Id` when the signed-in user belongs to more than one org.
- After login or signup, call `GET /v1/me`, store `currentOrganizationId`, and include it on all dashboard-scoped calls.
- If `GET /v1/dashboard/summary` or `POST /v1/auth/email-verification/request` returns `401 authentication_required`, the browser did not send the dashboard cookie. Check `credentials: "include"`, HTTPS, and that the cookie exists for `api.reqara.com`.
- Server integrations use `X-Atlas-Key`. Never put API keys in browser code.
- CORS is currently open for rapid integration. Tighten this before final hardening.

```ts
const API_BASE_URL = import.meta.env.VITE_ATLAS_API_BASE_URL ?? "https://api.reqara.com";

export async function api(path: string, init: RequestInit = {}) {
  const organizationId = window.localStorage.getItem("reqara.currentOrganizationId");
  const res = await fetch(`${API_BASE_URL}${path}`, {
    credentials: "include",
    ...init,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      ...(organizationId ? { "X-Atlas-Organization-Id": organizationId } : {}),
      ...(init.headers ?? {})
    }
  });
  if (!res.ok) throw await res.json();
  return res.status === 204 ? null : res.json();
}
```

## Error Shape

Most errors use problem details:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Name is required.",
  "status": 422,
  "code": "validation_failed"
}
```

Entitlement and quota failures use `402` and include the current plan and usage:

```json
{
  "type": "https://docs.atlas.example/errors/monthly_checklist_limit_exceeded",
  "title": "This organization has reached the Free monthly checklist limit.",
  "status": 402,
  "code": "monthly_checklist_limit_exceeded",
  "plan": {
    "code": "free",
    "name": "Free",
    "monthlyChecklistLimit": 10,
    "storageBytes": 524288000
  },
  "usage": {
    "checklistsSent": 10,
    "checklistsRemaining": 0,
    "storageBytesUsed": 245760,
    "storageBytesRemaining": 524042240
  }
}
```

Common codes:

```text
401: not signed in, no tenant, expired recipient session, invalid credentials
402: plan feature/quota not available
403: missing permission, sandbox key used on production endpoint, OTP required
404: resource not found
409: duplicate, state conflict, stale autosave version, idempotency conflict
410: expired/inactive recipient link
413: upload too large
422: validation failed
429: OTP locked/rate limited
503: email, storage, or provider failure
```

## Pagination

List endpoints use:

```text
page: 1-based, default 1
pageSize: default 25, max 100 unless noted
```

Response envelope:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 25,
  "total": 148
}
```

Dashboard lists with this envelope: `/v1/actions`, `/v1/submissions`.

## Enums

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
ApiKeyEnvironment: Sandbox, Production
DeveloperAccessStatus: SandboxOnly, ProductionRequested, ProductionApproved, ProductionRejected
```

## Product And Pricing Support

The backend now supports and enforces the public pricing table by DB-configurable plans.

Default plan for self-serve signup: `free`.

Default limits:

```text
Free: 10 sent recipient checklist requests/month, 500 MB storage, team workspace, library, Reqara branding
Starter: 100 sent recipient checklist requests/month, 5 GB storage, custom branding, automatic reminders
Business: 500 sent recipient checklist requests/month, 25 GB storage, API/webhooks, custom workflows, priority support
Scale: custom volume, SSO/security review/DPA/dedicated onboarding flags, custom retention
```

Enforced today:

- Monthly checklist quota when sending immediately or sending a draft.
- Storage quota before creating upload intents.
- Custom logo/color by `custom_branding`.
- Custom retention by `custom_retention`.
- Custom template creation/publish by `custom_workflows`.
- Production API keys and webhooks by `api_and_webhooks` plus platform approval.
- Automatic scheduled reminders only for plans with `automatic_reminders`.

Self-serve billing:

- Starter and Business use Stripe Checkout subscriptions.
- Billing management uses Stripe Customer Portal.
- Stripe webhooks update organization plan state and revenue records.
- Free requires no payment method. Scale remains contact sales/custom pricing.

## Website Claims Matrix

Use this as the frontend truth table.

| Claim | Backend status | Frontend note |
| --- | --- | --- |
| Secure checklist links | Supported | Tokens are CSPRNG path tokens. DB stores token hashes only. |
| No recipient account | Supported | Recipient opens `/c/{token}` and optional OTP, no account. |
| Works on mobile | API-supported | Responsive UX is frontend-owned. |
| Direct secure uploads | Supported | Browser uploads directly to private DigitalOcean Spaces signed URLs. |
| Private object storage | Supported by configuration | Objects are stored in private Spaces; never expose public object URLs. |
| Time-limited downloads | Supported | Reviewer downloads use short-lived signed URLs. |
| Progress tracking | Supported | Dashboard summary, action statuses, recipients, timeline. |
| One final package | Supported | Submissions collect responses/files into one package. |
| Automatic reminders | Supported | Worker sends before-due and overdue reminders for eligible plans. |
| API and webhooks | Supported for Business/Scale | Sandbox is self-serve. Production requires platform approval. |
| Malware scanning | Supported with ClamAV | Files are scanned after upload completion and blocked unless marked `Clean`. |
| Audit trail | Supported | Sends, views, uploads, OTP, submissions, reviews, admin events. |
| Auto-expiration | Supported | Recipient links expire; tokens rotate on resend/reminder. |
| Rate limiting | Supported broadly | Global fixed-window rate limiting plus OTP attempt caps. |
| Configurable retention | Supported | Retention days and purge worker are DB-configurable. |
| Deletion workflows | Supported | Submission delete deletes underlying objects and marks files deleted. |
| Consent records | Supported | Submission declaration timestamp and IP are stored. |
| Data export | Supported | Submission export endpoint includes package and timeline. |
| SSO | Plan flag only | Do not show as self-serve yet. Treat as Scale/sales. |
| Payments | Supported for product subscriptions | Starter/Business use Stripe Checkout; Scale is contact sales. |
| Signatures | Requirement type exists | Full e-signature workflow is not implemented yet. |

## Health

### GET `/v1/health`

Response:

```json
{
  "status": "ok",
  "service": "atlas-api"
}
```

## Auth And Organization Signup

Self-serve organizations do not require platform approval. Signup creates an active Free org, signs the user in, and sends an email verification link.

Users can explore and create drafts before email verification. Sending live checklist requests, creating API keys, and creating/rotating production webhooks require `emailVerified: true`.

### POST `/v1/auth/signup`

`organizationSlug` is optional. When omitted, the backend derives a unique slug from the organization name. Accepted aliases:

- full name: `fullName`, `name`, or `firstName` + `lastName`
- organization name: `organizationName`, `orgName`, `companyName`, or `company`
- organization slug: `organizationSlug`, `orgSlug`, or `slug`

```json
{
  "email": "ollie@northstarstaffing.com",
  "password": "correct-horse-battery-staple",
  "fullName": "Ollie Ed",
  "organizationName": "Northstar Staffing",
  "timezone": "America/Toronto",
  "defaultLanguage": "en"
}
```

`201`:

```json
{
  "userId": "11111111-1111-1111-1111-111111111111",
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "organizationSlug": "northstar-staffing",
  "role": "Owner",
  "emailVerified": false,
  "emailVerificationSent": true
}
```

See [FRONTEND_AUTH_PRICING_OTP_HANDOFF.md](FRONTEND_AUTH_PRICING_OTP_HANDOFF.md) for verification, resend, password reset, and UI states.

Invalid JSON or body-binding failures return RFC7807:

```json
{
  "type": "https://docs.atlas.example/errors/invalid_request_body",
  "title": "Invalid request body.",
  "status": 400,
  "detail": "The request body could not be parsed or did not match the expected JSON contract.",
  "code": "invalid_request_body",
  "errors": ["Check Content-Type, JSON syntax, and required request fields."]
}
```

### POST `/v1/auth/login`

```json
{
  "email": "ollie@northstarstaffing.com",
  "password": "correct-horse-battery-staple",
  "organizationId": "22222222-2222-2222-2222-222222222222"
}
```

`200`:

```json
{
  "userId": "11111111-1111-1111-1111-111111111111",
  "email": "ollie@northstarstaffing.com",
  "fullName": "Ollie Ed",
  "emailVerifiedAt": null,
  "emailVerified": false,
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

### GET `/v1/me`

Use on app boot to restore the dashboard session. Response is the same shape as login.

### POST `/v1/auth/logout`

Returns `204`.

## Organization Profile

### GET `/v1/organizations/{id}`

```json
{
  "id": "22222222-2222-2222-2222-222222222222",
  "name": "Northstar Staffing",
  "slug": "northstar-staffing",
  "status": "Active",
  "timezone": "America/Toronto",
  "defaultLanguage": "en",
  "accentColor": "#0f172a",
  "privacyStatement": "We use your documents only to process your request.",
  "retentionDays": 365,
  "createdAt": "2026-07-16T05:00:00Z",
  "updatedAt": "2026-07-16T05:00:00Z"
}
```

### PATCH `/v1/organizations/{id}`

Custom branding requires Starter or higher. Custom retention requires Scale.

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

## Billing And Entitlements

Full checkout and subscription flows are documented in [FRONTEND_STRIPE_BILLING_HANDOFF.md](FRONTEND_STRIPE_BILLING_HANDOFF.md).

### GET `/v1/billing/plans`

Use for pricing UI.

```json
{
  "items": [
    {
      "code": "free",
      "name": "Free",
      "description": "Try Reqara on real requests.",
      "monthlyPriceCents": 0,
      "annualPriceCents": 0,
      "currency": "USD",
      "monthlyChecklistLimit": 10,
      "storageBytes": 524288000,
      "customPricing": false,
      "features": {
        "teamWorkspace": true,
        "templateLibrary": true,
        "reqaraBranding": true,
        "customBranding": false,
        "automaticReminders": false,
        "apiAndWebhooks": false,
        "customWorkflows": false,
        "prioritySupport": false,
        "sso": false,
        "customRetention": false,
        "securityReview": false,
        "dpa": false,
        "dedicatedOnboarding": false
      }
    }
  ]
}
```

### GET `/v1/organizations/{id}/entitlements`

Use for plan badge, quota meter, feature locks, and upgrade prompts.

```json
{
  "plan": {
    "code": "business",
    "name": "Business",
    "monthlyChecklistLimit": 500,
    "storageBytes": 26843545600,
    "features": {
      "apiAndWebhooks": true,
      "customWorkflows": true,
      "prioritySupport": true
    }
  },
  "billing": {
    "planCode": "business",
    "billingCycle": "monthly",
    "status": "active",
    "currentPeriodStart": "2026-07-17T00:00:00Z",
    "currentPeriodEnd": "2026-08-17T00:00:00Z",
    "provider": "stripe",
    "stripeCustomerId": "cus_123",
    "stripeSubscriptionId": "sub_123",
    "cancelAtPeriodEnd": false
  },
  "usage": {
    "periodStart": "2026-07-01T00:00:00Z",
    "periodEnd": "2026-08-01T00:00:00Z",
    "checklistsSent": 18,
    "monthlyChecklistLimit": 500,
    "checklistsRemaining": 482,
    "storageBytesUsed": 13631488,
    "storageBytesLimit": 26843545600,
    "storageBytesRemaining": 26829914112
  }
}
```

## Dashboard Summary

### GET `/v1/dashboard/summary`

Use for the "who needs attention" dashboard.

```json
{
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "generatedAt": "2026-07-16T06:00:00Z",
  "needsAttention": {
    "waitingRecipients": 3,
    "overdueRecipients": 1,
    "dueSoonRecipients": 1
  },
  "checklists": {
    "total": 18,
    "draft": 2,
    "inFlight": 8,
    "submitted": 3,
    "completed": 5,
    "cancelled": 0,
    "expired": 0
  },
  "submissions": {
    "total": 12,
    "submitted": 3,
    "accepted": 8,
    "changesRequested": 1
  },
  "files": {
    "pendingScan": 2,
    "rejected": 0
  },
  "attentionItems": [
    {
      "actionId": "88888888-8888-8888-8888-888888888888",
      "checklistTitle": "Vendor Insurance Renewal",
      "dueAt": "2026-07-15T00:00:00Z",
      "recipientId": "99999999-9999-9999-9999-999999999999",
      "recipientName": "ABC Plumbing",
      "recipientEmail": "admin@abc.example",
      "recipientStatus": "Viewed",
      "lastActivityAt": "2026-07-14T13:00:00Z",
      "overdue": true,
      "dueSoon": false
    }
  ],
  "entitlements": {}
}
```

## Organization Members

### GET `/v1/organization-members`

```json
{
  "items": [
    {
      "id": "33333333-3333-3333-3333-333333333333",
      "organizationId": "22222222-2222-2222-2222-222222222222",
      "userId": "11111111-1111-1111-1111-111111111111",
      "email": "ollie@northstarstaffing.com",
      "fullName": "Ollie Ed",
      "role": "Owner",
      "status": "Active",
      "invitedAt": null,
      "joinedAt": "2026-07-16T05:00:00Z",
      "createdAt": "2026-07-16T05:00:00Z"
    }
  ]
}
```

### POST `/v1/organization-members`

Requires Owner/Admin. If creating a new user, include `password`. Existing users can be added without password.

```json
{
  "email": "member@northstarstaffing.com",
  "password": "temporary-password",
  "fullName": "Jamie Manager",
  "role": "Member",
  "status": "Active"
}
```

### PATCH `/v1/organization-members/{id}`

```json
{
  "fullName": "Jamie Manager",
  "password": null,
  "role": "Admin",
  "status": "Active"
}
```

### DELETE `/v1/organization-members/{id}`

Disables the member. Returns `204`.

## Organization Admin Settings

### GET `/v1/admin/settings?organizationId={id}`

Requires `admin:*`.

### PUT `/v1/admin/settings/{category}/{key}`

Use for org-specific overrides such as `app.baseUrl`, upload limits, retention, sender display, or token grace.

```json
{
  "scope": "Organization",
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "value": "https://reqara.com",
  "isSecret": false,
  "updatedByUserId": "11111111-1111-1111-1111-111111111111"
}
```

## Template Library

The intended UI should feel like Canva search, not a wall of cards.

Primary screen:

```text
What are you trying to collect?
[ Search the library... ]
```

Examples:

```text
insurance -> Vendor Insurance Renewal, Contractor Insurance, Vehicle Insurance, Property Insurance, Claim Evidence
employee -> Candidate Onboarding, New Employee Onboarding, Employee Offboarding, Equipment Return, Reference Check
canada -> New Hire Onboarding (Canada), WSIB Contractor Compliance, Tax Season Client Checklist (Canada)
```

### GET `/v1/templates`

Query params:

```text
query: search text
category: optional category
country: optional country
region: optional region
limit: 1..100, default 50
```

Example:

```text
GET /v1/templates?query=insurance&limit=8
```

Response:

```json
{
  "items": [
    {
      "id": "66666666-6666-6666-6666-666666666666",
      "name": "Healthcare Staffing Onboarding (Ontario)",
      "category": "HR & Recruitment",
      "description": "Ontario healthcare staffing intake with ID, credentials, immunization proof, vulnerable sector check, availability, and placement acknowledgements.",
      "status": "Published",
      "currentVersionId": "77777777-7777-7777-7777-777777777777",
      "isGlobal": true,
      "isEditable": false,
      "title": "Healthcare Staffing Onboarding (Ontario)",
      "instructions": "Use for Ontario healthcare staffing agencies before placement.",
      "settings": {
        "industry": "HR",
        "example": "Healthcare Staffing Onboarding (Ontario)",
        "rating": 5,
        "highlight": "Built for Ontario healthcare staffing workflows.",
        "country": "Canada",
        "region": "Ontario",
        "tags": ["healthcare", "staffing", "employee", "ontario"],
        "searchTerms": ["psw", "nurse", "vulnerable sector", "immunization"]
      },
      "industry": "HR",
      "example": "Healthcare Staffing Onboarding (Ontario)",
      "rating": 5,
      "highlight": "Built for Ontario healthcare staffing workflows.",
      "country": "Canada",
      "region": "Ontario",
      "tags": ["healthcare", "staffing", "employee", "ontario"],
      "searchTerms": ["psw", "nurse", "vulnerable sector", "immunization"],
      "requirements": [
        {
          "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "key": "government_id",
          "type": "File",
          "label": "Government ID",
          "description": "Upload a clear image or PDF of your ID.",
          "required": true,
          "displayOrder": 1,
          "configuration": { "allowReuse": true, "maxReuseAgeDays": 365 },
          "validation": { "allowedMimeTypes": ["application/pdf", "image/jpeg", "image/png"] },
          "condition": null
        }
      ],
      "createdAt": "2026-07-16T05:00:00Z",
      "updatedAt": "2026-07-16T05:00:00Z"
    }
  ]
}
```

### GET `/v1/templates/{id}`

Returns the same shape as list items, including `requirements`.

Use this when a sender opens a template preview, starts from a template, or opens an organization-owned custom template in the builder.

Important UI fields:

- `isGlobal: true`, `isEditable: false`: show "Use template" and "Customize a copy". Do not show direct edit controls.
- `isGlobal: false`, `isEditable: true`: show edit, publish, archive, and delete actions if the organization has `custom_workflows`.
- `status: Draft`: show "Publish" before this saved template can be used by `POST /v1/actions`.

### POST `/v1/templates`

Requires `custom_workflows` entitlement.

Creates a new organization-owned custom template in `Draft`.

```json
{
  "name": "Employee onboarding",
  "category": "HR",
  "description": "Collect identity and payroll documents.",
  "title": "Onboarding documents",
  "instructions": "Please complete each item carefully.",
  "settings": {
    "industry": "HR",
    "rating": 5,
    "tags": ["employee", "onboarding"]
  },
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": [
    {
      "key": "legal_name",
      "type": "Text",
      "label": "Full legal name",
      "description": "Enter your full legal name.",
      "required": true,
      "displayOrder": 1,
      "configuration": {},
      "validation": { "minLength": 2 },
      "condition": null
    }
  ]
}
```

### POST `/v1/templates/{id}/clone`

Requires `custom_workflows` entitlement.

Use when a user chooses a global library template and clicks "Customize a copy". The backend creates an organization-owned template. The source global template is never modified.

`additionalRequirements` appends fields to the copied template. For deeper editing, clone first, then call `PUT /v1/templates/{newId}` with the full `requirements` array.

```json
{
  "name": "Healthcare Staffing Onboarding - RyvePool",
  "category": "HR & Recruitment",
  "description": "RyvePool-specific onboarding checklist based on the Ontario healthcare staffing template.",
  "title": "Healthcare Staffing Onboarding",
  "instructions": "Complete all required items before placement.",
  "settings": {
    "industry": "HR",
    "tags": ["healthcare", "staffing", "ryvepool"]
  },
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "additionalRequirements": [
    {
      "key": "uniform_size",
      "type": "Text",
      "label": "Uniform size",
      "description": "Tell us your preferred uniform size.",
      "required": false,
      "displayOrder": 99,
      "configuration": {},
      "validation": {},
      "condition": null
    }
  ],
  "publishImmediately": false
}
```

`201` returns the organization-owned template with `isGlobal: false`, `isEditable: true`.

### PUT `/v1/templates/{id}`

Requires `custom_workflows` entitlement.

Only organization-owned templates can be edited. If the user tries to edit a global template directly, the backend returns:

```json
{
  "title": "Clone this library template before customizing it for your organization.",
  "status": 403,
  "code": "global_template_not_editable"
}
```

Send the complete `requirements` list when editing checklist fields. The backend creates a new draft template version and leaves the previously published version intact until publish.

```json
{
  "name": "Employee onboarding",
  "category": "HR",
  "description": "Collect identity, payroll, and equipment documents.",
  "title": "Employee onboarding documents",
  "instructions": "Complete each required item.",
  "settings": {
    "industry": "HR",
    "tags": ["employee", "onboarding"]
  },
  "updatedByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": [
    {
      "key": "legal_name",
      "type": "Text",
      "label": "Full legal name",
      "description": "Enter your full legal name.",
      "required": true,
      "displayOrder": 1,
      "configuration": {},
      "validation": { "minLength": 2 },
      "condition": null
    },
    {
      "key": "direct_deposit_form",
      "type": "File",
      "label": "Direct deposit form",
      "description": "Upload a completed direct deposit form.",
      "required": true,
      "displayOrder": 2,
      "configuration": { "allowReuse": true, "maxReuseAgeDays": 365 },
      "validation": { "allowedMimeTypes": ["application/pdf", "image/jpeg", "image/png"] },
      "condition": null
    }
  ]
}
```

`200` returns the updated template with `status: "Draft"`. Show a "Publish changes" CTA.

### POST `/v1/templates/{id}/publish`

Requires `custom_workflows`. Returns the template response.

Publishes the newest draft version of an organization-owned template. Published templates can be selected by `POST /v1/actions`.

### POST `/v1/templates/{id}/archive`

Requires `custom_workflows`. Archives an organization-owned custom template.

### DELETE `/v1/templates/{id}`

Requires `custom_workflows`. Soft-deletes an organization-owned custom template.

## Checklist Actions

### POST `/v1/actions`

Use `Idempotency-Key` for create.

Sending immediately consumes monthly checklist quota.

When `templateId` is provided, the backend clones the published template requirements into the action. `requirements: []` is valid and means "use the template requirements only."

When `templateId` is provided and `requirements` contains items, the backend appends those items as one-off custom fields for this action. It does not modify the saved template.

When `templateId` is omitted, provide explicit `requirements`; this creates a one-off custom checklist action.

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
  "settings": {},
  "sendImmediately": true,
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": [],
  "clientReference": "frontend-draft-01J2ZVR8V67M6P3F9W1X2G7F21",
  "allowDuplicate": false
}
```

Example: selected template plus one extra field for this recipient only:

```json
{
  "templateId": "66666666-6666-6666-6666-666666666666",
  "title": "Onboarding - Jamie Chen",
  "description": "Collect onboarding documents plus uniform preference.",
  "recipient": {
    "name": "Jamie Chen",
    "email": "jamie.chen@example.com",
    "phone": null,
    "otpRequired": false
  },
  "dueAt": "2026-08-15T00:00:00Z",
  "expiresAt": null,
  "settings": {},
  "sendImmediately": true,
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": [
    {
      "key": "uniform_size",
      "type": "Text",
      "label": "Uniform size",
      "description": "Tell us your preferred uniform size.",
      "required": false,
      "displayOrder": 1,
      "configuration": {},
      "validation": {},
      "condition": null
    }
  ],
  "clientReference": "frontend-draft-01J2ZVR8V67M6P3F9W1X2G7F22",
  "allowDuplicate": false
}
```

Builder UX recommendation:

- Use `POST /v1/actions` with `templateId + requirements` for one-off additions.
- Use `POST /v1/templates/{id}/clone`, `PUT /v1/templates/{id}`, then `POST /v1/templates/{id}/publish` when the user wants to save a reusable custom workflow.

`201`:

```json
{
  "id": "88888888-8888-8888-8888-888888888888",
  "publicReference": "act_5fd67eb7d9c2a001e3a4",
  "clientReference": "frontend-draft-01J2ZVR8V67M6P3F9W1X2G7F21",
  "title": "Onboarding - Jamie Chen",
  "status": "Sent",
  "dueAt": "2026-08-15T00:00:00Z",
  "expiresAt": "2026-09-14T00:00:00Z",
  "recipientUrl": "https://reqara.com/c/9f3kV2qP_hN8mL0aZxYtR7wQeUcJbGdKsMoXnBvIrAs",
  "createdAt": "2026-07-16T05:00:00Z"
}
```

Do not log the full `recipientUrl`. For support, show only the first six token characters.

Duplicate handling:

- Send a stable `clientReference` for each checklist creation attempt. If the frontend retries the same logical request without an `Idempotency-Key`, the backend returns `409 duplicate_client_reference` with `existingActionId` and `publicReference`.
- If `clientReference` is omitted, the backend still blocks a very recent same-recipient, same-template/title/due date create with `409 possible_duplicate_action`.
- Only send `allowDuplicate: true` after showing a confirmation UI to the sender.

### GET `/v1/actions`

Lists actions for dashboard tables.

```text
GET /v1/actions?page=1&pageSize=25
```

```json
{
  "items": [
    {
      "id": "88888888-8888-8888-8888-888888888888",
      "publicReference": "act_5fd67eb7d9c2a001e3a4",
      "clientReference": "frontend-draft-01J2ZVR8V67M6P3F9W1X2G7F21",
      "title": "Onboarding - Jamie Chen",
      "status": "Sent",
      "dueAt": "2026-08-15T00:00:00Z",
      "expiresAt": "2026-09-14T00:00:00Z",
      "sentAt": "2026-07-16T05:00:00Z",
      "completedAt": null,
      "templateId": "66666666-6666-6666-6666-666666666666",
      "templateName": "Candidate Onboarding",
      "recipient": {
        "id": "99999999-9999-9999-9999-999999999999",
        "name": "Jamie Chen",
        "email": "jamie.chen@example.com",
        "status": "Viewed",
        "lastActivityAt": "2026-07-16T06:30:00Z",
        "submittedAt": null
      },
      "completedRequirementCount": 2,
      "totalRequirementCount": 8,
      "createdAt": "2026-07-16T05:00:00Z",
      "updatedAt": "2026-07-16T06:30:00Z",
      "lastActivityAt": "2026-07-16T06:30:00Z"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

### GET `/v1/actions/{id}`

Returns action detail with recipients and requirements.

### POST `/v1/actions/{id}/send`

Mints fresh tokens, sends invitations, schedules automatic reminders when the plan allows it, and increments usage if the action was not previously sent.

```json
{ "sent": 1 }
```

### POST `/v1/actions/{id}/cancel`

Cancels an action.

### POST `/v1/actions/{id}/reminders`

Sends manual reminder or overdue email to pending recipients. Tokens are rotated for reminded recipients.

### GET `/v1/actions/{id}/timeline`

Returns audit events for the action.

## Files

Dashboard and recipient uploads are direct-to-Spaces. The backend creates a private object key and signed PUT URL.

### POST `/v1/files/upload-intents`

Requires production dashboard or production API key. Sandbox keys cannot touch production files.

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

`201`:

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "storageKey": "quarantine/22222222-2222-2222-2222-222222222222/cccccccc-cccc-cccc-cccc-cccccccccccc/passport.pdf",
  "uploadUrl": "https://tor1.digitaloceanspaces.com/...",
  "headers": {
    "Content-Type": "application/pdf"
  },
  "expiresAt": "2026-07-16T05:10:00Z",
  "retentionUntil": "2027-07-16T05:00:00Z"
}
```

Upload:

```ts
await fetch(uploadIntent.uploadUrl, {
  method: "PUT",
  headers: uploadIntent.headers,
  body: file
});
```

### POST `/v1/files/{id}/complete`

Checks object exists, verifies size, and scans with ClamAV in the current production `clamav` mode. Clean files return `Clean`; infected files return `Rejected`. If platform admin changes `fileScanning.mode` to `external`, this returns `Pending` until a scanner posts `/scan-result`.

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "scanStatus": "Clean",
  "scanCompletedAt": "2026-07-16T05:10:00Z"
}
```

### POST `/v1/files/{id}/scan-result`

Trusted scanner/admin callback.

```json
{
  "scanStatus": "Clean",
  "scanEngine": "clamav"
}
```

### GET `/v1/files/{id}/download-url`

Only returns URLs for files marked `Clean`.

## Submissions

### GET `/v1/submissions`

Submission inbox.

```text
GET /v1/submissions?page=1&pageSize=25
```

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
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

### GET `/v1/submissions/{id}`

Includes declaration timestamp/IP, content hash, response values, and file IDs.

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
  "declarationAcceptedAt": "2026-07-16T14:30:00Z",
  "declarationIpAddress": "203.0.113.25",
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

### GET `/v1/submissions/{id}/export`

Use for data export/package view. Includes title, recipient, responses, files, scan status, declaration, and timeline.

### POST `/v1/submissions/{id}/accept`

Marks the latest submitted package as accepted, stores `reviewComment`, and marks the action `Completed`.

```json
{
  "comment": "All documents accepted."
}
```

### POST `/v1/submissions/{id}/request-changes`

Marks the submitted package `ChangesRequested` and stores `reviewComment`. The recipient can submit a new version.

```json
{
  "comment": "Please upload a clearer ID."
}
```

### DELETE `/v1/submissions/{id}`

Requires admin. Deletes the submission and deletes underlying Spaces objects.

## Recipient Portal

The frontend route `/c/{token}` should render an HTML page with:

```text
Referrer-Policy: no-referrer
```

Then call the API:

### GET `/c/{token}` or `/r/{token}`

Validates token, creates recipient cookie, sends OTP if required.

Because this is a cross-origin API call from `https://reqara.com` to `https://api.reqara.com`, call it with credentials enabled. The cookie is HttpOnly and intentionally not readable by JavaScript.

```ts
await fetch(`${apiBaseUrl}/c/${token}`, {
  method: "GET",
  credentials: "include"
});
```

```json
{
  "otpRequired": true,
  "otpVerified": false,
  "sessionExpiresAt": "2026-07-16T08:00:00Z",
  "organizationName": "Northstar Staffing",
  "checklistTitle": "Onboarding - Jamie Chen"
}
```

If `otpRequired` is `true` and `otpVerified` is `false`, show the OTP screen immediately. The code has already been emailed.

Repeated calls to `/c/{token}` while an unexpired OTP already exists do not send another email. Keep the user on the OTP screen and let them use the resend action below if needed.

### POST `/v1/recipient/access/verify`

Must also include credentials so the backend receives the recipient session cookie created by `/c/{token}`. The code alone is not enough.

```ts
await fetch(`${apiBaseUrl}/v1/recipient/access/verify`, {
  method: "POST",
  credentials: "include",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ code })
});
```

```json
{
  "code": "123456"
}
```

`200`:

```json
{ "verified": true }
```

OTP errors:

```json
{
  "type": "https://docs.atlas.example/errors/invalid_otp",
  "title": "Access code is invalid.",
  "status": 422,
  "code": "invalid_otp"
}
```

```text
403 otp_required: returned by recipient data endpoints before OTP verification
410 otp_expired: reopen /c/{token} to send a fresh code
429 otp_locked: too many attempts
```

### POST `/v1/recipient/access/resend`

Use when the recipient clicks "Resend code". Requires the same recipient session cookie, so include credentials.

```ts
await fetch(`${apiBaseUrl}/v1/recipient/access/resend`, {
  method: "POST",
  credentials: "include"
});
```

`202`:

```json
{
  "sent": true,
  "otpRequired": true,
  "otpVerified": false,
  "expiresAt": "2026-07-16T21:10:00Z"
}
```

`429 otp_resend_too_soon` means the recipient clicked resend too quickly. Show the problem `title` or a short countdown before enabling resend again.

### GET `/v1/recipient/checklist`

Returns organization branding, checklist metadata, requirements, and draft responses.

All `/v1/recipient/*` calls need `credentials: "include"`. If the user is also signed into the dashboard in the same browser, the backend now prefers the recipient session cookie for recipient endpoints.

File requirements can include a `previouslySubmitted` object when the same recipient email has an accepted, clean prior submission for the same organization and opted-in requirement. See [PREVIOUSLY_SUBMITTED_DOCUMENTS_FRONTEND_FLOW.md](PREVIOUSLY_SUBMITTED_DOCUMENTS_FRONTEND_FLOW.md) for the full UI flow.

### PATCH `/v1/recipient/responses/{requirementId}`

Autosave with optimistic concurrency.

```json
{
  "value": "Jamie Chen",
  "expectedVersion": 2
}
```

`200`:

```json
{
  "requirementId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "version": 3,
  "updatedAt": "2026-07-16T05:25:00Z"
}
```

### POST `/v1/recipient/uploads`

Creates a signed upload URL and applies storage quota.

```json
{
  "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  "fileName": "government-id.pdf",
  "sizeBytes": 245760,
  "mimeType": "application/pdf"
}
```

Response includes `retentionUntil`, same shape as dashboard upload intents.

### POST `/v1/recipient/uploads/{fileId}/complete`

Marks upload received, verifies size, and scans with ClamAV in the current production `clamav` mode. Clean files return `Clean`; infected files return `Rejected`. If platform admin changes `fileScanning.mode` to `external`, this returns `Pending` until the scanner callback arrives.

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "scanStatus": "Clean",
  "scanCompletedAt": "2026-07-16T15:05:00Z"
}
```

### GET `/v1/recipient/uploads/{fileId}`

Recipient-safe polling endpoint for scan state. If the file is still `Pending` and the uploaded object exists in storage, the backend attempts to finalize the ClamAV scan during this request and returns the updated status. A backend worker also scans pending files on the configured interval, so polling is safe even if the initial complete call returned `Pending`.

```json
{
  "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "scanStatus": "Clean",
  "scanCompletedAt": "2026-07-16T15:05:00Z"
}
```

The recipient cannot submit while attached files are `Pending` or `Rejected`; submit returns `409 file_not_available`.

If an upload intent is created but the file never reaches storage, the backend worker leaves it `Pending` during the upload grace window and then marks it `Rejected` with `scanStatus: "Rejected"`. Frontend should show retry/upload-new for `Rejected` files.

### DELETE `/v1/recipient/uploads/{fileId}`

Deletes a draft upload.

### POST `/v1/recipient/return-link`

Rotates token and emails a fresh link. Returns `202`.

### POST `/v1/recipient/submit`

Use `Idempotency-Key`.

```json
{
  "declarationAccepted": true,
  "files": [
    {
      "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
      "usePreviouslySubmitted": false,
      "documentName": "Government ID",
      "documentExpiresAt": "2028-04-30T00:00:00Z"
    }
  ]
}
```

`201`:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-16T14:30:00Z",
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924...",
  "publicReference": "REQ-20260717-090A18",
  "receiptReference": "REQ-20260717-090A18",
  "verificationFingerprint": "E3B0-C442-98FC-1C14"
}
```

### GET `/v1/recipient/receipt`

Returns the latest submission receipt as JSON for rendering the success page.

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "checklistId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "publicReference": "REQ-20260717-090A18",
  "receiptReference": "REQ-20260717-090A18",
  "checklistTitle": "Vendor onboarding",
  "organizationName": "RyvePool",
  "recipientName": "Jamie Chen",
  "recipientEmail": "jamie@example.com",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-17T14:30:00Z",
  "declarationAcceptedAt": "2026-07-17T14:30:00Z",
  "requirementCount": 5,
  "responseCount": 3,
  "fileCount": 2,
  "previouslySubmittedFileCount": 1,
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924...",
  "verificationFingerprint": "E3B0-C442-98FC-1C14"
}
```

Frontend success-page guidance:

- Use this JSON to render the submitted state.
- Prefer `receiptReference` for all recipient-facing copy. Do not display raw machine-style action references such as `act_...`.
- Do not render raw JSON to recipients.
- The status means Reqara received the submission. It does not mean the organization accepted/reviewed it.

### GET `/v1/recipient/receipt.pdf`

Downloads a branded PDF receipt generated by the backend.

Use this for the primary "Download receipt" button on the recipient success page.

Response:

- `200 application/pdf`
- `Content-Disposition` filename: `{receiptReference}-receipt.pdf`

### GET `/v1/recipient/receipt.txt`

Downloads a plain-text fallback receipt generated by the backend.

Use this as a secondary fallback only if PDF download fails or the user explicitly wants a text receipt.

### POST `/v1/recipient/contact-organization`

Recipient-safe contact relay. The recipient must have a valid recipient session and completed OTP when OTP is required. The backend sends the message to the configured organization contact/sender, with `Reply-To` set to the recipient email. The API does not expose staff emails to the recipient.

```json
{
  "subject": "Question about my submission",
  "message": "I noticed my insurance document expires soon. Should I upload the new one now?"
}
```

`202`:

```json
{
  "accepted": true,
  "sentAt": "2026-07-17T14:36:00Z"
}
```

Errors:

- `401 recipient_session_required` when the recipient session is missing/expired.
- `403 otp_required` when checklist OTP has not been verified.
- `409 contact_not_configured` when no safe contact destination exists.
- `422 validation_failed` when message is empty, message is over 2,000 characters, or subject is over 120 characters.
- `429 rate_limited` after too many messages in the configured cooldown window.
- `503 email_send_failed` if the email provider fails.

## Developer Experience

Recommended frontend route: `/developer`.

Product policy:

- Every org can create sandbox keys immediately.
- Sandbox keys use `atl_test_...`.
- Sandbox keys can only call `/v1/sandbox/*`.
- Sandbox endpoints validate request shape and return realistic responses without creating production recipients, emails, tokens, files, submissions, or usage.
- Production keys use `atl_live_...`.
- Production keys require `api_and_webhooks` entitlement and platform admin approval.
- API secrets are shown once in the dashboard response. They are never emailed and never returned again.

### GET `/v1/developer/reference`

Returns the OpenAPI URL, endpoint groups, and sample code for a developer page.

### GET `/v1/developer/access`

```json
{
  "status": "SandboxOnly",
  "sandboxAvailable": true,
  "productionAvailable": false,
  "planAllowsProduction": false,
  "requestedAt": null,
  "approvedAt": null,
  "rejectedAt": null,
  "notes": null
}
```

### POST `/v1/developer/access/production-request`

```json
{
  "useCase": "Sync onboarding requests from our HRIS.",
  "expectedVolume": "200 checklists per month",
  "message": "We are ready to test production API access."
}
```

`202` returns the updated access response.

### GET `/v1/developer/api-keys`

Alias: `GET /v1/api-keys`.

```json
{
  "items": [
    {
      "id": "55555555-5555-5555-5555-555555555555",
      "name": "Sandbox integration",
      "keyPrefix": "atl_test_ab12cd34",
      "environment": "Sandbox",
      "scopes": ["sandbox:*"],
      "lastUsedAt": null,
      "expiresAt": "2027-01-12T05:00:00Z",
      "revokedAt": null,
      "createdAt": "2026-07-16T05:00:00Z"
    }
  ]
}
```

### POST `/v1/developer/api-keys`

Sandbox key:

```json
{
  "name": "Sandbox integration",
  "environment": "Sandbox",
  "scopes": ["sandbox:*"],
  "expiresAt": null
}
```

`201`:

```json
{
  "id": "55555555-5555-5555-5555-555555555555",
  "name": "Sandbox integration",
  "keyPrefix": "atl_test_ab12cd34",
  "environment": "Sandbox",
  "secret": "atl_test_ab12cd34_abcdefghijklmnopqrstuvwxyz",
  "scopes": ["sandbox:*"],
  "expiresAt": "2027-01-12T05:00:00Z",
  "createdAt": "2026-07-16T05:00:00Z"
}
```

Production key after approval:

```json
{
  "name": "Production integration",
  "environment": "Production",
  "scopes": ["templates:read", "actions:write", "files:write"],
  "expiresAt": null
}
```

Expected blocking states:

```text
402 feature_not_available: org is not on Business/Scale
403 production_developer_access_required: platform admin has not approved production access
```

### DELETE `/v1/developer/api-keys/{id}`

Revokes the key.

### Sandbox APIs

Use these with either dashboard auth or `atl_test_...` sandbox keys.

#### GET `/v1/sandbox/status`

```json
{
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "environment": "sandbox",
  "dataPersistence": "none",
  "message": "Sandbox endpoints validate request shape and return realistic sample responses without touching production checklist or file data."
}
```

#### GET `/v1/sandbox/templates`

Returns sample sandbox templates.

#### POST `/v1/sandbox/checklists`

```json
{
  "title": "Vendor Insurance Renewal",
  "recipientEmail": "jamie@example.com",
  "templateId": "sandbox-template-vendor-insurance",
  "payload": {
    "insurance_certificate": "sample.pdf",
    "expiry_date": "2026-12-31"
  }
}
```

`201`:

```json
{
  "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
  "organizationId": "22222222-2222-2222-2222-222222222222",
  "title": "Vendor Insurance Renewal",
  "recipientEmail": "jamie@example.com",
  "environment": "sandbox",
  "status": "validated",
  "message": "No production action, recipient token, email, or file record was created."
}
```

### Webhooks

Production webhooks require Business/Scale entitlement and platform production developer approval.

Aliases:

```text
/v1/webhooks
/v1/developer/webhooks
```

#### POST `/v1/developer/webhooks`

```json
{
  "url": "https://example.com/reqara/webhook",
  "eventTypes": ["action.sent", "submission.accepted"],
  "status": "Active"
}
```

`201`:

```json
{
  "id": "77777777-7777-7777-7777-777777777777",
  "url": "https://example.com/reqara/webhook",
  "eventTypes": ["action.sent", "submission.accepted"],
  "status": "Active",
  "secret": "one-time-webhook-signing-secret",
  "createdAt": "2026-07-16T05:00:00Z"
}
```

Other endpoints:

```text
GET /v1/developer/webhooks
GET /v1/developer/webhooks/{id}
PATCH /v1/developer/webhooks/{id}
POST /v1/developer/webhooks/{id}/rotate-secret
DELETE /v1/developer/webhooks/{id}
GET /v1/developer/webhooks/{id}/deliveries
```

## Public Interest Intake

Use on marketing/contact sales. Self-serve Free signup does not need this flow.

## Public Contact

Use for the website contact form. The frontend must render Cloudflare Turnstile with:

```text
VITE_TURNSTILE_SITE_KEY=0x4AAAAAAD3PDppjZsCXGkx4
```

Send the Turnstile response token in `X-Reqara-Captcha-Token`. The backend verifies it before sending email.

### POST `/v1/public/contact`

Headers:

```text
Content-Type: application/json
X-Reqara-Captcha-Token: <turnstile-response-token>
```

Request:

```json
{
  "name": "Ollie Ed",
  "email": "ollie@example.com",
  "topic": "Sales",
  "message": "I want to learn more about Reqara for staffing workflows."
}
```

The backend also accepts `turnstileToken` in the JSON body for non-browser test clients, but the browser should use the header.

`202`:

```json
{
  "accepted": true
}
```

Errors:

```text
422 validation_failed: name, email, topic, or message missing/too long
422 captcha_required: Turnstile token missing
403 captcha_failed: Turnstile rejected the token
503 captcha_not_configured: backend secret missing
503 captcha_unavailable: Cloudflare verification unavailable
503 email_send_failed: email provider rejected the message
```

Email destination is DB-configurable. Current production default:

```text
publicContact.toEmail = "hello@nextronyx.com"
```

### POST `/v1/public/interests`

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

`202`:

```json
{
  "id": "ffffffff-ffff-ffff-ffff-ffffffffffff",
  "status": "New"
}
```

## Frontend Implementation Checklist

- Use `GET /v1/me` on boot.
- Keep organization ID in frontend state and send `X-Atlas-Organization-Id` when needed.
- Use `GET /v1/organizations/{id}/entitlements` to lock paid features.
- Use `GET /v1/dashboard/summary` for the attention-first dashboard.
- Use template search as the first library experience.
- Show API key `secret` exactly once and push the user to copy it.
- Do not email, log, or store raw API keys.
- Do not log raw checklist links or signed URLs.
- Set `Referrer-Policy: no-referrer` on `/c/{token}` pages.
- Use `Idempotency-Key` for action create and recipient submit.
- On `402`, show upgrade messaging based on `plan` and `usage`.
- On `403 production_developer_access_required`, show request/approval status from `/v1/developer/access`.
- On `403 sandbox_key_not_allowed`, tell developers to use `/v1/sandbox/*` or create a production key after approval.
- Treat scanner engine integration, SSO, and full e-signatures as not self-serve yet.
