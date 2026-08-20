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
unreadable at panel width. It has no "upload everything" button of its own -- that one is in the toolbar,
where it belongs to the window rather than to the document being inspected. Selection runs both ways:
picking a document there selects its pages, and selecting pages that all sit in one document points the
panel at it. Finished documents stay in the list as the record that they went through, so a day's
scanning leaves a long one: **"Remove finished" clears them and takes their pages out of the window with
them** (`DocumentPipeline.RemoveFinished`). The pages go too, because pages that are already filed left
behind in the canvas would show as belonging to no document -- editable again, and draggable into a
document they have nothing to do with. Nothing is deleted from disk. In the inspector the detected barcodes are *radio buttons* -- the selected one is the
identification -- plus "own value" for free text. A "use as identification" button per row said what
would happen if you pressed it but never which barcode was actually in use.

**A document's pages are the window's page objects, not a copy of them.** `DocumentPageTracker` points
each document at the `UiImage`s the window holds, and `DocumentWriter` reads them at the moment it
writes — so a page straightened, cropped or deleted in the window is straightened, cropped or deleted in
the archived file. It used to be a frozen list of `ProcessedImage`s tied to the window only by value
equality (storage plus transforms), which is exactly what stops holding the moment anyone edits a page:
the archived PDF kept showing the raw scan, and rotating every page of a document made it look as though
all of its pages had been deleted, which took the document out of the list. Don't reintroduce a second
copy of the pages; if a document needs its own, clone at the point of use and dispose it there.

- **A document only takes the window's pages over once all of them have arrived.** A document exists the
  moment the scan is split, while its pages are still on their way in, so a document pointed at half of
  itself would write half a document. `WindowPageCandidates` is what the two ends are matched by, and it
  is matched **by instance**: the window gets a clone of every page, and a clone is equal by value to the
  original, so value equality would tie a document to any page that shares storage with one of its own.
- **Taking them over is not a change to the document.** At that moment the window holds exactly the pages
  the scan produced, so `PageRevision` stays where it is; counting it as a change would write a second
  copy of every document that files locally and uploads on the button.
- **Changing a document's pages invalidates the file it was written from.** `WrittenUnderPageRevision` is
  the counterpart to `WrittenUnderIdentifier` for the contents rather than the name, and `EnsureFile`
  compares both. Without it a page deleted after the document had been written left a file that looks
  filed and is not what the operator is looking at.
- **A document is dropped when every one of its pages has left the window.** Finished documents are never
  dropped -- they are the record that those pages reached the archive, and clearing the window is the
  normal way to start the next batch. There is no window at all for the command line scanner, where a
  document keeps the scan's own copies for good.
- **Leaving the window and being dragged into another document are not the same thing.** A finished
  document keeps pages that are gone from the window; it has to let go of ones that are still there,
  because the document they were dropped into holds them now. Two documents claiming one page made
  `_owners` take whichever the queue reached last -- the page was drawn under the document it had just
  left, in the middle of the one it was dropped into, which reads as *that* document having been split
  at the drop position. For the same reason a document on its way out of the queue is left out of
  `_owners`: it is still in `DocumentQueue.Documents` while the map is being rebuilt and gone by the time
  the canvas redraws, and a queue change starts no new sync to put it right.

### Sections in the canvas

The pages in the middle of the window are grouped by the document they belong to, under a heading naming
it. `DocumentSectionBuilder` works out the sections -- a run of consecutive pages per document, plus one
for the pages belonging to none -- and `IListView.SetSections` hands them to the platform list view,
which on Windows means native `ListViewGroup`s. **The sections address the pages by index**, so they have
to be rebuilt after every change to the list; `DesktopForm.UpdateSections` does that from both the image
list and the document queue, because a document that finishes uploading changes its heading without a
single page having moved.

Four things about the WinForms side, each of them measured on a throwaway harness rather than assumed:

- **The native group heading is never used.** comctl32 draws it in the light Explorer blue whatever the
  window's theme is, and `SetWindowTheme(…, "DarkMode_Explorer")` changes nothing about it. The heading
  text is set to a blank, and `WinFormsListView.DrawSectionHeaders` paints the real one into the band the
  group reserves, in `ColorScheme` colours. The band's position comes from `ListViewGroups.HeaderBounds`,
  which reaches through a non-public property for the group's native id and returns null rather than
  throwing if a future runtime renames it -- the caller then derives the band from the items instead.
- **The native insertion mark stops being drawn as soon as items are grouped.** That is why the drop
  position is drawn by `DrawDropIndicator` instead. Don't put `InsertionMark` back; it works only in the
  ungrouped case, which is no longer the normal one.
- **Never collapse a group.** Reading `ListViewItem.Bounds` for an item inside a collapsed group does not
  return -- the process hangs -- and `GetDragIndex` reads exactly those bounds on every drag. Collapsing
  finished documents would be a reasonable feature and needs a guard that never asks a collapsed group's
  items for their bounds.
- **A section is a range, not a set.** Because a document is a run of consecutive pages, the flat item
  index still says where a page is on screen, and everything addressing pages by position -- `MoveTo`,
  the drop index, the selection, `ApplyDiffs` -- keeps working untouched. This was verified for the
  grouped case: display order and item order stay identical.

Page numbers are per document (`2 / 4`), not per batch, wherever there are sections: which page of this
document it is answers the question the operator has.

### Moving a page from one document to another

You drag it there. `DocumentPageAssignment.Normalize` works out what that means, and it is deliberately
the one place that decides:

- **Position decides, and the pages that moved are the ones that adapt.** The commands that know which
  pages they acted on say so -- the drop and Move up/Move down, all through
  `ImageListActions.ReportMove` -> `DocumentPageTracker.ReportMove`, only after the guards have let the
  change through, and the hint is taken by the very next sync and never left lying around. Everything
  else (interleave, reverse, undo) is read back out of the new order instead: everything outside the
  longest subsequence still in its old relative order, so moving one page past ten others moves one page
  rather than eleven. **Reading it back is ambiguous for exactly the move that matters** -- for a short
  move across a document boundary, "this page went up" and "the one above it went down" describe the same
  result, and the longest-run reading settles it in favour of whichever document has more pages. The page
  that stayed put then changed document while the moved one kept its own, which is what "the pages swap
  instead of merging" was.
- **Being named by a command is not the same as having gone anywhere.** A drop back onto the position it
  came from, Move up on the top page and Move down on the last one all name a page the mutation then
  leaves exactly where it was. What counts is a page's place among the pages that were *not* named --
  those are the ones it can be said to have moved past, and the ones it takes its new document from.
  Without that, dropping a document back where it already sits would silently give its pages to the
  document above, and dropping one there is the first thing anyone trying to merge two of them does.
  It also settles the case where every page is named: nothing is left to have moved past, so nothing is
  reassigned. A page dropped between two documents joins **the one above it** -- it was dropped at the
  end of that section.
- **New pages never adopt a neighbour.** A page that was not in the window before is a page that has just
  been scanned (it belongs to the document the scan was split into) or imported (it belongs to none until
  someone drags it somewhere). Only pages that were already there and moved are reassigned.
- **Every document comes out as one run.** `MakeRunsContiguous` guarantees the invariant the canvas
  draws: a document left with pages in several places keeps its longest run and the others join what they
  now sit behind. Without it a wholesale reorder (interleave, reverse) would draw one document as several
  sections under the same heading.
- **A document whose pages all went elsewhere is dropped**, the same as one whose pages were deleted.

`ImageListActions` is where the two refusals live, because it is the one chokepoint both the keyboard and
the drop go through:

- **The pages of a document that reached an archive cannot be deleted or moved.** They are the record
  that exactly those pages are in there, so an edit that appears to work while the archive stays as it
  was would be worse than one that is refused. **Reaching an archive, not being finished**
  (`ScannedDocument.IsFiledRemotely`): a profile that only files into a folder finishes a document the
  moment it is written, and locking those would leave anyone who uploads nowhere unable to edit a page at
  all. A mixed selection deletes the rest and says how many stayed. **Clearing the window is not an
  edit** and is deliberately not caught: it is how the next batch starts, and a finished document keeps
  its own record either way.
