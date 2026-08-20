# ScanMe changelog

The full release notes for a version, in a form that can be handed to a customer, live under
[`docs/releases/`](docs/releases/). This file summarises them.

Before this file, the changelog of NAPS2 — the project ScanMe builds on — stood here. It remains
available at https://github.com/cyanfish/naps2/blob/master/CHANGELOG.md

---

## 1.1.0.0 — 20 August 2026

Full notes: [`docs/releases/1.1.0.0.md`](docs/releases/1.1.0.0.md)

- The pages in the scan window are grouped by the document they belong to, each section headed with the
  file it will be filed as, its page count and its status.
- A page can be moved into another document by dragging it there; documents can be split and merged from
  the context menu or with Ctrl+Shift+T and Ctrl+Shift+M. The identification follows the pages, and a
  value typed in by hand survives it.
- "Erledigte entfernen" clears the finished documents out of the list and their pages out of the window.
- Barcode detection can be restricted to one part of the page, drawn on a page in the profile settings.
  Off for every profile that already exists.
- Edits that are refused — pages of an archived document, moving pages between profiles — now say so
  instead of silently doing nothing.
- Only documents that reached an archive are locked; a save-only profile can still correct its pages, and
  a correction puts the document back into the queue and writes the corrected version next to the old one.
- A correction reaches the document list and the section heading as it is typed.
- Page edits made in the window reach the archived file.
- Fixed: changing the interface language killed the application.
- Fixed: a profile that only files locally reported a failed upload for a document that had been filed
  exactly as asked.
- Fixed: picking a detected barcode blanked the inspector and cleared the selection.
- Fixed: several ways a drag between documents landed the pages in the wrong one.

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
