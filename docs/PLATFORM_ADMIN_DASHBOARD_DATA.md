# Reqara Platform Admin Dashboard Data

This file is the frontend handoff for the platform admin overview screen.

Base API:

```text
https://api.reqara.com
```

All platform admin requests use the platform staff cookie:

```ts
fetch(`${API_BASE_URL}/v1/platform/metrics`, {
  credentials: "include",
  headers: { Accept: "application/json" }
});
```

## Main Metrics Endpoint

Use this as the source of truth for the overview cards:

```text
GET /v1/platform/metrics?from={iso}&to={iso}
```

If `from` and `to` are omitted, the backend uses the last 30 days.

Important: only `usageQuantityInPeriod` and `revenueInPeriod` are period-based today. Most other fields are current all-time/current-state counts.

## Response Shape

```json
{
  "from": "2026-06-16T17:34:22Z",
  "to": "2026-07-16T17:34:22Z",
  "organizationsTotal": 8,
  "organizationsActive": 8,
  "organizationsSuspended": 0,
  "organizationsClosed": 0,
  "activePlatformStaff": 1,
  "interestsNew": 0,
  "interestsQualified": 0,
  "interestsApproved": 0,
  "interestsRejected": 0,
  "checklistsTotal": 3,
  "checklistsInFlight": 0,
  "submissionsTotal": 0,
  "submissionsAccepted": 0,
  "filesPendingScan": 0,
  "failedNotifications": 6,
  "usageQuantityInPeriod": 0,
  "revenueInPeriod": [],
  "revenueAllTime": []
}
```

## Correct Card Mapping

| UI label | API field | Scope | Meaning |
| --- | --- | --- | --- |
| Active orgs | `organizationsActive` | Current state | Active organizations with no deletion timestamp. |
| Suspended | `organizationsSuspended` | Current state | Organizations suspended by platform admin. |
| Total organizations | `organizationsTotal` | All time/current rows | Every organization row, including closed/suspended. |
| Closed | `organizationsClosed` | Current state | Closed organizations or soft-deleted organizations. |
| Active staff | `activePlatformStaff` | Current state | Active platform staff accounts. |
| Created checklists | `checklistsTotal` | All time/current rows | All checklist action records, including drafts. Better UI label: `Total checklists`. |
| In flight | `checklistsInFlight` | Current state | Actions with status `Sent` or `InProgress`. Drafts are not in flight. |
| Sent this period | `usageQuantityInPeriod` | Selected period | Billable checklist sends in the selected date window. This is what plan limits use. |
| Accepted submissions | `submissionsAccepted` | All time/current rows | Submissions accepted by org reviewers. Not period-scoped yet. |
| New leads | `interestsNew` | Current state | Interested organizations with status `New`. |
| Qualified leads | `interestsQualified` | Current state | Interested organizations with status `Qualified`. |
| Files pending scan | `filesPendingScan` | Current state | Uploaded file assets still waiting for scan result. |
| Failed notifications | `failedNotifications` | All time/current rows | Notification delivery rows with status `Failed`. These may include old failures. |
| Revenue | `revenueInPeriod` | Selected period | Manual revenue events grouped by currency for selected range. Empty means no recorded revenue events. |
| Revenue all time | `revenueAllTime` | All time | Manual revenue events grouped by currency across all time. |

## Why Current Production Shows 3 Created And 0 Sent

Current production snapshot checked on `2026-07-16T17:34:22Z`:

```json
{
  "organizationsActive": 8,
  "organizationsSuspended": 0,
  "checklistsTotal": 3,
  "checklistsInFlight": 0,
  "usageQuantityInPeriod": 0,
  "submissionsAccepted": 0,
  "interestsNew": 0,
  "interestsQualified": 0,
  "filesPendingScan": 0,
  "failedNotifications": 6,
  "revenueInPeriod": [],
  "revenueAllTime": []
}
```

The 3 checklist records are currently `Draft`. Drafts count toward `checklistsTotal`, but they do not count as sent, billable, or in flight.

`usageQuantityInPeriod` increments when a checklist is actually sent. This is the metric behind pricing copy like `10 checklists / month`, meaning 10 sent checklist requests per month, not 10 draft templates or 10 draft actions.

