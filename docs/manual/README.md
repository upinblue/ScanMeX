# ScanMe manuals

The user manual, in German and English:

| File | Language |
| --- | --- |
| [`ScanMe-Handbuch-de.md`](ScanMe-Handbuch-de.md) | German |
| [`ScanMe-Manual-en.md`](ScanMe-Manual-en.md) | English |

Both are written for the operator: chapters 2–12 are the daily work, and the profile, SharePoint and SAP
setup is an appendix. They read as they are on GitHub and convert to a typeset PDF with one command.

## Building the PDFs

```bash
pwsh docs/manual/Build-Pdf.ps1
```

The PDFs land in [`pdf/`](pdf/). Nothing has to be installed: the Markdown is converted by PowerShell 7's
own `ConvertFrom-Markdown` (Markdig), [`pdf/style.css`](pdf/style.css) is inlined, and the page is
printed by headless Edge, which is on every Windows machine.

| Switch | Effect |
| --- | --- |
| `-Language de` / `-Language en` | Build one of them instead of both. |
| `-KeepHtml` | Leave the intermediate HTML beside the Markdown, to check the layout in a browser. |
| `-PageNumbers` | Add a page footer — see the caveat below. |

**On page numbers.** A footer of our own ("ScanMe – Benutzerhandbuch" · "Seite x von y") needs the
DevTools protocol, because the `--print-to-pdf` command line has no way to set one. Some browser builds
refuse it: **Edge 151 answers `Page.printToPDF` with "Printing is not available"**, with no policy
involved, and the script then falls back to the browser's own header and footer — which does carry a page
number, but also the date and the full file path of the temporary HTML. That is why the default is no
footer at all. The manuals are cross-referenced by chapter number rather than by page, so nothing depends
on it. If you build on a machine where Chrome is installed, `-PageNumbers` produces the proper footer.

## Writing conventions

The Markdown deliberately stays plain, so that GitHub and the PDF show the same document. The only thing
the build adds is one HTML comment:

```markdown
<!-- pagebreak -->
```

It is invisible on GitHub and becomes a page break in the PDF. **Everything before the first one is the
cover page**, which is why the cover is wrapped in `<div align="center">` — an attribute GitHub keeps.

Beyond that:

* `# Heading` starts a new page in the PDF. One per chapter.
* A paragraph containing only an image becomes a figure; an all-italic paragraph directly beneath it
  becomes its caption.
* `> Quote` becomes a note box.
* Tall, narrow screenshots are capped at 170 mm high by the stylesheet, so a sidebar crop three times as
  high as it is wide does not get scaled to the text width and run off the page.

## The screenshots

`assets/de/` and `assets/en/` hold the same set of images, captured from the same demo in each language.
**They contain no customer data**: the scan was served by a simulated scanner over the local network,
the paperwork is drawn from scratch, and the order numbers (`PA4711001`…), the profile
(`Fertigungsaufträge` / `Production orders`) and the folder (`C:\Scans\…`) are invented.

To retake them, the release skill's screenshot section describes the setup: back up
`%APPDATA%\ScanMe\profiles.xml` and `config.xml`, write a demo profile of your own, serve pages over a
fake ESCL scanner, set `<Culture>` in `config.xml` to the language you are capturing, and use
`tools/setup/Capture-Window.ps1`. Never capture with the installed profiles — they upload to a real
tenant.
