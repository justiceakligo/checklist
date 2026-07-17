# Reqara Platform Admin API Implementation Guide

This file is the handoff for the platform admin console: staff, settings, organizations, org requests, developer production approval, revenue, metrics, and audit.

Platform overview dashboard field mapping is documented in [PLATFORM_ADMIN_DASHBOARD_DATA.md](PLATFORM_ADMIN_DASHBOARD_DATA.md).

Customer dashboard, recipient portal, and developer endpoints are documented in [FRONTEND_API_IMPLEMENTATION.md](FRONTEND_API_IMPLEMENTATION.md).

Stripe checkout and subscription flows are documented in [FRONTEND_STRIPE_BILLING_HANDOFF.md](FRONTEND_STRIPE_BILLING_HANDOFF.md).

Frontend deployment is documented in [FRONTEND_DROPLET_DEPLOYMENT_GUIDE.md](FRONTEND_DROPLET_DEPLOYMENT_GUIDE.md).

## Base URLs

```text
API production origin: https://api.reqara.com
Health: https://api.reqara.com/v1/health
OpenAPI JSON: https://api.reqara.com/openapi/v1.json
```

Use:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
```

Platform auth uses the same HttpOnly cookie as dashboard auth: `__Host-atlas_dashboard`. Run admin on a separate app/subdomain or make it clear that platform login replaces the org dashboard session in the same browser.

All platform browser calls must use:

```ts
fetch(`${API_BASE_URL}/v1/platform/me`, {
  credentials: "include",
  headers: { Accept: "application/json" }
});
```

## Security Rules For Admin UI

- Never display secrets in list views.
- Reveal secrets only after a deliberate admin action.
- Never email raw API keys.
- API key secrets are returned once on creation and are never recoverable.
- Recommended production flow: platform admin approves production developer access; org admin creates and sees the production key once in their own dashboard.
- Platform admin can create keys for support cases, but the raw secret is returned only to the platform admin response. The notification email does not include the secret.
- Keep bootstrap keys, passwords, raw checklist links, raw API keys, and signed URLs out of logs.

## Error Shape

```json
{
  "type": "https://docs.atlas.example/errors/platform_auth_required",
  "title": "Platform staff authentication is required.",
  "status": 401,
  "code": "platform_auth_required"
}
```

Common codes:

```text
401 platform_auth_required, invalid_credentials, invalid_bootstrap_key
402 feature_not_available, monthly_checklist_limit_exceeded, storage_limit_exceeded
403 forbidden
404 not_found
409 email_in_use, slug_in_use, last_owner, setting_exists
422 validation_failed, invalid_assignee, invalid_organization
503 bootstrap_not_configured
```

## Pagination

List endpoints use:

```text
page: 1-based, default 1
pageSize: default 25, max 100
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

Admin list endpoints with this envelope:

- `GET /v1/platform/staff`
- `GET /v1/platform/organizations`
- `GET /v1/platform/organization-requests`
- `GET /v1/platform/interests`
- `GET /v1/platform/revenue-events`
- `GET /v1/platform/audit` (`pageSize` max 250)

## Enums

```text
PlatformStaffRole: Owner, Admin, Support, Finance
PlatformStaffStatus: Active, Disabled
OrganizationStatus: Active, Suspended, Closed
OrganizationInterestStatus: New, Qualified, Approved, Rejected, Archived
PlatformRevenueEventType: Subscription, Usage, Credit, Refund, Adjustment
AdminSettingScope: System, Organization
ApiKeyEnvironment: Sandbox, Production
DeveloperAccessStatus: SandboxOnly, ProductionRequested, ProductionApproved, ProductionRejected
```

## Role Matrix

Owner/Admin:

- Staff CRUD.
- Settings CRUD, including secret reveal.
- Organization CRUD.
- Developer production access approve/reject.
- Organization API key support actions.
- Lead/org request CRUD and approval.
- Revenue CRUD.
- Metrics and audit.

Support:

- Organization list/detail.
- Lead/org request list/detail/create/update.
- Metrics.

Finance:

- Revenue event CRUD.
- Metrics.

## Bootstrap And Auth

### POST `/v1/platform/bootstrap`

Creates the first platform Owner. Refuses after any platform staff exists.

```json
{
  "bootstrapKey": "server-only-bootstrap-key",
  "email": "owner@reqara.com",
  "password": "correct-horse-battery-staple",
  "fullName": "Platform Owner"
}
```

