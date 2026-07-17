# Reqara Submission Notifications Frontend Handoff

Production API origin:

```text
https://api.reqara.com
```

Frontend env:

```text
VITE_ATLAS_API_BASE_URL=https://api.reqara.com
```

## What Changed

When a recipient successfully submits a checklist through:

```text
POST /v1/recipient/submit
```

the backend now sends a safe notification email to the organization side.

The submission response payload did not change.

The backend:

- Saves the submission first.
- Saves the idempotency replay record if `Idempotency-Key` was provided.
- Attempts org notification emails after the submission is durable.
- Does not block recipient success if the org notification email fails.
- Records `notification_deliveries` rows using template key `organization_submission_submitted`.
- Records audit events:
  - `organization.submission_notification_sent`
  - `organization.submission_notification_failed`
  - `organization.submission_notification_skipped`

## Email Safety

The org notification email includes only:

- Checklist title.
- Recipient name and email.
- Receipt reference.
- Public reference.
- Submitted timestamp.
- Version number.
- Response count.
- File count.
- Previously submitted file count.
- Dashboard review link.

The email does not include:

- Submitted answers.
- Uploaded file names.
- Download links.
- Attachments.
- Raw content hash.
- Recipient access token.

## Recipient Submit Flow

No frontend submit payload change is required.

Recommended request:

```ts
await fetch(`${API_BASE_URL}/v1/recipient/submit`, {
  method: "POST",
  credentials: "include",
  headers: {
    "Content-Type": "application/json",
    "Idempotency-Key": crypto.randomUUID()
  },
  body: JSON.stringify({
    declarationAccepted: true,
    files: [
      {
        requirementId: "b9613c9f-7b80-40f5-8734-47af63e52d78",
        fileId: "7c3de87a-8770-4e66-bb35-9b44fbea2919",
        usePreviouslySubmitted: false,
        documentName: "Certificate of Insurance",
        documentExpiresAt: "2027-04-30T00:00:00Z"
      }
    ]
  })
});
```

Success response remains:

```json
{
  "submissionId": "0edc8d2f-f44e-4c24-82ff-b52bcbb77c59",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-17T15:02:11.991Z",
  "contentHash": "bfed8452b189b02342ab7c842d596605ad79...",
  "publicReference": "REQ-20260717-090A18",
  "receiptReference": "REQ-20260717-090A18",
  "verificationFingerprint": "BFED-8452-B189-B023"
}
```

Important UI rule:

```text
Do not show "email sent to organization" on the recipient success screen.
```

The recipient only needs to know the checklist was submitted. Org notification delivery is an internal operational side effect.

## Organization Settings UI

Recommended route:

```text
/app/settings/notifications
```

Suggested section:

```text
Submission notifications
```

Controls:

| UI label | Setting category | Setting key | Type | Default |
| --- | --- | --- | --- | --- |
| Email us when a recipient submits | `notifications` | `submissionSubmittedEnabled` | boolean | `true` |
| Notify checklist creator | `notifications` | `submissionSubmittedNotifyCreator` | boolean | `true` |
| Notify organization inbox | `notifications` | `submissionSubmittedNotifyInbox` | boolean | `true` when inbox email is present |
| Organization inbox email | `notifications` | `submissionSubmittedInboxEmail` | string email | empty |

Recommended helper copy:

```text
Submission emails include the checklist name, recipient, receipt reference, and a review link. They never include submitted answers or attachments.
```

## Admin Settings API

Use the existing admin settings CRUD endpoints.

Org-scoped examples below assume the frontend is authenticated as an org dashboard user and sends:

```text
X-Atlas-Organization-Id: {organizationId}
```

### Enable Or Disable Submission Emails

```http
PUT /v1/admin/settings/notifications/submissionSubmittedEnabled
Content-Type: application/json
```

```json
{
  "scope": "Organization",
  "value": true,
  "isSecret": false
}
```

### Toggle Creator Notification

```http
PUT /v1/admin/settings/notifications/submissionSubmittedNotifyCreator
Content-Type: application/json
```

```json
{
  "scope": "Organization",
  "value": true,
  "isSecret": false
}
```

### Set Organization Inbox Email

```http
PUT /v1/admin/settings/notifications/submissionSubmittedInboxEmail
Content-Type: application/json
```

```json
{
  "scope": "Organization",
  "value": "submissions@company.com",
  "isSecret": false
}
```

### Toggle Inbox Notification

```http
PUT /v1/admin/settings/notifications/submissionSubmittedNotifyInbox
Content-Type: application/json
```

```json
{
  "scope": "Organization",
  "value": true,
  "isSecret": false
}
```

If the creator email and inbox email are the same, the backend sends only one email.

## Dashboard UI

Recommended UX after a recipient submits:

- Dashboard tables should refresh through the normal actions/submissions endpoints.
- The organization does not need a special "notification sent" badge on the action row.
- Admin/audit screens may show `organization.submission_notification_sent` events if they already render audit event timelines.

Recommended dashboard toast when staff manually views the newly submitted action:

```text
Submission received
Review the recipient's answers and uploaded files.
```

## Email Preview Copy

Frontend can show this preview in settings:

```text
Subject:
Jamie Chen submitted Vendor Compliance Package

Body summary:
Jamie Chen submitted Vendor Compliance Package.
Receipt: REQ-20260717-090A18
Submitted: July 17, 2026 at 3:02 PM UTC

Review submission

For security, this email does not include submitted answers or file attachments.
```

## Failure Behavior

If the provider fails:

- Recipient still sees successful submission.
- Backend records `organization_submission_submitted` delivery with status `Failed`.
- Backend records `organization.submission_notification_failed`.
- Platform/admin dashboards can surface failed notifications through existing support alert patterns.

Frontend should not retry org notification emails from the recipient success page.

## Recommended Future UI

Later, add a notification preferences screen with:

- Submission received.
- All recipients completed.
- Checklist overdue.
- File rejected.
- Changes requested.
- Submission accepted.

For now, this handoff only covers recipient submitted checklist notifications.
