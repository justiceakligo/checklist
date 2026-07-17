# Reqara Frontend Auth, Pricing, And OTP Handoff

Use this file for the signup/login, pricing, recipient OTP, and password recovery flows.

Main API guide: [FRONTEND_API_IMPLEMENTATION.md](FRONTEND_API_IMPLEMENTATION.md)
Stripe billing handoff: [FRONTEND_STRIPE_BILLING_HANDOFF.md](FRONTEND_STRIPE_BILLING_HANDOFF.md)
Admin guide: [PLATFORM_ADMIN_API_IMPLEMENTATION.md](PLATFORM_ADMIN_API_IMPLEMENTATION.md)

## Base URLs

```text
API: https://api.reqara.com
Frontend app: https://reqara.com
OpenAPI: https://api.reqara.com/openapi/v1.json
```

All browser dashboard calls must use cookies:

```ts
fetch("https://api.reqara.com/v1/me", {
  credentials: "include",
  headers: { Accept: "application/json" }
});
```

Recommended frontend auth bootstrap:

```ts
const API_BASE_URL = import.meta.env.VITE_ATLAS_API_BASE_URL ?? "https://api.reqara.com";

export async function loadMe() {
  const res = await fetch(`${API_BASE_URL}/v1/me`, {
    credentials: "include",
    headers: { Accept: "application/json" }
  });
  if (!res.ok) throw await res.json();
  const me = await res.json();
  if (me.currentOrganizationId) {
    localStorage.setItem("reqara.currentOrganizationId", me.currentOrganizationId);
  }
  return me;
}

export async function dashboardFetch(path: string, init: RequestInit = {}) {
  const organizationId = localStorage.getItem("reqara.currentOrganizationId");
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

If a signed-in dashboard call returns `401 authentication_required`, the request did not include a valid `__Host-atlas_dashboard` cookie. Fix the frontend request first; do not retry as an anonymous public call.

## Pricing Meaning

When the website says:

```text
Free: 10 checklists / month
Starter: 100 checklists / month
Business: 500 checklists / month
```

The backend interprets this as **10/100/500 sent recipient requests per organization per calendar month**.

Current product model:

```text
1 action/checklist sent to 1 recipient = 1 checklist used
```

Drafts do not count. Template creation does not count. Recipient autosave does not count. Uploading files does not count against checklist count, but it counts against storage.

Billing event timing:

- `POST /v1/actions` with `sendImmediately: false`: no checklist usage.
- `POST /v1/actions` with `sendImmediately: true`: 1 checklist usage after the invitation is sent.
- `POST /v1/actions/{id}/send`: 1 checklist usage the first time a draft is sent.
- `POST /v1/actions/{id}/reminders`: no extra checklist usage.
- Future bulk send rule: sending the same checklist to 25 recipients should count as 25 sent recipient requests.

Quota exceeded response:

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

Frontend behavior:

- Show the pricing cards as "checklists sent per month" in tooltips or plan detail copy.
- Disable send buttons when entitlements show zero remaining, but still let users draft.
- On `402`, show the upgrade/contact sales modal using `plan` and `usage`.
- Starter and Business upgrades use Stripe Checkout. Scale stays contact sales. See the Stripe billing handoff for payloads and success handling.

## Signup And Email Verification

Self-serve signup is open. It creates:

- Active user.
- Active organization.
- Owner membership.
- Free plan by default.
- Signed-in dashboard session.
- Email verification token and email.

Users can sign in before verifying, but cannot send live checklists, create API keys, or create/rotate production webhooks until their email is verified.

### POST `/v1/auth/signup`

Request:

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

If `emailVerificationSent` is `false`, keep the account created and show a resend CTA.

### GET `/v1/me`

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

Frontend behavior:

- If `emailVerified` is `false`, show a persistent banner: "Verify your email to send live checklist requests."
- Keep template browsing, dashboard exploration, draft creation, and org settings accessible.
- On send/API-key actions, rely on the backend guard and show the backend error if returned.

Blocked action error:

```json
{
  "type": "https://docs.atlas.example/errors/email_verification_required",
  "title": "Verify your email before sending live checklist requests or creating API keys.",
  "status": 403,
  "code": "email_verification_required"
}
```

### POST `/v1/auth/email-verification/request`

Requires dashboard cookie. Send it with `credentials: "include"`. The backend can infer the first active organization from the cookie, but the frontend should still include `X-Atlas-Organization-Id` from `/v1/me` for consistency.

Request body:

```json
{}
```

`200` when sent:

```json
{
  "emailVerified": false,
  "emailVerifiedAt": null,
  "sent": true
}
```

`200` when already verified:

```json
{
  "emailVerified": true,
  "emailVerifiedAt": "2026-07-16T15:00:00Z",
  "sent": false
}
```

### Frontend route `/verify-email/:token`

This is a frontend route. It should call:

### POST `/v1/auth/email-verification/confirm`

```json
{
  "token": "raw-token-from-route"
}
```

`200`:

```json
{
  "emailVerified": true,
  "emailVerifiedAt": "2026-07-16T15:00:00Z"
}
```

Errors:

```text
422 invalid_token
409 token_consumed
410 token_expired
```

Expired-token UI:

- Show "This verification link expired."
- If the user is signed in, call `POST /v1/auth/email-verification/request`.
- If not signed in, send them to login.

## Password Reset

Password reset is for dashboard users. It is separate from recipient OTP.

### POST `/v1/auth/password-reset/request`

Request:

```json
{
  "email": "ollie@northstarstaffing.com"
}
```

Always returns `202` for valid email-shaped input, even if no user exists:

```json
{
  "accepted": true
}
```

Frontend behavior:

- Always show the same success message: "If an account exists, we sent reset instructions."
- Do not reveal whether the email exists.

### Frontend route `/reset-password/:token`

This is a frontend route. It should collect the new password and call:

### POST `/v1/auth/password-reset/confirm`

Request:

```json
{
  "token": "raw-token-from-route",
  "newPassword": "new-strong-password"
}
```

`200`:

```json
{
  "passwordReset": true
}
```

Errors:

```text
422 validation_failed: token/newPassword missing or password shorter than 12 chars
422 invalid_token
409 token_consumed
410 token_expired
```

## Recipient OTP

Recipient OTP is not for signup. It is an optional second step for external recipients opening sensitive checklist links.

Recommended labels:

- Sender UI: "Require email code before recipient can open this checklist"
- Recipient UI: "Enter the 6-digit code we sent to your email"

### Who Decides OTP?

The backend resolves `otpRequired` in this order:

1. Recipient override on create action: `recipient.otpRequired`.
2. Action settings: `settings.otpRequired`, `settings.recipientOtpRequired`, `settings.requireRecipientOtp`, or `settings.recipientOtp.required`.
3. Template settings: same keys as action settings.
4. Admin setting: `recipientOtp.defaultRequired`.
5. Fallback: `false`.

### Create Action With Org/Template Default

Omit `otpRequired` or set it to `null`.

```json
{
  "templateId": "33333333-3333-3333-3333-333333333333",
  "title": "Candidate Onboarding - Jamie Chen",
  "recipient": {
    "name": "Jamie Chen",
    "email": "jamie@example.com",
    "phone": null
  },
  "dueAt": "2026-08-15T00:00:00Z",
  "sendImmediately": true,
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": []
}
```

### Force OTP On For One Recipient

```json
{
  "recipient": {
    "name": "Jamie Chen",
    "email": "jamie@example.com",
    "phone": null,
    "otpRequired": true
  }
}
```

### Force OTP Off For One Recipient

```json
{
  "recipient": {
    "name": "Jamie Chen",
    "email": "jamie@example.com",
    "phone": null,
    "otpRequired": false
  }
}
```

### Template Setting Example

```json
{
  "recipientOtp": {
    "required": true
  }
}
```

## Recipient OTP Flow

The email link points to:

```text
https://reqara.com/c/{rawToken}
```

The frontend route must render with:

```text
Referrer-Policy: no-referrer
```

Then call:

### GET `/c/{token}` or `/r/{token}`

If OTP is required, the backend sends the OTP email during this call.

Use `credentials: "include"` so the browser stores the HttpOnly recipient session cookie from `api.reqara.com`.

```ts
await fetch(`${apiBaseUrl}/c/${token}`, {
  method: "GET",
  credentials: "include"
});
```

`200`:

```json
{
  "otpRequired": true,
  "otpVerified": false,
  "sessionExpiresAt": "2026-07-16T16:00:00Z",
  "organizationName": "Northstar Staffing",
  "checklistTitle": "Candidate Onboarding - Jamie Chen"
}
```

If `otpRequired` is `true` and `otpVerified` is `false`, show the OTP screen and do not request checklist details yet.

Repeated calls to `/c/{token}` while an unexpired OTP already exists do not send another email. This prevents React retries, refreshes, or route remounts from producing multiple different codes.

### POST `/v1/recipient/access/verify`

Use `credentials: "include"` here too. The backend verifies the code against the recipient session created by `/c/{token}`; sending only `{ code }` without the cookie returns `401 recipient_session_required`.

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
{
  "verified": true
}
```