`201`:

```json
{
  "id": "10000000-0000-0000-0000-000000000001",
  "email": "owner@reqara.com",
  "fullName": "Platform Owner",
  "role": "Owner",
  "status": "Active",
  "lastLoginAt": null,
  "disabledAt": null,
  "createdAt": "2026-07-16T05:00:00Z",
  "updatedAt": "2026-07-16T05:00:00Z"
}
```

### POST `/v1/platform/auth/login`

```json
{
  "email": "owner@reqara.com",
  "password": "correct-horse-battery-staple"
}
```

### GET `/v1/platform/me`

Returns the signed-in platform staff response.

### POST `/v1/platform/auth/logout`

Returns `204`.

## Staff

### GET `/v1/platform/staff?status=Active`

```json
{
  "items": [
    {
      "id": "10000000-0000-0000-0000-000000000001",
      "email": "owner@reqara.com",
      "fullName": "Platform Owner",
      "role": "Owner",
      "status": "Active",
      "lastLoginAt": "2026-07-16T05:00:00Z",
      "disabledAt": null,
      "createdAt": "2026-07-16T05:00:00Z",
      "updatedAt": "2026-07-16T05:00:00Z"
    }
  ]
}
```

### POST `/v1/platform/staff`

Defaults: `role=Support`, `status=Active`.

```json
{
  "email": "support@reqara.com",
  "password": "temporary-password",
  "fullName": "Support Staff",
  "role": "Support",
  "status": "Active"
}
```

### GET `/v1/platform/staff/{id}`

Returns one staff response.

### PUT `/v1/platform/staff/{id}`

All fields optional.

```json
{
  "email": "support@reqara.com",
  "password": null,
  "fullName": "Senior Support Staff",
  "role": "Support",
  "status": "Active"
}
```

### DELETE `/v1/platform/staff/{id}`

Disables staff. Backend prevents disabling/demoting the last active Owner.

## Settings

All admin-configurable values live in `admin_settings`.

Setting identity:

```text
scope + organizationId + category + key
```

Secret values return `"\"***\""` unless `includeSecrets=true` or `includeSecret=true` and the staff role allows reveal.

### Important Production Settings

```text
app.baseUrl = "https://reqara.com"
email.provider = "resend"
Email:Resend.From = "requests@reqara.com"
Email:Resend.FromName = "Reqara"
Email:Resend.ApiKey = secret
Email:Brand.LogoUrl = "https://api.reqara.com/brand/reqara-email-logo.png"
DigitalOceanSpaces.ServiceUrl = "https://tor1.digitaloceanspaces.com"
DigitalOceanSpaces.Region = "tor1"
DigitalOceanSpaces.BucketName = "reqara-bucket"
DigitalOceanSpaces.AccessKey = secret
DigitalOceanSpaces.SecretKey = secret
DigitalOceanSpaces.QuarantinePrefix = "quarantine"
DigitalOceanSpaces.ForcePathStyle = false
billing.defaultPlanCode = "free"
billing.plans = full pricing JSON
stripe.Enabled = true
stripe.PublishableKey = "pk_test_..."
stripe.SecretKey = secret
stripe.WebhookSigningSecret = secret
stripe.CustomerPortalReturnUrl = "https://reqara.com/dashboard/billing"
retention.defaultRetentionDays = 365
retention.purgeIntervalMinutes = 60
documentReuse.enabled = true
documentReuse.maximumAgeDays = 365
documentReuse.requireRecipientConfirmation = true
documentReuse.reuseExpiredDocuments = false
actions.duplicateWindowMinutes = 10
auth.emailVerificationMinutes = 1440
auth.passwordResetMinutes = 30
recipientOtp.defaultRequired = false
turnstile.siteKey = "0x4AAAAAAD3PDppjZsCXGkx4"
turnstile.secretKey = secret
publicContact.toEmail = "hello@nextronyx.com"
fileScanning.mode = "trusted"
security.recipientTokenGraceDays = 30
security.otpMinutes = 10
security.otpResendCooldownSeconds = 60
developer.apiKeyDefaultDays = 180
reminders.beforeDueDays = 3
reminders.overdueDays = 1
reminders.dispatchIntervalSeconds = 60
```

`fileScanning.mode` values:

- `trusted`: current production mode. After the API verifies the object exists in private storage and the byte size matches, it marks the file `Clean` immediately with scan engine `trusted-upload`.
- `external`: strict mode. Upload completion returns `Pending` until a scanner/admin integration posts `POST /v1/files/{id}/scan-result`.