- **A document being written or uploaded at this moment is left alone too.** With automatic upload that
  happens while the operator carries on working, so its pages are in use.
- **Pages do not move between documents of different profiles.** The profile decides the folder, the
  name and the archive, so that one drag would change all three with nothing on screen saying so.
- Both refusals go to the console *and* to `INotify.Refused`, which is what the notification channel was
  generalized for (`MessageNotification`, formerly `UploadNotification`): a refused edit that says
  nothing is exactly the silent nothing this app exists to make visible.

The guard asks the same page the rule does -- the one above the drop position -- so the two cannot
disagree about where a drop at a boundary would land.

**"The same profile" means the same profile object.** Both the drop guard and the merge compare by
reference, which is what two scans with one profile give you. A profile edited between the two scans can
therefore refuse a merge that would have been fine; refusing is the conservative direction, since the
profile is what decides the folder, the name and the archive.

### Splitting and merging documents

`DocumentEditor` is what a missed separator sheet costs to repair -- and a sheet that separated where it
should not have. "Split document here" makes the topmost selected page the first page of a new document
which takes everything to the end of the one it was in; "merge with previous document" gives a document
to the one directly above it. Both are in the canvas context menu and on `Mod+Shift+T` / `Mod+Shift+M`.

- **The identification follows the pages.** Both operations re-run `DocumentPipeline.AttachBarcodes`, the
  same method the scan uses, over each affected document's pages. Split a stack and the half that keeps
  the cover sheet keeps the order number, while the half without it stops claiming a barcode that left
  with the other half. Nothing is decoded again -- the barcodes were read when the pages were scanned, so
  this only picks among them the way the scan did.
- **A value typed by hand outlives it**, because it was a correction of exactly this. `Reidentify` puts a
  `Manual` identification back after refreshing the barcodes.
- **A document split off is inserted after the one it came from**, not appended: the list reads in page
  order, and the bottom of the list is where the next scan goes.
- Splitting at a document's first page is not offered (it would produce nothing), nor is anything on an
  archived document, nor merging across profiles -- the same rules the drag follows.

### A document with nowhere to upload to is finished when it is written

A profile that files locally and has no archive target used to leave its documents `Pending` for ever.
Nothing could take them further, but they still counted as ready to upload -- so the upload button stayed
lit, and pressing it ran them through `Advance`, which wrote nothing new and left them pending, whereupon
the controller counted them as not `Done` and reported that the upload had **failed**. For a document
that had been filed exactly as asked.

`Advance` now finishes them as soon as the file is written, and everything downstream follows: the button
is disabled because there is nothing to upload, the counts are right, and the status line reads "Filed
locally" instead of naming a queue that does not exist.

- **Cleanup only applies to a document that actually went somewhere.** `CleanupAfterCompletion` defaults
  to **on**, so finishing these documents would otherwise have started clearing the window after every
  scan for every profile that just files into a folder -- and the window is the one place a scan can
  still be looked at and corrected.
- **Correcting a filed document puts it back in the queue.** Change its pages and `DocumentPageTracker`
  sets it back to `Pending`; change its identification and `DocumentInspector` does. Either way the
  status says the file on disk is out of date and the upload button has something to do again -- it
  writes the corrected version **next to** the old one, because a file in the operator's own folder is
  theirs.

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
- **A file already on disk is only reusable while it still carries the name the document would get now.**
  `DocumentPipeline.EnsureFile` compares `ScannedDocument.WrittenUnderIdentifier` against the current
  identification and writes again when they differ. Without that comparison the file was reused verbatim
  while the SharePoint folder and the SAP object key were re-expanded at upload time, so a correction made
  after the document had been written (a profile that files locally and uploads on the button, or a retry
  after a failed upload) reached the archive key and not the name. A staging copy is replaced; a file in
  the operator's folder is left where it is and a second one written, because that file is theirs.
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

