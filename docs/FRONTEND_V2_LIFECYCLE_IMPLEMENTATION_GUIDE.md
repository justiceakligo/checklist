# Reqara V2 Frontend Implementation Guide

This guide defines the frontend product boundary and user flows for Version 2.

Detailed endpoint payloads live in:

```text
docs/FRONTEND_V2_PACKAGES_DESTINATIONS_HANDOFF.md
```

Production API:

```text
https://api.reqara.com
```

V2 API base:

```text
/api/v2
```

## Core Guardrail

Every V2 feature must support this lifecycle:

```text
Collect -> Review -> Accept -> Package -> Route -> Prove delivery -> Archive
```

Reqara should not replace SharePoint, Dayforce, Workday, an EMR, a CRM, payroll, or a document-management system.

Reqara exists to make sure what reaches those systems is:

- Complete
- Reviewed
- Organized
- Traceable
- Deliverable
- Auditable

Frontend copy, navigation, empty states, CTAs, dashboard metrics, and admin tools should protect this boundary.

Do not position Reqara as the final system of record. Position it as the trusted intake, review, packaging, routing, and delivery-evidence layer.

## Product Language

Use these terms consistently:

- Request: the checklist sent to a recipient.
- Submission: what the recipient submits.
- Review: the organization's decision step.
- Package: the accepted, organized, auditable output.
- Destination: where the package should go next.
- Delivery: an attempted send to a destination.
- Manual handoff: a human-confirmed transfer to an external system.
- Archive: the package is retained for audit but no longer active work.

Avoid these terms for V2:

- Inbox as final storage
- Permanent vault
- HRIS replacement
- EMR replacement
- CRM replacement
- Drive replacement
- Payroll replacement

## Frontend Navigation

Recommended org app navigation:

```text
Dashboard
Requests
Submissions
Packages
Destinations
Templates
Analytics
Settings
```

Recommended admin app navigation:

```text
Platform overview
Organizations
Revenue
Usage
Packages
Delivery health
Destinations
Templates
Audit log
Settings
```

## Lifecycle UI

### 1. Collect

This is the existing request/checklist flow.

User goal:

```text
Send a secure checklist to one or more recipients.
```

Primary screens:

- Template library
- Custom checklist builder
- Send checklist modal/page
- Recipient portal
- Request detail

Backend signals:

- Request/action created.
- Recipient link sent.
- Recipient draft autosaves.
- Recipient submits.

Existing endpoints remain under `/v1`.

UX guardrail:

Do not ask the org where the files should go before the request is even submitted unless they already use routing rules. Keep collect focused on getting a complete response.

### 2. Review

This is where the org verifies the submission.

User goal:

```text
Decide whether the submitted package is acceptable.
```

Primary CTAs:

- Accept
- Request changes
- Download file
- Preview file

Backend:

```http
GET /v1/submissions/{submissionId}
POST /v1/submissions/{submissionId}/accept
POST /v1/submissions/{submissionId}/request-changes
```

When the org accepts a submission, the backend automatically creates a V2 package.

UX guardrail:

Do not show routing/delivery as completed at this step. Acceptance means "reviewed and approved," not "sent to the downstream system."

### 3. Accept

Acceptance is the commitment point.

What acceptance means:

- The org reviewed the submission.
- The org considers it good enough to package.
- The accepted version becomes traceable.
- A package record is created.

What acceptance does not mean:

- The files were sent to Dayforce.
- The files were copied into SharePoint.
- The downstream system has received it.
- The package is archived.

Successful accept response still uses existing submission response shape. Frontend should then either:

1. Navigate to the new package detail if the package is found in package list/detail flow.
2. Show a "Next step" panel with package actions.
3. For old accepted submissions, call the backfill endpoint.

Backfill endpoint:

```http
POST /api/v2/submissions/{submissionId}/package
```

### 4. Package

Package is the V2 work object.

User goal:

```text
See the accepted package, files, summary, report, delivery history, and handoff status.
```

Primary screen:

```text
Package detail
```

Recommended tabs:

- Overview
- Files
- Deliveries
- Handoffs
- Audit

Core endpoints:

```http
GET /api/v2/submission-packages
GET /api/v2/submission-packages/{packageId}
GET /api/v2/submission-packages/{packageId}/summary
POST /api/v2/submission-packages/{packageId}/generate-report
GET /api/v2/submission-packages/{packageId}/report
POST /api/v2/submission-packages/{packageId}/generate-bundle
GET /api/v2/submission-packages/{packageId}/bundle
POST /api/v2/submission-packages/{packageId}/assign
```

Bundle behavior:

- `GET /api/v2/submission-packages/{packageId}/bundle` downloads a real ZIP with package JSON/text artifacts plus scan-clean submitted file bytes under `files/`.
- The ZIP also contains `file-manifest.json`. Use it as the source of truth for embedded vs skipped files.
- Skipped files have `included: false` and `excludedReason` such as `scan_pending`, `scan_rejected`, `storage_object_missing`, `max_file_count_exceeded`, or `max_zip_bytes_exceeded`.
- Admin settings control export size: `packageExport.maxFileCount` and `packageExport.maxZipBytes`.

