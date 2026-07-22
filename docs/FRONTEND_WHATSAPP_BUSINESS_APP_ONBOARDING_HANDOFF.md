# WhatsApp Business App And Manual WhatsApp Frontend Handoff

Last updated: 2026-07-22

This handoff covers the new frontend work for people who have a WhatsApp number but do not already understand or manage WhatsApp Cloud API assets. It extends the existing Reqara Conversations module with three explicit connection modes:

- `CloudApi`: a normal Meta WhatsApp Business Platform / Cloud API number.
- `BusinessAppCoexistence`: an existing WhatsApp Business App number connected through Meta Embedded Signup/coexistence.
- `ManualOnly`: any WhatsApp or WhatsApp Business App number tracked manually with external WhatsApp handoff links.

Important product truth:

- A customer can message from any normal WhatsApp account.
- A Reqara inbox number can only sync live messages when it is API-backed: `CloudApi` or `BusinessAppCoexistence`.
- A personal WhatsApp Messenger number cannot become an API-backed Reqara inbox directly.
- `ManualOnly` is the fallback for people who only want to keep using WhatsApp/WhatsApp Business App without API setup. It does not provide live sync, delivery receipts, inbound webhooks, or provider sending.
- Provider sending is still not implemented. API-backed conversations can queue outbound messages as `Pending`; manual-only conversations must open WhatsApp externally and then log the sent reply.

## Permissions

Use existing dashboard auth and tenant header behavior.

Required scopes:

```text
whatsapp_connections.manage
conversations.create_manual
conversations.reply
conversations.view_all
conversations.view_assigned
```

Wildcard scopes still work:

```text
dashboard:*
admin:*
conversations:*
whatsapp_connections:*
*
```

All endpoints are still gated by:

```text
conversations.enabled
```

## Connection Modes

### CloudApi

Use when the org already has Meta API assets.

Frontend behavior:

- Ask for `phoneNumberId`, `wabaId`, `displayNumber`, optional `secretReference`, optional `webhookVerifyToken`.
- Show "Validate" action.
- Use normal conversation webhook and queued reply flows.

### BusinessAppCoexistence

Use when the user has an existing WhatsApp Business App number and wants to keep using the mobile app while Reqara receives API/webhook traffic.

Frontend behavior:

- Present this as "Connect existing WhatsApp Business App number".
- Run Meta Embedded Signup/coexistence in the frontend.
- After Meta returns the `phoneNumberId`, `wabaId`, and related signup identifiers, call `POST /api/v1/whatsapp-connections`.
- Show "Validate" action.
- Use normal conversation webhook and queued reply flows.

Do not present this for personal WhatsApp Messenger numbers.

### ManualOnly

Use when the user has any WhatsApp number but does not want, cannot complete, or is not eligible for API-backed setup.

Frontend behavior:

- Present this as "Track WhatsApp manually".
- Create a connection with only `mode` and `displayNumber`.
- Allow users to create manual conversations/leads from phone numbers.
- Reply button should open `wa.me`.
- After staff sends from WhatsApp/WhatsApp Business App, let them log the reply in Reqara.
- Clearly show "Manual tracking" status.

## UI Flow 1: Choose Setup Path

Route suggestion:

```text
/settings/whatsapp-connections/new
```

### Endpoint

```http
GET /api/v1/whatsapp-connections/onboarding-options
```

Sample response:

```json
{
  "webhookCallbackUrl": "https://api.reqara.com/api/webhooks/meta/whatsapp",
  "modes": [
    {
      "mode": "CloudApi",
      "label": "Connect a Meta Cloud API number",
      "description": "Use this for a number that already has WhatsApp Business Platform assets.",
      "requiresEmbeddedSignup": false,
      "supportsExistingBusinessApp": false,
      "supportsPersonalWhatsapp": false,
      "canReceiveWebhooks": true,
      "canSendViaApi": true,
      "requiresExternalReply": false,
      "requiredCreateFields": ["mode", "phoneNumberId", "wabaId", "displayNumber"],
      "unsupportedReason": null
    },
    {
      "mode": "BusinessAppCoexistence",
      "label": "Connect an existing WhatsApp Business App number",
      "description": "Use Meta Embedded Signup/coexistence so the customer can keep the Business App while Reqara receives API/webhook traffic.",
      "requiresEmbeddedSignup": true,
      "supportsExistingBusinessApp": true,
      "supportsPersonalWhatsapp": false,
      "canReceiveWebhooks": true,
      "canSendViaApi": true,
      "requiresExternalReply": false,
      "requiredCreateFields": ["mode", "phoneNumberId", "wabaId", "displayNumber"],
      "unsupportedReason": "Personal WhatsApp Messenger numbers are not eligible for this API-backed mode."
    },
    {
      "mode": "ManualOnly",
      "label": "Track any WhatsApp number manually",
      "description": "Use this when the person only wants to keep using WhatsApp or the WhatsApp Business App without connecting Meta API assets.",
      "requiresEmbeddedSignup": false,
      "supportsExistingBusinessApp": true,
      "supportsPersonalWhatsapp": true,
      "canReceiveWebhooks": false,
      "canSendViaApi": false,
      "requiresExternalReply": true,
      "requiredCreateFields": ["mode", "displayNumber"],
      "unsupportedReason": "No live message sync, delivery status, or provider sending is available in manual-only mode."
    }
  ]
}
```

UX rules:

- If the user says they use WhatsApp Business App, recommend `BusinessAppCoexistence` first.
- If they only have personal WhatsApp Messenger, recommend `ManualOnly` or ask them to move the number to WhatsApp Business App for API-backed setup.
- If Meta signup fails, offer `ManualOnly` fallback.

## UI Flow 2: Create Manual-Only Connection

Use this for people who only want manual tracking.

```http
POST /api/v1/whatsapp-connections
Content-Type: application/json
```

Request:

```json
{
  "mode": "ManualOnly",
  "displayNumber": "+14165550199",
  "onboardingSource": "frontend_manual_setup",
  "notes": "Owner uses WhatsApp Business App but does not want to connect Meta API yet."
}
```

Response `201`:

```json
{
  "id": "7c27dd8d-81f4-48d0-92b6-77c28a147f3d",
  "mode": "ManualOnly",
  "phoneNumberId": null,
  "wabaId": null,
  "displayNumber": "+14165550199",
  "status": "Active",
  "businessPortfolioId": null,
  "embeddedSignupSessionId": null,
  "externalAccountId": null,
  "historySyncRequested": false,
  "canReceiveWebhooks": false,
  "canSendViaApi": false,
  "requiresExternalReply": true,
  "manualReplyUrl": "https://wa.me/14165550199",
  "hasSecretReference": false,
  "verifiedAt": null,
  "connectedAt": "2026-07-22T01:08:14.222+00:00",
  "lastValidatedAt": null,
  "lastInboundAt": null,
  "lastOutboundAt": null,
  "createdAt": "2026-07-22T01:08:14.222+00:00",
  "updatedAt": "2026-07-22T01:08:14.222+00:00"
}
```

