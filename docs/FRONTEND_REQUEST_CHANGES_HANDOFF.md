# Frontend Handoff: Submission Request Changes

Production API base URL: `https://api.reqara.com`

## What Changed

`POST /v1/submissions/{submissionId}/request-changes` now does the full recipient loop:

- updates the submission review status to `ChangesRequested`
- moves the checklist action back to `InProgress`
- moves the recipient back to `Started`
- rotates the recipient private link token
- sends a "Changes requested" email to the recipient
- stores a `recipient_changes_requested` notification delivery row
- writes audit events for both the review action and the email result

The endpoint also supports resending/updating a change request while the latest package is already `ChangesRequested`.

## Why The Old 409 Happened

The production submission `c8904d39-1284-4dfa-8ef5-599068d28a18` was already in `ChangesRequested` from an earlier review note. The previous backend only allowed review calls when the status was exactly `Submitted`, so the second request-change call returned:

```json
{
  "code": "state_conflict",
  "title": "Only submitted packages can be reviewed.",
  "status": 409
}
```

That second call never reached any email logic.

## Request Changes

```http
POST /v1/submissions/{submissionId}/request-changes
Content-Type: application/json
```

```json
{
  "comment": "Please upload a clearer copy of the certificate."
}
```

Successful response:

```json
{
  "id": "c8904d39-1284-4dfa-8ef5-599068d28a18",
  "actionId": "9f96effd-a0df-49fd-8307-98f30a11baff",
  "actionRecipientId": "e2f2a2ab-3f6e-4d86-b5c4-0b9b0f4cdd91",
  "versionNumber": 1,
  "status": "ChangesRequested",
  "submittedAt": "2026-07-17T22:35:00Z",
  "reviewedAt": "2026-07-17T22:45:00Z"
}
```

Frontend behavior:

- Show success copy such as `Changes requested and email sent to recipient.`
- Refresh the action/submission detail after success.
- Expect the action to leave the submitted/review queue and return to active/in-progress lists.
- If the user clicks request changes again on the same `ChangesRequested` submission, allow it as a resend/update flow.

## Failed Email

If the review state was saved but the email provider failed, the endpoint returns:

```json
{
  "type": "https://docs.atlas.example/errors/email_send_failed",
  "title": "Change request email could not be sent. The request is saved and can be resent.",
  "status": 503,
  "code": "email_send_failed",
  "traceId": "..."
}
```

Frontend behavior:

- Do not say the recipient was notified.
- Show: `Changes were saved, but the email could not be sent. Try sending the request again.`
- Keep the request changes modal/form available so the user can retry.

## State Conflicts

`409 state_conflict` now means the submission is no longer reviewable for request changes. Most commonly it is already `Accepted`.

Frontend behavior:

- Refresh the submission detail.
- Show: `This package can no longer be sent back for changes.`

## Recipient Email

The recipient receives a Reqara email with:

- checklist title
- public reference
- sender/org context
- the review comment
- a fresh private checklist link
- reply-to set to the sender's email

The raw token is only in the email link and is logged only by prefix in audit metadata.
