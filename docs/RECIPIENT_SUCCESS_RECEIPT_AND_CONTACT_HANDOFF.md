# Recipient Success Receipt And Contact Handoff

Base URL: `https://api.reqara.com`

## Success Page Flow

After `POST /v1/recipient/submit` returns `201`, show the success page using the response immediately, then refresh from `GET /v1/recipient/receipt` if you want the fuller receipt details.

Do not show raw JSON to the recipient.

Use these fields:

- `receiptReference`: primary visible reference.
- `publicReference`: internal/action reference fallback only. Prefer `receiptReference` everywhere recipient-facing.
- `submittedAt`: submitted timestamp.
- `organizationName`: organization that received the checklist.
- `checklistTitle`: submitted checklist name.
- `verificationFingerprint`: short proof fingerprint for support.

## Submit Response Additions

`POST /v1/recipient/submit` now includes:

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-17T14:30:00Z",
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924...",
  "publicReference": "REQ-20260717-090A18",
  "receiptReference": "REQ-20260717-090A18",
  "verificationFingerprint": "E3B0-C442-98FC-1C14"
}
```

## Full Receipt JSON

`GET /v1/recipient/receipt`

Use this for rendering the success page.

```json
{
  "id": "dddddddd-dddd-dddd-dddd-dddddddddddd",
  "checklistId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "publicReference": "REQ-20260717-090A18",
  "receiptReference": "REQ-20260717-090A18",
  "checklistTitle": "Vendor onboarding",
  "organizationName": "RyvePool",
  "recipientName": "Jamie Chen",
  "recipientEmail": "jamie@example.com",
  "versionNumber": 1,
  "status": "Submitted",
  "submittedAt": "2026-07-17T14:30:00Z",
  "declarationAcceptedAt": "2026-07-17T14:30:00Z",
  "requirementCount": 5,
  "responseCount": 3,
  "fileCount": 2,
  "previouslySubmittedFileCount": 1,
  "contentHash": "e3b0c44298fc1c149afbf4c8996fb924...",
  "verificationFingerprint": "E3B0-C442-98FC-1C14"
}
```

## Download Receipt

Primary download:

`GET /v1/recipient/receipt.pdf`

Returns:

- `200 application/pdf`
- backend-generated branded receipt
- human-readable receipt number and verification code
- no raw `act_...` machine reference
- no full content hash
- filename `{receiptReference}-receipt.pdf`

Fallback download:

`GET /v1/recipient/receipt.txt`

Use this only if PDF download fails or if the user explicitly requests a text receipt.

## Contact Organization

Use the relay endpoint instead of exposing sender or staff emails.

`POST /v1/recipient/contact-organization`

```json
{
  "subject": "Question about my submission",
  "message": "I noticed my insurance document expires soon. Should I upload the new one now?"
}
```

`202`:

```json
{
  "accepted": true,
  "sentAt": "2026-07-17T14:36:00Z"
}
```

Frontend behavior:

- Show a small modal or inline form with subject and message.
- Message is required and max 2,000 characters.
- Subject is optional and max 120 characters.
- On `202`, show "Message sent. The organization can reply by email."
- Do not show any staff email address.

Errors:

- `401 recipient_session_required`: ask recipient to reopen the secure link.
- `403 otp_required`: send recipient to OTP verification.
- `409 contact_not_configured`: show "Please reply to the original email from the sender."
- `422 validation_failed`: show field-level validation.
- `429 rate_limited`: show "Please wait before sending another message."
- `503 email_send_failed`: show retry guidance.