Validation errors:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Display number is required.",
  "status": 422,
  "code": "validation_failed"
}
```

Duplicate manual number:

```json
{
  "type": "https://docs.atlas.example/errors/duplicate_connection",
  "title": "This manual WhatsApp number is already connected.",
  "status": 409,
  "code": "duplicate_connection"
}
```

Do not show a Meta validate button for `ManualOnly`. If called anyway:

```json
{
  "type": "https://docs.atlas.example/errors/manual_validation_not_supported",
  "title": "Manual-only WhatsApp connections do not use Meta webhook validation.",
  "status": 422,
  "code": "manual_validation_not_supported"
}
```

## UI Flow 3: Create Business App Coexistence Connection

Use this after Meta Embedded Signup/coexistence completes in the browser.

```http
POST /api/v1/whatsapp-connections
Content-Type: application/json
```

Request:

```json
{
  "mode": "BusinessAppCoexistence",
  "phoneNumberId": "109876543210987",
  "wabaId": "123456789012345",
  "displayNumber": "+14165550144",
  "businessPortfolioId": "587004772991044",
  "embeddedSignupSessionId": "es_01J3Z9QF7RT3T6S4Q1V45V2T8Y",
  "externalAccountId": "acct_meta_587004772991044",
  "historySyncRequested": true,
  "secretReference": "meta/waba/coexistence/109876543210987",
  "webhookVerifyToken": "reqara-coexistence-verify-61f2d6",
  "onboardingSource": "embedded_signup",
  "notes": "Existing WhatsApp Business App number connected through coexistence."
}
```

Response `201`:

```json
{
  "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
  "mode": "BusinessAppCoexistence",
  "phoneNumberId": "109876543210987",
  "wabaId": "123456789012345",
  "displayNumber": "+14165550144",
  "status": "PendingVerification",
  "businessPortfolioId": "587004772991044",
  "embeddedSignupSessionId": "es_01J3Z9QF7RT3T6S4Q1V45V2T8Y",
  "externalAccountId": "acct_meta_587004772991044",
  "historySyncRequested": true,
  "canReceiveWebhooks": true,
  "canSendViaApi": true,
  "requiresExternalReply": false,
  "manualReplyUrl": "https://wa.me/14165550144",
  "hasSecretReference": true,
  "verifiedAt": null,
  "connectedAt": null,
  "lastValidatedAt": null,
  "lastInboundAt": null,
  "lastOutboundAt": null,
  "createdAt": "2026-07-22T01:15:42.411+00:00",
  "updatedAt": "2026-07-22T01:15:42.411+00:00"
}
```

Then call validate after webhook verification/setup is complete:

```http
POST /api/v1/whatsapp-connections/01bd774c-7a5b-4962-a2d1-38a830a83af7/validate
```

Response `200`:

```json
{
  "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
  "mode": "BusinessAppCoexistence",
  "phoneNumberId": "109876543210987",
  "wabaId": "123456789012345",
  "displayNumber": "+14165550144",
  "status": "Active",
  "businessPortfolioId": "587004772991044",
  "embeddedSignupSessionId": "es_01J3Z9QF7RT3T6S4Q1V45V2T8Y",
  "externalAccountId": "acct_meta_587004772991044",
  "historySyncRequested": true,
  "canReceiveWebhooks": true,
  "canSendViaApi": true,
  "requiresExternalReply": false,
  "manualReplyUrl": "https://wa.me/14165550144",
  "hasSecretReference": true,
  "verifiedAt": "2026-07-22T01:17:11.122+00:00",
  "connectedAt": "2026-07-22T01:17:11.122+00:00",
  "lastValidatedAt": "2026-07-22T01:17:11.122+00:00",
  "lastInboundAt": null,
  "lastOutboundAt": null,
  "createdAt": "2026-07-22T01:15:42.411+00:00",
  "updatedAt": "2026-07-22T01:17:11.122+00:00"
}
```

Validation error when API-backed IDs are missing:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Phone number ID, WABA ID and display number are required for API-backed WhatsApp connections.",
  "status": 422,
  "code": "validation_failed"
}
```

## UI Flow 4: Create Cloud API Connection

This is the existing flow, now with explicit mode.

```http
POST /api/v1/whatsapp-connections
Content-Type: application/json
```

Request:

```json
{
  "mode": "CloudApi",
  "phoneNumberId": "109876543210987",
  "wabaId": "123456789012345",
  "displayNumber": "+14165550144",
  "secretReference": "meta/waba/reqara-demo/phone-109876543210987",
  "webhookVerifyToken": "reqara-demo-verify-token-8a91d3",
  "onboardingSource": "admin_form"
}
```

Existing requests without `mode` still work and default to `CloudApi`.

## UI Flow 5: List Connections

```http
GET /api/v1/whatsapp-connections
```

Sample response:

```json
{
  "items": [
    {
      "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
      "mode": "BusinessAppCoexistence",
      "phoneNumberId": "109876543210987",
      "wabaId": "123456789012345",
      "displayNumber": "+14165550144",
      "status": "Active",
      "businessPortfolioId": "587004772991044",
      "embeddedSignupSessionId": "es_01J3Z9QF7RT3T6S4Q1V45V2T8Y",
      "externalAccountId": "acct_meta_587004772991044",
      "historySyncRequested": true,
      "canReceiveWebhooks": true,
      "canSendViaApi": true,
      "requiresExternalReply": false,
      "manualReplyUrl": "https://wa.me/14165550144",
      "hasSecretReference": true,
      "verifiedAt": "2026-07-22T01:17:11.122+00:00",
      "connectedAt": "2026-07-22T01:17:11.122+00:00",
      "lastValidatedAt": "2026-07-22T01:17:11.122+00:00",
      "lastInboundAt": "2026-07-22T01:28:43.000+00:00",
      "lastOutboundAt": null,
      "createdAt": "2026-07-22T01:15:42.411+00:00",
      "updatedAt": "2026-07-22T01:28:43.512+00:00"
    },
    {
      "id": "7c27dd8d-81f4-48d0-92b6-77c28a147f3d",
      "mode": "ManualOnly",
      "phoneNumberId": null,
      "wabaId": null,
      "displayNumber": "+14165550199",
      "status": "Active",
      "businessPortfolioId": null,
      "embeddedSignupSessionId": null,
      "externalAccountId": null,
      "historySyncRequested": false,
      "canReceiveWebhooks": false,
      "canSendViaApi": false,
      "requiresExternalReply": true,
      "manualReplyUrl": "https://wa.me/14165550199",
      "hasSecretReference": false,
      "verifiedAt": null,
      "connectedAt": "2026-07-22T01:08:14.222+00:00",
      "lastValidatedAt": null,
      "lastInboundAt": null,
      "lastOutboundAt": "2026-07-22T01:43:18.512+00:00",
      "createdAt": "2026-07-22T01:08:14.222+00:00",
      "updatedAt": "2026-07-22T01:43:18.512+00:00"
    }
  ],
  "total": 2
}
```

Connection list UI:

- Show mode badge.
- For `ManualOnly`, show "Manual tracking" and hide validate/webhook health actions.
- For `BusinessAppCoexistence`, show "Business App connected" once `status=Active`.
- For `CloudApi`, show "Cloud API".
- Use `requiresExternalReply` to decide composer behavior.

## UI Flow 6: Create Manual Conversation / Lead

Use this when staff wants to track a person who contacted them in WhatsApp outside Reqara, or when the org has a manual-only connection.

```http
POST /api/v1/conversations/manual
Content-Type: application/json
```

Request:

```json
{
  "connectionId": "7c27dd8d-81f4-48d0-92b6-77c28a147f3d",
  "customerPhone": "+14165559822",
  "customerName": "Adenike Okafor",
  "leadStatus": "New",
  "campaignRef": "instagram-bio",
  "assignedUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "assignedTeamId": "4a92d2e8-4f17-49ab-85c1-177f138d7c98",
  "initialNote": "Customer messaged the owner in WhatsApp Business App asking about lease documents."
}
```

Response `201`:

```json
{
  "conversationId": "5c501d33-49c7-45a8-9090-221dff234709",
  "connectionId": "7c27dd8d-81f4-48d0-92b6-77c28a147f3d",
  "contactId": "9938489a-8015-4f22-a06a-814222247084",
  "leadId": "288311f1-e6c0-4c58-94ea-cb3d9a6538dc",
  "normalizedPhone": "+14165559822",
  "contactName": "Adenike Okafor",
  "leadStatus": "Assigned",
  "assignedUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "assignedTeamId": "4a92d2e8-4f17-49ab-85c1-177f138d7c98",
  "waMeUrl": "https://wa.me/14165559822",
  "requiresExternalReply": true
}
```

Notes:

- `leadStatus` is promoted from `New` to `Assigned` when `assignedUserId` or `assignedTeamId` is provided.
- Terminal statuses `Won`, `Lost`, and `Closed` are rejected for manual conversation creation.
- Use `GET /api/v1/conversations/{conversationId}` after creation to load the full detail page.

