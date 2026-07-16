# Previously Submitted Documents Frontend Flow

Base API URL:

```text
https://api.reqara.com
```

This document covers three connected flows:

- Recipient save and continue later.
- "Previously Submitted" document choices for repeat requests.
- Sender-side duplicate prevention when creating checklists.

The product name to show in the UI is **Previously Submitted**. Avoid labels like "reuse vault", "document vault", or "saved documents". Recipients do not create accounts and do not manage a persistent file library.

## Current Backend Support

Supported now:

- Recipient draft autosave by requirement.
- Return link email for "save and continue later".
- Same-organization, same-recipient-email, same-file-requirement document lookup.
- Recipient preview of a previous document through a short-lived download URL.
- Recipient-confirmed use of a previous document in a new submission.
- File references instead of duplicate file copies.
- Reference-aware submission deletion.
- Action duplicate protection through `clientReference` and recent duplicate detection.

Important constraints:

- Only accepted submissions are eligible.
- Only clean, non-deleted files are eligible.
- Only the same organization and same recipient email are eligible.
- Only file requirements with reuse enabled are eligible.
- Expired documents are blocked by default.
- The backend never automatically reuses a previous document. Always ask the recipient to confirm.

## Save And Continue Later

Recipients can already leave and come back without an account.

Flow:

1. Recipient opens `/c/{token}` on the frontend.
2. Frontend resolves the token through the API and stores the recipient session cookie.
3. Frontend calls `GET /v1/recipient/checklist`.
4. As the recipient edits answers, frontend calls `PATCH /v1/recipient/responses/{requirementId}`.
5. If the recipient wants a new link, call `POST /v1/recipient/return-link`.

Autosave:

```http
PATCH /v1/recipient/responses/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
```

```json
{
  "value": "Jamie Chen",
  "expectedVersion": 2
}
```

Response:

```json
{
  "requirementId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "version": 3,
  "updatedAt": "2026-07-16T05:25:00Z"
}
```

Return link:

```http
POST /v1/recipient/return-link
```

Response:

```http
202 Accepted
```

UI recommendation:

- Autosave silently after each field change or blur.
- Show a small "Saved" state based on the latest autosave success.
- Add "Email me a fresh link" for recipients who want to continue later.

## Organization Settings

Platform admins and organization admins can manage these as DB-backed settings through the settings endpoints:

```text
documentReuse.enabled = true
documentReuse.maximumAgeDays = 365
documentReuse.requireRecipientConfirmation = true
documentReuse.reuseExpiredDocuments = false
actions.duplicateWindowMinutes = 10
```

Recommended defaults:

- `documentReuse.enabled`: `true`
- `documentReuse.maximumAgeDays`: `365`
- `documentReuse.requireRecipientConfirmation`: `true`
- `documentReuse.reuseExpiredDocuments`: `false`
- `actions.duplicateWindowMinutes`: `10`

Product rationale:

- 365 days is a good default for IDs, certificates, resumes, and common compliance documents.
- Expired documents should never be reused by default.
- Recipient confirmation should remain required even when a document is an obvious match.
- The duplicate window should catch double-clicks and retry bugs without blocking normal repeat business.

## Template Builder Configuration

Document reuse is opt-in per file requirement.

Recommended requirement configuration:

```json
{
  "fileReuse": {
    "allowPreviouslySubmitted": true,
    "maximumAgeDays": 365,
    "requireRecipientConfirmation": true,
    "reuseExpiredDocuments": false
  }
}
```

Accepted aliases:

```json
{
  "allowReuse": true,
  "allowPreviouslySubmitted": true,
  "maximumReuseAgeDays": 365,
  "previouslySubmitted": {
    "allowReuse": true,
    "maximumAgeDays": 365,
    "requireRecipientConfirmation": true,
    "reuseExpiredDocuments": false
  }
}
```

Template builder UI:

- Checkbox: `Allow previously submitted document`
- Number input: `Maximum age`, default `365 days`
- Checkbox: `Require recipient confirmation`, default checked
- Checkbox: `Allow expired documents`, default unchecked and preferably hidden behind an advanced section

Do not expose automatic reuse in the UI. The backend does not implement automatic reuse.

## Recipient Checklist Response

Endpoint:

```http
GET /v1/recipient/checklist
```

Example file requirement with an eligible previous document:

```json
{
  "id": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  "key": "drivers_licence",
  "type": "File",
  "label": "Driver's Licence",
  "description": "Upload a current driver's licence.",
  "required": true,
  "displayOrder": 4,
  "configuration": {
    "fileReuse": {
      "allowPreviouslySubmitted": true,
      "maximumAgeDays": 365,
      "requireRecipientConfirmation": true,
      "reuseExpiredDocuments": false
    }
  },
  "validation": {},
  "condition": null,
  "previouslySubmitted": {
    "fileAssetId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
    "sourceSubmissionId": "dddddddd-dddd-dddd-dddd-dddddddddddd",
    "sourceSubmissionFileId": "ffffffff-ffff-ffff-ffff-ffffffffffff",
    "fileName": "ontario-drivers-licence.pdf",
    "documentName": "Ontario Driver's Licence",
    "submittedAt": "2026-06-12T14:25:00Z",
    "reviewedAt": "2026-06-13T09:10:00Z",
    "scanStatus": "Clean",
    "documentExpiresAt": "2028-04-30T00:00:00Z",
    "retentionUntil": "2027-06-12T14:25:00Z",
    "ageDays": 34,
    "maximumAgeDays": 365,
    "canUse": true,
    "status": "Available",
    "message": "We found an Ontario Driver's Licence you previously submitted to this organization on June 12, 2026.",
    "requireRecipientConfirmation": true
  }
}
```

Expired previous document example:

```json
{
  "previouslySubmitted": {
    "fileAssetId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
    "documentName": "Insurance Certificate",
    "documentExpiresAt": "2024-04-30T00:00:00Z",
    "ageDays": 812,
    "maximumAgeDays": 365,
    "canUse": false,
    "status": "Expired",
    "message": "Previous document found, but it expired on April 30, 2024. Please upload a new one."
  }
}
```

Status values:

```text
Available
Expired
TooOld
Unavailable
```

Frontend behavior:

- If `previouslySubmitted` is absent, show the normal upload control.
- If present with `canUse: true`, show the "Previously Submitted" choice UI.
- If present with `canUse: false`, show the message as helper text and require a new upload.

## Recipient UI Copy

Recommended card:

```text
Driver's Licence

Found previously submitted document

We found an Ontario Driver's Licence you previously submitted to ABC Staffing on June 12, 2026.

Current document
Ontario Driver's Licence

Submitted
June 12, 2026

Status
Valid

Expires
April 2028

[Review] [Use This] [Upload New]
```

If the previous document expires soon:

```text
Found previously submitted document

This document expires in 8 days. A new upload is recommended.

[Review] [Use This] [Upload New]
```

If expired:

```text
Previous document found

Expired April 2024. Please upload a new one.

[Upload New]
```

## Preview Previous Document

Endpoint:

```http
GET /v1/recipient/requirements/{requirementId}/previously-submitted/{fileAssetId}/download-url
```

Example:

```http
GET /v1/recipient/requirements/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee/previously-submitted/cccccccc-cccc-cccc-cccc-cccccccccccc/download-url
```

Response:

```json
{
  "url": "https://tor1.digitaloceanspaces.com/reqara-bucket/...",
  "expiresAt": "2026-07-16T15:05:00Z",
  "headers": {}
}
```

The exact response shape comes from the object storage signed download response. Treat it as short-lived and never store it permanently.

Possible errors:

```text
404 not_found
409 previous_document_not_usable
409 file_not_available
403 otp_required
401 recipient_session_required
```

## Submit With Previous Document

Endpoint:

```http
POST /v1/recipient/submit
```

Use an `Idempotency-Key`.

```json
{
  "declarationAccepted": true,
  "files": [
    {
      "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      "fileId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
      "usePreviouslySubmitted": true,
      "documentName": "Ontario Driver's Licence",
      "documentExpiresAt": "2028-04-30T00:00:00Z"
    }
  ]
}
```

Response:

```json
{
  "id": "12121212-1212-1212-1212-121212121212",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-16T15:00:00Z",
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924..."
}
```

For a new upload, set `usePreviouslySubmitted` to `false`:

```json
{
  "declarationAccepted": true,
  "files": [
    {
      "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
      "fileId": "99999999-9999-9999-9999-999999999999",
      "usePreviouslySubmitted": false,
      "documentName": "Ontario Driver's Licence",
      "documentExpiresAt": "2028-04-30T00:00:00Z"
    }
  ]
}
```

Submit errors:

```json
{
  "type": "https://docs.atlas.example/errors/previous_document_not_usable",
  "title": "Previous document found, but it expired on April 30, 2024. Please upload a new one.",
  "status": 409,
  "code": "previous_document_not_usable"
}
```

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Validation failed",
  "status": 422,
  "code": "validation_failed",
  "errors": {
    "requirements": ["drivers_licence"]
  }
}
```

## Action Duplicate Prevention

When creating a checklist, pass a stable `clientReference` generated by the frontend for that logical create attempt.

Endpoint:

```http
POST /v1/actions
```

Recommended request:

```json
{
  "templateId": "66666666-6666-6666-6666-666666666666",
  "title": "Candidate Onboarding - John Smith",
  "description": "Collect onboarding documents.",
  "recipient": {
    "name": "John Smith",
    "email": "john.smith@example.com",
    "phone": null,
    "otpRequired": false
  },
  "dueAt": "2026-08-15T00:00:00Z",
  "expiresAt": null,
  "settings": {},
  "sendImmediately": true,
  "createdByUserId": "11111111-1111-1111-1111-111111111111",
  "requirements": [],
  "clientReference": "reqara-web-01J2ZVR8V67M6P3F9W1X2G7F21",
  "allowDuplicate": false
}
```

Success:

```json
{
  "id": "88888888-8888-8888-8888-888888888888",
  "publicReference": "act_5fd67eb7d9c2a001e3a4",
  "clientReference": "reqara-web-01J2ZVR8V67M6P3F9W1X2G7F21",
  "title": "Candidate Onboarding - John Smith",
  "status": "Sent",
  "dueAt": "2026-08-15T00:00:00Z",
  "expiresAt": "2026-09-14T00:00:00Z",
  "recipientUrl": "https://reqara.com/c/9f3kV2qP_hN8mL0aZxYtR7wQeUcJbGdKsMoXnBvIrAs",
  "createdAt": "2026-07-16T15:00:00Z"
}
```

Duplicate client reference:

```json
{
  "type": "https://docs.atlas.example/errors/duplicate_client_reference",
  "title": "An action with this client reference already exists.",
  "status": 409,
  "code": "duplicate_client_reference",
  "existingActionId": "88888888-8888-8888-8888-888888888888",
  "publicReference": "act_5fd67eb7d9c2a001e3a4",
  "clientReference": "reqara-web-01J2ZVR8V67M6P3F9W1X2G7F21",
  "existingActionStatus": "Sent"
}
```

Recent duplicate:

```json
{
  "type": "https://docs.atlas.example/errors/possible_duplicate_action",
  "title": "A very similar checklist was just created. Confirm before creating it again.",
  "status": 409,
  "code": "possible_duplicate_action",
  "existingActionId": "88888888-8888-8888-8888-888888888888",
  "publicReference": "act_5fd67eb7d9c2a001e3a4",
  "clientReference": null,
  "existingActionStatus": "Draft"
}
```

Frontend flow:

1. Generate `clientReference` when the user starts the final send/create action.
2. Send `Idempotency-Key` and `clientReference`.
3. If `duplicate_client_reference`, show the existing checklist and do not retry as a new create.
4. If `possible_duplicate_action`, show a confirmation dialog.
5. If the sender confirms, retry with a new `clientReference` and `allowDuplicate: true`.

Suggested confirmation copy:

```text
This looks like a checklist you just created for John Smith.

Create another one anyway?

[Cancel] [Create another checklist]
```

## Audit Events

The backend writes these events:

```text
recipient.previous_document_previewed
recipient.previous_document_confirmed
submission.deleted
action.created
action.submitted
```

For document reuse, audit data includes the requirement ID, reused file ID, source submission ID, and source submission file ID.

## Storage And Retention

The backend does not duplicate files.

Example:

```text
FileAsset 123

Submission A
SubmissionFile -> FileAsset 123

Submission B
SubmissionFile -> FileAsset 123
isPreviouslySubmitted = true
sourceSubmissionId = Submission A
sourceSubmissionFileId = Submission A file row
```

When a previous document is confirmed, the file retention date is extended using the current organization retention policy when needed.

When deleting a submission, the backend deletes the physical object only when no other submission still references the same file asset.

## QA Scenarios

Use these cases for frontend QA:

- No previous document: show only upload.
- Previous document available: show review, use this, upload new.
- Previous document expired: show expired message, force upload.
- Previous document too old: show too-old message, force upload.
- Previous document pending or rejected scan: force upload.
- Recipient uses previous document and submits successfully.
- Recipient chooses upload new instead of previous document.
- Recipient leaves mid-flow, reopens link, sees autosaved fields.
- Sender double-clicks create: backend returns duplicate response.
- Sender intentionally creates another same request after confirming duplicate warning.
