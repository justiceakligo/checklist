# Reqara Conversations Frontend Implementation Handoff

Last updated: 2026-07-21

This document describes the frontend contract for the new Reqara Conversations module. The backend module is implemented as a tenant-scoped product area on the existing Reqara platform. It adds WhatsApp lead intake, conversation inboxes, lead assignment, internal notes, follow-ups, lead status changes, basic reporting, WhatsApp connection management, and Meta webhook ingestion.

Important current backend scope:

- Inbound Meta WhatsApp webhook processing is implemented.
- Outbound replies are accepted and stored as `Pending`, but the actual Meta send worker/client is not implemented yet.
- SignalR/realtime fanout is not implemented yet. Poll or refetch after mutations.
- Follow-up create/list is implemented through conversation detail, but complete/cancel follow-up endpoints are not implemented yet.
- Cross-module "create Reqara Request from lead" is not implemented yet.
- Webhook endpoints are backend/provider facing and are included for QA/reference, not normal frontend use.

## Auth, Tenancy, And Permissions

Use existing Reqara dashboard auth.

Browser requests must include credentials so the dashboard cookie is sent. If the user belongs to more than one organization, include:

```http
X-Atlas-Organization-Id: 7ef767d5-8b84-4ec8-9fb7-a5255ac7b340
```

The module is entitlement-gated by:

```text
conversations.enabled
```

If the organization is not entitled, endpoints return HTTP `402`.

Conversation permissions supported by the backend:

```text
conversations.view_all
conversations.view_assigned
conversations.reply
conversations.assign
conversations.update_status
conversations.add_note
conversations.manage_followups
whatsapp_connections.manage
conversations_reports.view
```

Existing dashboard/admin wildcard scopes also work:

```text
dashboard:*
admin:*
conversations:*
whatsapp_connections:*
*
```

Frontend behavior:

- If user can view all conversations, show all queues.
- If user can only view assigned conversations, default to `queue=mine` and avoid showing all-team counts.
- Hide write actions when the relevant permission is absent, but still handle HTTP `403`.
- Treat HTTP `401` as session missing/expired.
- Treat HTTP `402` as billing/module-not-enabled state.
- Treat HTTP `422` as validation state to show inline.

## Core Objects

### Lead Status

```text
New
Assigned
Contacted
Interested
FollowUp
Won
Lost
Closed
```

Recommended UI labels:

- `New`: New
- `Assigned`: Assigned
- `Contacted`: Contacted
- `Interested`: Interested
- `FollowUp`: Follow-up
- `Won`: Won
- `Lost`: Lost
- `Closed`: Closed

### Conversation Status

```text
Open
Closed
```

`Won`, `Lost`, and `Closed` lead statuses close the conversation automatically. Setting a non-terminal lead status reopens it.

### Message Direction

```text
Incoming
Outgoing
```

### Message Type

```text
Text
Image
Video
Audio
Document
Location
Unsupported
```

MVP outbound UI should send `Text` only.

### Message Status

```text
Received
Pending
Sent
Delivered
Read
Failed
```

Frontend status treatment:

- Incoming messages usually use `Received`.
- Outgoing messages created by the frontend return `Pending`.
- `Sent`, `Delivered`, `Read`, and `Failed` can be applied later by provider status webhooks once actual provider send is implemented.

### Follow-up Status

```text
Pending
Completed
Cancelled
```

The backend currently supports creating follow-ups and reading them in conversation detail. There is no complete/cancel endpoint yet.

### WhatsApp Connection Status

```text
PendingVerification
Active
Disabled
Invalid
Revoked
```

## Recommended Navigation

Add a top-level product nav item:

```text
Conversations
```

Suggested module routes:

```text
/conversations
/conversations/:conversationId
/conversations/reports
/settings/whatsapp-connections
```

Suggested left/inbox queues:

- New
- Mine
- Unassigned
- Follow-up
- Closed
- All open

Queue parameter values:

```text
new
mine
unassigned
follow-up
closed
```

