# ScanMe SAP/Upload Architecture Memo

- Placeholder engine is `NAPS2.ImportExport.Placeholders` in `NAPS2.Sdk\ImportExport\Placeholders.cs`.
- Existing substitution is `Substitute(string? filePath, bool incrementIfExists = true, int numberSkip = 0, int autoNumberDigits = 0)`.
- Date/time tokens such as `$(YYYY)` and numeric tokens such as `$(nnn)` are handled there.
- Auto-save is implemented by `NAPS2.Lib\ImportExport\AutoSaver.cs`.
- Auto-save paths are configured through `AutoSaveSettings.FilePath` and expanded with `Placeholders.All.WithDate(...)`.
- Scan splitting is handled by `SaveSeparatorHelper` using `SaveSeparator.FilePerScan`, `FilePerPage`, `PatchT`, and `Code39Barcode`.
- Barcode metadata is stored on `ProcessedImage.PostProcessingData.Barcode`.
- `BarcodeDetector` in `NAPS2.Sdk\Scan\BarcodeDetector.cs` wraps ZXing. It decodes each page twice (full size and 60%) and stores every result in `Barcode.AllDetections`, with `DetectedText` holding the primary one.
- Patch-T detection uses `Barcode.IsPatchT`, backed by detected text `PATCHT` and format `CODE_39`.
- Scan options enable barcode detection for PatchT/Code39 separation in `ScanPerformer.BuildOptions`.
- SharePoint upload settings are stored on `ScanProfile.EnableSharePointUpload` and `SharePointUploadSettings`.
- SharePoint upload is triggered in `AutoSaver.SaveOneFile` after a successful local PDF save.
- Current SharePoint upload receives the saved file path/file name; placeholder expansion is inherited from AutoSave file path generation.
- SAP settings are stored as nullable `ScanProfile.SapArchiveSettings` to preserve existing profile XML compatibility.

## Decisions

- `DetectedBarcode.BarcodeType` is a string instead of an enum because ZXing formats and future detectors can evolve independently.
- `ScanContext` lives in `NAPS2.Lib` because it references `ScanProfile`, which is part of `NAPS2.Lib`.
- `DetectedBarcode` and `BarcodeExtractor` live in `NAPS2.Sdk` because barcode metadata and `ProcessedImage` are SDK-level concepts.
- The new `FileNamePlaceholders` wrapper lives in `NAPS2.Lib` and delegates existing date/number behavior to `Placeholders` to avoid replacing upstream behavior.
- Unresolved barcode placeholders expand to an empty string so file names never contain literal `$(barcode)` tokens.
- `SanitizeForFileName` is explicit and caller-controlled; substitution itself remains text-only.
- `BarcodeExtractor` reads existing post-processing barcode metadata and does not introduce a new ZXing dependency.
- ~~The extractor returns at most one barcode per page~~ — superseded. `Barcode.AllDetections` holds every
  code decoded on a page, and `BarcodeExtractor` returns them with the one matching the profile's regex
  (`SelectionPattern`) first, so `$(barcode:1)` is the order number rather than whichever code happened to
  read first.
- ~~Barcode recognition is opt-in through `ScanProfile.BarcodeRecognitionEnabled`~~ — superseded.
  That property is vestigial (no UI sets it); `ScanProfile.NeedsBarcodeValues()` is the gate, and it also
  turns detection on for profiles that only need a barcode for a file-name or upload-path template.

## Post-Scan Pipeline

A sink-based pipeline (`PostScanOrchestrator`, `IPostScanSink`, `AutoSaveSink`, `SharePointSink`,
`SapArchiveSink`, `SavedArtifact`) was designed here but never wired into the Autofac container, so it
never ran in the shipped app while carrying its own drifting copies of the barcode and path logic. It has
been deleted. The pipeline that actually runs is:

- `AutoSaver` splits the scan into documents, resolves the file name and writes the PDF.
- `DocumentUploadService` takes one saved document to every target the profile enables, attempting all of
  them and joining the failures into one message, so one target being down doesn't stop the other.
- `DocumentUploadQueue` holds documents waiting for the manual upload button plus any whose automatic
  upload failed; `DocumentUploadController` drives the button.
- Barcode extraction is decided by `ScanProfile.NeedsBarcodeValues()`, which covers separation, the SAP
  object key and any template containing `$(barcode` or `$(id)`.
- Unresolved placeholders are detected after substitution by checking for `$(` and fail the save before
  anything is written or uploaded.
- A failed upload never deletes the local file, and the document stays in the queue so it can be retried.

See the "path a scan takes" section of `CLAUDE.md` for the full call chain. Do not reintroduce a second
post-scan path.

## Migration Notes

- Existing profiles remain compatible because all new fields default to empty/null/false.
- The old `Placeholders.Substitute(...)` signature and behavior are unchanged.
- The new `FileNamePlaceholders.SubstitutePlaceholders(..., ScanContext, ...)` is additive.
- Whether the separator sheet stays in the output is `DocumentWorkflowSettings.KeepSeparatorPage`. It
  defaults to on for barcode separation, because the barcode cover sheet is part of the order's paperwork
  and carries the visible SAP business reference, and to off for patch-T, whose sheets are only markers.
