# ScanMe

A fork of NAPS2 that adds document separation, SharePoint upload and SAP ArchiveLink upload on top of
the scanning pipeline. The upstream NAPS2 projects are unchanged in structure; the ScanMe-specific code
lives in `NAPS2.Lib/PostScan`, `NAPS2.Lib/Sap`, `NAPS2.Lib/SharePoint` and the `ScanMe.Sap` project.

## The path a scan takes

There is exactly one, and it is worth knowing by heart before changing anything downstream of scanning:

```
ScanPerformer            BarcodeDetectionPlan decides whether barcodes are decoded, and with what
                         restriction; refuses to decode rather than decode unrestricted
  -> DocumentPipeline    splits into documents, attaches their barcodes and identifying value,
     |                   puts them in the queue, then advances each as far as the profile allows
     |-> DocumentWriter          renders a document to a file *when it is needed*, from the
     |                          document's state at that moment
     |-> DocumentUploadService   one document -> SharePoint and/or SAP, aggregates the failures
     |      -> SharePointUploadService        (Microsoft Graph)
     |      -> SapArchivePostScanService      (ScanMe.Sap / HttpSapArchiveUploader)
     -> DocumentQueue    every document of the session, finished ones included
           -> DocumentPanel            the document list beside the pages
           -> DocumentUploadController the upload button, which calls DocumentPipeline.Advance
```

Autofac registers these explicitly in `CommonModule`; there is **no assembly scanning**, so a type that
nothing constructs by name is dead. A second, sink-based pipeline (`PostScanOrchestrator` plus
`AutoSaveSink`/`SharePointSink`/`SapArchiveSink`) once existed alongside this one, was never wired into
the container, and carried drifting copies of the barcode and path logic while its tests suggested the
behaviour was covered. It was deleted; don't reintroduce a parallel post-scan path.

### A document is an object, not a file

`ScannedDocument` lives from the moment a scan is split until it has reached everywhere the profile
sends it: its pages, its barcodes, the value it is filed under, its status. **The file is not part of
its identity.** It used to be — a document *was* the path auto save had already written — and that is
why a barcode corrected afterwards could not change the name the document was filed under, and why a
profile that only wanted to archive to SAP still had to nominate a folder first.

`DocumentWriter` produces the file on demand: into the profile's folder when it keeps one, otherwise
into a staging folder under `Paths.Temp` that is deleted once every target succeeded. Because the
`ScanContext` is rebuilt from the document at that moment, a correction made in the document list
reaches the file name, the SharePoint folder and the SAP object key together.

`DocumentPipeline.Advance` is the single method that takes a document further, used by both the
automatic trigger and the upload button, so the two cannot drift.

The panel is a list plus an inspector, not a card per document: the two answer different questions
("is everything through?" and "what is wrong with this one?"), and one control trying to answer both is
unreadable at panel width. In the inspector the detected barcodes are *radio buttons* -- the selected
one is the identification -- plus "own value" for free text. A "use as identification" button per row
said what would happen if you pressed it but never which barcode was actually in use.

**A document is dropped when its pages are deleted from the window, driven by what disappeared, not by
what is present.** A document exists the moment the scan finishes, while its pages are still on their
way into the window, so "has no page in the list" is briefly true for every document that was just
produced and would delete them all. Finished documents are never dropped -- they are the record that
those pages reached the archive.

### Saving, uploading and the trigger are three settings, not one

`DocumentWorkflowSettings` carries `SaveLocally` + `LocalFolder`, the upload targets (on the profile),
and `UploadTrigger` independently. "Keep nothing locally, upload on the button" is therefore a
combination that can simply be selected. `EnableAutoSave` and `AutoSaveSettings` still exist, are
written from the workflow on save, and are read only by the command line scanner — don't edit them
directly from the UI.

**`DocumentWorkflowSettings.Version` guards the migration.** A profile written before the split has no
`SaveLocally` in its file, and taking the deserialized `false` at face value would stop saving for a
profile that had been saving all along; `ForProfile` fills it in from the legacy settings and stamps the
version so it happens once. `SeparationMode.None` used to mean "ask the auto save separator", so a
stored `None` is re-read through the legacy separator — otherwise every one-file-per-page profile
silently starts merging a day's pages into one file. `DocumentWorkflowMigrationTests` pins all of this.

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
- **A document that fails stays in `DocumentQueue` as `Failed`**, whether the trigger was manual or
  automatic, so it can be retried from the upload button or from its own row in the document list. A
  failed upload must never be a dead end. `DocumentPipelineUploadTests` pins this down, along with the
  other half of it: a document that failed to *write* has nothing to upload and must not be uploaded.
- **A document whose identification is missing is held back, not filed under a stand-in name.** Only
  when the profile sets `RequireIdentifier`; the upload button then uploads the rest and says how many
  it left alone. A wrongly named document in an archive is not something anyone finds again.
- **One target failing does not stop the other.** `DocumentUploadService` attempts every enabled target
  and joins the failures into one message, so a SharePoint outage still lets the document reach SAP.
  `UploadToSharePointAsync`/`UploadToSapAsync` are `protected virtual` purely so
  `DocumentUploadServiceTests` can drive that logic without a reachable tenant or gateway.