Omit `queue` for all open conversations.

## UI Flow 1: Empty / Not Enabled

### Scenario

User opens Conversations but organization is not entitled.

### API

Any protected conversation endpoint may return:

```json
{
  "type": "https://docs.atlas.example/errors/feature_not_available",
  "title": "The conversations.enabled feature is not included in the current plan.",
  "status": 402,
  "code": "feature_not_available",
  "plan": {
    "code": "free",
    "name": "Free",
    "description": "Try Reqara on real requests.",
    "monthlyPriceCents": 0,
    "annualPriceCents": 0,
    "currency": "CAD",
    "monthlyChecklistLimit": 10,
    "storageBytes": 524288000,
    "customPricing": false,
    "features": {
      "teamWorkspace": true,
      "templateLibrary": true,
      "reqaraBranding": true,
      "customBranding": false,
      "automaticReminders": false,
      "apiAndWebhooks": false,
      "customWorkflows": false,
      "prioritySupport": false,
      "sso": false,
      "customRetention": false,
      "securityReview": false,
      "dpa": false,
      "dedicatedOnboarding": false
    }
  },
  "billing": {
    "planCode": "free",
    "billingCycle": "monthly",
    "status": "active",
    "currentPeriodStart": null,
    "currentPeriodEnd": null,
    "provider": "manual",
    "stripeCustomerId": null,
    "stripeSubscriptionId": null,
    "cancelAtPeriodEnd": false
  },
  "usage": {
    "periodStart": "2026-07-01T00:00:00+00:00",
    "periodEnd": "2026-08-01T00:00:00+00:00",
    "checklistsSent": 3,
    "monthlyChecklistLimit": 10,
    "checklistsRemaining": 7,
    "storageBytesUsed": 1823341,
    "storageBytesLimit": 524288000,
    "storageBytesRemaining": 522464659
  }
}
```

### UX

Show a module-not-enabled state with a billing/admin CTA if the current user can manage billing. Do not show a broken inbox.

## UI Flow 2: WhatsApp Connection Setup

Route: `/settings/whatsapp-connections`

Required permission:

```text
whatsapp_connections.manage
```

### List Connections

```http
GET /api/v1/whatsapp-connections
```

Sample response:

```json
{
  "items": [
    {
      "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
      "phoneNumberId": "109876543210987",
      "wabaId": "123456789012345",
      "displayNumber": "+14165550144",
      "status": "Active",
      "hasSecretReference": true,
      "verifiedAt": "2026-07-21T23:51:11.122+00:00",
      "lastValidatedAt": "2026-07-21T23:51:11.122+00:00",
      "lastInboundAt": "2026-07-21T23:57:43.000+00:00",
      "lastOutboundAt": "2026-07-21T23:59:12.194+00:00",
      "createdAt": "2026-07-21T23:49:42.411+00:00",
      "updatedAt": "2026-07-21T23:59:12.194+00:00"
    }
  ],
  "total": 1
}
```

### Create Connection

```http
POST /api/v1/whatsapp-connections
Content-Type: application/json
```

Request:

```json
{
  "phoneNumberId": "109876543210987",
  "wabaId": "123456789012345",
  "displayNumber": "+14165550144",
  "secretReference": "meta/waba/reqara-demo/phone-109876543210987",
  "webhookVerifyToken": "reqara-demo-verify-token-8a91d3"
}
```

Response `201`:

```json
{
  "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
  "phoneNumberId": "109876543210987",
  "wabaId": "123456789012345",
  "displayNumber": "+14165550144",
  "status": "PendingVerification",
  "hasSecretReference": true,
  "verifiedAt": null,
  "lastValidatedAt": null,
  "lastInboundAt": null,
  "lastOutboundAt": null,
  "createdAt": "2026-07-21T23:49:42.411+00:00",
  "updatedAt": "2026-07-21T23:49:42.411+00:00"
}
```

