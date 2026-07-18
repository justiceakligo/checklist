# Reqara V2 Final Backend Contracts

Last updated: 2026-07-17

Production API:

```text
https://api.reqara.com
```

Org V2 base path:

```text
/api/v2
```

Platform admin reporting base path:

```text
/v1/platform
```

All examples use JSON unless the endpoint explicitly returns a file. Enum values serialize as strings.

## Auth

Org dashboard calls use the existing authenticated dashboard session cookie and must be sent with browser credentials:

```ts
fetch("https://api.reqara.com/api/v2/submission-packages", {
  credentials: "include"
});
```

Destination and routing rule write actions require organization admin scope. Platform reporting endpoints require platform staff auth.

Common headers:

```http
Content-Type: application/json
Accept: application/json
```

## Product Lifecycle

V2 should support this lifecycle:

```text
Collect -> Review -> Accept -> Package -> Route -> Prove delivery -> Archive
```

Reqara is the intake, review, packaging, routing, and proof layer. It should not be presented as the final system of record for HRIS, EMR, payroll, CRM, SharePoint, or document-management systems.

## Status Enums

`SubmissionPackageStatus`:

```text
Preparing
Ready
RoutingPending
PartiallyDelivered
Delivered
DeliveryFailed
HandoffConfirmed
Archived
Superseded
```

`DestinationType`:

```text
ManualDownload
Email
Webhook
ApiPull
CsvExport
JsonExport
SharePoint
OneDrive
GoogleDrive
Sftp
```

`DestinationStatus`:

```text
Active
Disabled
Invalid
```

`RoutingTrigger`:

```text
OnSubmission
OnAcceptance
OnPackageReady
ManualOnly
```

`DeliveryJobStatus`:

```text
Queued
Preparing
Sending
Succeeded
Failed
RetryScheduled
Cancelled
RequiresAttention
```

`DeliveryAttemptStatus`:

```text
Succeeded
Failed
Cancelled
```

`FileScanStatus` is returned on package files. Frontend should treat only `Clean` files as preview/download ready.

## Package Identity After Acceptance

When an org accepts a submitted checklist, the backend creates or returns the package immediately.

```http
POST /v1/submissions/{submissionId}/accept
```

Request:

```json
{
  "comment": "Looks good."
}
```

Success `200`:

```json
{
  "id": "c8904d39-1284-4dfa-8ef5-599068d28a18",
  "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
  "actionRecipientId": "6bc9e9e5-2d26-49a9-9a61-4f3d83e8627a",
  "versionNumber": 1,
  "status": "Accepted",
  "submittedAt": "2026-07-17T01:55:24Z",
  "reviewedAt": "2026-07-17T02:03:11Z",
  "package": {
    "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
    "packageReference": "REQ-20260717-BFED84",
    "status": "Ready",
    "versionNumber": 1
  }
}
```

Frontend flow:

1. Call accept.
2. If `package` is present, navigate to package detail or show "Ready for handoff".
3. If `package` is null because the client hit an older backend or stale response, call the backfill endpoint below.

Backfill or create a package for an accepted submission:

```http
POST /api/v2/submissions/{submissionId}/package
```

Use this for older accepted submissions that predate package creation. It returns full package detail with `201 Created`.

Find package by submission:

```http
GET /api/v2/submissions/{submissionId}/package
```

Find packages for an action/request:

```http
GET /api/v2/actions/{actionId}/packages
GET /api/v2/actions/{actionId}/packages?latest=true
```

## Package List

```http
GET /api/v2/submission-packages
```

Supported query params:

```text
page=1
pageSize=25
status=Ready
ownerUserId={userGuid}
assignedTeamId={teamGuid}
from=2026-07-01T00:00:00Z
to=2026-07-31T23:59:59Z
q=jamie
```

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
      "actionRecipientId": "6bc9e9e5-2d26-49a9-9a61-4f3d83e8627a",
      "recipientName": "Jamie Chen",
      "recipientEmail": "jamie@example.com",
      "acceptedAt": "2026-07-17T02:03:11Z",
      "generatedAt": "2026-07-17T02:04:00Z",
      "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
      "ownerName": "Ryve Test",
      "assignedTeamId": null,
      "deliveryCount": 2,
      "failedDeliveryCount": 0,
      "latestDeliveryStatus": "Succeeded",
      "handoffCount": 1
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

