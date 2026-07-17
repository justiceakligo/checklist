# Frontend Handoff: Platform Revenue, Subscriptions, And Pricing

Production API origin:

```text
https://api.reqara.com
```

## Pricing Copy

Do not hardcode `Prices in USD`.

Use `GET /v1/billing/plans` as the source of truth. Render prices with `plan.currency`:

```ts
new Intl.NumberFormat("en-CA", {
  style: "currency",
  currency: plan.currency
}).format(priceCents / 100);
```

Recommended hero copy:

```text
Priced by sent checklists.
One checklist sent to one recipient counts as one sent checklist. Drafts, templates, autosave, reminders, and uploads do not count toward the monthly checklist limit.
```

Optional small line under the toggle:

```text
Prices shown in CAD.
```

Only show that line from API data when all paid plans return the same currency.

## Duplicate Plan Checkout

Frontend should not call Checkout when the selected plan and billing cycle already match the current entitlement state.

If it still calls the backend, the backend returns:

```json
{
  "type": "https://docs.atlas.example/errors/already_subscribed",
  "title": "This workspace is already subscribed to Starter.",
  "status": 422,
  "code": "already_subscribed",
  "message": "This workspace is already on the Starter annual plan.",
  "planCode": "starter",
  "billingCycle": "annual",
  "canManageBilling": false,
  "billing": {
    "planCode": "starter",
    "billingCycle": "annual",
    "status": "active",
    "provider": "manual",
    "cancelAtPeriodEnd": false
  }
}
```

UI behavior:

- Show `Current plan` on the matching plan card.
- Disable the matching plan CTA.
- If `canManageBilling === true`, show `Manage billing`.
- If `canManageBilling === false`, show neutral copy such as `Your workspace is already on this plan.`
- Only allow Checkout for a different paid plan/cycle.

## Revenue Page: Cash Ledger

Use this endpoint for the Revenue table:

```text
GET /v1/platform/revenue-events?page=1&pageSize=25
GET /v1/platform/revenue-events?page=1&pageSize=25&type=Subscription
GET /v1/platform/revenue-events?page=1&pageSize=25&organizationId={id}
GET /v1/platform/revenue-events?page=1&pageSize=25&from=2026-07-01T00:00:00Z&to=2026-07-31T23:59:59Z
```

Important frontend detail: omit filters for “All types” and “All organizations”. Do not send `type=all`.

Response:

```json
{
  "items": [
    {
      "id": "356998eb-4fb2-4c86-a412-a68a81302ebe",
      "organizationId": "f18ace48-e455-48ae-8170-01c2841b36c0",
      "type": "Subscription",
      "amount": 390.0,
      "currency": "CAD",
      "source": "stripe",
      "externalReference": "stripe:checkout_session:cs_...",
      "occurredAt": "2026-07-17T02:10:38.107887+00:00",
      "periodStart": "2026-07-17T01:46:08+00:00",
      "periodEnd": "2027-07-17T01:46:08+00:00",
      "metadata": {
        "eventType": "checkout.session.sync",
        "providerObjectId": "cs_..."
      },
      "recordedByStaffId": null,
      "createdAt": "2026-07-17T02:10:38.107887+00:00",
      "updatedAt": "2026-07-17T02:10:38.107887+00:00",
      "organizationName": "RyvePool",
      "organizationSlug": "ryvepool"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

This table is a revenue/payment ledger. Annual billing appears as the full annual cash amount in one row. Refunds are negative `Refund` rows. Cancellations do not delete historical revenue.

## Subscription State Page

Use this endpoint for current billing/subscription rows:

```text
GET /v1/platform/billing/subscriptions?page=1&pageSize=25
GET /v1/platform/billing/subscriptions?status=canceling
GET /v1/platform/billing/subscriptions?planCode=starter
GET /v1/platform/billing/subscriptions?provider=stripe
```

Response:

```json
{
  "items": [
    {
      "organizationId": "f18ace48-e455-48ae-8170-01c2841b36c0",
      "organizationName": "RyvePool",
      "organizationSlug": "ryvepool",
      "organizationStatus": "Active",
      "planCode": "starter",
      "planName": "Starter",
      "billingCycle": "annual",
      "billingStatus": "active",
      "displayStatus": "active",
      "provider": "manual",
      "cancelAtPeriodEnd": false,
      "currentPeriodStart": "2026-07-17T01:46:08+00:00",
      "currentPeriodEnd": "2027-07-17T01:46:08+00:00",
      "stripeCustomerId": null,
      "stripeSubscriptionId": null,
      "monthlyRecurringRevenue": 32.5,
      "currency": "CAD",
      "monthlyChecklistLimit": 100,
      "storageBytesLimit": 5368709120,
      "organizationUpdatedAt": "2026-07-16T04:39:41.631599+00:00"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

Use this endpoint for:

- Paying orgs table.
- Canceling subscriptions.
- Past-due subscriptions.
- Manual/admin-granted subscriptions.
- Stripe-backed subscriptions.
- MRR per organization.

## MRR And ARR

MRR is now normalized from active billing state, not from cash revenue events.

Examples:

- Starter monthly at CAD 39/month: MRR = 39, ARR = 468.
- Starter annual at CAD 390/year: MRR = 32.50, ARR = 390.
- Business annual at CAD 990/year: MRR = 82.50, ARR = 990.

The platform overview and investor summary should use:

```text
GET /v1/platform/analytics/overview
GET /v1/platform/investor/summary
```

The Revenue table should still use `GET /v1/platform/revenue-events`.

## Cancellation Behavior

When an org cancels in Stripe Customer Portal:

- If Stripe sets `cancel_at_period_end=true`, the subscription remains active until `currentPeriodEnd`.
- Backend returns `billingStatus: "active"` and `displayStatus: "canceling"`.
- MRR still counts until `currentPeriodEnd`.
- Admin should show a `Cancels on {currentPeriodEnd}` badge.

When the subscription is fully canceled:

- Backend receives `customer.subscription.deleted`.
- The org billing state becomes Free/canceled.
- MRR drops to zero.
- Historical revenue events remain visible.
- If Stripe issues a refund, a negative `Refund` revenue event appears.

UI labels:

- `displayStatus: "active"` -> Active
- `displayStatus: "canceling"` -> Canceling at period end
- `billingStatus: "past_due"` -> Payment past due
- `billingStatus: "canceled"` -> Canceled
- `provider: "manual"` -> Admin granted/manual billing
- `provider: "stripe"` -> Stripe billing