## Recommended Overview Layout

Top cards:

```ts
const cards = [
  { label: "Active orgs", value: metrics.organizationsActive },
  { label: "Suspended", value: metrics.organizationsSuspended },
  { label: "Total checklists", value: metrics.checklistsTotal },
  { label: "In flight", value: metrics.checklistsInFlight },
  { label: "Sent this period", value: Number(metrics.usageQuantityInPeriod) },
  { label: "Accepted submissions", value: metrics.submissionsAccepted },
  { label: "Revenue this period", value: formatRevenue(metrics.revenueInPeriod) },
  { label: "New leads", value: metrics.interestsNew }
];
```

Revenue helper:

```ts
type RevenueByCurrency = {
  currency: string;
  amount: number;
};

function formatRevenue(rows: RevenueByCurrency[]) {
  if (!rows.length) return "No revenue events";

  return rows
    .map((row) =>
      new Intl.NumberFormat("en-US", {
        style: "currency",
        currency: row.currency
      }).format(row.amount)
    )
    .join(" · ");
}
```

Support alerts:

```ts
const supportAlerts = [
  { label: "New leads", value: metrics.interestsNew },
  { label: "Qualified leads", value: metrics.interestsQualified },
  { label: "Files pending scan", value: metrics.filesPendingScan },
  { label: "Failed notification deliveries", value: metrics.failedNotifications }
];
```

Use `Failed notification deliveries`, not just `Failed notifications`, because this number is delivery-row based and can include historical failures.

## Dashboard Sections

### Checklist Creation

Use:

```ts
{
  created: metrics.checklistsTotal,
  sentThisPeriod: Number(metrics.usageQuantityInPeriod),
  inFlight: metrics.checklistsInFlight,
  accepted: metrics.submissionsAccepted,
  totalOrganizations: metrics.organizationsTotal,
  closedOrganizations: metrics.organizationsClosed,
  activeStaff: metrics.activePlatformStaff
}
```

Recommended copy:

```text
Created checklists: all checklist records, including drafts.
Sent this period: billable sent checklist requests in the selected date range.
In flight: sent or in-progress recipient requests.
Accepted: submissions accepted by reviewers.
```

### Revenue By Currency

Use `metrics.revenueInPeriod` for the selected period and `metrics.revenueAllTime` for all time.

If both arrays are empty, show:

```text
No revenue events recorded yet
```

Do not show a dash if the UI can show a clearer empty state.

### Recent Admin Actions

Use:

```text
GET /v1/platform/audit?page=1&pageSize=10
```

Response:

```json
{
  "items": [
    {
      "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
      "staffId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
      "eventType": "platform.login",
      "eventData": "{\"id\":\"...\",\"email\":\"admin@example.com\"}",
      "createdAt": "2026-07-16T17:30:00Z"
    }
  ],
  "page": 1,
  "pageSize": 10,
  "total": 42
}
```

## Endpoint List For This Screen

Overview:

```text
GET /v1/platform/metrics?from=&to=
```

Organizations drill-in:

```text
GET /v1/platform/organizations?page=1&pageSize=25&status=Active
```

Leads drill-in:

```text
GET /v1/platform/interests?page=1&pageSize=25&status=New
GET /v1/platform/interests?page=1&pageSize=25&status=Qualified
```

Revenue drill-in:

```text
GET /v1/platform/revenue-events?page=1&pageSize=25&from=&to=
```

Audit drill-in:

```text
GET /v1/platform/audit?page=1&pageSize=25
```

## Current Backend Limitations To Reflect In UI

- There is no platform-wide action list endpoint yet. The overview can show counts, but cannot drill into all checklist records from one platform endpoint today.
- `failedNotifications` is not an unresolved-alert count. It is a count of failed notification delivery rows.
- `submissionsAccepted` is not period-scoped today.
- Revenue comes from Stripe subscription webhooks and any manual platform revenue records.
- The metrics endpoint does not return action status breakdown. If the UI wants to explain why total checklists is greater than in-flight, use helper copy or request a backend enhancement.