Dashboard table recommendations:

- Primary label: `packageReference`
- Secondary label: `requestTitle`
- Recipient column: `recipientName` and `recipientEmail`
- Owner column: `ownerName`, fallback to `ownerUserId`
- Status pill: `status`
- Problems badge: `failedDeliveryCount > 0` or `latestDeliveryStatus` is `RequiresAttention`

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
  "status": "Ready",
  "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
  "requestTitle": "Vendor Onboarding",
  "publicReference": "act_5038097ed3084085c5b5",
  "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18",
  "actionRecipientId": "6bc9e9e5-2d26-49a9-9a61-4f3d83e8627a",
  "recipientName": "Jamie Chen",
  "recipientEmail": "jamie@example.com",
  "acceptedAt": "2026-07-17T02:03:11Z",
  "acceptedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "reviewerName": "Ryve Test",
  "generatedAt": "2026-07-17T02:04:00Z",
  "contentHash": "bfed8452b189b02342ab7c842d596605ad79...",
  "previousPackageId": null,
  "supersededByPackageId": null,
  "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "ownerName": "Ryve Test",
  "assignedTeamId": null,
  "responseCount": 4,
  "fileCount": 2,
  "files": [
    {
      "fileAssetId": "7c3de87a-8770-4e66-bb35-9b44fbea2919",
      "requirementId": "b9613c9f-7b80-40f5-8734-47af63e52d78",
      "fileName": "certificate-of-insurance.pdf",
      "mimeType": "application/pdf",
      "sizeBytes": 104448,
      "scanStatus": "Clean",
      "isPreviouslySubmitted": false,
      "documentName": "Certificate of insurance",
      "documentExpiresAt": "2027-04-30T00:00:00Z"
    }
  ],
  "deliveries": [],
  "manualHandoffs": [],
  "createdAt": "2026-07-17T02:03:11Z",
  "updatedAt": "2026-07-17T02:04:00Z"
}
```

Use `files[].fileName`, `mimeType`, `sizeBytes`, and `scanStatus` for dashboard file previews. Do not label package files as "Submitted file 1" unless these fields are missing.

## Package Contents and Integrity

```http
GET /api/v2/submission-packages/{packageId}/contents
```

Response:

```json
{
  "packageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "packageReference": "REQ-20260717-BFED84",
  "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18",
  "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
  "responses": [
    {
      "requirementId": "b9613c9f-7b80-40f5-8734-47af63e52d78",
      "value": {
        "text": "ABC Plumbing Inc."
      }
    }
  ],
  "files": [
    {
      "fileAssetId": "7c3de87a-8770-4e66-bb35-9b44fbea2919",
      "requirementId": "b9613c9f-7b80-40f5-8734-47af63e52d78",
      "fileName": "certificate-of-insurance.pdf",
      "mimeType": "application/pdf",
      "sizeBytes": 104448,
      "scanStatus": "Clean",
      "isPreviouslySubmitted": false,
      "documentName": "Certificate of insurance",
      "documentExpiresAt": "2027-04-30T00:00:00Z"
    }
  ],
  "integrity": {
    "contentHash": "bfed8452b189b02342ab7c842d596605ad79...",
    "metadataJson": "{\"request\":{\"id\":\"5038097e-d308-4085-c5b5-9bb9c2c10aac\"}}",
    "versionNumber": 1,
    "acceptedAt": "2026-07-17T02:03:11Z",
    "acceptedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
    "declarationAcceptedAt": "2026-07-17T01:55:24Z",
    "declarationIpAddress": "203.0.113.10"
  }
}
```

The content hash is for audit/integrity, not human-readable receipt display. In normal UI, show the package reference and accepted date; keep the hash under "Audit details" or "Integrity proof".

## Package Summary

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
    "submittedAt": "2026-07-17T01:55:24Z",
    "acceptedAt": "2026-07-17T02:03:11Z",
    "reviewerName": "Ryve Test",
    "contentHash": "bfed8452b189b02342ab7c842d596605ad79..."
  }
}
```

