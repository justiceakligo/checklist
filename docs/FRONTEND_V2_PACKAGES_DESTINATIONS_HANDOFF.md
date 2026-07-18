# Reqara Frontend Handoff: V2 Packages, Destinations, Routing, Handoffs

Backend base URL:

```text
https://api.reqara.com
```

V2 package/routing base path:

```text
/api/v2
```

All dashboard calls must include the existing dashboard cookie credentials. Treat all examples as JSON unless the endpoint returns a file.

## Product Model

Accepted recipient submissions now become immutable `SubmissionPackage` records.

Use this language in the UI:

- Request: the checklist sent by an organization.
- Submission: the recipient's completed response package before/while being reviewed.
- Package: the accepted, auditable output after review.
- Destination: where a package can be sent or handed off.
- Delivery job: an attempt to send a package to a destination.
- Manual handoff: a human-confirmed transfer to an outside system.

Recommended org flow:

1. Recipient submits checklist.
2. Org reviews submission.
3. Org accepts submission.
4. Backend creates a package automatically.
5. Org sees package in Package Inbox / Completed Work.
6. Org downloads report or bundle, sends to a configured destination, or records a manual handoff.
7. Delivery history and handoff history remain visible for audit.

## Package Creation

New accepted submissions package automatically when `POST /v1/submissions/{id}/accept` succeeds.

For already-accepted submissions that predate this release:

```http
POST /api/v2/submissions/{submissionId}/package
```

Success `201`:

```json
{
  "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "packageReference": "REQ-20260717-BFED84",
  "versionNumber": 1,
  "status": "Ready",
  "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
  "requestTitle": "Vendor Onboarding",
  "publicReference": "act_5038097ed3084085c5b5",
  "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18",
  "recipientName": "Jamie Chen",
  "recipientEmail": "jamie@example.com",
  "acceptedAt": "2026-07-17T00:55:24Z",
  "responseCount": 4,
  "fileCount": 2,
  "files": [],
  "deliveries": [],
  "manualHandoffs": []
}
```

If the submission is not accepted:

```json
{
  "type": "https://docs.atlas.example/errors/state_conflict",
  "title": "Only accepted submissions can be packaged.",
  "status": 409,
  "code": "state_conflict"
}
```

## Package Inbox

```http
GET /api/v2/submission-packages?page=1&pageSize=25&status=Ready&q=jamie
```

Query params:

- `page`: defaults to `1`.
- `pageSize`: defaults to `25`, max `100`.
- `status`: optional enum.
- `q`: searches package reference, request title, recipient name, recipient email.

Package statuses:

- `Preparing`
- `Ready`
- `RoutingPending`
- `PartiallyDelivered`
- `Delivered`
- `DeliveryFailed`
- `HandoffConfirmed`
- `Archived`
- `Superseded`

Response:

```json
{
  "items": [
    {
      "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
      "packageReference": "REQ-20260717-BFED84",
      "versionNumber": 1,
      "status": "Ready",
      "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
      "requestTitle": "Vendor Onboarding",
      "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18",
      "actionRecipientId": "b9613c9f-7b80-40f5-8734-47af63e52d78",
      "recipientName": "Jamie Chen",
      "recipientEmail": "jamie@example.com",
      "acceptedAt": "2026-07-17T00:55:24Z",
      "generatedAt": null,
      "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
      "assignedTeamId": null
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

UI notes:

- Use this list for Completed Work, Package Inbox, or Handoff Queue.
- Show `DeliveryFailed` and `RoutingPending` as needs-attention states.
- Hide `Archived` by default unless user filters for archived packages.
- Show `Superseded` as historical version only.

## Package Detail

```http
GET /api/v2/submission-packages/{packageId}
```

Response:

```json
{
  "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "packageReference": "REQ-20260717-BFED84",
  "versionNumber": 1,
  "status": "Delivered",
  "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
  "requestTitle": "Vendor Onboarding",
  "publicReference": "act_5038097ed3084085c5b5",
  "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18",
  "actionRecipientId": "b9613c9f-7b80-40f5-8734-47af63e52d78",
  "recipientName": "Jamie Chen",
  "recipientEmail": "jamie@example.com",
  "acceptedAt": "2026-07-17T00:55:24Z",
  "acceptedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "reviewerName": "Ryve Test",
  "generatedAt": "2026-07-17T01:05:00Z",
  "contentHash": "bfed8452b189b02342ab7c842d596605ad79...",
  "previousPackageId": null,
  "supersededByPackageId": null,
  "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "assignedTeamId": null,
  "responseCount": 4,
  "fileCount": 2,
  "files": [
    {
      "fileAssetId": "7c3de87a-8770-4e66-bb35-9b44fbea2919",
      "requirementId": "b9613c9f-7b80-40f5-8734-47af63e52d78",
      "fileName": "Certificate of Insurance.pdf",
      "mimeType": "application/pdf",
      "sizeBytes": 1552760,
      "scanStatus": "Clean",
      "isPreviouslySubmitted": false,
      "documentName": "Certificate of Insurance",
      "documentExpiresAt": "2027-04-30T00:00:00Z"
    }
  ],
  "deliveries": [],
  "manualHandoffs": [],
  "createdAt": "2026-07-17T00:55:25Z",
  "updatedAt": "2026-07-17T01:05:00Z"
}
```

Recommended tabs:

- Summary
- Files
- Deliveries
- Manual handoffs
- Audit timeline, using existing audit endpoints if shown.

## Reports And Bundles

Generate report metadata:

```http
POST /api/v2/submission-packages/{packageId}/generate-report
```

Response `202`:

```json
{
  "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "packageReference": "REQ-20260717-BFED84",
  "status": "Ready",
  "artifact": "report",
  "generatedAt": "2026-07-17T01:05:00Z",
  "url": "/api/v2/submission-packages/74fd5f68-0d87-40c9-9d63-28e95f760800/report"
}
```

Download PDF report:

```http
GET /api/v2/submission-packages/{packageId}/report
Accept: application/pdf
```

Generate bundle metadata:

```http
POST /api/v2/submission-packages/{packageId}/generate-bundle
```

Download ZIP bundle:

```http
GET /api/v2/submission-packages/{packageId}/bundle
Accept: application/zip
```

Current bundle contents:

- `Submission Report.txt`
- `answers.json`
- `metadata.json`
- `audit-summary.json`
- `file-manifest.json`

Important: the current ZIP contains a signed download URL manifest for clean files, not raw file bytes. This keeps the backend compatible with private object storage without routing sensitive files through app memory. UI should call the existing file download/preview flow for actual file content.

Summary endpoint:

```http
GET /api/v2/submission-packages/{packageId}/summary
```

Response:

```json
{
  "plainText": "Jamie Chen <jamie@example.com> completed Vendor Onboarding on July 17, 2026. The submission was accepted by Ryve Test. Reference: REQ-20260717-BFED84.",
  "json": {
    "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
    "packageReference": "REQ-20260717-BFED84",
    "versionNumber": 1,
    "status": "Ready",
    "requestTitle": "Vendor Onboarding",
    "recipientName": "Jamie Chen",
    "recipientEmail": "jamie@example.com",
    "submittedAt": "2026-07-17T00:50:24Z",
    "acceptedAt": "2026-07-17T00:55:24Z",
    "reviewerName": "Ryve Test",
    "contentHash": "bfed8452b189b02342ab7c842d596605ad79..."
  }
}
```

## Archive And Assignment

Archive:

```http
POST /api/v2/submission-packages/{packageId}/archive
```

Assign:

```http
POST /api/v2/submission-packages/{packageId}/assign
Content-Type: application/json

{
  "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "assignedTeamId": null
}
```

## Destinations

Destinations are org-level configurations controlled by admins.

```http
GET /api/v2/destinations?page=1&pageSize=25&type=Email
```

Destination types:

- `ManualDownload`
- `Email`
- `Webhook`
- `ApiPull`
- `CsvExport`
- `JsonExport`
- `SharePoint`
- `OneDrive`
- `GoogleDrive`
- `Sftp`

Create email destination:

```http
POST /api/v2/destinations
Content-Type: application/json

{
  "name": "HR shared inbox",
  "type": "Email",
  "configuration": {
    "recipients": ["hr@example.com"],
    "cc": ["ops@example.com"]
  },
  "status": "Active",
  "isDefault": true,
  "isActive": true
}
```

Create webhook destination:

```http
POST /api/v2/destinations
Content-Type: application/json

{
  "name": "Dayforce intake webhook",
  "type": "Webhook",
  "configuration": {
    "url": "https://integrations.example.com/reqara"
  },
  "isDefault": false,
  "isActive": true
}
```

Webhook create response includes `generatedSigningSecret` once if no `secretConfiguration` was supplied:

```json
{
  "id": "bc7ec00f-c864-41dd-adf4-508a17744f5d",
  "name": "Dayforce intake webhook",
  "type": "Webhook",
  "status": "Active",
  "configuration": {
    "url": "https://integrations.example.com/reqara"
  },
  "hasSecretConfiguration": true,
  "generatedSigningSecret": "do-not-show-again",
  "isDefault": false,
  "isActive": true,
  "createdByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "createdAt": "2026-07-17T01:00:00Z",
  "updatedAt": "2026-07-17T01:00:00Z",
  "lastValidatedAt": null,
  "lastValidationStatus": null
}
```

UI handling:

- Show the generated signing secret once, with copy action.
- After navigation away, show only `hasSecretConfiguration: true`.
- Webhook URLs must be HTTPS.
- Email destinations require at least one recipient.

Update destination:

```http
PATCH /api/v2/destinations/{destinationId}
```

Disable destination:

```http
DELETE /api/v2/destinations/{destinationId}
```

Validate config:

```http
POST /api/v2/destinations/{destinationId}/validate
```

Send test:

```http
POST /api/v2/destinations/{destinationId}/test
```

If the test fails, show the RFC7807 error body.

## Routing Rules

Routing rules connect templates to destinations.

```http
GET /api/v2/routing-rules?templateId={templateId}
```

Create rule:

```http
POST /api/v2/routing-rules
Content-Type: application/json

