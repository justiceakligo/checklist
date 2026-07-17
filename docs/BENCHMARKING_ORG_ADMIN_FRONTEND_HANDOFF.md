# Reqara Benchmarking Frontend Handoff

Production API origin:

```text
https://api.reqara.com
```

Frontend env:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
```

This file covers the new benchmarking experience for both:

- Organization dashboard: customer-facing, anonymized same-plan comparison.
- Platform admin dashboard: internal benchmark, cohort, and delay-driver intelligence.

## Product Decision

Keep pricing and analytics usage-first.

One checklist sent to one recipient counts as one sent checklist. Seats do not drive plan limits right now. Benchmarking should therefore explain operational performance around sent checklists, recipient completion, reminders, file reuse, and time to complete.

The first production-safe customer benchmark cohort is:

```text
same billing plan
minimum 5 comparable organizations
no organization names
no recipient data
no checklist names
no file data
```

Industry/vertical benchmarking is intentionally not exposed to customer dashboards yet because the backend does not currently have structured organization industry data. Admin can still use the benchmark endpoint to see plan cohorts and overall platform distributions.

## Auth

Organization dashboard request:

```ts
await fetch(`${API_BASE_URL}/v1/analytics/benchmarks`, {
  credentials: "include",
  headers: {
    Accept: "application/json",
    "X-Atlas-Organization-Id": organizationId
  }
});
```

Platform admin request:

```ts
await fetch(`${API_BASE_URL}/v1/platform/analytics/benchmarks`, {
  credentials: "include",
  headers: { Accept: "application/json" }
});
```

Both endpoints accept optional ISO date filters:

```text
from=2026-07-01T00:00:00Z
to=2026-07-31T23:59:59Z
```

Default period is the current UTC month.

## Organization Dashboard

Recommended UI route:

```text
/app/analytics/benchmarks
```

Endpoint:

```text
GET /v1/analytics/benchmarks?from=&to=
```

Use this to power:

- Benchmark cards.
- Same-plan cohort comparison.
- Delay-driver list.
- Recommended actions.
- Empty/suppressed benchmark states.

### Available Response

```json
{
  "period": {
    "from": "2026-07-01T00:00:00+00:00",
    "to": "2026-07-31T23:59:59+00:00"
  },
  "cohort": "same_plan:starter",
  "cohortLabel": "Similar starter workspaces",
  "cohortSampleSize": 18,
  "minimumCohortSize": 5,
  "benchmarksAvailable": true,
  "metrics": [
    {
      "key": "recipient_completion_rate",
      "label": "Recipient completion rate",
      "unit": "percent",
      "organizationValue": 82.4,
      "benchmarkMedian": 91.0,
      "benchmarkP75": 94.7,
      "benchmarkP90": 97.2,
      "deltaFromMedian": -8.6,
      "direction": "below_benchmark",
      "higherIsBetter": true,
      "suppressed": false
    },
    {
      "key": "median_completion_minutes",
      "label": "Median completion time",
      "unit": "minutes",
      "organizationValue": 1480.5,
      "benchmarkMedian": 920.0,
      "benchmarkP75": 540.0,
      "benchmarkP90": 320.0,
      "deltaFromMedian": 560.5,
      "direction": "below_benchmark",
      "higherIsBetter": false,
      "suppressed": false
    }
  ],
  "delayDrivers": [
    {
      "key": "not_opened",
      "label": "Recipients have not opened",
      "value": 4,
      "unit": "count",
      "recommendedAction": "Use a short reminder or verify the recipient email address."
    }
  ],
  "insights": [
    {
      "key": "completion_below_cohort",
      "title": "Completion is below similar workspaces",
      "severity": "warning",
      "summary": "This workspace is 8.6 percentage points below the same-plan median.",
      "recommendedAction": "Look at the delay drivers and simplify the highest-friction requirements first."
    }
  ],
  "privacyNote": "Benchmarks are anonymized and aggregated. Reqara never exposes another organization's name, recipient, checklist, file, or raw activity to a customer dashboard."
}
```

### Suppressed Response

When there are not enough comparable organizations, show the org's own values but hide benchmark comparison values.

```json
{
  "cohort": "same_plan:starter",
  "cohortLabel": "Similar starter workspaces",
  "cohortSampleSize": 3,
  "minimumCohortSize": 5,
  "benchmarksAvailable": false,
  "metrics": [
    {
      "key": "recipient_completion_rate",
      "label": "Recipient completion rate",
      "unit": "percent",
      "organizationValue": 82.4,
      "benchmarkMedian": null,
      "benchmarkP75": null,
      "benchmarkP90": null,
      "deltaFromMedian": null,
      "direction": "not_available",
      "higherIsBetter": true,
      "suppressed": true
    }
  ],
  "insights": [
    {
      "key": "cohort_building",
      "title": "Benchmark cohort is still building",
      "severity": "info",
      "summary": "Reqara needs at least 5 comparable workspaces before showing customer-facing benchmark values.",
      "recommendedAction": "Keep using operational metrics here; benchmark comparison will unlock automatically."
    }
  ]
}
```

### Metric Keys

Render these in the org dashboard:

| Key | Unit | Meaning |
| --- | --- | --- |
| `recipient_completion_rate` | percent | Sent recipients who submitted. |
| `median_completion_minutes` | minutes | Median time from send to recipient submission. Lower is better. |
| `median_first_open_minutes` | minutes | Median time from send to first open. Lower is better. |
| `document_reuse_rate` | percent | Previously submitted files reused with recipient confirmation. |
| `reminder_coverage_rate` | percent | Sent recipients with at least one sent reminder. |
| `checklists_sent` | count | Checklist requests sent in the selected period. |

### Direction Rules

Use `direction` and `higherIsBetter`; do not infer from metric key alone.

```text
above_benchmark = visually positive
at_benchmark = neutral
below_benchmark = needs attention
not_available = no comparison
```

For time metrics, lower values are better. The backend already accounts for that.

### Org UI Flow

1. User opens `/app/analytics/benchmarks`.
2. Frontend calls `GET /v1/analytics/benchmarks`.
3. Show a small privacy note near the cohort label.
4. Render metric cards using `organizationValue`.
5. If `metric.suppressed === false`, also show median and delta.
6. If `benchmarksAvailable === false`, show a calm locked/suppressed state:

```text
Benchmarks are still building
We show comparisons once at least 5 similar workspaces are active.
```

7. Render `delayDrivers` as a "Where requests slow down" list.
8. Render `insights` as action cards.

Good homepage/dashboard copy:

```text
Your workspace compared with similar Starter workspaces.
Benchmarks are anonymized and only shown when enough comparable workspaces exist.
```

Do not say "similar staffing agencies" or "similar accounting firms" until an industry cohort exists in the API.

## Platform Admin Dashboard

Recommended UI route:

```text
/admin/analytics/benchmarks
```

Endpoint:

```text
GET /v1/platform/analytics/benchmarks?from=&to=
```

Use this to power:

- Platform benchmark overview.
- Plan cohort comparison.
- Benchmark readiness.
- Delay-driver intelligence.
- Product roadmap decisions around templates, reminders, and Organization Memory.

### Admin Response

The legacy `checklistsSentPerOrganization` field remains for compatibility.

```json
{
  "period": {
    "from": "2026-07-01T00:00:00+00:00",
    "to": "2026-07-31T23:59:59+00:00"
  },
  "checklistsSentPerOrganization": {
    "key": "checklists_sent_per_org",
    "label": "Checklists sent per active organization",
    "p50": 12,
    "p75": 31,
    "p90": 74,
    "unit": "count"
  },
  "metrics": [
    {
      "key": "recipient_completion_rate",
      "label": "Recipient completion rate",
      "unit": "percent",
      "organizationsWithValue": 22,
      "p50": 88.2,
      "p75": 94.1,
      "p90": 97.8,
      "higherIsBetter": true
    },
    {
      "key": "document_reuse_rate",
      "label": "Previously submitted document reuse",
      "unit": "percent",
      "organizationsWithValue": 9,
      "p50": 14.3,
      "p75": 27.5,
      "p90": 41.0,
      "higherIsBetter": true
    }
  ],
  "cohorts": [
    {
      "cohortType": "plan",
      "cohortKey": "starter",
      "cohortLabel": "starter workspaces",
      "sampleSize": 18,
      "metrics": [
        {
          "key": "recipient_completion_rate",
          "label": "Recipient completion rate",
          "unit": "percent",
          "organizationsWithValue": 18,
          "p50": 91.0,
          "p75": 94.7,
          "p90": 97.2,
          "higherIsBetter": true
        }
      ]
    }
  ],
  "delayDrivers": [
    {
      "key": "not_opened",
      "label": "Recipients have not opened",
      "value": 46,
      "unit": "count",
      "recommendedAction": "Use a short reminder or verify the recipient email address."
    }
  ],
  "notes": "Benchmarks are internal anonymized platform distribution benchmarks for the selected period. Customer-facing benchmarks use same-plan cohorts with privacy thresholds; industry cohorts should be enabled after structured organization vertical data is collected."
}
```

### Admin UI Flow

1. User opens `/admin/analytics/benchmarks`.
2. Frontend calls `GET /v1/platform/analytics/benchmarks`.
3. Top cards:
   - Active benchmark organizations: count cohorts/organizations with sent checklists.
   - Median checklists sent per active org.
   - Median recipient completion rate.
   - Median document reuse rate.
4. Middle section:
   - Benchmark distribution table using `metrics`.
   - Plan cohort table using `cohorts`.
5. Bottom section:
   - Delay drivers using `delayDrivers`.
   - Notes panel using `notes`.

### Admin Benchmark Readiness

For each cohort:

```ts
const readyForCustomerBenchmark = cohort.sampleSize >= 5;
```

Show:

```text
Ready for customer-facing benchmark
```

or:

```text
Needs more sample size
```

Do not expose customer-facing benchmark values for cohorts under 5.

## Event Tracking

Use the existing event endpoint for engagement.

```text
POST /v1/analytics/events
```

Sample:

```json
{
  "eventType": "benchmark_page_viewed",
  "source": "org_dashboard",
  "properties": {
    "route": "/app/analytics/benchmarks",
    "cohort": "same_plan:starter",
    "benchmarksAvailable": true
  }
}
```

Recommended events:

| Event | When |
| --- | --- |
| `benchmark_page_viewed` | User opens benchmark page. |
| `benchmark_metric_expanded` | User expands a metric card. |
| `benchmark_insight_clicked` | User clicks an insight action. |
| `benchmark_date_range_changed` | User changes date filter. |
| `admin_benchmark_cohort_viewed` | Platform staff opens a cohort. |

## Frontend Formatting

Use these formatting defaults:

```ts
function formatMetric(value: number | null, unit: string) {
  if (value == null) return "Not available";
  if (unit === "percent") return `${value.toFixed(1)}%`;
  if (unit === "minutes" && value >= 1440) return `${(value / 1440).toFixed(1)} days`;
  if (unit === "minutes" && value >= 60) return `${(value / 60).toFixed(1)} hours`;
  if (unit === "minutes") return `${value.toFixed(0)} min`;
  return value.toLocaleString();
}
```

For deltas:

```ts
function formatDelta(metric) {
  if (metric.deltaFromMedian == null) return null;
  const sign = metric.deltaFromMedian > 0 ? "+" : "";
  return `${sign}${formatMetric(metric.deltaFromMedian, metric.unit)} vs median`;
}
```

Use `direction` for color, not the sign alone.

## UX Copy

Organization dashboard:

```text
Similar workspaces
Compare this workspace with anonymized Reqara workspaces on the same plan.
```

Suppressed state:

```text
Benchmarks are still building
We show comparisons once enough similar workspaces are active. Your own metrics are still available below.
```

Platform admin:

```text
Benchmark intelligence
Understand how workspaces perform across completion, speed, reminders, and document reuse.
```

## Current Limitations

The API does not yet expose:

- Industry-specific customer cohorts.
- Organization-size cohorts.
- Region-specific cohorts.
- Predictive benchmark forecast per organization.
- Customer-facing peer names or examples.

That is intentional for privacy and data-quality reasons.

Next backend step for industry benchmarking:

```text
Add structured organization profile fields:
industry
region
employeeCountBand
primaryUseCase
```

Then introduce customer-facing cohorts only after each cohort can pass the same minimum sample threshold.
