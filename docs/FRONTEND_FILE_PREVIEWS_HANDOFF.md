# Frontend Handoff: Submitted File Previews

Production API origin:

```text
https://api.reqara.com
```

## Data Source

Use `GET /v1/submissions/{submissionId}` when opening the review modal.

The `files[]` payload now includes dashboard-friendly metadata:

```json
{
  "requirementId": "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
  "fileAssetId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
  "fileName": "SoloSuya_RyvePool_Vendor_Onboarding_Proposal.pdf",
  "originalFileName": "SoloSuya_RyvePool_Vendor_Onboarding_Proposal.pdf",
  "mimeType": "application/pdf",
  "sizeBytes": 104448,
  "scanStatus": "Clean",
  "isPreviouslySubmitted": false,
  "documentName": null,
  "documentExpiresAt": null
}
```

Use:

- `fileName` for the visible file label.
- `mimeType` for icon and preview decisions.
- `sizeBytes` for the secondary label.
- `scanStatus` to disable preview/download unless it is `Clean`.
- `isPreviouslySubmitted`, `documentName`, and `documentExpiresAt` for “Previously Submitted” badges when present.

Do not show `Submitted file 1` unless `fileName` is empty.

## Preview/Download Flow

Only request the signed URL when the user clicks Preview or Download:

```text
GET /v1/files/{fileAssetId}/download-url
```

Response:

```json
{
  "downloadUrl": "https://reqara-bucket...X-Amz-Signature=...",
  "expiresAt": "2026-07-17T17:10:00Z"
}
```

Treat the URL as short-lived and do not cache it beyond `expiresAt`.

## Browser Preview Rules

Preview inline when:

- `mimeType === "application/pdf"`
- `mimeType.startsWith("image/")`
- `mimeType.startsWith("text/")`

For other MIME types, show:

```text
This file type may not preview in the browser. Use Download when you are ready to open it.
```

Recommended behavior:

- Preview opens a modal/iframe/new tab using the signed URL.
- Download uses the same signed URL and allows the browser/content-disposition to save the file.
- If the signed URL returns expired/403, request a fresh URL and retry once.

## Error States

If `GET /v1/files/{fileAssetId}/download-url` returns:

- `409 file_not_available`: file is not clean yet. Show “File is still being scanned or was rejected.”
- `404 not_found`: file was removed or is outside this organization.
- `401/403`: refresh session or show a permission message.