Validation examples:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Customer phone is required.",
  "status": 422,
  "code": "validation_failed"
}
```

```json
{
  "type": "https://docs.atlas.example/errors/invalid_connection",
  "title": "This WhatsApp connection cannot accept manual conversations.",
  "status": 422,
  "code": "invalid_connection"
}
```

## UI Flow 7: Manual Reply And Log

For `ManualOnly` conversations, do not call the normal send endpoint as the primary action.

Recommended UI:

- Primary button: "Open WhatsApp".
- Open `waMeUrl` from manual conversation response or build from contact phone: `https://wa.me/{digits}`.
- Secondary action after sending: "Log reply".

If the frontend accidentally calls normal queued send:

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/messages
Content-Type: application/json
```

Request:

```json
{
  "body": "Hi Adenike, I can send the lease checklist now.",
  "type": "Text",
  "templateName": null,
  "clientMessageId": "web-20260722-014210-7dcf95fe"
}
```

Response `422`:

```json
{
  "type": "https://docs.atlas.example/errors/manual_reply_required",
  "title": "This connection is manual-only. Open WhatsApp externally and log the sent reply with the manual message endpoint.",
  "status": 422,
  "code": "manual_reply_required"
}
```

After staff sends in WhatsApp, log it:

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/manual-messages
Content-Type: application/json
```

Request:

```json
{
  "body": "Hi Adenike, for an apartment lease we usually need ID, proof of income, employment letter, and references. I can send a checklist now.",
  "sentAt": "2026-07-22T01:43:18.512+00:00",
  "clientMessageId": "manual-20260722-014318-7dcf95fe"
}
```

Response `201`:

```json
{
  "id": "982de8d7-cf57-4786-a3b7-fd6ec967d7a0",
  "providerMessageId": null,
  "direction": "Outgoing",
  "type": "Text",
  "body": "Hi Adenike, for an apartment lease we usually need ID, proof of income, employment letter, and references. I can send a checklist now.",
  "status": "Sent",
  "createdByUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "createdByUserName": null,
  "sentAt": "2026-07-22T01:43:18.512+00:00",
  "deliveredAt": null,
  "readAt": null,
  "failedAt": null,
  "failureCode": null,
  "createdAt": "2026-07-22T01:43:18.512+00:00",
  "updatedAt": "2026-07-22T01:43:18.512+00:00"
}
```

Validation:

```json
{
  "type": "https://docs.atlas.example/errors/validation_failed",
  "title": "Manual sent time cannot be in the future.",
  "status": 422,
  "code": "validation_failed"
}
```

## UI Flow 8: API-Backed Reply Behavior

For `CloudApi` and `BusinessAppCoexistence`, keep using the existing queued reply endpoint.

```http
POST /api/v1/conversations/5c501d33-49c7-45a8-9090-221dff234709/messages
Content-Type: application/json
```

Request:

```json
{
  "body": "Hi Adenike, I can send that checklist now.",
  "type": "Text",
  "templateName": null,
  "clientMessageId": "web-20260722-014512-7dcf95fe"
}
```

Response `202`:

```json
{
  "id": "18d2ea26-d1a3-4f16-bf2c-27547b8f75b9",
  "providerMessageId": null,
  "direction": "Outgoing",
  "type": "Text",
  "body": "Hi Adenike, I can send that checklist now.",
  "status": "Pending",
  "createdByUserId": "7dcf95fe-ed5e-4455-98fa-d6a3fcfc1991",
  "createdByUserName": null,
  "sentAt": null,
  "deliveredAt": null,
  "readAt": null,
  "failedAt": null,
  "failureCode": null,
  "createdAt": "2026-07-22T01:45:12.194+00:00",
  "updatedAt": "2026-07-22T01:45:12.194+00:00"
}
```

UX copy must say "queued" or "accepted", not "sent", until provider send is implemented.

## UI Flow 9: Inbound Webhooks

Webhook behavior is unchanged for API-backed modes. Meta sends `phone_number_id`; backend finds a non-disabled, non-revoked connection with that `phoneNumberId`.

`ManualOnly` connections never receive webhook events.

Sample inbound webhook still uses:

```http
POST /api/webhooks/meta/whatsapp
```

The resulting conversation detail now includes the expanded connection response:

```json
{
  "id": "5c501d33-49c7-45a8-9090-221dff234709",
  "status": "Open",
  "connection": {
    "id": "01bd774c-7a5b-4962-a2d1-38a830a83af7",
    "mode": "BusinessAppCoexistence",
    "phoneNumberId": "109876543210987",
    "wabaId": "123456789012345",
    "displayNumber": "+14165550144",
    "status": "Active",
    "businessPortfolioId": "587004772991044",
    "embeddedSignupSessionId": "es_01J3Z9QF7RT3T6S4Q1V45V2T8Y",
    "externalAccountId": "acct_meta_587004772991044",
    "historySyncRequested": true,
    "canReceiveWebhooks": true,
    "canSendViaApi": true,
    "requiresExternalReply": false,
    "manualReplyUrl": "https://wa.me/14165550144",
    "hasSecretReference": true,
    "verifiedAt": "2026-07-22T01:17:11.122+00:00",
    "connectedAt": "2026-07-22T01:17:11.122+00:00",
    "lastValidatedAt": "2026-07-22T01:17:11.122+00:00",
    "lastInboundAt": "2026-07-22T01:28:43+00:00",
    "lastOutboundAt": null,
    "createdAt": "2026-07-22T01:15:42.411+00:00",
    "updatedAt": "2026-07-22T01:28:43.512+00:00"
  }
}
```

## UI Scenario Matrix

| Scenario | Recommended mode | UI behavior |
| --- | --- | --- |
| Customer has a Meta Cloud API number | `CloudApi` | Ask for Meta IDs and verify token. |
| Customer uses WhatsApp Business App and wants to keep it | `BusinessAppCoexistence` | Launch Embedded Signup/coexistence; create connection after Meta returns IDs. |
| Customer uses WhatsApp Business App but refuses API setup | `ManualOnly` | Track leads manually, open WhatsApp externally, log sent replies. |
| Customer has personal WhatsApp Messenger only | `ManualOnly` | Explain no live sync; offer manual tracking or move to WhatsApp Business App. |
| Embedded Signup fails or user is ineligible | `ManualOnly` fallback | Preserve entered display number and continue manual setup. |
| Manual-only user tries to send from Reqara composer | Not allowed | Show "Open WhatsApp" and "Log reply" actions. |
| API-backed user sends before provider worker exists | Queue only | Show message as `Pending`; avoid sent/delivered copy. |

## Endpoint Summary

```http
GET    /api/v1/whatsapp-connections/onboarding-options
GET    /api/v1/whatsapp-connections
POST   /api/v1/whatsapp-connections
POST   /api/v1/whatsapp-connections/{connectionId}/validate
POST   /api/v1/conversations/manual
POST   /api/v1/conversations/{conversationId}/manual-messages
POST   /api/v1/conversations/{conversationId}/messages
GET    /api/v1/conversations
GET    /api/v1/conversations/{conversationId}
POST   /api/webhooks/meta/whatsapp
GET    /api/webhooks/meta/whatsapp
```

## Frontend Checklist

- Add a connection-mode picker.
- Add `BusinessAppCoexistence` copy and Meta Embedded Signup callback handling.
- Add `ManualOnly` setup form requiring only display number.
- Update connection table columns for `mode`, `requiresExternalReply`, `canReceiveWebhooks`, `connectedAt`.
- Hide validate action for manual-only connections.
- Add manual conversation create UI.
- Add manual composer state: open `wa.me`, then log reply.
- Guard normal send endpoint for manual-only conversations.
- Keep queued/pending copy for API-backed sends until provider send worker ships.
- Add failure fallback from Embedded Signup to manual-only setup.
- Add empty states explaining the difference between live sync and manual tracking.

## Backend Gaps To Keep Visible

- Provider send worker is not implemented yet.
- There is no backend endpoint to initiate Meta Embedded Signup; frontend handles Meta JS/redirect and posts completed IDs to Reqara.
- Manual-only mode has no inbound sync, delivery status, read receipts, or media sync.
- Personal WhatsApp Messenger can only be manually tracked unless the user moves to a supported WhatsApp Business setup.