Validation errors:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Phone number ID, WABA ID and display number are required.",
  "status": 422,
  "code": "validation_failed"
}
```

Duplicate connection:

```json
{
  "type": "https://docs.atlas.example/errors/duplicate_connection",
  "title": "This WhatsApp phone number is already connected.",
  "status": 409,
  "code": "duplicate_connection"
}
```

### Validate Connection

This marks the connection active in the current backend. Use after setup or for an admin manual validation button.

```http
POST /api/v1/whatsapp-connections/01bd774c-7a5b-4962-a2d1-38a830a83af7/validate
```

Response `200`:

```json
{
  "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
  "phoneNumberId": "109876543210987",
  "wabaId": "123456789012345",
  "displayNumber": "+14165550144",
  "status": "Active",
  "hasSecretReference": true,
  "verifiedAt": "2026-07-21T23:51:11.122+00:00",
  "lastValidatedAt": "2026-07-21T23:51:11.122+00:00",
  "lastInboundAt": null,
  "lastOutboundAt": null,
  "createdAt": "2026-07-21T23:49:42.411+00:00",
  "updatedAt": "2026-07-21T23:51:11.122+00:00"
}
```

### UX Notes

Connection list columns:

- Display number
- WABA ID
- Phone number ID
- Status
- Last inbound
- Last outbound
- Last validated

Connection creation form:

- Phone number ID
- WABA ID
- Display number
- Secret reference
- Webhook verify token

Do not display or try to read back `webhookVerifyToken`. The backend only returns `hasSecretReference`.

## UI Flow 3: Inbox List

Route: `/conversations`

Required permission:

```text
conversations.view_all
```

or:

```text
conversations.view_assigned
```

### Endpoint

```http
GET /api/v1/conversations?queue=mine&page=1&pageSize=25
```

Filters:

```text
queue=new|mine|unassigned|follow-up|closed
assigneeId=7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991
status=Interested
search=phone-or-name
updatedAfter=2026-07-21T00:00:00Z
page=1
pageSize=25
```

Default behavior:

- If `queue` is omitted, backend returns open conversations.
- `queue=closed` returns closed conversations.
- `queue=follow-up` returns conversations with any pending follow-up.
- If user only has assigned visibility, backend limits results to `assignedUserId == current user`.

Sample request:

```http
GET /api/v1/conversations?queue=unassigned&search=ade&page=1&pageSize=25
```

Sample response:

```json
{
  "items": [
    {
      "id": "5c501d33-49c7-45a8-9090-221dff234709",
      "status": "Open",
      "leadStatus": "New",
      "connectionId": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
      "displayNumber": "+14165550144",
      "contactId": "9938489a-8015-4f22-a06a-814222247084",
      "normalizedPhone": "+14165559822",
      "contactName": "Adenike Okafor",
      "assignedUserId": null,
      "assignedUserName": null,
      "assignedTeamId": null,
      "unreadCount": 2,
      "lastMessageAt": "2026-07-21T23:57:43+00:00",
      "lastInboundMessageAt": "2026-07-21T23:57:43+00:00",
      "lastOutboundMessageAt": null,
      "lastMessagePreview": "Hi, I want to know what documents I need for an apartment lease.",
      "updatedAt": "2026-07-21T23:57:43.512+00:00"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 1
}
```

### UX Notes

Inbox list item should show:

- Contact display name, fallback to phone.
- Lead status badge.
- Assignment badge: assigned user, unassigned, or team.
- Last message preview.
- Last message timestamp.
- Unread count.
- WhatsApp number/display number if organization has multiple connections.

Polling recommendation until realtime exists:

- Refetch current queue every 15-30 seconds.
- Refetch immediately after any mutation.
- Keep selected conversation ID in URL so refresh/reconnect is stable.

## UI Flow 4: Conversation Detail

Route: `/conversations/:conversationId`

### Endpoint

```http
GET /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709
```

Sample response:

```json
{
  "id": "5c501d33-49c7-45a8-9090-221dff234709",
  "status": "Open",
  "connection": {
    "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
    "phoneNumberId": "109876543210987",
    "wabaId": "123456789012345",
    "displayNumber": "+14165550144",
    "status": "Active",
    "hasSecretReference": true,
    "verifiedAt": "2026-07-21T23:51:11.122+00:00",
    "lastValidatedAt": "2026-07-21T23:51:11.122+00:00",
    "lastInboundAt": "2026-07-21T23:57:43+00:00",
    "lastOutboundAt": null,
    "createdAt": "2026-07-21T23:49:42.411+00:00",
    "updatedAt": "2026-07-21T23:57:43.512+00:00"
  },
  "contact": {
    "id": "9938489a-8015-4f22-a06a-814222247084",
    "normalizedPhone": "+14165559822",
    "displayName": "Adenike Okafor",
    "profileJson": "{\"source\":\"whatsapp\"}"
  },
  "lead": {
    "id": "288311f1-e6c0-4c58-94ea-cb3d9a6538dc",
    "status": "New",
    "source": "whatsapp",
    "campaignRef": null,
    "wonAt": null,
    "lostReason": null,
    "createdAt": "2026-07-21T23:56:18.101+00:00",
    "updatedAt": "2026-07-21T23:57:43.512+00:00"
  },
  "assignedUserId": null,
  "assignedUserName": null,
  "assignedTeamId": null,
  "unreadCount": 2,
  "lastMessageAt": "2026-07-21T23:57:43+00:00",
  "lastInboundMessageAt": "2026-07-21T23:57:43+00:00",
  "lastOutboundMessageAt": null,
  "messages": [
    {
      "id": "3404a8dd-c7d5-4f22-baf9-b0ed3f84bcd1",
      "providerMessageId": "wamid.HBgLMTQxNjU1NTk4MjIVAgASGBQzQUNFNDhBM0M0NkI1ODczOAA=",
      "direction": "Incoming",
      "type": "Text",
      "body": "Hi, I want to know what documents I need for an apartment lease.",
      "status": "Received",
      "createdByUserId": null,
      "createdByUserName": null,
      "sentAt": "2026-07-21T23:56:18+00:00",
      "deliveredAt": null,
      "readAt": null,
      "failedAt": null,
      "failureCode": null,
      "createdAt": "2026-07-21T23:56:18+00:00",
      "updatedAt": "2026-07-21T23:56:18.101+00:00"
    },
    {
      "id": "18d2ea26-d1a3-4f16-bf2c-27547b8f75b9",
      "providerMessageId": "wamid.HBgLMTQxNjU1NTk4MjIVAgASGBQzQUU2NjcxNjRFN0FDNTE0OQA=",
      "direction": "Incoming",
      "type": "Text",
      "body": "The lease starts August 1.",
      "status": "Received",
      "createdByUserId": null,
      "createdByUserName": null,
      "sentAt": "2026-07-21T23:57:43+00:00",
      "deliveredAt": null,
      "readAt": null,
      "failedAt": null,
      "failureCode": null,
      "createdAt": "2026-07-21T23:57:43+00:00",
      "updatedAt": "2026-07-21T23:57:43.512+00:00"
    }
  ],
  "notes": [],
  "followUps": [],
  "assignments": [],
  "activities": [
    {
      "id": "5dff84d8-1901-4da8-a488-ef85471a6289",
      "type": "conversation.created",
      "actorType": "System",
      "actorId": null,
      "metadataJson": "{\"contactId\":\"9938489a-8015-4f22-a06a-814222247084\",\"leadId\":\"288311f1-e6c0-4c58-94ea-cb3d9a6538dc\",\"connectionId\":\"01bd774c-7a5b-4962-a2d1-38a830a83af7\"}",
      "createdAt": "2026-07-21T23:56:18.101+00:00"
    }
  ],
  "links": [],
  "createdAt": "2026-07-21T23:56:18.101+00:00",
  "updatedAt": "2026-07-21T23:57:43.512+00:00"
}
```

### Recommended Detail Layout

Main panel:

- Message timeline
- Reply composer
- Message delivery/read/failed indicators

Right panel:

- Contact card
- Lead status selector
- Assignment control
- Follow-up panel
- Internal notes
- Activity timeline
- Links section

Do not show internal notes inline in the customer message thread.

## UI Flow 5: Assignment And Reassignment

Required permission:

```text
conversations.assign
```

### Assign

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/assign
Content-Type: application/json
```

Request:

```json
{
  "userId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "teamId": null,
  "reason": "Adenike is asking about leasing documents; assigning to sales."
}
```

Response:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "assignedUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "assignedTeamId": null,
  "leadStatus": "Assigned"
}
```

### Assign To Team Only

Request:

```json
{
  "userId": null,
  "teamId": "4a92d2e8-4f17-49ab-85c1-177f138d7c98",
  "reason": "Routing to leasing team queue."
}
```

Response:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "assignedUserId": null,
  "assignedTeamId": "4a92d2e8-4f17-49ab-85c1-177f138d7c98",
  "leadStatus": "Assigned"
}
```

### Unassign

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/unassign
Content-Type: application/json
```

Request:

```json
{
  "reason": "Returning to unassigned queue during shift handoff."
}
```

Response:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "assignedUserId": null,
  "assignedTeamId": null,
  "leadStatus": "New"
}
```

Validation errors:

```json
{
  "type": "https://docs.atlas.example/errors/invalid_assignee",
  "title": "Assignee must be an active member of this organization.",
  "status": 422,
  "code": "invalid_assignee"
}
```

### UX Notes

Assignment modal:

- User dropdown
- Optional team dropdown
- Reason textarea

After mutation:

- Refetch conversation detail.
- Refetch visible queue counts/list.
- Show assignment history in the activity or assignment panel.

## UI Flow 6: Reply To Customer

Required permission:

```text
conversations.reply
```

### Send Text Reply

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/messages
Content-Type: application/json
```

Request:

```json
{
  "body": "Hi Adenike, for an apartment lease we usually need ID, proof of income, employment letter, and references. I can send a checklist now.",
  "type": "Text",
  "templateName": null,
  "clientMessageId": "web-20260721-235912-7dcf95fe"
}
```

Response `202`:

```json
{
  "id": "982de8d7-cf57-4786-a3b7-fd6ec967d7a0",
  "providerMessageId": null,
  "direction": "Outgoing",
  "type": "Text",
  "body": "Hi Adenike, for an apartment lease we usually need ID, proof of income, employment letter, and references. I can send a checklist now.",
  "status": "Pending",
  "createdByUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "createdByUserName": null,
  "sentAt": null,
  "deliveredAt": null,
  "readAt": null,
  "failedAt": null,
  "failureCode": null,
  "createdAt": "2026-07-21T23:59:12.194+00:00",
  "updatedAt": "2026-07-21T23:59:12.194+00:00"
}
```

If the 24-hour WhatsApp customer-service window has expired and no template name is provided:

```json
{
  "type": "https://docs.atlas.example/errors/message_template_required",
  "title": "The 24-hour WhatsApp customer-service window has expired. Send an approved template message.",
  "status": 422,
  "code": "message_template_required"
}
```

Template request shape is accepted by backend, but template send is not provider-implemented:

```json
{
  "body": "Hi Adenike, following up about your lease documents.",
  "type": "Text",
  "templateName": "lease_document_follow_up",
  "clientMessageId": "web-20260722-101500-7dcf95fe"
}
```

### UX Notes

Reply composer:

- Disable send for empty text.
- Show pending state after `202`.
- Do not mark as sent/delivered unless the backend later returns those statuses.
- If `message_template_required`, show template-required state instead of retrying freeform text.

Because actual provider sending is not yet implemented, avoid copy like "sent to WhatsApp" after this endpoint. Use "queued" or "accepted".

## UI Flow 7: Internal Notes

Required permission:

```text
conversations.add_note
```

### Create Note

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/notes
Content-Type: application/json
```

Request:

```json
{
  "body": "Customer is likely qualified. Mentioned lease start date and needs checklist today."
}
```

Response `201`:

```json
{
  "id": "2441def4-458d-4eb3-ae24-eb0c5a6e9af1",
  "authorUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "authorName": null,
  "body": "Customer is likely qualified. Mentioned lease start date and needs checklist today.",
  "createdAt": "2026-07-22T00:02:09.844+00:00"
}
```

Validation error:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Note body is required.",
  "status": 422,
  "code": "validation_failed"
}
```

### UX Notes

- Label notes clearly as internal.
- Do not put notes in the WhatsApp/customer message thread.
- After creating a note, refetch conversation detail so notes and activity are current.

## UI Flow 8: Follow-ups

Required permission:

```text
conversations.manage_followups
```

### Create Follow-up

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/follow-ups
Content-Type: application/json
```

