# ScanMe

A fork of NAPS2 that adds document separation, SharePoint upload and SAP ArchiveLink upload on top of
the scanning pipeline. The upstream NAPS2 projects are unchanged in structure; the ScanMe-specific code
lives in `NAPS2.Lib/PostScan`, `NAPS2.Lib/Sap`, `NAPS2.Lib/SharePoint` and the `ScanMe.Sap` project.

## The path a scan takes

There is exactly one, and it is worth knowing by heart before changing anything downstream of scanning:

```
ScanPerformer            decides whether barcodes are decoded (ScanProfile.NeedsBarcodeValues)
  -> AutoSaver           splits into documents, resolves the file name, writes the PDF
     -> DocumentUploadService   one document -> SharePoint and/or SAP, aggregates the failures
        -> SharePointUploadService        (Microsoft Graph)
        -> SapArchivePostScanService      (ScanMe.Sap / HttpSapArchiveUploader)
     -> DocumentUploadQueue  holds manual-trigger documents and failed automatic uploads
        -> DocumentUploadController  the manual upload button
```

Autofac registers these explicitly in `CommonModule`; there is **no assembly scanning**, so a type that
nothing constructs by name is dead. A second, sink-based pipeline (`PostScanOrchestrator` plus
`AutoSaveSink`/`SharePointSink`/`SapArchiveSink`) once existed alongside this one, was never wired into
the container, and carried drifting copies of the barcode and path logic while its tests suggested the
behaviour was covered. It was deleted; don't reintroduce a parallel post-scan path. If the upload flow
needs to change, change `DocumentUploadService`.

---

## The diagnostic console — read this before changing scan, barcode, separation or upload code

There is a console window (toolbar button "Konsole" / "Console") that shows a running log of everything
that happens to a scan. It exists because the failure mode operators hit is not a crash — it is a step
that quietly does nothing, with no way to tell which one.

**Rule: every step in the scan → barcode → separation → save → upload chain must report itself to the
console, including when it decides to do nothing.**

If you add, move or change a step in that chain and it can succeed, fail, be skipped, or find nothing,
it needs a line in the console. A silent `return`, a `continue` past an empty result, or an `if` whose
else-branch does nothing are exactly the cases that must be logged. Reviewers should treat a new
early-return in this chain without a console line as an incomplete change.

### How to log

In `NAPS2.Lib` (and anything referencing it), use `NAPS2.Logging.ScanConsole` — it is a global using, so
no `using` is needed:

```csharp
ScanConsole.Scan("Page 3 received.");
ScanConsole.Barcode("Page 3: no barcode detected.");
ScanConsole.Document("Document 2: 4 page(s) -> 'C:\\Scans\\4711.pdf'");
ScanConsole.Upload("No upload target enabled for '4711.pdf'; the file stays local.");
ScanConsole.Profile("Auto save is disabled, so this scan is not saved or uploaded automatically.");
ScanConsole.App("ScanMe 1.0.5.0 started.");
```

Pick the category that matches what the line is about; they are only a prefix, but they make the console
readable. Write full sentences in English, name the file/page/document the line is about, and quote
values so an empty string is visibly empty.

In `NAPS2.Sdk` and other projects that cannot reference `NAPS2.Lib`, log through the injected
`ILogger` (usually `ScanningContext.Logger`) at Debug level. It is the same logger instance, so it ends
up in the same console.

### How it is wired

- `ScanConsole.*` writes to `Log.Logger` at **Debug** level.
- `NLogConfig.CreateLogger` registers `ConsoleLogTarget` with a `Trace`-level rule and **no filter**, so
  the console records everything regardless of the "enable debug logging" setting. The debug *file* is
  still gated by that setting.
- `ConsoleLogTarget` appends to `ConsoleLog`, a capped in-memory ring buffer (`ConsoleLog.MaxLines`).
  Nothing from the console is written to disk.
- `ConsoleForm` polls `ConsoleLog.ReadFrom(cursor)` on a UI timer. It deliberately does **not** subscribe
  to an event: logging happens on scanner and upload threads, and they must never block on the UI thread.
- Because the target sits in the NLog pipeline, plain `Log.Logger.LogInformation(...)` /
  `Log.ErrorException(...)` calls anywhere in the app already show up. `ScanConsole` exists to make the
  scan-specific events deliberate and greppable, not because the pipeline needs it.

### Do not

- Do not use `Debug.WriteLine` for diagnostics that matter. It is `[Conditional("DEBUG")]` and vanishes
  from the shipped Release/MSI build — which is the build operators actually run. This bit the SharePoint
  upload code once already.