## Where on the page barcodes are looked for

`DocumentWorkflowSettings.RestrictBarcodeArea` + `BarcodeArea` (a `BarcodeSearchArea`, four fractions of
the page) restrict detection to part of the sheet, and `BarcodeSearchAreaPicker` is how it is drawn --
a page in A4 proportions with the rectangle on it, three presets and the percentages written underneath.
Paperwork that always carries its barcode in the same place can have the rest of the sheet ignored, which
is the strongest defence there is against a phantom read out of a ruled table: what isn't looked at can't
decode.

- **Off is what every profile written before this existed reads as**, because neither element is in
  those files and both default to "no restriction". A restriction arriving with an update would quietly
  stop a working profile from seeing its barcode, which is the failure this feature exists to prevent,
  not to cause.
- **`GetBarcodeSearchArea()` is the only thing anything downstream asks.** It returns null -- the whole
  page -- when the box is off, when the area covers the page anyway, and when a stored area has collapsed
  to nothing. That last one can only come from a hand-edited profile, and honouring it would mean
  decoding nothing at all; the same reasoning makes `BarcodeDetector` fall back to the whole page when
  the crop can't be made. **The area is kept while the box is off** so unticking and re-ticking doesn't
  lose the rectangle.
- **The crop is a copy, and the page belongs to the caller.** `BarcodeDetector.CropToSearchArea` builds
  it with `CopyBitwiseImageOp` rather than `image.Copy().PerformTransform(new CropTransform(...))`:
  `PerformTransform` consumes what it is given -- it would take the scan with it -- and the copy route
  allocates a full-size copy of a 300 dpi page to throw most of it away, on every page of every scan.
  The crop is disposed as soon as the passes are done with it.
- **Positions stay relative to whatever was decoded.** Every barcode found came out of the same crop, so
  their order in it is their order on the page and the primary is still the topmost-leftmost one.
- **Every scan says which area is in force**, and says so when a restriction is enabled but covers the
  whole page. A barcode outside the area is not detected, and the document that results is
  indistinguishable from paper that carried no barcode at all -- the console line is the only thing that
  says why. `BarcodeSearchAreaTests` and `BarcodeSearchAreaDetectionTests` pin both directions.

## Barcode strictness, and the damaged stop guard