Request:

```json
{
  "ownerUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "dueAt": "2026-07-22T15:00:00+00:00",
  "note": "Check whether she wants a lease checklist created."
}
```

Response `201`:

```json
{
  "id": "fc6773fd-bb9e-4e0d-8db3-790af8c22c35",
  "ownerUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "ownerName": null,
  "dueAt": "2026-07-22T15:00:00+00:00",
  "status": "Pending",
  "note": "Check whether she wants a lease checklist created.",
  "completedAt": null,
  "cancelledAt": null,
  "createdAt": "2026-07-22T00:04:30.330+00:00",
  "updatedAt": "2026-07-22T00:04:30.330+00:00"
}
```

Validation errors:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Follow-up due date must be in the future.",
  "status": 422,
  "code": "validation_failed"
}
```

```json
{
  "type": "https://docs.atlas.example/errors/invalid_owner",
  "title": "Follow-up owner must be an active member of this organization.",
  "status": 422,
  "code": "invalid_owner"
}
```

### UX Notes

- Use a date/time picker.
- Default owner to current user.
- Show pending follow-ups in the right panel.
- Queue `follow-up` includes conversations with pending follow-ups.
- Complete/cancel UI should be hidden or disabled until endpoints exist.

## UI Flow 9: Lead Status Progression

Required permission:

```text
conversations.update_status
```

### Update Status

```http
PATCH /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/lead-status
Content-Type: application/json
```

Request:

```json
{
  "status": "Interested",
  "lostReason": null
}
```

Response:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "previousStatus": "Assigned",
  "status": "Interested",
  "conversationStatus": "Open"
}
```

