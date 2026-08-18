# ScanMe changelog

The full release notes for a version, in a form that can be handed to a customer, live under
[`docs/releases/`](docs/releases/). This file summarises them.

Before this file, the changelog of NAPS2 — the project ScanMe builds on — stood here. It remains
available at https://github.com/cyanfish/naps2/blob/master/CHANGELOG.md

---

## 1.0.16.0 — 18 August 2026

First public release. Full notes: [`docs/releases/1.0.16.0.md`](docs/releases/1.0.16.0.md)

- Scanning from WIA, TWAIN and ESCL devices to PDF, TIFF, JPEG or PNG, optionally with OCR.
- Automatic document separation at Code 39, Code 128, EAN/UPC and patch-T separator sheets. A repeated
  barcode continues the document instead of starting a copy of it.
- The separating barcode is the value the document is filed under: it names the file, decides the
  SharePoint folder and supplies the SAP object key.
- Upload to SharePoint through Microsoft Graph and to the SAP archive through ArchiveLink. If one target
  is down, the other is still served.
- Document list with inspector: check and correct the identification before archiving. A failed upload
  stays in the list and can be retried.
- Diagnostic console logging every step — including when a step deliberately does nothing.
- German-language interface in the Windows 11 Fluent style, light and dark.
- The installer creates a Start menu entry again, names "up in blue GmbH" as the publisher, and shows
  ScanMe's licence.
- The About dialog was redesigned.