- Do not log passwords, client secrets, tokens or full request bodies. `SharePointUploadService` masks
  tenant/client IDs and logs only the length of the secret; keep it that way.
- Do not log inside a tight per-percent or per-pixel loop. Log stage changes, not progress ticks.

---

## Archiving invariants

- **The barcode that separated a document is the value it is archived under.** It names the file *and*
  supplies the SAP object key (`SapObjectKeyResolver.FromScannedBarcodes` prefers it over the pages'
  primary barcodes). Deriving the key separately lets the file name and the archive key drift apart,
  which is unnoticeable after the fact — don't reintroduce a second derivation path.
- **A page's *primary* barcode is not "the" barcode of the page.** It is only the first decoded barcode
  matching the profile's symbologies, so on a sheet with several Code 39 codes it comes down to reading
  order. Whenever a regex is configured, it — not the primary — decides which barcode is meant, and the
  code has to look at `Barcode.GetAllValues()` rather than `DetectedText`. `DocumentSeparator`,
  `FileNamePlaceholders.$(barcode)` and `SapObjectKeyResolver` all follow this rule; keep it that way.
- **A repeated separator barcode continues the document; it does not start a copy of it.** The papers of
  one process order carry the order barcode on every cover sheet they contain — accompanying document,
  route cards, manufacturing instruction, storage slip — so splitting at each of them yields several
  files with identical names, which is indistinguishable from a duplicate scan afterwards.
  `DocumentWorkflowSettings.NewDocumentOnlyOnValueChange` (default on) is what prevents that, and a stack
  of several orders still splits where the order number changes. Patch-T sheets carry no value to compare
  and always separate.
- **A document that fails to upload goes into `DocumentUploadQueue`**, whether the trigger was manual or
  automatic, so it can be retried from the upload button. A failed upload must never be a dead end.
  `AutoSaverUploadTests` pins this down, along with the other half of it: a document that failed to
  *save* has nothing to upload and must not be queued either.
- **One target failing does not stop the other.** `DocumentUploadService` attempts every enabled target
  and joins the failures into one message, so a SharePoint outage still lets the document reach SAP.
  `UploadToSharePointAsync`/`UploadToSapAsync` are `protected virtual` purely so
  `DocumentUploadServiceTests` can drive that logic without a reachable tenant or gateway.
- **A staging file is only deleted once every target succeeded.** For a profile with `KeepLocalCopy` off
  it is the only copy of the scan, so removing it after a failure would destroy a document that never
  reached the archive.
- Uploads use `ShowBackgroundProgress`, not `ShowProgress`: a batch produces one upload per document and
  modal dialogs would block the window throughout.
- **Progress is reported through `InlineProgress<T>`, not `Progress<T>`.** An upload runs on a background
  thread with no synchronization context, so `Progress<T>` posts its callbacks to the thread pool: they
  can arrive out of order or after the upload has finished, which shows as a bar that jumps backwards or
  a finished upload still reading "uploading". Both the SharePoint and the SAP operation report inline.
- **An uploader that owns an `HttpClient` has to be disposed.** `HttpSapArchiveUploader` is created once
  per document, so `using` it is what keeps a batch from leaving one connection pool per scan open.

## Which barcode goes where

- **`$(barcode:1)` is the barcode the profile's regex accepts, not the first one on the page.**
  `BarcodeExtractor.SelectionPattern` orders the values before the per-page cap and then promotes the
  first match to the front of the document's list; the rest stay in reading order, which is what the
  higher indexes mean. The pattern comes from `ScanProfile.GetBarcodeSelectionPattern()` — the separation
  pattern, falling back to the SAP object key regex for profiles that archive without separating.
  `$(barcode)` differs on purpose: it is the document's identifying value, so the regex's capturing group
  has been applied. Both must always name the *same* barcode.
- **`ScanProfile.NeedsBarcodeValues()` is the single gate for barcode detection.** It is what
  `ScanPerformer.BuildOptions` asks, and it covers templates as well as separation: an auto save path of
  `$(barcode).pdf`, a SharePoint folder of `$(barcode)` or a SAP object id of `$(barcode)` needs the pages
  decoded just as much. Missing one of those doesn't fail — the placeholder expands to nothing and the
  document is filed under a name with a hole in it. Add every new template to `UsesBarcodePlaceholder`.

## SharePoint upload