- **A staging file is only deleted once every target succeeded.** For a profile with `SaveLocally` off
  it is the only copy of the scan, so removing it after a failure would destroy a document that never
  reached the archive. `ScannedDocument.DiscardStagingFile` only ever touches the temporary copy — a
  file in the operator's own folder is theirs.
- **Patch-T separator sheets are never part of the document.** They are reusable blank cards, so
  `KeepsSeparatorPage()` ignores `KeepSeparatorPage` for patch-T; the setting only means something for
  barcode separation, where the sheet carrying the order number is the document's own cover sheet.
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
- **One barcode regex, on the workflow.** It decides document boundaries, which barcode the variables
  yield, and -- through the document's identification -- the SAP object key, so all three necessarily
  agree. There was a second one on `SapArchiveSettings.BarcodeRegex`; a profile could then file under one
  barcode and name the file after another, and nothing afterwards showed the two had parted company.
  `ForProfile` folds the old value in for profiles that only ever set that one, and SAP's "from barcode"
  source now simply takes `ctx.DocumentId`.
- **The ArchiveLink object-type headers are not sent.** `ArObject` and `SapObject` were always optional,
  are never validated, and this installation's endpoint does not read them, so the profile dialog no
  longer offers them and saving a profile clears them. The properties stay on `SapArchiveProfileSettings`
  and `HttpSapArchiveUploader` still emits `x-sap-arobject`/`x-sap-sapobj` when something is set, so a
  profile carrying values from before keeps working -- and the console names them when it does, because
  a header going out with nowhere in the UI to see it is exactly the invisible setting to avoid.
- **`ScanProfile.NeedsBarcodeValues()` is the single gate for barcode detection.** It covers templates
  as well as separation: a file name of `$(barcode).pdf`, a SharePoint folder of `$(barcode)` or a SAP
  object id of `$(barcode)` needs the pages decoded just as much. Missing one of those doesn't fail —
  the placeholder expands to nothing and the document is filed under a name with a hole in it. Add every
  new template to `UsesBarcodePlaceholder`.
- **Detection is refused rather than run unrestricted.** `BarcodeDetectionPlan` gates it on an explicit
  symbology (or the patch-T path). With no restriction ZXing tries every format it knows, and ITF,
  Codabar and the EAN/UPC family decode the ruled tables and print noise of a real form into barcodes
  that are not on the paper — measured on a customer certificate carrying two Code 128 codes, noisy
  renderings yielded three to five, and one of the phantoms sorted ahead of the real codes and became
  the page's primary value. A profile that decodes nothing says so in the console on every scan; a
  phantom that names a file or an archive key is indistinguishable from a correct scan afterwards.
  `PhantomBarcodeTests` builds a form-shaped noisy page and pins the restricted direction.
- **EAN/UPC is the risky one.** Eight digits with no usable self-check, so it is the symbology that most
  readily reads out of noise. It stays selectable — some paperwork really carries an article code — but
  the profile dialog and the console both warn when it is on.

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

## Icons and theming

The icons come from [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (MIT)
and are **generated, not hand-placed**. `tools/icons/icon-map.tsv` maps each ScanMe icon name to a
Fluent one; `tools/icons/Generate-Icons.ps1` fetches the SVGs and renders them into `NAPS2.Lib/Icons`.
To change an icon, edit the mapping and re-run the script — don't drop a PNG in by hand, it will be
overwritten on the next run.

```bash
pwsh tools/icons/Generate-Icons.ps1
```

- **The script only writes names that already have an `Icons.resx` entry**, so it cannot create an
  icon nothing looks up. Adding a new icon therefore means adding the resx entry (and the
  `Icons.Designer.cs` property) first. The naming convention is `foo_small` → `Icons\foo-small.png`
  at 16px, `foo` → `Icons\foo.png` at 32px, `foo_hires` → `Icons\foo-hires.png` at 64px, and
  `foo_96` → `Icons\foo_96.png` at 96px (note the underscore — that is the existing convention for
  the oversized variants, which `DefaultIconProvider` serves when asked for `foo_48`).
- **The left column of the mapping is always the *base* name**, never a variant: the generator
  derives the variants from it and writes the ones that have a resx entry. An exact variant name on
  the left is an override for that one size, which is how `arrow_up` can be the upload arrow at 32px
  and a plain up arrow at 16px.
- **`name:filled` selects Fluent's filled style**, a bare name the regular one. Fluent draws state
  with the filled style, which is why the notification severity icons use it — at 16px a filled disc
  reads as a status badge where an outline reads as another button.
- **The generated PNGs must declare 192 dpi**, which the script does explicitly. This is not
  metadata hygiene: `ToolStripDoubleButton` (the stacked *Move up/Move down* and *Settings/About*
  buttons) paints with `Graphics.DrawImage(image, Point)`, the overload that sizes an image by its
  *physical* size — `graphicsDpi / imageDpi`. A 96 dpi icon draws at double size there on a 192 dpi
  screen while the label is still offset by the pixel width, so the text lands on top of the glyph.
  The rest of NAPS2's icons have always been 192 dpi; the generated ones match them.
  `ToolStripDoubleButton` now calls `SetResolution` on the bitmap to match the surface before painting,
  because the overload cuts both ways: it *halves* a 192 dpi icon on a 96 dpi screen, which is why
  Settings and About came out shrunken at 100% scaling and looked right at 200%. Matching the resolution
  makes the draw 1:1 in pixels at any scaling and covers the disabled path too, which has no size
  overload to use instead.
- **Icons are stored as black glyphs and tinted at load time** by `DefaultIconProvider`, which is
  what makes dark mode work. The exclusion list there (brand logos, `scanner_*`, `favicon`) mirrors
  the one at the top of `icon-map.tsv` — an icon that is regenerated is monochrome and belongs in
  neither list. `exclamation` is tinted with the caution colour rather than the text colour, because
  it only ever means an error state and would otherwise lose the signal the old coloured icon had.
- **`ColorScheme` is the only place a colour is chosen.** Its constants are Fluent 2 design tokens,
  named in comments after the WinUI resources they come from, so a value can be checked rather than
  guessed at. `AccentColor` follows the user's Windows accent (via `IDarkModeProvider.AccentColor`,
  a default interface member so Gtk/Mac need no implementation) and is nudged for contrast, since
  Windows lets you pick an accent that would vanish against the current surface.