`DocumentWorkflowSettings.BarcodeStrictness` (a slider under the profile's barcode types) decides how
damaged a printed barcode may be and still be accepted. `Strict` is the default, is what the enum's zero
value gives every profile written before the setting existed, and is exactly the old behaviour — so
nothing starts accepting damaged barcodes because of an update.

The two lowered levels add `DamagedCode39Reader`, a second pass that recovers Code 39 symbols ZXing
throws away. It exists for a measured defect: on a customer's process-order cover sheets the data
characters and the start guard decode perfectly, but in the stop guard the edge between the fourth and
fifth element sits ~1.5 modules too far right (the space 2.5 modules instead of 1, the bar 1.5 instead of
3, total width unchanged), so the character matches no Code 39 pattern and ZXing discards the whole
symbol. The same defect is on every sheet from that source, at the same *character* rather than at a
fixed place on the page, so it comes from whatever prints them — ZXing.Net, zxing-cpp and ZBar all refuse
those sheets.

- **Code 39 only, and never patch-T.** Code 128, EAN and UPC carry a check character, so tolerating one
  means overruling the code's own statement that it was misread. A patch-T sheet is a reusable blank card
  carrying a fixed word: a damaged one gets replaced, and accepting one would separate in the wrong place.
- **The guards are the whole design, and they are calibrated, not guessed.** An intact `*` start guard to
  anchor on, every data character decoding cleanly, a terminating group that is character-shaped and
  followed by a quiet zone, a minimum length, and confirmation across scan lines. Measured with only the
  geometry guards active, over the customer's eight pages and a form-shaped noisy page under four noise
  seeds: a real barcode is 14 characters agreed on by 35 scan lines, while the longest thing print noise
  produces is 3 characters on at most 5 lines. **Length is the guard that does the work** — the scan-line
  count narrows the gap but decides nothing on its own. A tolerant reader without the geometry guards
  reads five phantoms (`ZZ`, `$`, `Z`, `M`, `%/%$/`) out of the customer's own document.
- **Recovered values are merged, not used as a fallback.** A page can carry a readable Code 128 next to a
  damaged Code 39, and a pass that only ran when the page yielded nothing would drop the second one
  silently. They join the page's list in reading order and de-duplicate against the strict results;
  `BarcodeValue.IsRecovered` is only true when no pass read the symbol properly.
- **A recovered value is named in the console, one line per value**, because it names files and archive
  keys like any other and nothing on the finished document says a lowered setting let it through.
  `DamagedCode39Tests` and `PhantomBarcodeTests.LoweringTheStrictnessDoesNotInventBarcodesOnANoisyForm`
  pin both directions; the second one is the one that matters.

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

## Progress reporting

`FluentProgressBar` is drawn, not themed: the native WinForms bar renders the Windows 7-era comctl32
block, which matches nothing else in the app. The geometry is WinUI 3's own — a 1px track, a 3px rounded
indicator in the user's accent colour — and the colours all come from `ColorScheme`, so it follows the
theme like everything else. `EtoPlatform.FormatProgressBar` states its height, because a drawn control
has no preferred size for the layout engine to ask for.

- **The native bar's own easing is gone, and so is the workaround for it.** `RenderStatus` used to nudge
  the value up and back down to force the comctl32 bar to catch up with what it had been told; the drawn
  one repaints from the value it was last set to and eases it itself. Don't reintroduce the nudge.
- **`Value` is clamped, not validated.** The native control throws when the value leaves the range, and
  an operation reporting 101% of an estimate must not take down the window it is reporting into.
- **`MaxProgress <= 1` means indeterminate.** `ImportOperation` sets `MaxProgress = 0` for a single file
  precisely because it has no page count yet, and that used to draw a determinate bar sitting at empty for
  the whole import — the "step that quietly does nothing" this app exists to make visible.
- The bar reads `ColorScheme` at paint time, never in its constructor: the config the scheme needs is
  attached in an Autofac build callback and only when there is an Eto platform, so a control that touched
  it while a form's fields were initializing would make construction order load-bearing (and would throw
  outright in tests). A bar on a tinted surface is told what it sits on through `SurfaceColor`.

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

- **`AttachDpiDependency` subscribes; it belongs in a constructor, never in a method that runs again.**
  Each call adds a `DpiChangedAfterParent` handler that is only removed when the control's handle is
  destroyed, so calling it from a refresh method leaks one subscription per call. `DocumentInspector` had
  it inside `Refresh`, which runs on every keystroke in the identifier box *and* on every queue change.
  Attach once, keep the scale in a field, and repaint from that.

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

**The product name is not a localizable string.** It lives in `AppBranding.Name`, and window titles are
built with `AppBranding.WindowTitle`. The title used to come from `UiStrings.Naps2TitleFormat`, whose
neutral value the rebranding fixed — but the key exists in about forty inherited translation files, every
one of which still reads `NAPS2 - {0}`, so the window came up branded NAPS2 for anyone not running in
English. It looked intermittent because German only ships in Release builds. Anything that names the app
to the operator must not go through the resources, or the next translation refresh from upstream rebrands
it again.

## Building the solution

`ScanMe.sln` builds clean in `Debug|Any CPU` and `Release|Any CPU`, which is what Visual Studio picks on
its own. Keep it that way: a solution whose default configuration does not build is one where a real
compile error is indistinguishable from the usual noise, and the one that hid here for a while was a
`DesktopForm` constructor the Gtk subclass had never been updated for.

- **The three Mac projects are excluded from `Debug|*` and `Release|*`** — their `Build.0` lines are
  gone, their `ActiveCfg` lines are not, exactly as upstream already did it for `Debug-Windows|Any CPU`.
  They need the `macos` workload, which no Windows machine here has, so building them fails with
  NETSDK1147 before anything else gets a chance to report. `Debug-Mac` and `Release-Mac` still build
  them, so nothing is lost for anyone actually on a Mac, and **`Release-Msi` still builds them too** —
  which is why `build msi` is still the wrong thing to run on Windows.
