# Checklist Cancellation Frontend Handoff

Only the changed frontend contract is listed here.

## 1. Active Board Query

Use the new `view=active` query when rendering active checklist boards.

```http
GET https://api.reqara.com/v1/actions?view=active&page=1&pageSize=25
```

`view=active` returns only:

- `Draft`
- `Sent`
- `InProgress`
- `Submitted`

Use archive views for inactive records:

```http
GET https://api.reqara.com/v1/actions?view=archive&page=1&pageSize=25
```

`view=archive` returns:

- `Completed`
- `Cancelled`
- `Expired`

You can still filter directly:

```http
GET https://api.reqara.com/v1/actions?status=Cancelled&page=1&pageSize=25
```

## 2. Cancel Checklist

Endpoint:

```http
POST https://api.reqara.com/v1/actions/{actionId}/cancel
Content-Type: application/json
```

Request body:

```json
{
  "reason": "The request is no longer needed.",
  "notifyRecipients": true
}
```

Notes:

- `reason` is optional, max 500 chars.
- `notifyRecipients` is optional and defaults to `true`.
- If the checklist was never sent, no recipient email is sent.
- Cancellation is allowed for `Draft`, `Sent`, and `InProgress`.
- Cancellation is rejected for `Submitted`, `Completed`, `Cancelled`, and `Expired`.

Success response:

```json
{
  "id": "3a6cf902-6a43-4fb2-a292-1435b285088b",
  "status": "Cancelled",
  "cancelledAt": "2026-07-17T00:42:11.195221+00:00",
  "cancellationReason": "The request is no longer needed.",
  "recipientNotificationCount": 1,
  "recipientNotificationsSent": 1,
  "recipientNotificationsFailed": 0,
  "notifications": [
    {
      "recipientId": "2ec36cf0-a5ae-4921-b3fe-608fd6168dcb",
      "recipientEmail": "recipient@example.com",
      "sent": true,
      "error": null
    }
  ]
}
```

Conflict response:

```json
{
  "type": "https://docs.atlas.example/errors/state_conflict",
  "title": "Action cannot be cancelled in its current state.",
  "status": 409,
  "code": "state_conflict"
}
```

## 3. UI Flow

Add a **Cancel checklist** action for `Draft`, `Sent`, and `InProgress`.

Confirmation modal fields:

- Optional reason textarea.
- `Notify recipient` checkbox, checked by default.
- Confirmation copy: "The recipient link will stop working and reminders will stop."

After success:

- Remove the checklist from active board views.
- Show it in archive/cancelled views.
- Show notification warning if `recipientNotificationsFailed > 0`.
- Refresh dashboard summary and action list.

## 4. Response Shape Additions

`GET /v1/actions` items and `GET /v1/actions/{id}` now include:

```json
{
  "cancelledAt": "2026-07-17T00:42:11.195221+00:00",
  "cancellationReason": "The request is no longer needed."
}
```

## 5. Recipient Link After Cancellation

Recipient link and recipient session endpoints now return:

```http
410 Gone
```

```json
{
  "type": "https://docs.atlas.example/errors/cancelled_checklist",
  "title": "This checklist request was cancelled by the sender.",
  "status": 410,
  "code": "cancelled_checklist"
}
```

Recipient UI should render a terminal state:

```text
This checklist request was cancelled by the sender.
No further action is needed.
```

## 6. Backend Side Effects

When cancellation succeeds, backend now:

- Sets action status to `Cancelled`.
- Sets recipient status to `Cancelled` for non-submitted recipients.
- Stops pending reminders.
- Revokes active recipient sessions.
- Expires recipient access tokens.
- Clears pending OTP codes.
- Sends a cancellation email when `notifyRecipients` is true and the checklist was already sent.
- Writes an audit event: `action.cancelled`.