- `FluentToolStripRenderer` draws the toolbars and menus; `DwmWindowStyle` opts windows into rounded
  corners and the dark title bar. Both attributes are Windows 11 only and fail silently on Windows
  10, which the app still targets (`net9.0-windows10.0.17763.0`).

## The Eto layout engine — three things that will bite you

The layout system in `NAPS2.Lib/EtoForms/Layout` is custom: `LayoutElement`s are measured and placed by
`LayoutController`, not by Eto's own containers. Three consequences, each of which cost a round of
"why is nothing showing":

- **`Control.Visible = false` does not stick.** The engine re-shows controls as it lays them out. To
  hide something, wrap it in `L.Column(x).Visible(vis)` with a `LayoutVisibility` and toggle that.
- **A label does not wrap on its own.** `DynamicWrap(width)` only sets the width it is *measured* at;
  without a `MaxWidth` as well it is laid out at its full natural width and runs off the edge of the
  panel. Long sentences need both, and `C.Secondary` never wraps at all -- it is `C.NoWrap` with a
  colour, so an explanatory line built with it is a line that will be cut off.
- **A label that starts empty has no height, and setting its text later doesn't give it any** until the
  layout is redone. Give warning labels their text up front and toggle their visibility, or call
  `LayoutController.Invalidate()` after changing the text.
- **Controls paint their own background.** On a tinted surface (a status card, a coloured panel) every
  label shows as a pale box unless its `BackgroundColor` is set to match — see `DocumentCardView`, which
  keeps a list of the controls it has to repaint when the card's severity changes.

A nested layout element also has to ask for its space: `LayoutRightPanel` sets `Scale = true` in its
constructor because, unlike `LayoutLeftPanel` at the root of a window, it sits inside a row and would
otherwise take only its natural width.

- **Controls are removed from the container they were added to**, which is not always the window's own:
  anything inside a scrollable or a tab page belongs to that page's container. `LayoutControl` records it
  in `MaterializedContainer` and `LayoutController.RemoveControls` uses that. Removing from the root
  instead is a silent no-op, and a panel whose contents change -- the document inspector switching
  between documents with different numbers of barcodes -- then keeps every control it has ever shown,
  drawn on top of the new ones. **Changing a `LayoutColumn`'s children is not enough on its own: nothing
  leaves the screen until a layout pass runs, so call `LayoutController.Invalidate()` afterwards.**

- **Anything that reacts to a control's value has to leave the focus alone while it is being typed in.**
  Setting a radio button's `Checked`, replacing a row in a `GridView`, or re-running the layout all move
  the caret out of the box -- and since a `TextChanged` handler runs on every keystroke, the effect is
  that exactly one character can be typed before the field loses focus. `DocumentInspector` exposes
  `IsEditingIdentifier` for this, and defers that work to `LostFocus`.

A `GridView` does not inherit the theme: its background comes out black on the Fluent surface unless
`BackgroundColor` is set, and its cell images are bitmaps, so they need the real DPI scale rather than
`1f`. `&` in a label is an accelerator prefix and eats the following character -- "Documents & barcodes"
renders as "Documents _barcodes".

`L.Tabs(...)` follows `LayoutScrollable`: each page owns a container from
`EtoPlatform.Current.CreateContainer()` and its content is laid out into that. **Every page is laid out,
not only the selected one** — switching tabs doesn't go through the layout system, so a page sized only
when it became visible would come up empty the first time it is opened.

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

The document pipeline's own coverage: `DocumentPipelineTests` (splitting and writing),
`DocumentPipelineUploadTests` (the hand-off to the archive and everything that must not happen),
`DocumentWorkflowMigrationTests` (reading old profiles), `BarcodeDetectionPlanTests` (whether to decode
at all) and `PhantomBarcodeTests` (what a noisy page yields).

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