Then call:

```text
GET /v1/recipient/checklist
```

### POST `/v1/recipient/access/resend`

Use only when the recipient clicks "Resend code".

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

`429 otp_resend_too_soon`: show a short wait message/countdown before enabling resend.

OTP errors:

```text
403 otp_required: recipient data endpoint called before verifying
410 otp_expired: reopen /c/{token} to send a fresh code
422 invalid_otp: wrong code
429 otp_locked: too many failed attempts
```

## Core UI Scenarios

### New User, Verification Email Arrives

1. User submits signup form.
2. Backend returns `201` and sets auth cookie.
3. Frontend routes to dashboard.
4. Banner says "Verify your email to send live checklist requests."
5. User clicks email link.
6. `/verify-email/:token` calls confirm.
7. Frontend refreshes `/v1/me`.
8. Banner disappears.

### New User, Verification Email Missing

1. User signs in.
2. `/v1/me.emailVerified === false`.
3. Show resend CTA.
4. CTA calls `POST /v1/auth/email-verification/request`.
5. If `sent: true`, show "Verification email sent."

### Unverified User Tries To Send

1. User creates a draft checklist successfully.
2. User clicks Send.
3. Backend returns `403 email_verification_required`.
4. Frontend opens verification modal/banner, not a generic failure toast.

### Recipient With OTP

1. Recipient opens `/c/{token}`.
2. Frontend calls `GET /c/{token}`.
3. Backend emails code and returns `otpRequired: true`.
4. Recipient enters code.
5. Frontend calls `/v1/recipient/access/verify`.
6. On success, frontend loads `/v1/recipient/checklist`.

### Recipient Without OTP

1. Recipient opens `/c/{token}`.
2. Frontend calls `GET /c/{token}`.
3. Backend returns `otpRequired: false`.
4. Frontend immediately loads `/v1/recipient/checklist`.

## Testing Checklist

- Signup success returns `emailVerificationSent`.
- `/v1/me` includes `emailVerified` and `emailVerifiedAt`.
- Unverified user can create draft.
- Unverified user cannot send.
- Resend verification works.
- Expired verification token shows a recoverable UI.
- Password reset request always shows generic success.
- Recipient OTP screen appears only when `otpRequired` is true and `otpVerified` is false.
- Pricing copy says "sent checklists" or explains the recipient-send meaning in details.
