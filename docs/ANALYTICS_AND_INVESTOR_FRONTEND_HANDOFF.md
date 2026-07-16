# Reqara Analytics And Investor Frontend Handoff

This file documents the implemented full analytics API surface for:

- Customer organization analytics.
- Platform admin analytics.
- Investor & Board summary inside Platform Admin.
- Investor reports.
- Event tracking.

Production API origin:

```text
https://api.reqara.com
```

Frontend env:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
```

## Architecture Decision

The pasted analytics brief recommends `/admin/v1/...` for admin analytics. The backend already uses `/v1/platform/...` for platform admin, so the implemented routes stay consistent:

```text
Organization analytics: /v1/analytics/*
Organization usage:     /v1/usage/current
Platform analytics:     /v1/platform/analytics/*
Investor & Board:       /v1/platform/investor/*
```

The Investor & Board section is a curated subsection of Platform Admin, not a separate product and not a separate analytics system.

## Auth Rules

Organization analytics requires a signed-in organization dashboard session.

```ts
await fetch(`${API_BASE_URL}/v1/analytics/overview`, {
  credentials: "include",
  headers: {
    Accept: "application/json",
    "X-Atlas-Organization-Id": organizationId
  }
});
```

Platform analytics requires a signed-in platform staff session.

```ts
await fetch(`${API_BASE_URL}/v1/platform/analytics/overview`, {
  credentials: "include",
  headers: { Accept: "application/json" }
});
```

Investor & Board summary allows platform `Owner`, `Admin`, and `Finance`. `Support` can view operational platform analytics but not investor summary.

## Full Phase Coverage

All requested phases now have concrete endpoints:

| Requested capability | Endpoint |
| --- | --- |
| Analytics event tracking | `POST /v1/analytics/events`, `GET /v1/platform/analytics/events` |
| Organization overview | `GET /v1/analytics/overview` |
| Needs attention | Included in `GET /v1/analytics/overview` |
| Completion funnel | `GET /v1/analytics/completion` |
| Platform overview | `GET /v1/platform/analytics/overview` |
| System health | `GET /v1/platform/analytics/system-health` |
| Template analytics | `GET /v1/analytics/templates` |
| Reminder analytics | `GET /v1/analytics/reminders` |
| Activation funnel | `GET /v1/platform/analytics/activation` |
| Product engagement | `GET /v1/platform/analytics/engagement` |
| Revenue metrics | `GET /v1/platform/analytics/revenue` |
| Retention cohorts | `GET /v1/platform/analytics/retention/cohorts` |
| Unit economics | `GET /v1/platform/analytics/unit-economics` |
| Requirement analytics | `GET /v1/analytics/requirements`, `GET /v1/platform/analytics/requirements` |
| Compliance analytics | `GET /v1/analytics/compliance`, `GET /v1/platform/analytics/compliance` |
| Investor executive summary | `GET /v1/platform/investor/summary` |
| Investor reports | `POST /v1/platform/investor/reports`, `GET /v1/platform/investor/reports` |
| Vertical intelligence | `GET /v1/platform/analytics/segments` |
| Time-saved estimates | `GET /v1/platform/analytics/time-saved` |
| Predictive insights | `GET /v1/platform/analytics/predictive-insights` |
| Benchmarking | `GET /v1/platform/analytics/benchmarks` |

Some advanced metrics return `null` where Reqara does not yet have a trusted data source. Do not substitute fake values in the UI.

## Shared Date Filters

Most endpoints accept:

```text
from: ISO timestamp, optional
to: ISO timestamp, optional
```

Default period:

- Most analytics endpoints default to the current UTC month.
- Platform growth defaults to the previous 180 days.

Percentages are returned as `0-100` decimals. Example: `77.47` means `77.47%`.

Important frontend rule:

```text
null means "not tracked yet" or "not enough data", not zero.
```

Render `null` as `Not available yet`, `No data yet`, or hide the card depending on context.

## Organization Analytics

Recommended routes:

```text
/app/analytics
/app/analytics/completion
/app/analytics/templates
/app/analytics/requirements
/app/analytics/reminders
/app/analytics/recipient-experience
/app/analytics/compliance
/app/usage
```

### GET `/v1/analytics/overview`

Purpose: show what needs attention now.

Query:

```text
from
to
```

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "summary": {
    "activeChecklists": 84,
    "awaitingRecipient": 21,
    "inProgress": 32,
    "overdue": 9,
    "submitted": 46
  },
  "needsAttention": [
    {
      "checklistId": "11111111-1111-1111-1111-111111111111",
      "recipientId": "22222222-2222-2222-2222-222222222222",
      "recipientName": "ABC Plumbing",
      "recipientEmail": "ops@abc.example",
      "checklistTitle": "Contractor Compliance",
      "missingRequiredItems": 2,
      "dueAt": "2026-07-14T23:59:59Z",
      "daysOverdue": 2,
      "lastActivityAt": "2026-07-12T14:31:00Z",
      "recommendedAction": "send_overdue_reminder"
    }
  ],
  "upcomingDueDates": [
    {
      "checklistId": "33333333-3333-3333-3333-333333333333",
      "checklistTitle": "Vendor Registration",
      "dueAt": "2026-08-01T23:59:59Z",
      "status": "Sent"
    }
  ],
  "recentSubmissions": [],
  "expiringDocuments": []
}
```

UI guidance:

- KPI cards: Active Checklists, Awaiting recipient, In progress, Overdue, Submitted this period.
- Main table: `needsAttention`.
- Use `recommendedAction` to decide CTA copy. Examples: `Send reminder`, `Send overdue reminder`.

### GET `/v1/analytics/completion`

Purpose: show recipient completion efficiency.

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "funnel": {
    "sent": 120,
    "delivered": 116,
    "viewed": 98,
    "started": 84,
    "submitted": 71,
    "accepted": 65
  },
  "rates": {
    "deliveryRate": 96.67,
    "viewRate": 84.48,
    "startRate": 85.71,
    "submissionRate": 84.52,
    "acceptanceRate": 91.55
  },
  "performance": {
    "medianCompletionMinutes": 1870,
    "completedBeforeDueRate": 78.4,
    "firstAttemptAcceptanceRate": 86.2
  }
}
```

Rate denominators:

```text
deliveryRate = delivered / sent
viewRate = viewed / delivered
startRate = started / viewed
submissionRate = submitted / started
acceptanceRate = accepted / submitted
```

Note: `delivered` currently means the email provider accepted/sent the message. True mailbox delivery webhooks are a future enhancement.

### GET `/v1/analytics/templates`

Purpose: compare workflows/templates.

Query:

```text
from
to
templateId
page
pageSize
sort=sent_desc | completion_rate_desc | completion_rate_asc | name_asc | name_desc
```

Response:

```json
{
  "items": [
    {
      "templateId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "templateName": "Contractor Compliance",
      "sent": 82,
      "submitted": 61,
      "completionRate": 74.39,
      "medianCompletionMinutes": 5800,
      "averageReminderCount": 1.8,
      "firstAttemptAcceptanceRate": 81.2,
      "topMissingRequirement": null,
      "topRejectedRequirement": null,
      "dropOffRequirement": null
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 5
  }
}
```

The `topMissingRequirement`, `topRejectedRequirement`, and `dropOffRequirement` fields are reserved. They return `null` until requirement-level rejection/drop-off events are tracked.

### GET `/v1/analytics/requirements`

Purpose: identify hard requirements.

Query:

```text
from
to
templateId
page
pageSize
```

Response:

```json
{
  "items": [
    {
      "templateRequirementId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "requirementKey": "proof_of_insurance",
      "label": "Proof of Insurance",
      "type": "File",
      "presentedCount": 100,
      "completedCount": 74,
      "completionRate": 74,
      "averageCompletionMinutes": null,
      "validationErrorRate": null,
      "uploadFailureRate": null,
      "correctionRequestRate": null,
      "fileRejectionRate": null,
      "estimatedDropOffCount": 11
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 20,
    "totalItems": 12
  }
}
```

### GET `/v1/analytics/reminders`

Purpose: show whether reminders correlate with completion.

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "summary": {
    "automaticSent": 148,
    "manualSent": 34,
    "completionWithin24HoursRate": 39.2,
    "completionWithin72HoursRate": 58.7,
    "averageRemindersPerCompletion": 1.4
  },
  "timingPerformance": [
    {
      "timing": "BeforeDue",
      "sent": 42,
      "completedWithin24Hours": 19,
      "conversionRate": 45.24
    }
  ]
}
```

UI wording should say:

```text
Completed within 24 hours after reminder
```

Do not claim the reminder caused the completion.

### GET `/v1/analytics/recipient-experience`

Purpose: show recipient friction without exposing sensitive recipient behavior.

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "mobileShare": null,
  "desktopShare": null,
  "saveAndReturnRate": 24.5,
  "averageSessionsPerSubmission": 1.8,
  "medianRecipientSessionDurationMinutes": 12.4,
  "uploadFailureRate": null,
  "submissionRetryRate": 2.1,
  "turnstileFailureRate": null,
  "linkRecoveryRequests": null,
  "recipientSatisfaction": null
}
```

Mobile/desktop and Turnstile failure metrics are `null` until user-agent and captcha failure analytics events are added.

### GET `/v1/analytics/compliance`

Purpose: show document, review, scan, retention, and reuse status.

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "documentsExpiring30Days": 4,
  "documentsExpiring60Days": 9,
  "documentsExpiring90Days": 14,
  "expiredDocuments": 2,
  "missingRequiredDocuments": 7,
  "pendingReviews": 3,
  "filesPendingScan": 1,
  "filesQuarantined": 0,
  "retentionDeletionsUpcoming": 5,
  "reusedPreviousSubmissions": 12
}
```

### GET `/v1/usage/current`

Purpose: usage and plan page.

Response:

```json
{
  "billing": {
    "planCode": "starter",
    "billingCycle": "monthly",
    "status": "active",
    "currentPeriodStart": "2026-07-01T00:00:00Z",
    "currentPeriodEnd": "2026-08-01T00:00:00Z"
  },
  "plan": {
    "code": "starter",
    "name": "Starter",
    "description": "For solo pros and small teams.",
    "monthlyPriceCents": 3900,
    "annualPriceCents": 39000,
    "currency": "USD",
    "monthlyChecklistLimit": 100,
    "storageBytes": 5368709120,
    "customPricing": false,
    "features": {
      "teamWorkspace": true,
      "templateLibrary": true,
      "customBranding": true,
      "automaticReminders": true
    }
  },
  "usage": {
    "periodStart": "2026-07-01T00:00:00Z",
    "periodEnd": "2026-08-01T00:00:00Z",
    "checklistsSent": 18,
    "checklistLimit": 100,
    "checklistsRemaining": 82,
    "storageBytesUsed": 245760,
    "storageBytes": 5368709120,
    "storageBytesRemaining": 5368463360
  },
  "apiRequests": null,
  "webhookDeliveries": null,
  "aiCredits": null
}
```

Reminder: plan allowance means sent checklist requests per month. Draft checklists do not count.

### GET `/v1/analytics/metric-definitions`

Returns metric definitions for organization analytics cards.

```json
{
  "items": [
    {
      "key": "active_checklists",
      "name": "Active Checklists",
      "description": "Checklists currently sent, in progress, or submitted but not completed.",
      "formula": "Checklists currently sent, in progress, or submitted but not completed.",
      "unit": "count",
      "dataSource": "actions",
      "refreshFrequency": "near_real_time",
      "owner": "Product",
      "version": "2026-07-16",
      "effectiveFrom": "2026-07-16",
      "isInvestorVisible": false
    }
  ]
}
```

### POST `/v1/analytics/events`

Purpose: client-side product/event tracking for non-sensitive analytics events.

Request:

```json
{
  "eventName": "analytics_tab_viewed",
  "checklistId": null,
  "templateId": null,
  "recipientId": null,
  "sessionId": "browser-session-123",
  "properties": {
    "tab": "completion",
    "source": "dashboard"
  },
  "occurredAt": "2026-07-16T18:00:00Z"
}
```

Response:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "accepted": true
}
```

Do not send passwords, raw recipient links, raw tokens, API keys, signed URLs, SIN/SSN, banking details, or document contents in `properties`.

## Platform Admin Analytics

Recommended routes:

```text
/admin
/admin/analytics/growth
/admin/analytics/revenue
/admin/operations
/admin/security
/admin/investor
```

### GET `/v1/platform/analytics/overview`

Purpose: founder/internal platform health view.

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "business": {
    "mrr": 28450,
    "arr": 341400,
    "payingOrganizations": 184,
    "monthlyActiveOrganizations": 236,
    "newOrganizations": 31
  },
  "product": {
    "checklistsSent": 18420,
    "checklistsCompleted": 14270,
    "completionRate": 77.47,
    "completedPerActiveOrganization": 60.47,
    "medianCompletionMinutes": 2210,
    "secondChecklistRate": 64.3
  },
  "retention": {
    "logoRetention90Day": 82.6,
    "grossRevenueRetention": null,
    "netRevenueRetention": null
  },
  "operations": {
    "emailDeliverySuccessRate": 98.7,
    "webhookSuccessRate": 99.1,
    "fileScanSuccessRate": 99.8,
    "openIncidents": null
  }
}
```

### GET `/v1/platform/analytics/growth`

Query:

```text
from
to
interval=day | week | month
```

Response:

```json
{
  "period": {
    "from": "2026-01-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "interval": "month",
  "series": [
    {
      "period": "2026-07",
      "newOrganizations": 22,
      "activatedOrganizations": 16,
      "payingOrganizations": null,
      "activeOrganizations": 74,
      "completedChecklists": 2310
    }
  ]
}
```

Definitions:

- `activatedOrganizations`: organizations that sent at least one checklist in that interval.
- `activeOrganizations`: same as activated for Phase 1.
- `payingOrganizations`: `null` until billing integration or richer plan event history is added.

### GET `/v1/platform/analytics/activation`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "funnel": {
    "signedUp": 31,
    "organizationConfigured": 31,
    "templateSelectedOrCreated": 19,
    "firstChecklistCreated": 17,
    "firstChecklistSent": 14,
    "firstChecklistViewed": 11,
    "firstChecklistCompleted": 8,
    "secondChecklistSent": 5,
    "paid": 2
  },
  "rates": {
    "signupToFirstChecklistCreated": 54.84,
    "signupToFirstChecklistSent": 45.16,
    "signupToFirstSubmission": 25.81,
    "activationWithinPeriod": 45.16,
    "secondChecklistRate": 35.71,
    "trialToPaidConversion": 6.45
  }
}
```

### GET `/v1/platform/analytics/engagement`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "activeOrganizations": 74,
  "checklistsPerActiveOrganization": 12.4,
  "templatesPerOrganization": 1.7,
  "uniqueRecipientsPerOrganization": 18.2,
  "activeOrganizationUsers": 219,
  "autoReminderAdoptionRate": 61.2,
  "apiAdoptionRate": 8.5,
  "webhookAdoptionRate": 4.2,
  "customTemplateAdoptionRate": 47.9,
  "renewalFeatureAdoptionRate": null,
  "previousDocumentReuseAdoptionRate": 13.5
}
```

### GET `/v1/platform/analytics/revenue`

Allowed roles: Owner, Admin, Finance.

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "openingMrr": null,
  "newMrr": 4200,
  "expansionMrr": 1800,
  "reactivationMrr": null,
  "contractionMrr": 650,
  "churnedMrr": 2300,
  "closingMrr": 28450,
  "arr": 341400,
  "arpa": null,
  "trialToPaidConversionRate": null,
  "revenueByCurrency": [
    {
      "currency": "USD",
      "amount": 28450
    }
  ]
}
```

Revenue currently comes from manual `platform_revenue_events`. The frontend should say `Manual revenue records` somewhere in the revenue page until Stripe/billing integration exists.

### GET `/v1/platform/analytics/retention/cohorts`

Query:

```text
from
to
cohortBy=signup_month
activity=checklist_sent
```

Response:

```json
{
  "cohortBy": "signup_month",
  "activity": "checklist_sent",
  "period": {
    "from": "2026-01-01T00:00:00Z",
    "to": "2026-12-31T23:59:59Z"
  },
  "cohorts": [
    {
      "cohort": "2026-01",
      "startingOrganizations": 40,
      "retention": {
        "month0": 100,
        "month1": 72.5,
        "month2": 65,
        "month3": 60
      }
    }
  ]
}
```

### GET `/v1/platform/analytics/unit-economics`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "infrastructureCost": null,
  "costPerChecklistSent": null,
  "costPerCompletedChecklist": null,
  "costPerThousandChecklists": null,
  "storageBytes": 1245000000,
  "storageCost": null,
  "emailCost": null,
  "malwareScanningCost": null,
  "aiProcessingCost": null,
  "grossMarginPercent": null,
  "cac": null,
  "paybackPeriodMonths": null,
  "ltv": null,
  "supportCostPerOrganization": null,
  "checklistsSent": 18420,
  "completedChecklists": 14270
}
```

Unit economics cost inputs are intentionally `null` until cost settings or billing integrations are connected.

### GET `/v1/platform/analytics/requirements`

Same response shape as organization requirement analytics, but aggregated across all organizations.

### GET `/v1/platform/analytics/compliance`

Same response shape as organization compliance analytics, but aggregated across all organizations.

### GET `/v1/platform/analytics/segments`

Purpose: vertical and template intelligence.

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "industries": [
    {
      "segment": "HR",
      "organizations": 12,
      "payingOrganizations": null,
      "mrr": null,
      "completionRate": null,
      "medianCompletionMinutes": null,
      "retentionRate": null,
      "averageChecklistVolume": null,
      "averageContractValue": null,
      "supportBurden": null
    }
  ],
  "templates": [
    {
      "templateName": "Contractor Compliance",
      "usage": 82,
      "completed": 61,
      "completionRate": 74.39,
      "repeatUsageRate": null,
      "conversionToPaidRate": null,
      "industriesUsingTemplate": [],
      "retentionCorrelation": null
    }
  ]
}
```

### GET `/v1/platform/analytics/time-saved`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "completedChecklists": 14270,
  "reusedDocuments": 231,
  "estimatedMinutesSaved": 286555,
  "estimatedHoursSaved": 4775.92,
  "methodology": "Estimate: 20 minutes saved per accepted checklist plus 5 minutes per previously submitted document reuse. This is directional, not audited."
}
```

Label this as an estimate, not a guaranteed customer outcome.

### GET `/v1/platform/analytics/predictive-insights`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "insights": [
    {
      "key": "overdue_backlog",
      "title": "Overdue checklist backlog",
      "severity": "medium",
      "summary": "There are 9 sent or in-progress checklists past due.",
      "recommendedAction": "Review reminder performance and consider an overdue follow-up campaign.",
      "confidence": 0.75
    }
  ]
}
```

### GET `/v1/platform/analytics/benchmarks`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "checklistsSentPerOrganization": {
    "key": "checklists_sent_per_org",
    "label": "Checklists sent per organization",
    "p50": 12,
    "p75": 25,
    "p90": 55,
    "unit": "count"
  },
  "notes": "Benchmarks are internal platform distribution benchmarks for the selected period. Industry-specific benchmarks require more segment data."
}
```

### GET `/v1/platform/analytics/operations`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "apiUptimeRate": null,
  "apiErrorRate": null,
  "p50LatencyMs": null,
  "p95LatencyMs": null,
  "p99LatencyMs": null,
  "jobBacklog": null,
  "failedJobs": null,
  "emailDeliverySuccessRate": 98.7,
  "emailBounceRate": 0,
  "uploadFailureRate": null,
  "fileScanSuccessRate": 99.8,
  "fileScanFailureRate": 0.2,
  "webhookDeliverySuccessRate": 99.1,
  "databaseHealthy": true,
  "storageBytes": 1245000000,
  "turnstileFailureCount": null,
  "rateLimitTriggerCount": null
}
```

### GET `/v1/platform/analytics/system-health`

Response:

```json
{
  "asOf": "2026-07-16T18:00:00Z",
  "status": "ok",
  "databaseHealthy": true,
  "apiHealthy": true,
  "pendingFileScans": 0,
  "failedNotificationDeliveries": 6,
  "pendingWebhookDeliveries": 0,
  "storageBytes": 1245000000,
  "openIncidents": null
}
```

### GET `/v1/platform/analytics/security`

Response:

```json
{
  "period": {
    "from": "2026-07-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "failedOrganizationLogins": null,
  "suspiciousRecipientLinkActivity": 0,
  "invalidTokenAttempts": null,
  "revokedLinkAttempts": null,
  "rateLimitTriggers": null,
  "malwareDetections": 0,
  "quarantinedFiles": 0,
  "crossTenantAuthorizationDenials": null,
  "apiKeyFailures": null,
  "webhookSignatureFailures": null,
  "administrativeRoleChanges": 2,
  "dataExports": 0,
  "deletionRequests": 0
}
```

### GET `/v1/platform/analytics/metric-definitions`

Returns the platform metric catalogue. Use it for tooltips, glossary panels, and investor report metadata.

### GET `/v1/platform/analytics/events`

Query:

```text
from
to
organizationId
eventName
page
pageSize
```

Response:

```json
{
  "items": [
    {
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "eventName": "analytics_tab_viewed",
      "organizationId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "checklistId": null,
      "templateId": null,
      "recipientId": null,
      "userId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
      "sessionId": "browser-session-123",
      "propertiesJson": "{\"tab\":\"completion\"}",
      "occurredAt": "2026-07-16T18:00:00Z",
      "receivedAt": "2026-07-16T18:00:01Z"
    }
  ],
  "pagination": {
    "page": 1,
    "pageSize": 50,
    "totalItems": 1
  }
}
```

## Investor & Board

Recommended routes:

```text
/admin/investor
/admin/investor/revenue
/admin/investor/retention
/admin/investor/product
/admin/investor/verticals
/admin/investor/reports
```

### GET `/v1/platform/investor/summary`

Allowed roles: Owner, Admin, Finance.

Response:

```json
{
  "asOf": "2026-07-31T23:59:59Z",
  "business": {
    "mrr": 28450,
    "arr": 341400,
    "mrrGrowthPercent": null,
    "payingOrganizations": 184,
    "averageRevenuePerOrganization": 154.62,
    "grossMarginPercent": null,
    "monthlyBurn": null,
    "runwayMonths": null
  },
  "product": {
    "monthlyActiveOrganizations": 236,
    "completedChecklists": 14270,
    "completedPerActiveOrganization": 60.47,
    "completionRate": 77.47,
    "medianCompletionMinutes": 2210,
    "secondChecklistRate": 64.3
  },
  "retention": {
    "logoRetention90Day": 82.6,
    "grossRevenueRetention": null,
    "netRevenueRetention": null,
    "monthlyLogoChurn": null
  },
  "growth": {
    "newActivatedOrganizations": 31,
    "trialToPaidRate": null,
    "qualifiedPipelineValue": null,
    "averageSalesCycleDays": null,
    "averageContractValue": null
  }
}
```

Investor UI rules:

- Keep this read-only.
- Do not expose organization-level sensitive data.
- Do not show support, security, raw audit, raw recipient, or API secret data here.
- Display unavailable finance metrics as `Not connected yet` or `Add finance data`.

### Investor trend endpoints

Implemented:

```text
GET /v1/platform/investor/revenue-trend
GET /v1/platform/investor/organization-growth
GET /v1/platform/investor/product-usage
GET /v1/platform/investor/retention
GET /v1/platform/investor/unit-economics
GET /v1/platform/investor/verticals
GET /v1/platform/investor/milestones
```

Trend endpoints accept:

```text
from
to
interval=day | week | month
```

Revenue trend response:

```json
{
  "period": {
    "from": "2026-01-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "interval": "month",
  "series": [
    {
      "period": "2026-07",
      "mrr": 28450,
      "arr": 341400
    }
  ]
}
```

Product usage trend response:

```json
{
  "period": {
    "from": "2026-01-01T00:00:00Z",
    "to": "2026-07-31T23:59:59Z"
  },
  "interval": "month",
  "series": [
    {
      "period": "2026-07",
      "checklistsSent": 18420,
      "completedChecklists": 14270,
      "completionRate": 77.47
    }
  ]
}
```

Milestones response:

```json
{
  "asOf": "2026-07-16T18:00:00Z",
  "items": [
    {
      "key": "organizations_created",
      "label": "Organizations created",
      "value": 8,
      "target": null,
      "status": "achieved"
    }
  ]
}
```

### Investor reports

Create a report:

```http
POST /v1/platform/investor/reports
```

Request:

```json
{
  "reportType": "monthly_investor_update",
  "period": "2026-07",
  "includeSections": [
    "executive_summary",
    "revenue",
    "growth",
    "retention",
    "product",
    "milestones"
  ]
}
```

Response:

```json
{
  "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "reportType": "monthly_investor_update",
  "period": "2026-07",
  "includeSections": [
    "executive_summary",
    "revenue",
    "growth",
    "retention",
    "product",
    "milestones"
  ],
  "status": "Completed",
  "resultJson": "{\"reportType\":\"monthly_investor_update\",\"period\":\"2026-07\"}",
  "requestedByStaffId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  "createdAt": "2026-07-16T18:00:00Z",
  "updatedAt": "2026-07-16T18:00:00Z",
  "completedAt": "2026-07-16T18:00:00Z"
}
```

List and detail:

```text
GET /v1/platform/investor/reports?page=1&pageSize=25
GET /v1/platform/investor/reports/{id}
```

Reports are stored as JSON report payloads today. PDF, CSV, and Excel export can be layered onto the same records later.

## Suggested Frontend Navigation

Platform Admin:

```text
Overview
Organizations
Usage
Product Analytics
Revenue
Retention
Growth
Investor & Board
Operations
Security
Support
System Health
Configuration
```

Do not put every metric on one page. The overview should show the high-signal cards and link into dedicated pages.

## Generic Card Model

Frontend can normalize cards into this shape:

```ts
type MetricCard = {
  key: string;
  label: string;
  value: number | string | null;
  unit: "count" | "percent" | "currency" | "minutes" | "bytes";
  unavailableLabel?: string;
  description?: string;
};
```

Example:

```ts
const card: MetricCard = {
  key: "completion_rate",
  label: "Completion rate",
  value: response.product.completionRate,
  unit: "percent",
  unavailableLabel: "No sent checklists yet"
};
```

## Known Data Maturity Limits

The full requested endpoint surface exists now. These areas intentionally return `null` or directional estimates until the product has trusted source data:

- True email delivery webhooks.
- API latency/error telemetry.
- Captcha/rate-limit analytics events.
- Cost inputs for unit economics.
- SSO/Board Viewer/Investor Viewer roles.
- Industry data for every organization.
- Predictive models beyond rule-based operational insights.
- PDF, CSV, and Excel rendering for investor reports.

The frontend can build every page now. Use `null` and explanatory empty states instead of placeholders that imply the data is measured.
