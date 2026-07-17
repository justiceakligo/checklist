# Reqara Stripe Billing Frontend Handoff

Use this file for pricing, checkout, subscription management, and billing UI.

## Product Decision

Starter and Business should be paid on the website with Stripe Checkout.

Free stays self-serve with no payment method required. Scale stays contact-sales because it includes custom volume, SSO, DPA, custom retention, security review, and onboarding.

This is product billing only. Recipient checklist forms do not collect payments.

## Base URLs

```text
API production origin: https://api.reqara.com
Frontend production origin: https://reqara.com
```

All dashboard billing calls require the dashboard auth cookie:

```ts
fetch(`${API_BASE_URL}/v1/billing/checkout-sessions`, {
  method: "POST",
  credentials: "include",
  headers: {
    "Content-Type": "application/json",
    "X-Atlas-Organization-Id": currentOrganizationId
  },
  body: JSON.stringify(payload)
});
```

## Pricing Page Flow

1. Call `GET /v1/billing/plans`.
2. Call `GET /v1/billing/config`.
3. Render Free, Starter, Business, Scale.
4. Free button:
   - Signed-out user: send to signup.
   - Signed-in free org: send to dashboard.
5. Starter or Business button:
   - If signed out, signup first. Signup creates a Free org.
   - Then call `POST /v1/billing/checkout-sessions`.
   - Redirect to `checkoutUrl`.
6. Scale button:
   - Send to contact sales or `POST /v1/public/interests`.
7. On Stripe success redirect:
   - Read `session_id` from URL.
   - Call `GET /v1/billing/checkout-sessions/{session_id}` once to let the backend verify the Checkout Session with Stripe and reconcile the workspace plan if the webhook has not arrived yet.
   - Then call `GET /v1/organizations/{id}/entitlements`.
   - Show the active plan once webhook or session sync has completed.

## GET `/v1/billing/config`

Use to determine whether Stripe checkout is available and to load the publishable key if the UI needs it.

```json
{
  "provider": "stripe",
  "enabled": true,
  "publishableKey": "pk_test_...",
  "selfServePlans": ["free", "starter", "business"],
  "contactSalesPlan": "scale"
}
```

The current checkout flow redirects to Stripe-hosted Checkout, so the frontend does not need Stripe Elements. The publishable key is still returned for future embedded flows.

## GET `/v1/billing/plans`

Use this as the source of truth for pricing and feature flags.

```json
{
  "items": [
    {
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
        "reqaraBranding": false,
        "customBranding": true,
        "automaticReminders": true,
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

`monthlyChecklistLimit` means sent recipient checklist requests per calendar month, not templates or drafts.

## POST `/v1/billing/checkout-sessions`

Creates a Stripe Checkout Session for a subscription.

Request:

```json
{
  "planCode": "starter",
  "billingCycle": "monthly",
  "successUrl": "https://reqara.com/billing/success",
  "cancelUrl": "https://reqara.com/pricing"
}
```

`billingCycle` accepts `monthly` or `annual`. If `successUrl` does not include `session_id={CHECKOUT_SESSION_ID}`, the backend appends it.

Local development may use `http://localhost:*` return URLs. Production return URLs must use HTTPS.

`201`:

```json
{
  "sessionId": "cs_test_123",
  "checkoutUrl": "https://checkout.stripe.com/c/pay/cs_test_123...",
  "expiresAt": "2026-07-17T01:30:00Z"
}
```

Frontend action:

```ts
window.location.assign(response.checkoutUrl);
```

Expected errors:

```text
401 authentication_required: user is not signed in or cookie missing
403 dashboard_user_required: API key/recipient context tried to start checkout
404 plan_not_found: unknown plan code
422 checkout_not_required: Free plan
422 contact_sales_required: Scale/custom plan
422 plan_not_purchasable: plan lacks price
503 billing_not_configured: Stripe keys/settings missing
502 stripe_request_failed: Stripe rejected the checkout request
```

## Billing Success Page

Suggested route:

```text
/billing/success?session_id=cs_test_...
```

UI behavior:

1. Show a short "Finishing setup..." state.
2. If `session_id` is present, call `GET /v1/billing/checkout-sessions/{session_id}`.
3. Refetch `GET /v1/organizations/{id}/entitlements`.
4. If `billing.status` is `active` or `trialing`, show success.
5. If still `free` after a few seconds, show "Payment received, plan activation is syncing" with a refresh button.

Do not call Stripe from the browser to verify payment. Use the backend session status endpoint; it verifies the Checkout Session server-side and is safe to call from the success page.

## GET `/v1/billing/checkout-sessions/{sessionId}`

Use on the billing success page. This endpoint retrieves the Checkout Session from Stripe, confirms it belongs to the current organization, and reconciles the local billing state if the session is complete and paid.