Graph addresses a drive item by path as `root:/{folder}/{name}:/content` — **one** colon opens the path
expression and one closes it, with the folders and the file name forming a single path in between. Ending
the folder with its own colon (`root:/{folder}:/{name}:/content`) makes Graph read the file name as a
second path expression and reject the request, so uploads to the library root worked while uploads into a
subfolder failed. `SharePointUploadService.BuildUploadUrl` is the only place that builds this, and
`SharePointUploadUrlTests` pins both cases down.

When the configured library name matches no drive, the upload falls back to the first one rather than
failing. That is deliberate but silent-looking, so it logs a WARNING naming the libraries that do exist.

## Barcode detection

`BarcodeDetector.Detect` decodes every page **twice** — once at full size and once on a copy scaled to
60% — and merges the results. This is not belt-and-braces: a real supplier invoice in the customer's
samples (`Examples_of_Barcoded_Paperwork.pdf`, page 2, Code 39 `C10108930`) yields nothing at all at
2550×3300 and decodes cleanly on the smaller copy, while other pages need the full resolution for their
narrow bars. Neither pass dominates the other, so don't turn one into a fallback for the other.

The merge scales the smaller pass's coordinates back up before sorting, because the merged list is in
page reading order and its first entry becomes the page's primary barcode — an appended result would
silently change which barcode a profile without a regex picks.

The failure is a property of the whole page, not of the barcode: cropping the barcode out of that page
makes it decode at full resolution, and clean synthetic pages of any size always decode. There is
therefore **no unit test that fails without the second pass** — the merge logic is covered in
`MultiBarcodeDetectionTests`, and the end-to-end behaviour was verified against the sample PDFs, which
live outside the repo (customer documents, not committed).

## Localization

All user-visible strings go through `NAPS2.Lib/Lang/Resources/UiStrings.resx`, with a German translation
in `UiStrings.de.resx` and a hand-maintained property in `UiStrings.Designer.cs`. There is no separate
German/English switch in code — an earlier `SapUi` class did that and was removed.

Coverage is currently complete: every key in `UiStrings.resx` has a German counterpart. Keep it that way
when adding strings.

Note that **Debug builds only compile the fr/he/pt-BR satellite assemblies** (see the `EmbeddedResource
Remove`/`Include` block in `NAPS2.Lib.csproj`). German only appears in Release builds — a Debug build
showing English is not a bug.

## Versioning and release

- The single source of truth for the version is `NAPS2.Setup/targets/VersionTargets.targets`.
- The MSI is built with the `NAPS2.Tools` CLI (`dotnet run --project NAPS2.Tools -- pkg msi`), which
  needs WiX Toolset v3.14. Output lands in `NAPS2.Setup/publish/<version>/` and is named
  `ScanMe-<version>-<platform>.msi` — the `naps2-` prefix comes from `ProjectHelper.GetPackagePath`, which
  every packager and verifier routes through, so change it there and nowhere else.
- `pkg msi` publishes NAPS2.App.WinForms, NAPS2.App.Console and NAPS2.App.Worker itself, so it is all you
  need. Do **not** run `build msi` first on Windows: it runs `dotnet build -c Release-Msi` over the whole
  solution, which pulls in NAPS2.App.Mac and fails with NETSDK1147 unless the `macos` workload is
  installed. The same applies to building `ScanMe.sln` as a whole — build the individual projects.

## Tests

`dotnet test NAPS2.Lib.Tests` currently has 5 pre-existing failures unrelated to ScanMe changes
(`Naps2ConfigTests`, `CommandLineIntegrationTests.ScanPdfSettings_*`,
`DesktopControllerTests.Initialize_IfRun30DaysAgo_ShowsDonatePrompt`). `dotnet test NAPS2.Sdk.Tests`
likewise fails `PageSizeTests.InchesToString` and `PageSizeTests.CentimetresToString` on a German
locale, because they assume a `.` decimal separator. Compare against `master` before assuming a failure
is yours.

`NAPS2.Sdk.Tests.Remoting.*` (`ScanServerTests`, `TlsScanServerTests`, `FallbackScanServerTests`) is
**flaky**: it starts real servers and does mDNS discovery, and a different two or three of them fail on
each run, on `master` as well. Don't chase one of these unless it fails reproducibly.

A test fake that stands in for an HTTP transport has to drain `request.Content`
(`await request.Content.CopyToAsync(Stream.Null)`). Upload progress comes out of
`HttpContent.SerializeToStreamAsync`, which a handler that answers without reading the body never calls —
so the streaming looks untested and reports one event instead of many.