{
  "templateId": "dd105733-c136-4192-a7e4-b5344c30b2f7",
  "destinationId": "bc7ec00f-c864-41dd-adf4-508a17744f5d",
  "trigger": "OnAcceptance",
  "sequence": 1,
  "isRequired": true,
  "isAutomatic": true,
  "configurationOverrides": {
    "includeReport": true,
    "includeFiles": false
  }
}
```

Triggers:

- `OnSubmission`
- `OnAcceptance`
- `OnPackageReady`
- `ManualOnly`

Current production behavior:

- Automatic delivery jobs are created on acceptance for `OnAcceptance` rules.
- A hosted dispatcher processes queued jobs and due retries.
- `OnSubmission` and `OnPackageReady` are reserved for upcoming automation surfaces.

## Deliver Package

Manual delivery:

```http
POST /api/v2/submission-packages/{packageId}/deliver
Content-Type: application/json

{
  "destinationIds": ["bc7ec00f-c864-41dd-adf4-508a17744f5d"],
  "includeReport": true,
  "includeFiles": false,
  "idempotencyKey": "ui-click-20260717-001"
}
```

If `destinationIds` is omitted or empty, backend uses active default destinations.

`includeReport` and `includeFiles` are accepted for forward compatibility. Current email and webhook deliveries send package metadata/summary only; submitted files remain available through authenticated Reqara downloads and the package bundle manifest.

Response `202`:

```json
{
  "items": [
    {
      "id": "30b0dbd7-594e-4986-8ee0-090ad8b6e86b",
      "submissionPackageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
      "destinationId": "bc7ec00f-c864-41dd-adf4-508a17744f5d",
      "destinationName": "Dayforce intake webhook",
      "destinationType": "Webhook",
      "status": "Succeeded",
      "attemptCount": 1,
      "maximumAttempts": 5,
      "requestedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
      "triggeredBy": "manual",
      "queuedAt": "2026-07-17T01:10:00Z",
      "startedAt": "2026-07-17T01:10:00Z",
      "completedAt": "2026-07-17T01:10:02Z",
      "nextRetryAt": null,
      "failureCode": null,
      "failureMessage": null,
      "externalReference": "Webhook",
      "idempotencyKey": "ui-click-20260717-001:bc7ec00fc86441ddadf4508a17744f5d"
    }
  ]
}
```

Delivery statuses:

- `Queued`
- `Preparing`
- `Sending`
- `Succeeded`
- `Failed`
- `RetryScheduled`
- `Cancelled`
- `RequiresAttention`

UI notes:

- Disable duplicate send buttons while a delivery with the same destination is `Queued`, `Sending`, or `RetryScheduled`.
- If user retries, call retry endpoint instead of creating a new delivery.
- Show `RequiresAttention` with failure message and retry action.

## Delivery History

List package deliveries:

```http
GET /api/v2/submission-packages/{packageId}/deliveries
```

Get delivery job detail:

```http
GET /api/v2/delivery-jobs/{jobId}
```

Response:

```json
{
  "job": {
    "id": "30b0dbd7-594e-4986-8ee0-090ad8b6e86b",
    "submissionPackageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
    "destinationId": "bc7ec00f-c864-41dd-adf4-508a17744f5d",
    "destinationName": "Dayforce intake webhook",
    "destinationType": "Webhook",
    "status": "Succeeded",
    "attemptCount": 1,
    "maximumAttempts": 5,
    "failureCode": null,
    "failureMessage": null,
    "externalReference": "Webhook",
    "idempotencyKey": "ui-click-20260717-001:bc7ec00fc86441ddadf4508a17744f5d"
  },
  "attempts": [
    {
      "id": "1f679919-e6e6-43e6-8585-c6defaa0631f",
      "attemptNumber": 1,
      "startedAt": "2026-07-17T01:10:00Z",
      "completedAt": "2026-07-17T01:10:02Z",
      "status": "Succeeded",
      "httpStatusCode": 200,
      "providerReference": null,
      "responseSummary": "ok",
      "failureCode": null,
      "failureMessage": null,
      "durationMilliseconds": 492
    }
  ]
}
```

Retry:

```http
POST /api/v2/delivery-jobs/{jobId}/retry
```

Cancel:

```http
POST /api/v2/delivery-jobs/{jobId}/cancel
```

## Manual Handoff

Use this when HR, healthcare, property management, finance, or ops teams move a package into an external system manually.

Create handoff:

```http
POST /api/v2/submission-packages/{packageId}/handoffs
Content-Type: application/json