Package statuses:

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

Recommended status labels:

- `Ready`: Ready for next step
- `RoutingPending`: Routing in progress
- `PartiallyDelivered`: Partially delivered
- `Delivered`: Delivered
- `DeliveryFailed`: Needs attention
- `HandoffConfirmed`: Handoff confirmed
- `Archived`: Archived
- `Superseded`: Older version

UX guardrail:

Package pages should answer "What is this, is it complete, who reviewed it, and where did it go?"

They should not become a general-purpose document drive.

### 5. Route

Routing is how packages move toward external systems.

User goal:

```text
Send or prepare the accepted package for the right downstream destination.
```

Routing can be:

- Manual: user clicks deliver or records a handoff.
- Automatic: template routing rule creates delivery jobs after acceptance.

Destination endpoints:

```http
GET /api/v2/destinations
POST /api/v2/destinations
GET /api/v2/destinations/{destinationId}
PATCH /api/v2/destinations/{destinationId}
DELETE /api/v2/destinations/{destinationId}
POST /api/v2/destinations/{destinationId}/validate
POST /api/v2/destinations/{destinationId}/test
```

Routing rule endpoints:

```http
GET /api/v2/routing-rules?templateId={templateId}
POST /api/v2/routing-rules
PATCH /api/v2/routing-rules/{ruleId}
DELETE /api/v2/routing-rules/{ruleId}
```

Delivery endpoint:

```http
POST /api/v2/submission-packages/{packageId}/deliver
```

Destination types:

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

Current production behavior:

- `Email` sends package summary and points users back to Reqara for secure files.
- `Webhook` sends package metadata and signs the payload.
- Other destination types can be configured and tracked as handoff/destination metadata, but native file push is not yet a full downstream connector.
- ZIP bundle currently contains manifest and signed file download URLs, not raw file bytes.

UX guardrail:

For systems like SharePoint, Dayforce, Workday, EMR, CRM, and payroll, say:

```text
Send package details
Record handoff
Configure destination
```

Do not say:

```text
Sync employee to Dayforce
Store in SharePoint forever
Update payroll record
Write EMR chart
```

unless a real verified connector exists.

### 6. Prove Delivery

Delivery proof is the key V2 differentiator.

User goal:

```text
Know whether the accepted package was delivered, retried, failed, or manually handed off.
```

Delivery history:

```http
GET /api/v2/submission-packages/{packageId}/deliveries
GET /api/v2/delivery-jobs/{jobId}
POST /api/v2/delivery-jobs/{jobId}/retry
POST /api/v2/delivery-jobs/{jobId}/cancel
```

Manual handoff:

```http
POST /api/v2/submission-packages/{packageId}/handoffs
GET /api/v2/submission-packages/{packageId}/handoffs
```

Delivery statuses:

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

Recommended UI for delivery evidence:

- Destination name
- Destination type
- Current status
- Attempt count
- Last attempt timestamp
- HTTP status, if webhook
- Failure code/message, if failed
- External reference, if known
- Retry button for failed/retryable jobs
- Cancel button for queued/retry-scheduled jobs
- Manual handoff evidence, if applicable

Manual handoff fields:

- Destination label
- External reference
- Notes
- Completed by
- Completed at
- Optional evidence file

UX guardrail:

The claim should be "Reqara proves what it sent or what a user confirmed was handed off."

Do not imply the external system accepted or processed the record unless the destination returned a success response or a human recorded that evidence.

### 7. Archive

Archiving closes the active work loop.

User goal:

```text
Remove completed package from active work while preserving audit history.
```

Endpoint:

```http
POST /api/v2/submission-packages/{packageId}/archive
```

Recommended behavior:

- Hide archived packages from default active lists.
- Keep archived packages searchable.
- Keep reports, manifests, delivery history, and handoffs visible.
- Do not delete underlying files from this action.
- Use retention/deletion policy separately.

UX guardrail:

Archive means "no longer active work." It does not mean "delete all evidence."

## Required Org Screens

### Package Inbox

Use:

```http
GET /api/v2/submission-packages
```

Filters:

- Status
- Template/request title
- Recipient name/email
- Accepted date
- Needs attention
- Archived

Default columns:

- Package reference
- Request title
- Recipient
- Status
- Accepted at
- Deliveries
- Handoffs
- Owner

Primary row action:

```text
Open package
```

### Package Detail

Use:

```http
GET /api/v2/submission-packages/{packageId}
```

Primary actions:

- Download report
- Download package manifest
- Send to destination
- Mark manual handoff
- Archive

Secondary actions:

- Assign owner
- Retry failed delivery
- Configure routing for template

### Destination Settings

Use:

```http
GET /api/v2/destinations
POST /api/v2/destinations
PATCH /api/v2/destinations/{destinationId}
DELETE /api/v2/destinations/{destinationId}
```

Show:

- Name
- Type
- Default status
- Active/disabled
- Last validation
- Test action

Webhook setup must show generated signing secret once.

### Template Routing

Use:

```http
GET /api/v2/routing-rules?templateId={templateId}
POST /api/v2/routing-rules
PATCH /api/v2/routing-rules/{ruleId}
DELETE /api/v2/routing-rules/{ruleId}
```

Recommended UI:

```text
When this template is accepted, route package to:
[Destination]
[Automatic toggle]
[Required toggle]
[Sequence]
```

Copy:

```text
Routing starts after review acceptance, so downstream systems receive organized packages instead of unreviewed submissions.
```

## Required Admin Screens

### Platform Packages

Purpose:

```text
See package volume and operational health across organizations.
```

Show:

- Packages created
- Packages ready
- Routing pending
- Delivered
- Delivery failed
- Manual handoffs
- Archived

### Delivery Health

Purpose:

```text
Support staff can see delivery failures and help organizations resolve them.
```

Show:

- Failed delivery jobs
- Retry scheduled jobs
- Destination test failures
- Webhook failure codes
- Organizations with repeated failures

### Destination Adoption

Purpose:

```text
Understand which downstream workflows customers use.
```

Show:

- Email destinations
- Webhook destinations
- Manual handoffs
- SharePoint/Drive/SFTP configured destinations
- API/webhook feature adoption

### Audit And Compliance

Purpose:

```text
Prove the lifecycle happened.
```

Show audit events:

- Package created
- Package downloaded
- Report generated
- Bundle generated
- Delivery queued
- Delivery succeeded
- Delivery failed
- Delivery retried
- Delivery cancelled
- Handoff confirmed
- Package archived

## Next-Step Panel After Accept

After a submission is accepted, show:

```text
Package ready

This submission has been reviewed and organized into a package.

Next step:
[Download report]
[Send to destination]
[Record manual handoff]
[Configure automatic routing]
```

If delivery failed:

```text
Delivery needs attention

The package is accepted, but Reqara could not complete delivery to {destination}.

[View delivery history]
[Retry delivery]
[Record manual handoff]
```

If no destinations exist:

```text
No destinations configured

You can download the package now or set up destinations so accepted packages can be routed consistently.

[Download report]
[Create destination]
```

## Copy Rules

Use:

- "Package is ready"
- "Send package"
- "Record handoff"
- "Delivery succeeded"
- "Delivery needs attention"
- "Archived"
- "Accepted and organized"

Avoid:

- "Synced to HRIS"
- "Saved to SharePoint"
- "Updated payroll"
- "Stored permanently"
- "Delivered to final system"
- "Uploaded to EMR"

Unless there is verified connector evidence, the UI should say "handoff" or "delivery attempt," not "system updated."

## Metrics To Add

Org dashboard:

- Packages ready
- Packages delivered
- Packages needing attention
- Manual handoffs this period
- Average accepted-to-delivered time
- Delivery success rate
- Top destination

Admin dashboard:

- Total packages created
- Delivery success rate
- Delivery failure rate
- Packages by organization
- Packages by template/category
- Manual handoffs by destination label
- Webhook/API adoption
- Failed destinations by organization

## Frontend State Machine

Package-level state:

```text
Ready
  -> RoutingPending
  -> Delivered
  -> PartiallyDelivered
  -> DeliveryFailed
  -> HandoffConfirmed
  -> Archived
```

Delivery-level state:

```text
Queued
  -> Sending
  -> Succeeded
  -> RetryScheduled
  -> RequiresAttention
  -> Cancelled
```

Review-level state:

```text
Submitted
  -> Accepted
  -> ChangesRequested
```

Important:

Accepted submission state and delivered package state are different. The UI should never collapse them into one generic "Complete" state.

## Error Handling

All API errors use RFC7807:

```json
{
  "type": "https://docs.atlas.example/errors/state_conflict",
  "title": "Only accepted submissions can be packaged.",
  "status": 409,
  "code": "state_conflict",
  "traceId": "00-..."
}
```

Common frontend responses:

- `401`: redirect/sign in or restore org context.
- `403`: show permission message.
- `402`: show upgrade CTA.
- `404`: show missing/deleted resource.
- `409`: refresh state and explain the action no longer applies.
- `422`: show field-level validation if possible.
- `502`: destination/webhook/email provider failed.

## Implementation Priority

Phase 1:

- Package inbox
- Package detail
- Report download
- Bundle manifest download
- Manual handoff
- Delivery history
- Retry failed delivery

Phase 2:

- Destination settings
- Destination test/validate
- Template routing rules
- Post-accept next-step panel

Phase 3:

- Org analytics cards
- Admin delivery health
- Admin package operations
- Webhook developer docs display

Phase 4:

- Native downstream connectors
- Rich bundle generation with file bytes
- Advanced routing conditions
- Benchmarking and vertical intelligence

## Final Boundary Check

Before shipping any V2 frontend feature, ask:

1. Does this help the user move from collect to archive?
2. Does this make the package more complete, reviewed, organized, traceable, deliverable, or auditable?
3. Does this avoid pretending Reqara is the downstream system of record?
4. Can the user prove what happened later?

If the answer to any of these is no, the feature needs another pass.
