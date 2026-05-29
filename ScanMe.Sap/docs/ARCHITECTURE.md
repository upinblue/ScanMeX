# ScanMe SAP/Upload Architecture Memo

- Placeholder engine is `NAPS2.ImportExport.Placeholders` in `NAPS2.Sdk\ImportExport\Placeholders.cs`.
- Existing substitution is `Substitute(string? filePath, bool incrementIfExists = true, int numberSkip = 0, int autoNumberDigits = 0)`.
- Date/time tokens such as `$(YYYY)` and numeric tokens such as `$(nnn)` are handled there.
- Auto-save is implemented by `NAPS2.Lib\ImportExport\AutoSaver.cs`.
- Auto-save paths are configured through `AutoSaveSettings.FilePath` and expanded with `Placeholders.All.WithDate(...)`.
- Scan splitting is handled by `SaveSeparatorHelper` using `SaveSeparator.FilePerScan`, `FilePerPage`, `PatchT`, and `Code39Barcode`.
- Barcode metadata is stored on `ProcessedImage.PostProcessingData.Barcode`.
- `BarcodeDetector` in `NAPS2.Sdk\Scan\BarcodeDetector.cs` wraps ZXing and currently stores the preferred detected barcode per image.
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
- The extractor returns at most one barcode per page today because the current `ProcessedImage` metadata stores one barcode; the API is list-shaped for future multi-barcode support.
- Barcode recognition remains opt-in through `ScanProfile.BarcodeRecognitionEnabled` to avoid performance regressions for existing profiles.

## Post-Scan Pipeline

- `PostScanOrchestrator` coordinates active sinks in strict order: `AutoSave`, `SharePoint`, then `Sap`.
- `IPostScanSink` is the common abstraction; `SavedArtifact` describes files written by AutoSave and consumed by upload sinks.
- `AutoSaveSink` owns artifact production; upload sinks skip when no artifact is available.
- `SharePointSink` wraps the existing `SharePointUploadService` and resolves file names with `FileNamePlaceholders`.
- `SapArchiveSink` resolves barcode/object-id/slug templates before calling `ISapArchiveUploader`; the uploader receives only final strings.
- Barcode extraction is lazy: it runs only when `BarcodeRecognitionEnabled` is true or an active template contains `$(barcode`.
- Patch-T segmentation for this pipeline keeps the separator sheet as page 1 of the new segment.
- The first segment may have no leading Patch-T sheet; in that case `SeparatorBarcodeValue` is null and `$(barcode)` falls back to the first detected barcode.
- Unknown placeholders are detected after substitution by checking for `$(` and fail the sink before writing/uploading.
- Upload failures do not delete the local artifact; this preserves evidence for retry/support.

## Migration Notes

- Existing profiles remain compatible because all new fields default to empty/null/false.
- The old `Placeholders.Substitute(...)` signature and behavior are unchanged.
- The new `FileNamePlaceholders.SubstitutePlaceholders(..., ScanContext, ...)` is additive.
- Patch-T behavior differs in the new post-scan pipeline: separator sheets are retained in output files to preserve the visible SAP business reference.
