# ScanMe

<p align="center">
  <img src="docs/releases/assets/1.0.16.0/hauptfenster.png" width="800" alt="The ScanMe main window" />
  <br/>
  <i>Scan settings on the left, the pages in the middle, the document list and its inspector on the right</i>
</p>

ScanMe is a Windows scanning application for archiving paper. It splits a scanned stack into documents
at the barcodes printed on their cover sheets, files each document under the barcode that separated it,
and uploads it to SharePoint and to the SAP archive over ArchiveLink.

It is a fork of [NAPS2](https://www.naps2.com), whose scanning pipeline it builds on; the document
separation, the SharePoint upload and the SAP ArchiveLink upload are ScanMe's own.

## What it does

**Scanning.** WIA, TWAIN and ESCL (network) scanners, saved as PDF, TIFF, JPEG or PNG, optionally with
optical character recognition. A profile holds the device, paper source, page size, resolution and bit
depth, so a recurring job starts with one click.

**Barcode document separation.** A stack is split at the barcodes on its cover sheets — Code 39,
Code 128, EAN/UPC — or at patch-T separator sheets, which are reusable cards and never become part of a
document. A repeated barcode *continues* the document instead of starting a copy of it: the papers of
one process order carry the order number on every cover sheet they contain, and a stack of several
orders is still split where the number changes. Where a sheet carries more than one barcode, a regular
expression decides which one is meant.

**Filing and archiving.** The barcode that separated a document is the value it is filed under: it names
the file, decides the SharePoint folder and supplies the SAP object key, so the three cannot drift apart.
Saving locally, uploading and the trigger are three separate settings, so "keep nothing locally, upload
on the button" is a combination you can simply select. A document whose identification is missing is held
back rather than filed under a stand-in name.

**Check and correct before archiving.** The document list shows every document of the session with its
status, and the inspector beside it shows the detected barcodes as a choice, plus a field for your own
value. A correction made there reaches the file name, the SharePoint folder and the SAP object key
together. A failed upload is not a dead end — the document stays in the list and can be retried, and if
one target is down the other is still served.

**Diagnostics.** A console logs every step from scanning through barcode detection and separation to
upload, *including when a step deliberately does nothing*. That is the failure operators actually hit:
not the crash, but the step that quietly has no effect.

**Interface.** German, drawn in the Windows 11 Fluent style, with light and dark mode and the user's
chosen accent colour.

## Download

[**Latest release**](https://github.com/upinblue/ScanMeX/releases/latest) — an MSI installer for 64-bit
Windows. Requires Windows 10 version 1809 (build 17763) or later.

Full release notes live in [`docs/releases/`](docs/releases/); [`CHANGELOG.md`](CHANGELOG.md) summarises
them.

## Building

You need the .NET 9 SDK. Build the individual projects rather than `ScanMe.sln` as a whole — the
solution still contains the macOS projects inherited from NAPS2, which need the `macos` workload
installed:

```bash
dotnet build NAPS2.App.WinForms
```

The installer is built with the NAPS2.Tools CLI and needs WiX Toolset v3.14. It publishes the
applications itself, so it is the only command required:

```bash
dotnet run --project NAPS2.Tools -- pkg msi
```

The result lands in `NAPS2.Setup/publish/<version>/`. [`CLAUDE.md`](CLAUDE.md) describes the path a scan
takes through the code, and the invariants that path depends on — worth reading before changing anything
around scanning, barcodes, separation or upload.

## Acknowledgements

The SAP ArchiveLink interface was shaped substantially by **Norbert Müller**, **Petra Michl** and
**Klaus Sichart** of Schwan Cosmetics International. Our thanks go to them.

ScanMe builds on [NAPS2](https://www.naps2.com) by Ben Olden-Cooligan and the NAPS2 contributors
(see [`CONTRIBUTORS`](CONTRIBUTORS)), and is drawn with Microsoft's
[Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT).

## License

ScanMe is licensed under the GNU GPL 2.0 (or later), as NAPS2 is. Some projects have additional license
options:
- NAPS2.Escl.* - GNU LGPL 2.1 (or later)
- NAPS2.Images.* - GNU LGPL 2.1 (or later)
- NAPS2.Internals - GNU LGPL 2.1 (or later)
- NAPS2.Sdk - GNU LGPL 2.1 (or later)
- NAPS2.Sdk.Samples - MIT