Mark won:

```json
{
  "status": "Won",
  "lostReason": null
}
```

Response:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "previousStatus": "Interested",
  "status": "Won",
  "conversationStatus": "Closed"
}
```

Mark lost:

```json
{
  "status": "Lost",
  "lostReason": "Customer already selected another provider."
}
```

Response:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "previousStatus": "Interested",
  "status": "Lost",
  "conversationStatus": "Closed"
}
```

Reopen after closed:

```json
{
  "status": "FollowUp",
  "lostReason": null
}
```

Response:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "previousStatus": "Closed",
  "status": "FollowUp",
  "conversationStatus": "Open"
}
```

### UX Notes

- Use a lead status dropdown or segmented status control.
- Require/show lost reason input when selecting `Lost`.
- Move `Won`, `Lost`, and `Closed` conversations out of open queues.
- After mutation, refetch detail and list.

## UI Flow 10: Reports Summary

Route: `/conversations/reports`

Required permission:

```text
conversations_reports.view
```

### Endpoint

```http
GET /api/v1/conversation-reports/summary?from=2026-07-01T00:00:00Z&to=2026-07-31T23:59:59Z
```

Sample response:

```json
{
  "from": "2026-07-01T00:00:00+00:00",
  "to": "2026-07-31T23:59:59+00:00",
  "newLeads": 42,
  "wonLeads": 9,
  "lostLeads": 5,
  "conversations": 42,
  "unassignedConversations": 6,
  "dueFollowUps": 3,
  "incomingMessages": 188,
  "outgoingMessages": 97,
  "leadsByStatus": {
    "New": 6,
    "Assigned": 7,
    "Contacted": 8,
    "Interested": 4,
    "FollowUp": 3,
    "Won": 9,
    "Lost": 5,
    "Closed": 0
  }
}
```

Default period:

- If `from`/`to` are omitted, backend returns the last 30 days.

### UX Notes

Recommended cards:

- New leads
- Won leads
- Lost leads
- Open conversations
- Unassigned
- Due follow-ups
- Incoming messages
- Outgoing messages

Recommended chart:

- Leads by status

## Backend/QA Flow: Meta Webhook Verification

Frontend usually does not call this. Include for admin setup docs or QA tools.

### Verify Webhook

```http
GET /api/webhooks/meta/whatsapp?hub.mode=subscribe&hub.verify_token=reqara-demo-verify-token-8a91d3&hub.challenge=837462
```

Success response:

```text
837462
```

Failure response:

```json
{
  "type": "https://docs.atlas.example/errors/forbidden",
  "title": "Webhook verify token was not accepted.",
  "status": 403,
  "code": "forbidden"
}
```

## Backend/QA Flow: Inbound WhatsApp Message Webhook

Frontend usually does not call this. This is how conversations are created.

```http
POST /api/webhooks/meta/whatsapp
Content-Type: application/json
X-Hub-Signature-256: sha256=<computed-hmac>
```

Sample inbound text payload:

```json
{
  "object": "whatsapp_business_account",
  "entry": [
    {
      "id": "123456789012345",
      "changes": [
        {
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "metadata": {
              "display_phone_number": "14165550144",
              "phone_number_id": "109876543210987"
            },
            "contacts": [
              {
                "profile": {
                  "name": "Adenike Okafor"
                },
                "wa_id": "14165559822"
              }
            ],
            "messages": [
              {
                "from": "14165559822",
                "id": "wamid.HBgLMTQxNjU1NTk4MjIVAgASGBQzQUNFNDhBM0M0NkI1ODczOAA=",
                "timestamp": "1784678178",
                "text": {
                  "body": "Hi, I want to know what documents I need for an apartment lease."
                },
                "type": "text"
              }
            ]
          }
        }
      ]
    }
  ]
}
```

Success response:

```json
{
  "received": true,
  "eventId": "8c85a8c6-cf8c-49a3-b5eb-82b8848213bb",
  "status": "Processed"
}
```

Duplicate response:

```json
{
  "received": true,
  "duplicate": true,
  "eventId": "8c85a8c6-cf8c-49a3-b5eb-82b8848213bb"
}
```

Ignored connection-not-found response:

```json
{
  "received": true,
  "ignored": true,
  "reason": "connection_not_found"
}
```

## Backend/QA Flow: Message Status Webhook

Sample delivered status payload:

```json
{
  "object": "whatsapp_business_account",
  "entry": [
    {
      "id": "123456789012345",
      "changes": [
        {
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "metadata": {
              "display_phone_number": "14165550144",
              "phone_number_id": "109876543210987"
            },
            "statuses": [
              {
                "id": "wamid.HBgLMTQxNjU1NTk4MjIVAgASGBQzQUNFNDhBM0M0NkI1ODczOAA=",
                "status": "delivered",
                "timestamp": "1784678334",
                "recipient_id": "14165559822"
              }
            ]
          }
        }
      ]
    }
  ]
}
```

Failure status payload:

```json
{
  "object": "whatsapp_business_account",
  "entry": [
    {
      "id": "123456789012345",
      "changes": [
        {
          "field": "messages",
          "value": {
            "messaging_product": "whatsapp",
            "metadata": {
              "display_phone_number": "14165550144",
              "phone_number_id": "109876543210987"
            },
            "statuses": [
              {
                "id": "wamid.HBgLMTQxNjU1NTk4MjIVAgASGBQzQUNFNDhBM0M0NkI1ODczOAA=",
                "status": "failed",
                "timestamp": "1784678334",
                "recipient_id": "14165559822",
                "errors": [
                  {
                    "code": "131047",
                    "title": "Re-engagement message",
                    "message": "More than 24 hours have passed since the recipient last replied."
                  }
                ]
              }
            ]
          }
        }
      ]
    }
  ]
}
```

## Common Error Responses

### Authentication Required

```json
{
  "type": "https://docs.atlas.example/errors/authentication_required",
  "title": "Dashboard authentication or an API key is required. Browser calls must include credentials so the dashboard cookie is sent.",
  "status": 401,
  "code": "authentication_required"
}
```

### Forbidden

```json
{
  "type": "https://docs.atlas.example/errors/forbidden",
  "title": "Conversation permission is required.",
  "status": 403,
  "code": "forbidden"
}
```

### Not Found

```json
{
  "type": "https://docs.atlas.example/errors/not_found",
  "title": "Conversation was not found.",
  "status": 404,
  "code": "not_found"
}
```

### Invalid Request Body

Malformed JSON returns:

```json
{
  "type": "https://docs.atlas.example/errors/invalid_request_body",
  "title": "Invalid request body.",
  "detail": "The request body could not be parsed or did not match the expected JSON contract.",
  "status": 400,
  "code": "invalid_request_body",
  "traceId": "0HN5GQE2T3UMG:00000001",
  "errors": [
    "Check Content-Type, JSON syntax, and required request fields."
  ]
}
```

### Rate Limited

```json
{
  "type": "https://docs.atlas.example/errors/rate_limited",
  "title": "Too many requests.",
  "status": 429,
  "code": "rate_limited"
}
```

## Frontend Implementation Checklist

### Settings

- Add WhatsApp Connections settings page.
- Show connection list.
- Add create connection form.
- Add validate action.
- Show connection health timestamps.

### Inbox

- Add conversations product nav.
- Add queue filters.
- Add search.
- Add pagination.
- Add unread badges.
- Handle assigned-only users.
- Poll/refetch until realtime exists.

### Detail

- Add message timeline.
- Add reply composer.
- Add lead status control.
- Add assignment control.
- Add notes panel.
- Add follow-up panel.
- Add activity timeline.
- Add links placeholder.

### Mutations

- Assign/reassign.
- Unassign.
- Queue text reply.
- Create internal note.
- Create follow-up.
- Update lead status.

### Reports

- Add date range picker.
- Add summary cards.
- Add leads-by-status chart/table.

### Empty States

- Module not enabled.
- No WhatsApp connection.
- No conversations in selected queue.
- Assigned-only user with no assigned conversations.
- Closed queue empty.

### Loading/Error States

- Skeleton list and detail.
- Inline form validation.
- Permission-denied state.
- Billing/module-disabled state.
- Network retry affordance.

## Endpoint Summary

```http
GET    /api/v1/conversations
GET    /api/v1/conversations/{conversationId}
POST   /api/v1/conversations/{conversationId}/assign
POST   /api/v1/conversations/{conversationId}/unassign
POST   /api/v1/conversations/{conversationId}/messages
POST   /api/v1/conversations/{conversationId}/notes
POST   /api/v1/conversations/{conversationId}/follow-ups
PATCH  /api/v1/conversations/{conversationId}/lead-status
GET    /api/v1/conversation-reports/summary
GET    /api/v1/whatsapp-connections
POST   /api/v1/whatsapp-connections
POST   /api/v1/whatsapp-connections/{connectionId}/validate
GET    /api/webhooks/meta/whatsapp
POST   /api/webhooks/meta/whatsapp
```

## Known Gaps For Frontend Planning

- No frontend-facing endpoint yet to complete/cancel follow-ups.
- No frontend-facing endpoint yet to create a Reqara Request from a qualified lead.
- No provider send worker yet for outgoing WhatsApp messages.
- No media upload/send endpoint for conversation attachments yet.
- No SignalR events yet.
- No user/team listing endpoint was added by this slice; use existing org member/team APIs if available in the frontend app.
- No platform-admin conversation diagnostics endpoint was added by this slice.