`200` for a paid annual Starter checkout:

```json
{
  "sessionId": "cs_test_123",
  "status": "complete",
  "paymentStatus": "paid",
  "mode": "subscription",
  "planCode": "starter",
  "billingCycle": "annual",
  "amountTotal": 39000,
  "currency": "USD",
  "stripeCustomerId": "cus_123",
  "stripeSubscriptionId": "sub_123",
  "stripeSubscriptionStatus": "active",
  "billing": {
    "planCode": "starter",
    "billingCycle": "annual",
    "status": "active",
    "provider": "stripe",
    "stripeCustomerId": "cus_123",
    "stripeSubscriptionId": "sub_123",
    "cancelAtPeriodEnd": false
  }
}
```

Expected errors:

```text
401 authentication_required: user is not signed in or cookie missing
403 dashboard_user_required: API key/recipient context tried to verify checkout
404 checkout_session_not_found: session does not belong to this organization
422 validation_failed: invalid Checkout Session ID
503 billing_not_configured: Stripe keys/settings missing
502 stripe_request_failed: Stripe rejected the session lookup
```

## POST `/v1/billing/customer-portal-sessions`

Use for "Manage billing", card updates, invoice history, cancellation, and plan changes handled by Stripe.

Request:

```json
{
  "returnUrl": "https://reqara.com/dashboard/billing"
}
```

`200`:

```json
{
  "portalUrl": "https://billing.stripe.com/p/session/test_..."
}
```

Frontend action:

```ts
window.location.assign(response.portalUrl);
```

Expected errors:

```text
409 stripe_customer_missing: org has not completed Stripe checkout yet
503 billing_not_configured: Stripe keys/settings missing
502 stripe_request_failed: Stripe rejected portal session creation
```

## GET `/v1/organizations/{id}/entitlements`

Use this after checkout and on every dashboard boot.

```json
{
  "plan": {
    "code": "business",
    "name": "Business",
    "monthlyChecklistLimit": 500,
    "storageBytes": 26843545600
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

Entitlement rules:

- `active`, `trialing`, and `past_due` keep paid entitlements.
- `canceled`, `unpaid`, `incomplete`, and `incomplete_expired` should be treated as inactive/limited. The backend returns the effective Free plan for hard inactive states.
- `cancelAtPeriodEnd: true` means show "Cancels at period end" but keep the current plan until Stripe sends cancellation.

## Platform Admin Settings

Admin CRUD already supports these in `admin_settings`.

```text
stripe.Enabled = true
stripe.PublishableKey = "pk_test_..."
stripe.SecretKey = secret
stripe.WebhookSigningSecret = secret
stripe.CustomerPortalReturnUrl = "https://reqara.com/dashboard/billing"
```

Store secret values with `isSecret: true`. Admin list views should show masked values unless an Owner/Admin deliberately reveals the secret.

## Stripe Webhook

Configure this endpoint in Stripe:

```text
POST https://api.reqara.com/v1/billing/webhook/stripe
```

The backend verifies the `Stripe-Signature` header using `stripe.WebhookSigningSecret`.

Events processed:

```text
checkout.session.completed
customer.subscription.created
customer.subscription.updated
customer.subscription.deleted
invoice.payment_succeeded
invoice.payment_failed
charge.refunded
```

Events accepted as safe no-ops:

```text
payment_intent.succeeded
payment_intent.payment_failed
invoice.finalized
invoice.paid
customer.created
customer.updated
customer.deleted
```

Webhook effects:

- Checkout/subscription events update organization `billing.plan`.
- Successful invoice payments create Stripe revenue events.
- Failed invoice payments mark billing `past_due`.
- Subscription cancellation removes paid entitlements.
- Refunds create negative revenue events.
- Duplicate Stripe event IDs do not double-count revenue.

## Admin Revenue

Stripe-created revenue events appear in:

```text
GET /v1/platform/revenue-events
GET /v1/platform/metrics
Analytics revenue endpoints
```

Stripe event rows use:

```json
{
  "source": "stripe",
  "externalReference": "stripe:evt_123",
  "type": "Subscription",
  "amount": 99.0,
  "currency": "USD"
}
```

Refunds are negative amounts with `type: "Refund"`.

## Frontend Checklist

- Use plans from `GET /v1/billing/plans`; do not hardcode limits except as fallback copy.
- For Starter/Business, call checkout and redirect to `checkoutUrl`.
- For Scale, route to contact sales.
- Use customer portal for billing management.
- Use entitlements for all feature locks and quota displays.
- Do not display or store Stripe secret keys in frontend code.
- Do not mark a paid plan active from the success URL alone; wait for entitlements/webhook sync.