### GET `/v1/platform/settings`

Query params:

```text
organizationId: optional
category: optional
includeSecrets: optional boolean
```

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
      "updatedAt": "2026-07-16T05:00:00Z"
    }
  ]
}
```

### POST `/v1/platform/settings`

Creates or updates by natural key.

```json
{
  "organizationId": null,
  "scope": "System",
  "category": "billing",
  "key": "defaultPlanCode",
  "value": "free",
  "isSecret": false
}
```

Organization plan example:

```json
{
  "organizationId": "30000000-0000-0000-0000-000000000001",
  "scope": "Organization",
  "category": "billing",
  "key": "plan",
  "value": {
    "planCode": "business",
    "billingCycle": "monthly",
    "status": "active",
    "currentPeriodStart": "2026-07-01T00:00:00Z",
    "currentPeriodEnd": "2026-08-01T00:00:00Z",
    "provider": "stripe",
    "stripeCustomerId": "cus_123",
    "stripeSubscriptionId": "sub_123",
    "cancelAtPeriodEnd": false
  },
  "isSecret": false
}
```

### GET `/v1/platform/settings/{id}?includeSecret=true`

Returns one setting, optionally unmasked.

### PUT `/v1/platform/settings/{id}`

All fields optional.

### DELETE `/v1/platform/settings/{id}`

Returns `204`.

## Pricing Plans

Plans are DB-configurable through `billing.plans`.

Default pricing:

```json
{
  "plans": [
    {
      "code": "free",
      "name": "Free",
      "monthlyPriceCents": 0,
      "annualPriceCents": 0,
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
    },
    {
      "code": "starter",
      "monthlyPriceCents": 3900,
      "annualPriceCents": 39000,
      "monthlyChecklistLimit": 100,
      "storageBytes": 5368709120
    },
    {
      "code": "business",
      "monthlyPriceCents": 9900,
      "annualPriceCents": 99000,
      "monthlyChecklistLimit": 500,
      "storageBytes": 26843545600
    },
    {
      "code": "scale",
      "customPricing": true,
      "monthlyChecklistLimit": null,
      "storageBytes": null
    }
  ]
}
```

The platform admin console should expose a safe plan editor or a structured JSON editor with validation. Do not require code deploys for pricing changes.

## Checklist Templates

Use these endpoints for platform-admin CRUD of the global checklist library and any organization-owned templates.

Read access: Owner, Admin, Support.  
Write access: Owner, Admin.

Global library templates have `organizationId: null` and `isGlobal: true`.

### GET `/v1/platform/templates`

Query params:

```text
page
pageSize
q
status=Draft | Published | Archived
organizationId
globalOnly=true
```

Response:

```json
{
  "items": [
    {
      "id": "66666666-6666-6666-6666-666666666666",
      "organizationId": null,
      "organizationName": null,
      "isGlobal": true,
      "name": "Vendor Insurance Renewal",
      "category": "Property Management",
      "description": "Collect updated insurance, expiry dates, WSIB, and trade licence.",
      "status": "Published",
      "currentVersionId": "77777777-7777-7777-7777-777777777777",
      "versionId": "77777777-7777-7777-7777-777777777777",
      "versionNumber": 1,
      "title": "Vendor Insurance Renewal",
      "instructions": "Use when vendors need to renew compliance documents.",
      "settings": {
        "industry": "Property Management",
        "rating": 5,
        "country": "Canada",
        "tags": ["insurance", "vendor", "compliance"]
      },
      "publishedAt": "2026-07-16T18:00:00Z",
      "requirements": [
        {
          "id": "88888888-8888-8888-8888-888888888888",
          "key": "insurance_certificate",
          "type": "File",
          "label": "Insurance certificate",
          "description": "Upload the current certificate of insurance.",
          "required": true,
          "displayOrder": 1,
          "configuration": {},
          "validation": {},
          "condition": null
        }
      ],
      "createdAt": "2026-07-16T18:00:00Z",
      "updatedAt": "2026-07-16T18:00:00Z",
      "deletedAt": null
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

### GET `/v1/platform/templates/{id}`

Returns the same `PlatformTemplateResponse` shape with full editable requirements.

### POST `/v1/platform/templates`

Create a global template by sending `organizationId: null`. Create an org-owned template by sending an organization id.

```json
{
  "organizationId": null,
  "name": "Vendor Insurance Renewal",
  "category": "Property Management",
  "description": "Collect updated insurance and compliance documents.",
  "title": "Vendor Insurance Renewal",
  "instructions": "Please upload current compliance documents.",
  "settings": {
    "industry": "Property Management",
    "rating": 5,
    "country": "Canada",
    "tags": ["insurance", "vendor", "compliance"],
    "searchTerms": ["certificate of insurance", "wsib", "licence"]
  },
  "requirements": [
    {
      "key": "insurance_certificate",
      "type": "File",
      "label": "Insurance certificate",
      "description": "Upload the current certificate of insurance.",
      "required": true,
      "displayOrder": 1,
      "configuration": {},
      "validation": {},
      "condition": null
    }
  ],
  "publishImmediately": true
}
```

### PUT `/v1/platform/templates/{id}`

All fields are optional. Updating `title`, `instructions`, `settings`, or `requirements` creates a new draft version. Use publish after editing to make the newest version live.

```json
{
  "name": "Vendor Insurance Renewal",
  "category": "Property Management",
  "description": "Collect vendor compliance documents.",
  "status": "Draft",
  "title": "Vendor Insurance Renewal",
  "instructions": "Please upload current compliance documents.",
  "settings": {
    "industry": "Property Management",
    "rating": 5
  },
  "requirements": []
}
```

### POST `/v1/platform/templates/{id}/publish`

Publishes the newest version and sets `status` to `Published`.

### POST `/v1/platform/templates/{id}/archive`

Sets `status` to `Archived`.

### DELETE `/v1/platform/templates/{id}`

Soft-deletes the template and returns `204`. Deleted templates are hidden from both admin list and customer library search.

## Organizations

Self-serve signup creates Active organizations. Platform approval is not required for Free usage.

Use org requests/interests for sales, high-touch onboarding, Scale, and production developer approval context.

### GET `/v1/platform/organizations?q=&status=Active`

Response includes entitlement snapshot and developer access state.

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
      "developerAccessStatus": "ProductionRequested",
      "developerProductionRequestedAt": "2026-07-16T05:10:00Z",
      "developerProductionApprovedAt": null,
      "developerProductionRejectedAt": null,
      "developerProductionNotes": "HRIS integration",
      "createdAt": "2026-07-16T05:00:00Z",
      "updatedAt": "2026-07-16T05:10:00Z",
      "deletedAt": null,
      "memberCount": 3,
      "actionCount": 18,
      "submissionCount": 12,
      "revenueAllTime": [
        { "currency": "USD", "amount": 499.00 }
      ],
      "entitlements": {
        "plan": { "code": "business", "name": "Business" },
        "billing": { "planCode": "business", "billingCycle": "monthly", "status": "active" },
        "usage": { "checklistsSent": 18, "checklistsRemaining": 482 }
      }
    }
  ]
}
```

### POST `/v1/platform/organizations`

Creates an org manually. Defaults to Active and configured default retention.

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

### GET `/v1/platform/organizations/{id}`

Returns one organization response.

### PATCH `/v1/platform/organizations/{id}`

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

### POST `/v1/platform/organizations/{id}/approve`

Reactivates or approves an existing org by setting status `Active`.

### DELETE `/v1/platform/organizations/{id}`

Closes the org.

## Developer Production Access

Recommended product flow:

1. Org signs up and can use the product immediately on Free.
2. Org creates sandbox API keys from their dashboard immediately.
3. Org tests against `/v1/sandbox/*`.
4. Org requests production developer access.
5. Platform admin reviews org, use case, plan, and risk.
6. Platform admin moves org to Business/Scale plan if needed.
7. Platform admin approves production developer access.
8. Org creates its own production API key in dashboard and sees the secret once.

Why this is safer:

- Platform admin does not need to handle raw API secrets.
- Raw secrets are not emailed.
- The org receives the production key inside its own authenticated dashboard session.

### POST `/v1/platform/organizations/{id}/developer-access/approve`

Requires Owner/Admin. Also requires the org plan to include `apiAndWebhooks`; otherwise returns `402 feature_not_available`.

```json
{
  "notes": "Approved for HRIS production integration after Business plan activation."
}
```

Response: platform organization response with:

```json
{
  "developerAccessStatus": "ProductionApproved",
  "developerProductionApprovedAt": "2026-07-16T05:30:00Z",
  "developerProductionNotes": "Approved for HRIS production integration after Business plan activation."
}
```

### POST `/v1/platform/organizations/{id}/developer-access/reject`

```json
{
  "notes": "Need verified domain and privacy contact before production API access."
}
```

Response has `developerAccessStatus: "ProductionRejected"`.

## Organization API Key Support Actions

Prefer org self-service key creation. Use these support endpoints only for admin-assisted cases.

### GET `/v1/platform/organizations/{id}/api-keys`

Secrets are never returned.

```json
{
  "items": [
    {
      "id": "55555555-5555-5555-5555-555555555555",
      "organizationId": "30000000-0000-0000-0000-000000000001",
      "name": "Production integration",
      "keyPrefix": "atl_live_ab12cd34",
      "environment": "Production",
      "scopes": ["templates:read", "actions:write", "files:write"],
      "lastUsedAt": null,
      "expiresAt": "2027-01-12T05:00:00Z",
      "revokedAt": null,
      "createdAt": "2026-07-16T05:00:00Z"
    }
  ]
}
```

### POST `/v1/platform/organizations/{id}/api-keys`

Sandbox support key:

```json
{
  "name": "Sandbox support key",
  "environment": "Sandbox",
  "scopes": ["sandbox:*"],
  "expiresAt": null,
  "notifyEmail": "developer@customer.example"
}
```

Production support key:

```json
{
  "name": "Production integration",
  "environment": "Production",
  "scopes": ["templates:read", "actions:write", "files:write"],
  "expiresAt": null,
  "notifyEmail": "developer@customer.example"
}
```

Rules:

- `Sandbox` keys are allowed for any active org.
- `Production` keys require the org plan to include API/webhooks.
- Creating a production key through platform admin also marks developer access approved.
- The raw `secret` is returned once in the API response.
- Notification email includes key name/prefix and dashboard link only. It does not include the secret.

`201`:

```json
{
  "id": "55555555-5555-5555-5555-555555555555",
  "organizationId": "30000000-0000-0000-0000-000000000001",
  "name": "Production integration",
  "keyPrefix": "atl_live_ab12cd34",
  "environment": "Production",
  "secret": "atl_live_ab12cd34_abcdefghijklmnopqrstuvwxyz",
  "scopes": ["templates:read", "actions:write", "files:write"],
  "lastUsedAt": null,
  "expiresAt": "2027-01-12T05:00:00Z",
  "revokedAt": null,
  "createdAt": "2026-07-16T05:00:00Z",
  "notificationSent": true,
  "notificationError": null
}
```

### DELETE `/v1/platform/organizations/{organizationId}/api-keys/{apiKeyId}`

Revokes the key. Returns `204`.

## Organization Requests / Interested Organizations

The admin console can use either route:

```text
GET /v1/platform/organization-requests
GET /v1/platform/interests
```

Both represent prospects, sales leads, contact-sales submissions, and high-touch onboarding requests. Self-serve Free signup does not create an org request.

### POST `/v1/public/interests`

Public marketing form.

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

### GET `/v1/platform/interests?status=New&q=northstar`

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
      "createdAt": "2026-07-16T05:00:00Z",
      "updatedAt": "2026-07-16T05:00:00Z",
      "approvedAt": null,
      "rejectedAt": null
    }
  ]
}
```

### POST `/v1/platform/interests`

Admin/support-created lead.

### GET `/v1/platform/interests/{id}`

Returns one lead.

### PUT `/v1/platform/interests/{id}`

Updates status, notes, assignee, and contact fields.

### POST `/v1/platform/interests/{id}/approve`

Approve without creating an org:

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

Approve and create org plus owner:

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

### DELETE `/v1/platform/interests/{id}`

Deletes the lead.

## Revenue

Stripe subscription webhooks now create revenue events automatically. Platform admins can still create manual adjustments, credits, refunds, or imported revenue records.

### GET `/v1/platform/revenue-events`

Query params:

```text
organizationId: optional
type: Subscription, Usage, Credit, Refund, Adjustment
from: ISO timestamp
to: ISO timestamp
```

### POST `/v1/platform/revenue-events`

```json
{
  "organizationId": "30000000-0000-0000-0000-000000000001",
  "type": "Subscription",
  "amount": 99.00,
  "currency": "USD",
  "source": "manual",
  "externalReference": "INV-1001",
  "occurredAt": "2026-07-16T00:00:00Z",
  "periodStart": "2026-07-01T00:00:00Z",
  "periodEnd": "2026-08-01T00:00:00Z",
  "metadata": {
    "plan": "business"
  }
}
```

Stripe-created rows use `source: "stripe"` and `externalReference: "stripe:evt_..."`. Refunds are stored as negative `Refund` amounts so revenue totals are net of refunds.

### GET `/v1/platform/revenue-events/{id}`

Returns one revenue event.

### PUT `/v1/platform/revenue-events/{id}`

Replaces the revenue event fields.

### DELETE `/v1/platform/revenue-events/{id}`

Returns `204`.

## Metrics

### GET `/v1/platform/metrics?from={iso}&to={iso}`

Default range is last 30 days.

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
    { "currency": "USD", "amount": 14320.00 }
  ],
  "revenueAllTime": [
    { "currency": "USD", "amount": 58200.00 }
  ]
}
```

Recommended admin dashboard cards:

- Active orgs.
- Production developer requests waiting.
- New/qualified org requests.
- In-flight checklists.
- Accepted submissions.
- Files pending scan.
- Failed notifications.
- Revenue in period.

## Audit

### GET `/v1/platform/audit`

Query params:

```text
staffId: optional
eventType: optional exact event type
from: optional ISO timestamp
to: optional ISO timestamp
```

Returns latest 250 matching platform audit events.

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
platform.organization_developer_access_approved
platform.organization_developer_access_rejected
platform.organization_api_key_created
platform.organization_api_key_revoked
platform.interest_created
platform.interest_updated
platform.interest_approved
platform.interest_deleted
platform.revenue_event_created
platform.revenue_event_updated
platform.revenue_event_deleted
```

## Admin UI Route Map

```text
Auth
- POST /v1/platform/bootstrap
- POST /v1/platform/auth/login
- GET /v1/platform/me
- POST /v1/platform/auth/logout

Staff
- GET /v1/platform/staff
- POST /v1/platform/staff
- GET /v1/platform/staff/{id}
- PUT /v1/platform/staff/{id}
- DELETE /v1/platform/staff/{id}

Settings
- GET /v1/platform/settings
- POST /v1/platform/settings
- GET /v1/platform/settings/{id}
- PUT /v1/platform/settings/{id}
- DELETE /v1/platform/settings/{id}

Checklist Templates
- GET /v1/platform/templates
- POST /v1/platform/templates
- GET /v1/platform/templates/{id}
- PUT /v1/platform/templates/{id}
- POST /v1/platform/templates/{id}/publish
- POST /v1/platform/templates/{id}/archive
- DELETE /v1/platform/templates/{id}

Organizations
- GET /v1/platform/organizations
- POST /v1/platform/organizations
- GET /v1/platform/organizations/{id}
- PATCH /v1/platform/organizations/{id}
- POST /v1/platform/organizations/{id}/approve
- POST /v1/platform/organizations/{id}/developer-access/approve
- POST /v1/platform/organizations/{id}/developer-access/reject
- GET /v1/platform/organizations/{id}/api-keys
- POST /v1/platform/organizations/{id}/api-keys
- DELETE /v1/platform/organizations/{organizationId}/api-keys/{apiKeyId}
- DELETE /v1/platform/organizations/{id}

Org Requests
- POST /v1/public/interests
- GET /v1/platform/organization-requests
- GET /v1/platform/interests
- POST /v1/platform/interests
- GET /v1/platform/interests/{id}
- PUT /v1/platform/interests/{id}
- POST /v1/platform/interests/{id}/approve
- DELETE /v1/platform/interests/{id}

Revenue
- GET /v1/platform/revenue-events
- POST /v1/platform/revenue-events
- GET /v1/platform/revenue-events/{id}
- PUT /v1/platform/revenue-events/{id}
- DELETE /v1/platform/revenue-events/{id}

Metrics and Audit
- GET /v1/platform/metrics
- GET /v1/platform/audit
```

## Remaining Admin/Product Gaps

- Platform admin MFA is not implemented yet.
- Password reset and forced password change are not implemented yet.
- Stripe Billing is implemented for Starter/Business subscriptions. Scale remains custom/contact-sales.
- SSO is a Scale plan flag, not an implemented SAML/OIDC login flow yet.
- Malware scan engine is external; backend supports pending/clean/rejected state and scanner callback.
- Full e-signature and recipient-side payment collection are not implemented yet.