{
  "destinationLabel": "Dayforce",
  "externalReference": "DF-EMP-10029",
  "notes": "Uploaded report and clean files to employee profile.",
  "evidenceFileAssetId": null
}
```

Response `201`:

```json
{
  "id": "6bfdb96c-5c16-4754-b9a2-4ddadbc902f4",
  "submissionPackageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "destinationLabel": "Dayforce",
  "completedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "completedAt": "2026-07-17T01:15:00Z",
  "externalReference": "DF-EMP-10029",
  "notes": "Uploaded report and clean files to employee profile.",
  "evidenceFileAssetId": null
}
```

List:

```http
GET /api/v2/submission-packages/{packageId}/handoffs
```

UI notes:

- Show a "Mark as handed off" action after package acceptance.
- Good destination labels: `Dayforce`, `SharePoint`, `EMR`, `HRIS`, `CRM`, `Case Management`, `Network Folder`.
- Ask for external reference only if the org has one.
- Handoff changes package status to `HandoffConfirmed`.

## Dashboard Cards To Add

Org dashboard:

- Packages ready
- Packages delivered
- Deliveries needing attention
- Manual handoffs this period
- Average time from accepted to delivered
- Top destinations by volume

Admin dashboard:

- Total packages generated
- Delivery success rate
- Failed deliveries / requires attention
- Top destination types
- Packages by vertical/template
- Manual handoffs by org
- API/webhook adoption

Useful event types already emitted:

- `package_created`
- `package_report_generated`
- `package_bundle_generated`
- `delivery_job_executed`
- `manual_handoff_confirmed`

Audit event types already emitted:

- `package.created`
- `package.downloaded`
- `package.report_generated`
- `package.bundle_generated`
- `package.archived`
- `package.assigned`
- `destination.created`
- `destination.updated`
- `destination.disabled`
- `destination.validated`
- `destination.tested`
- `routing_rule.created`
- `routing_rule.updated`
- `routing_rule.deleted`
- `delivery.queued`
- `delivery.succeeded`
- `delivery.failed`
- `delivery.retried`
- `delivery.cancelled`
- `delivery.requires_attention`
- `package.handoff_confirmed`

## Webhook Contract

Destination webhooks receive:

```json
{
  "id": "evt_7a65db6b268ce00ce172ee6a",
  "type": "package.delivery_started",
  "occurredAt": "2026-07-17T01:10:00Z",
  "organizationId": "f18ace48-e455-48ae-8170-01c2841b36c0",
  "data": {
    "packageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
    "packageReference": "REQ-20260717-BFED84",
    "requestId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
    "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18"
  }
}
```

Headers:

```text
X-Reqara-Event: package.delivery_started
X-Reqara-Timestamp: 1784250600
X-Reqara-Signature: sha256={hmac}
```

Signature is HMAC-SHA256 over the raw JSON payload using the destination signing secret.

## Permissions And Plan Gating

General package read/download:

- Dashboard users with dashboard access.

Destination and routing rule CRUD:

- Admin scope required.

Feature gating:

- Package reports, bundle manifests, and manual handoff are available wherever team workspace is available.
- Email destinations require automatic reminders, currently Starter and above.
- Webhook/API destinations require API and webhooks, currently Business and above.
- SharePoint/advanced custom destinations require custom workflows, currently Business and above.

Frontend should show locked states instead of hiding everything. Use `402` entitlement responses to drive upgrade CTAs.

## Error Shape

All validation and state errors follow RFC7807:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Email destination requires at least one recipient.",
  "status": 422,
  "code": "validation_failed",
  "traceId": "00-..."
}
```

Common codes:

- `authentication_required`
- `tenant_required`
- `forbidden`
- `not_found`
- `validation_failed`
- `state_conflict`
- `feature_not_available`
- `destination_test_failed`

## UX Recommendation

After accepting a submission, do not dead-end on "download files."

Show a "Next step" panel:

- Download report
- Download package manifest
- Send to destination
- Mark manual handoff complete
- Configure routing for this template

For HR and operations buyers, the value is not only "we received the files." It is "we got the completed package into the system where work continues."
