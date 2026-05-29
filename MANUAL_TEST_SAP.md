# SAP ArchiveLink OData Manual Smoke Test

1. Open `Settings` and then `SAP-Verbindung`.
2. Enter a valid HTTPS SAP host, service name (`ZARCHIVE_UPLOAD_SRV`), client, language, user and password.
3. Click `Verbindung testen` and verify a green success indicator with a shortened CSRF token.
4. Repeat with an intentionally wrong password and verify the error message is understandable and no password is logged.
5. Create or edit a scan profile and enable `SAP ArchiveLink` upload.
6. Configure `ArchiveId` (for example `PS`) and barcode source `Fixed` with a known test barcode.
7. Scan and auto-save a PDF. Verify the operation log shows `SAP-Upload OK` with `DocId` and `Barcode`.
8. Configure barcode source `FromScannedBarcode`, scan a barcode sheet and verify the detected barcode is used. If no barcode is detected, verify there is no prompt fallback.
9. Upload a large PDF (> 5 MB) and verify the upload completes within the configured request timeout.
10. Disconnect the network during upload and verify retry behavior (1s, then 3s) and final error logging.
11. Simulate CSRF expiration, for example by waiting long enough between token fetch and upload, and verify one token refresh/retry happens on `403 CSRF token validation failed`.
12. Verify local files remain present after SAP upload failures, even when cleanup/delete-after-upload behavior is enabled elsewhere.