- **`NAPS2.Sdk.Worker.Win32` is excluded from `Release|Any CPU`** for the same reason it was already
  excluded from `Debug|Any CPU`: it packs a NuGet package out of `NAPS2.Sdk.Worker.Build`'s *publish*
  output, which a plain build never produces. It belongs to the `Sdk` configuration, where that publish
  happens.
- **`NAPS2.Sdk` still targets `net462`, so `Math.Clamp` is not available in it.** Nor is anything else
  added to `System.Math` after .NET Framework. Use `NumberExtensions.Clamp` (`value.Clamp(min, max)`) —
  it is a global using in the Sdk and is what the rest of the codebase uses anyway. The trap is that
  `dotnet run --project NAPS2.Tools -- pkg msi` builds only the net9 apps and so **passes**: a `net462`
  break shows up in Visual Studio and nowhere near the release path. `NAPS2.Internals` and
  `NAPS2.Images` target it too; `NAPS2.Lib` is net9 only, so the same call is fine there.

---

## Versioning and release

- The single source of truth for the version is `NAPS2.Setup/targets/VersionTargets.targets`.
- The MSI is built with the `NAPS2.Tools` CLI (`dotnet run --project NAPS2.Tools -- pkg msi`), which
  needs WiX Toolset v3.14. Output lands in `NAPS2.Setup/publish/<version>/` and is named
  `ScanMe-<version>-<platform>.msi` — the `naps2-` prefix comes from `ProjectHelper.GetPackagePath`, which
  every packager and verifier routes through, so change it there and nowhere else.
- `pkg msi` publishes NAPS2.App.WinForms, NAPS2.App.Console and NAPS2.App.Worker itself, so it is all you
  need. Do **not** run `build msi` first on Windows: it runs `dotnet build -c Release-Msi` over the whole
  solution, which pulls in NAPS2.App.Mac and fails with NETSDK1147 unless the `macos` workload is
  installed. `Debug` and `Release` no longer do — see **Building the solution** above.
- **Every component GUID in `setup.template.wxs` is ScanMe's own, and must stay that way.** They were
  inherited unchanged from upstream NAPS2, and Windows Installer reference-counts components by GUID
  *across products*: on a machine that had NAPS2 installed, the shortcut component was already registered
  against NAPS2's Start menu folder, so installing ScanMe found it present and created no Start menu
  entry at all. Never copy a GUID from upstream when adding a component — generate one.
- **The shortcut component's key path has to stay under HKCU, and its key has to be ScanMe's own.** ICE38
  and ICE43 treat `ProgramMenuFolder` as user-profile data and fail the build outright for a component
  that keeps a non-advertised shortcut there with a per-machine key path — so HKLM is not an option, even
  though this is a perMachine install. The key was `HKCU\Software\Microsoft\NAPS2`; it is
  `HKCU\Software\ScanMe` now, because sharing that with NAPS2 as well is part of what made the component
  indistinguishable from NAPS2's.
- The MSI's registry component and the Inno script must agree on the ProgID. Both register `ScanMe` in the
  `OpenWithProgids` lists, so `HKCR\ScanMe\shell\open\command` is where the open command belongs — it sat
  under `HKCR\NAPS2`, which put ScanMe in Explorer's "Open with" list with nothing behind it.

## Tests

The document pipeline's own coverage: `DocumentPipelineTests` (splitting and writing),
`DocumentPipelineUploadTests` (the hand-off to the archive and everything that must not happen),
`DocumentPageTrackerTests` (what editing pages in the window does to the document that will be archived),
`DocumentSectionBuilderTests` (which pages the canvas draws under which heading),
`DocumentPageAssignmentTests` (where a page belongs after it has been dragged somewhere else) and
`FinishedDocumentGuardTests` (what the window refuses to do to an archived document),
`DocumentEditorTests` (splitting a document and merging one back),
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