## Package Artifacts

Generate report:

```http
POST /api/v2/submission-packages/{packageId}/generate-report
```

Success `202`:

```json
{
  "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "packageReference": "REQ-20260717-BFED84",
  "status": "Ready",
  "artifact": "report",
  "generatedAt": "2026-07-17T02:04:00Z",
  "url": "/api/v2/submission-packages/74fd5f68-0d87-40c9-9d63-28e95f760800/report"
}
```

Download report PDF:

```http
GET /api/v2/submission-packages/{packageId}/report
```

Returns:

```text
Content-Type: application/pdf
Content-Disposition: attachment; filename="{packageReference}-Submission-Report.pdf"
```

Generate bundle:

```http
POST /api/v2/submission-packages/{packageId}/generate-bundle
```

Success `202`:

```json
{
  "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "packageReference": "REQ-20260717-BFED84",
  "status": "Ready",
  "artifact": "bundle",
  "generatedAt": "2026-07-17T02:04:00Z",
  "url": "/api/v2/submission-packages/74fd5f68-0d87-40c9-9d63-28e95f760800/bundle"
}
```

Download bundle ZIP:

```http
GET /api/v2/submission-packages/{packageId}/bundle
```

Final behavior:

- The ZIP contains real file bytes for submitted files whose scan status is `Clean`.
- The ZIP does not use signed URLs for included clean files.
- Pending, rejected, missing, or limit-exceeded files are skipped and listed in `file-manifest.json`.
- Defaults are `packageExport.maxFileCount=100` and `packageExport.maxZipBytes=262144000`.

ZIP structure:

```text
REQ-20260717-BFED84/
  Submission Report.txt
  answers.json
  metadata.json
  audit-summary.json
  files/
    7c3de87a87704e66bb359b44fbea2919-certificate-of-insurance.pdf
  file-manifest.json
```

`file-manifest.json`:

```json
{
  "generatedAt": "2026-07-17T02:04:00Z",
  "manifestVersion": 2,
  "maxEmbeddedFileCount": 100,
  "maxEmbeddedBytes": 262144000,
  "embeddedFileCount": 1,
  "skippedFileCount": 1,
  "files": [
    {
      "requirementId": "b9613c9f-7b80-40f5-8734-47af63e52d78",
      "fileAssetId": "7c3de87a-8770-4e66-bb35-9b44fbea2919",
      "fileName": "certificate-of-insurance.pdf",
      "mimeType": "application/pdf",
      "sizeBytes": 104448,
      "scanStatus": "Clean",
      "isPreviouslySubmitted": false,
      "documentName": "Certificate of insurance",
      "documentExpiresAt": "2027-04-30T00:00:00Z",
      "included": true,
      "zipEntryName": "REQ-20260717-BFED84/files/7c3de87a87704e66bb359b44fbea2919-certificate-of-insurance.pdf",
      "excludedReason": null
    }
  ]
}
```

Download full JSON export:

```http
GET /api/v2/submission-packages/{packageId}/export.json
```

Returns `PackageExportResponse`:

```json
{
  "package": {},
  "contents": {},
  "summary": {},
  "deliveries": [],
  "manualHandoffs": [],
  "exportedAt": "2026-07-17T02:05:00Z"
}
```

## Package Assignment and Archive

Assign owner/team:

```http
POST /api/v2/submission-packages/{packageId}/assign
```

Request:

```json
{
  "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "assignedTeamId": null
}
```

Response:

```json
{
  "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "assignedTeamId": null
}
```

Archive:

```http
POST /api/v2/submission-packages/{packageId}/archive
```

Response:

```json
{
  "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "packageReference": "REQ-20260717-BFED84",
  "status": "Archived",
  "artifact": "archive",
  "generatedAt": "2026-07-17T02:04:00Z",
  "url": "/api/v2/submission-packages/74fd5f68-0d87-40c9-9d63-28e95f760800/summary"
}
```

## Dashboard Summary Additions

`GET /v1/dashboard/summary` includes V2 package counts.

Response excerpt:

```json
{
  "packages": {
    "total": 12,
    "readyForHandoff": 3,
    "routingPending": 1,
    "delivered": 5,
    "partiallyDelivered": 1,
    "deliveryFailures": 1,
    "deliveryProblems": 2,
    "handoffConfirmed": 4,
    "archived": 2,
    "superseded": 1
  }
}
```

Use these for dashboard cards:

- Ready for handoff: `packages.readyForHandoff`
- Delivery problems: `packages.deliveryProblems`
- Archived count: `packages.archived`
- Delivered: `packages.delivered + packages.handoffConfirmed`

## Destinations

List destinations:

```http
GET /api/v2/destinations?page=1&pageSize=25&type=Webhook
```

Response:

```json
{
  "items": [
    {
      "id": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
      "name": "Operations webhook",
      "type": "Webhook",
      "status": "Active",
      "configuration": {
        "url": "https://example.com/reqara/webhooks"
      },
      "hasSecretConfiguration": true,
      "generatedSigningSecret": null,
      "isDefault": true,
      "isActive": true,
      "createdByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
      "createdAt": "2026-07-17T02:00:00Z",
      "updatedAt": "2026-07-17T02:00:00Z",
      "lastValidatedAt": "2026-07-17T02:01:00Z",
      "lastValidationStatus": "valid"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

Create destination:

```http
POST /api/v2/destinations
```

Update destination:

```http
PATCH /api/v2/destinations/{destinationId}
```

Get destination:

```http
GET /api/v2/destinations/{destinationId}
```

Disable destination:

```http
DELETE /api/v2/destinations/{destinationId}
```

Delete is a soft disable. It sets `isActive=false`, `status=Disabled`, and clears default.

Create/update request:

```json
{
  "name": "Operations webhook",
  "type": "Webhook",
  "configuration": {
    "url": "https://example.com/reqara/webhooks"
  },
  "secretConfiguration": {
    "signingSecret": "optional-custom-secret"
  },
  "status": "Active",
  "isDefault": true,
  "isActive": true
}
```

Secrets:

- `secretConfiguration` is encrypted at rest.
- Existing secrets are never returned.
- `hasSecretConfiguration` indicates whether a secret exists.
- If a Webhook destination is created without `secretConfiguration`, backend generates a signing secret.
- `generatedSigningSecret` is returned only on that create response. Show it once and ask the user to store it.

Webhook create response with one-time secret:

```json
{
  "id": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
  "name": "Operations webhook",
  "type": "Webhook",
  "status": "Active",
  "configuration": {
    "url": "https://example.com/reqara/webhooks"
  },
  "hasSecretConfiguration": true,
  "generatedSigningSecret": "rq_whsec_...",
  "isDefault": true,
  "isActive": true,
  "createdByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "createdAt": "2026-07-17T02:00:00Z",
  "updatedAt": "2026-07-17T02:00:00Z",
  "lastValidatedAt": null,
  "lastValidationStatus": null
}
```

Validate:

```http
POST /api/v2/destinations/{destinationId}/validate
```

Success:

```json
{
  "id": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
  "valid": true,
  "validatedAt": "2026-07-17T02:01:00Z"
}
```

Test:

```http
POST /api/v2/destinations/{destinationId}/test
```

Success `202`:

```json
{
  "id": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
  "sent": true,
  "lastValidatedAt": "2026-07-17T02:01:00Z"
}
```

### Destination Config Schemas

`ManualDownload`:

```json
{
  "instructions": "Download the ZIP and upload it to SharePoint.",
  "ownerTeam": "Operations"
}
```

`Email`:

```json
{
  "recipients": ["ops@example.com"],
  "cc": ["manager@example.com"]
}
```

Validation requires at least one recipient. Email destination messages notify recipients that a package is ready; submitted files are not attached.

`Webhook`:

```json
{
  "url": "https://example.com/reqara/webhooks",
  "eventTypes": ["package.delivery_started"]
}
```

Webhook delivery sends:

```http
X-Reqara-Event: package.delivery_started
X-Reqara-Timestamp: 1784253720
X-Reqara-Signature: sha256={hmac}
```

Webhook payload:

```json
{
  "id": "evt_9f0b2f52af42b2d6da1a3f51",
  "type": "package.delivery_started",
  "occurredAt": "2026-07-17T02:04:00Z",
  "organizationId": "f18ace48-e455-48ae-8170-01c2841b36c0",
  "data": {
    "packageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
    "packageReference": "REQ-20260717-BFED84",
    "requestId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
    "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18"
  }
}
```

`Sftp`:

```json
{
  "host": "sftp.example.com",
  "port": 22,
  "username": "reqara",
  "remotePath": "/incoming/reqara"
}
```

Secret example:

```json
{
  "password": "secret"
}
```

or:

```json
{
  "privateKey": "-----BEGIN PRIVATE KEY-----..."
}
```

`SharePoint`:

```json
{
  "siteUrl": "https://contoso.sharepoint.com/sites/hr",
  "driveId": "b!abc123",
  "folderPath": "/Reqara Packages"
}
```

Secret example:

```json
{
  "tenantId": "00000000-0000-0000-0000-000000000000",
  "clientId": "00000000-0000-0000-0000-000000000000",
  "clientSecret": "secret"
}
```

Current connector caveat:

- Email and Webhook have real delivery behavior.
- ManualDownload, CsvExport, JsonExport, Sftp, SharePoint, OneDrive, GoogleDrive, and ApiPull can be configured and tracked as destinations.
- Until dedicated connectors are completed, non-email and non-webhook delivery jobs are recorded as successful handoff/export records. UI should label them as "configured handoff" or "manual/export route", not "connected sync".

## Template Routing Rules

List rules:

```http
GET /api/v2/routing-rules
GET /api/v2/routing-rules?templateId={templateId}
```

Response:

```json
{
  "items": [
    {
      "id": "45a1f3b4-6f20-4500-b117-0d16ced74c40",
      "templateId": "ef5823dd-8d83-438a-934f-a21d44d42ed1",
      "destinationId": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
      "destinationName": "Operations webhook",
      "destinationType": "Webhook",
      "trigger": "OnAcceptance",
      "sequence": 1,
      "isRequired": true,
      "isAutomatic": true,
      "configurationOverrides": {},
      "createdAt": "2026-07-17T02:00:00Z",
      "updatedAt": "2026-07-17T02:00:00Z"
    }
  ]
}
```

Create:

```http
POST /api/v2/routing-rules
```

Patch:

```http
PATCH /api/v2/routing-rules/{ruleId}
```

Delete:

```http
DELETE /api/v2/routing-rules/{ruleId}
```

Create/update request:

```json
{
  "templateId": "ef5823dd-8d83-438a-934f-a21d44d42ed1",
  "destinationId": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
  "trigger": "OnAcceptance",
  "sequence": 1,
  "isRequired": true,
  "isAutomatic": true,
  "configurationOverrides": {}
}
```

There is no separate `enabled` field yet. Treat a saved rule as enabled and delete it to disable routing. If the UI needs a switch, implement it as create/delete for now.

## Delivery Jobs

Deliver package manually:

```http
POST /api/v2/submission-packages/{packageId}/deliver
```

Request:

```json
{
  "destinationIds": [
    "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1"
  ],
  "includeReport": true,
  "includeFiles": true,
  "idempotencyKey": "pkg-74fd5f68-send-ops-20260717"
}
```

If `destinationIds` is empty or omitted, backend uses active default destinations.

`includeReport` and `includeFiles` are accepted for future connector behavior. Current Email/Webhook delivery sends package metadata/notification, not submitted file attachments. Files should be retrieved through report, bundle, or JSON export endpoints.

Success `202`:

```json
{
  "items": [
    {
      "id": "ebc68083-c443-4601-b18e-06c6cfb9164c",
      "submissionPackageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
      "destinationId": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
      "destinationName": "Operations webhook",
      "destinationType": "Webhook",
      "status": "Succeeded",
      "attemptCount": 1,
      "maximumAttempts": 5,
      "requestedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
      "triggeredBy": "manual",
      "queuedAt": "2026-07-17T02:04:00Z",
      "startedAt": "2026-07-17T02:04:00Z",
      "completedAt": "2026-07-17T02:04:01Z",
      "nextRetryAt": null,
      "failureCode": null,
      "failureMessage": null,
      "externalReference": "Webhook",
      "idempotencyKey": "pkg-74fd5f68-send-ops-20260717:4f59ef5494224e66bbe12a09ef80e5c1"
    }
  ]
}
```

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
    "id": "ebc68083-c443-4601-b18e-06c6cfb9164c",
    "submissionPackageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
    "destinationId": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
    "destinationName": "Operations webhook",
    "destinationType": "Webhook",
    "status": "Succeeded",
    "attemptCount": 1,
    "maximumAttempts": 5,
    "requestedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
    "triggeredBy": "manual",
    "queuedAt": "2026-07-17T02:04:00Z",
    "startedAt": "2026-07-17T02:04:00Z",
    "completedAt": "2026-07-17T02:04:01Z",
    "nextRetryAt": null,
    "failureCode": null,
    "failureMessage": null,
    "externalReference": "Webhook",
    "idempotencyKey": "pkg-74fd5f68-send-ops-20260717:4f59ef5494224e66bbe12a09ef80e5c1"
  },
  "attempts": [
    {
      "id": "eb9e9f3f-2c33-40ce-b97d-14bb0aa65320",
      "attemptNumber": 1,
      "startedAt": "2026-07-17T02:04:00Z",
      "completedAt": "2026-07-17T02:04:01Z",
      "status": "Succeeded",
      "httpStatusCode": 200,
      "providerReference": null,
      "responseSummary": "OK",
      "failureCode": null,
      "failureMessage": null,
      "durationMilliseconds": 421
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

Frontend retry rules:

- Show Retry for `Failed`, `RetryScheduled`, and `RequiresAttention`.
- Backend rejects retry for `Succeeded` and `Cancelled`.
- Show Cancel for `Queued`, `Preparing`, `Sending`, `RetryScheduled`, `Failed`, and `RequiresAttention`.
- Backend rejects cancel for `Succeeded`.

## Manual Handoff

Create manual handoff:

```http
POST /api/v2/submission-packages/{packageId}/handoffs
```

Request:

```json
{
  "destinationLabel": "Dayforce employee profile",
  "externalReference": "DAYFORCE-EMP-100293",
  "notes": "Uploaded accepted package to employee documents.",
  "evidenceFileAssetId": null
}
```

Success `201`:

```json
{
  "id": "710e77bb-c6ff-4312-842f-39877d5a5f63",
  "submissionPackageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
  "destinationLabel": "Dayforce employee profile",
  "completedByUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
  "completedAt": "2026-07-17T02:10:00Z",
  "externalReference": "DAYFORCE-EMP-100293",
  "notes": "Uploaded accepted package to employee documents.",
  "evidenceFileAssetId": null
}
```

List handoffs:

```http
GET /api/v2/submission-packages/{packageId}/handoffs
```

Evidence files:

- `evidenceFileAssetId` is supported as a nullable field now.
- There is no dedicated V2 evidence upload flow yet.
- Hide evidence upload unless the dashboard already has a reusable secure admin file upload flow. Notes plus external reference are enough for the first V2 UI.

## Platform Admin Reporting

These endpoints are for platform console pages, not org dashboards.

### Platform Packages

```http
GET /v1/platform/packages
```

Supported query params:

```text
page=1
pageSize=25
status=Ready
organizationId={organizationGuid}
ownerUserId={userGuid}
assignedTeamId={teamGuid}
from=2026-07-01T00:00:00Z
to=2026-07-31T23:59:59Z
q=ryve
```

Response:

```json
{
  "items": [
    {
      "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
      "organizationId": "f18ace48-e455-48ae-8170-01c2841b36c0",
      "organizationName": "RyvePool",
      "packageReference": "REQ-20260717-BFED84",
      "versionNumber": 1,
      "status": "Ready",
      "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
      "requestTitle": "Vendor Onboarding",
      "publicReference": "act_5038097ed3084085c5b5",
      "submissionId": "c8904d39-1284-4dfa-8ef5-599068d28a18",
      "actionRecipientId": "6bc9e9e5-2d26-49a9-9a61-4f3d83e8627a",
      "recipientName": "Jamie Chen",
      "recipientEmail": "jamie@example.com",
      "acceptedAt": "2026-07-17T02:03:11Z",
      "generatedAt": "2026-07-17T02:04:00Z",
      "ownerUserId": "1e960383-bea0-4576-a21f-3b483c741d29",
      "ownerName": "Ryve Test",
      "assignedTeamId": null,
      "deliveryCount": 2,
      "failedDeliveryCount": 0,
      "latestDeliveryStatus": "Succeeded",
      "handoffCount": 1,
      "createdAt": "2026-07-17T02:03:11Z",
      "updatedAt": "2026-07-17T02:04:00Z"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

### Delivery Health

```http
GET /v1/platform/packages/delivery-health?from=2026-07-01T00:00:00Z&to=2026-07-31T23:59:59Z
```

Response:

```json
{
  "from": "2026-07-01T00:00:00Z",
  "to": "2026-07-31T23:59:59Z",
  "totalJobs": 20,
  "succeededJobs": 17,
  "retryScheduledJobs": 1,
  "requiresAttentionJobs": 2,
  "cancelledJobs": 0,
  "successRate": 85.0,
  "byStatus": [
    { "status": "Succeeded", "count": 17 },
    { "status": "RequiresAttention", "count": 2 }
  ],
  "byDestinationType": [
    { "destinationType": "Email", "count": 12 },
    { "destinationType": "Webhook", "count": 8 }
  ],
  "failureCodes": [
    { "code": "webhook_failed", "count": 2 }
  ],
  "attempts": {
    "total": 24,
    "succeeded": 19,
    "failed": 5,
    "successRate": 79.17
  },
  "recentProblems": [
    {
      "id": "ebc68083-c443-4601-b18e-06c6cfb9164c",
      "organizationId": "f18ace48-e455-48ae-8170-01c2841b36c0",
      "submissionPackageId": "74fd5f68-0d87-40c9-9d63-28e95f760800",
      "destinationId": "4f59ef54-9422-4e66-bbe1-2a09ef80e5c1",
      "destinationName": "Operations webhook",
      "destinationType": "Webhook",
      "status": "RequiresAttention",
      "failureCode": "webhook_failed",
      "failureMessage": "HTTP 500",
      "attemptCount": 5,
      "nextRetryAt": null,
      "queuedAt": "2026-07-17T02:04:00Z"
    }
  ]
}
```

### Destination Adoption

```http
GET /v1/platform/packages/destination-adoption?from=2026-07-01T00:00:00Z&to=2026-07-31T23:59:59Z
```

Response:

```json
{
  "from": "2026-07-01T00:00:00Z",
  "to": "2026-07-31T23:59:59Z",
  "totalDestinations": 10,
  "activeDestinations": 8,
  "defaultDestinations": 4,
  "organizationsWithDestinations": 3,
  "byType": [
    {
      "type": "Webhook",
      "total": 3,
      "active": 3,
      "defaults": 2,
      "organizations": 2
    }
  ],
  "deliveryVolumeByType": [
    {
      "type": "Webhook",
      "jobs": 8,
      "succeeded": 7,
      "failed": 1
    }
  ]
}
```

### Package Compliance

```http
GET /v1/platform/packages/compliance?from=2026-07-01T00:00:00Z&to=2026-07-31T23:59:59Z
```

Response:

```json
{
  "from": "2026-07-01T00:00:00Z",
  "to": "2026-07-31T23:59:59Z",
  "totalPackages": 12,
  "readyForHandoff": 3,
  "delivered": 5,
  "deliveryFailures": 1,
  "archived": 2,
  "superseded": 1,
  "packagesWithContentHash": 12,
  "packagesWithEmbeddedFileCandidates": 10,
  "cleanFiles": 18,
  "pendingFiles": 1,
  "rejectedFiles": 0,
  "packageDownloads": 8,
  "manualHandoffs": 4,
  "deliveryJobs": 20,
  "auditEvents": 46,
  "byStatus": [
    { "status": "Ready", "count": 3 },
    { "status": "Delivered", "count": 5 }
  ],
  "auditByType": [
    { "eventType": "package.created", "count": 12 },
    { "eventType": "delivery.queued", "count": 20 }
  ]
}
```

### Package Audit

```http
GET /v1/platform/packages/audit?page=1&pageSize=25&eventType=package.created
```

Supported query params:

```text
page=1
pageSize=25
organizationId={organizationGuid}
eventType=package.created
from=2026-07-01T00:00:00Z
to=2026-07-31T23:59:59Z
```

Response:

```json
{
  "items": [
    {
      "id": "ab3ebd9d-4820-4b3f-8e77-0ed0c9c9e92e",
      "organizationId": "f18ace48-e455-48ae-8170-01c2841b36c0",
      "organizationName": "RyvePool",
      "actionId": "5038097e-d308-4085-c5b5-9bb9c2c10aac",
      "actorType": "User",
      "actorId": "1e960383-bea0-4576-a21f-3b483c741d29",
      "eventType": "package.created",
      "eventData": {
        "id": "74fd5f68-0d87-40c9-9d63-28e95f760800",
        "packageReference": "REQ-20260717-BFED84",
        "versionNumber": 1,
        "status": "Ready"
      },
      "correlationId": "00-f696b7a62e34efde7fd293ac969d5e72-c946abe67c133198-00",
      "createdAt": "2026-07-17T02:03:11Z"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1,
  "from": "2026-07-01T00:00:00Z",
  "to": "2026-07-31T23:59:59Z"
}
```

## V2 Error Shapes

All errors use RFC7807:

```json
{
  "type": "https://docs.atlas.example/errors/state_conflict",
  "title": "Only accepted submissions can be packaged.",
  "status": 409,
  "code": "state_conflict",
  "traceId": "00-f696b7a62e34efde7fd293ac969d5e72-c946abe67c133198-00"
}
```

Current implemented codes:

```text
authentication_required
tenant_required
platform_auth_required
forbidden
not_found
validation_failed
state_conflict
destination_test_failed
plan_required
feature_not_available
limit_exceeded
```

Frontend-specific mapping:

| Scenario | Current code | Recommended UI copy |
| --- | --- | --- |
| Package not found | `not_found` | Package was not found or is no longer available. |
| Submission not accepted before packaging | `state_conflict` | Accept the submission before creating a package. |
| No active/default destination on deliver | `validation_failed` | Add or select an active destination before delivery. |
| Destination config invalid | `validation_failed` | Check the destination settings and try again. |
| Destination test failed | `destination_test_failed` | The test message could not be delivered. |
| Retry succeeded/cancelled delivery | `state_conflict` | This delivery can no longer be retried. |
| Cancel succeeded delivery | `state_conflict` | This delivery has already succeeded and cannot be cancelled. |
| Feature blocked by plan | `feature_not_available` or `plan_required` | Upgrade this workspace to use this route. |
| Limit exceeded | `limit_exceeded` | This workspace has reached its current plan limit. |

Reserved codes frontend can safely prepare for, but backend does not emit everywhere yet:

```text
package_not_ready
destination_not_configured
delivery_not_retryable
routing_rule_conflict
bundle_generation_failed
```

Until those are emitted precisely, map by HTTP status and current `code`.

## OpenAPI

Use the live OpenAPI document for field discovery and SDK generation:

```text
https://api.reqara.com/openapi/v1.json
```

The V2 routes are included in the same document.

## Frontend Implementation Checklist

- After `POST /v1/submissions/{id}/accept`, read `response.package.id` and `response.package.packageReference`.
- Add Packages navigation and table using `GET /api/v2/submission-packages`.
- Add package detail using `GET /api/v2/submission-packages/{packageId}`.
- Use `files[].fileName`, `mimeType`, and `scanStatus` for file preview labels.
- Add report PDF download from `/report`.
- Add ZIP download from `/bundle`; treat it as a true file bundle.
- Add JSON export from `/export.json`.
- Add Destinations settings page.
- Hide connector language for SFTP/SharePoint until dedicated connector delivery exists.
- Add Routing Rules inside template settings.
- Add Delivery History and Retry/Cancel controls.
- Add Manual Handoff modal with destination label, external reference, and notes.
- Add dashboard cards from `/v1/dashboard/summary.packages`.
- Add platform package, delivery health, destination adoption, compliance, and audit pages from `/v1/platform/packages/*`.
