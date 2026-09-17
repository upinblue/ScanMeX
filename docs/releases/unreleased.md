# Unreleased — scan throughput

Material for the next release's notes. When that release is cut, fold the **Improved** section below into
`docs/releases/<version>.md` and its summary into `CHANGELOG.md`, then delete this file.

Everything here was measured on the customer's own paperwork, from Release builds, on an Intel Core
i5-14400 (10 cores / 16 threads) under Windows 11. The measurement drives one page at a time through the
real post-processing chain — the transforms, blank detection, writing the page to its backing file,
barcode detection and the thumbnail — the way the scanner's own driver does, so the figures are what a
feeder is actually waiting for.

## Improved

- **A scanned stack is filed three to four times faster.** The work each page goes through between the
  scanner and the window used to run on a single thread while the rest of the machine sat idle; it now
  runs across the available cores, and each step of it was made to do less. On the customer's 300 dpi
  paperwork that is 153 → 517 pages a minute, and on 600 dpi scans 63 → 247. Pages still reach the
  window, the document separation and the archive in the order they were scanned.

- **Nothing about barcode detection changed.** Verified page by page against the customer's own sample
  documents: 35 pages read in three detection settings each, 109 recorded values and their order,
  identical before and after.

- **Older workstations gain too.** On a machine with only two usable threads the same stacks go 136 → 153
  and 55 → 75 pages a minute; with four, 147 → 189 and 62 → 110. There is no machine on which this is a
  step backwards.

- **Thumbnails are drawn more cleanly.** Fine print and ruled tables on a scanned form come out slightly
  softer and without the stepping the previous scaling produced.

- **The console reports what each page cost** — its size and the time of every step — so a report of slow
  scanning can be answered with the machine's own figures. It also says when a page was detected as blank
  and dropped, which until now happened silently and looked exactly like a sheet the feeder had missed.

## The measurements

`90714456.pdf` is 22 pages of the customer's process-order paperwork at 300 dpi;
`Examples_of_Barcoded_Paperwork.pdf` is 4 pages at 300 dpi; `ATT32081.pdf` is 8 pages at 600 dpi. Per-page
figures are wall-clock time for the whole post-processing chain.

### On the full machine (16 threads)

| Document | Before | After | |
| --- | --- | --- | --- |
| 90714456, 300 dpi | 392 ms/page — 153 pages/min | **116 ms/page — 517 pages/min** | 3.4× |
| Examples, 300 dpi | 396 ms/page — 151 pages/min | **149 ms/page — 402 pages/min** | 2.7× |
| ATT32081, 600 dpi | 946 ms/page — 63 pages/min | **243 ms/page — 247 pages/min** | 3.9× |

For scale: a 60 ppm duplex feeder delivers 120 images a minute, which the 300 dpi case previously did not
keep up with and the 600 dpi case missed by half.

### On a constrained machine

Simulated by holding the runtime to that many threads. This is the check that mattered most, because the
work is now spread rather than reduced, and a machine with nothing to spread it over could have come out
slower.

| Threads | Document | Before | After | |
| --- | --- | --- | --- | --- |
| 4 | 90714456, 300 dpi | 408 ms/page — 147 pages/min | 318 ms/page — 189 pages/min | 1.3× |
| 4 | ATT32081, 600 dpi | 975 ms/page — 62 pages/min | 544 ms/page — 110 pages/min | 1.8× |
| 2 | 90714456, 300 dpi | 442 ms/page — 136 pages/min | 393 ms/page — 153 pages/min | 1.1× |
| 2 | ATT32081, 600 dpi | 1096 ms/page — 55 pages/min | 799 ms/page — 75 pages/min | 1.4× |

### Where the gain comes from

Two separate things, and they are worth keeping apart.

**Less work per page.** Measured with the parallelism held at one, so this is the reduction alone:

| Document | Before | After, single-threaded | |
| --- | --- | --- | --- |
| 90714456, 300 dpi | 392 ms/page | 263 ms/page | 1.5× |
| Examples, 300 dpi | 396 ms/page | 266 ms/page | 1.5× |
| ATT32081, 600 dpi | 946 ms/page | 342 ms/page | 2.8× |

The page's luminance matrix is now built once instead of three times, the barcode pass's downscaled copy
filters one channel instead of three, and a page carrying no barcode is no longer scanned twice. Barcode
detection alone: 227 → 122 ms a page at 300 dpi, 574 → 99 ms at 600 dpi.

**More of the machine used.** This is the larger half, and it costs CPU time rather than saving it:

| Document | Before | After |
| --- | --- | --- |
| 90714456, 300 dpi | 479 ms CPU/page, 1.2 threads busy | 831 ms CPU/page, 7.2 threads busy |
| ATT32081, 600 dpi | 1186 ms CPU/page, 1.3 threads busy | 2230 ms CPU/page, 9.2 threads busy |

The application used to occupy 1.2 of 16 threads during a scan. It now occupies 7 to 9, and finishes in a
third of the time for 66 to 88 % more processor-milliseconds. That trade is deliberate: a scanning
workstation is doing nothing else while the feeder runs. Part of the extra is parallelism's own overhead,
part is the new resampler doing more arithmetic than the one it replaces — which is also what makes the
thumbnails cleaner.

### Why it was capped before

GDI+ serializes every interpolated `Graphics.DrawImage` across the whole process. Measured with each
thread holding its own source and destination bitmap, eight threads resize no faster than one, at every
size down to 128×128 — 48 KB, which fits in L1 cache, so memory bandwidth cannot account for it. Two of
those resizes sat in the per-page chain, so no amount of page-level parallelism could have got past them.
Replacing them was the precondition for everything else.

## Verification

- **Barcode results, over the customer's sample documents.** 35 pages × 3 detection settings = 109
  recorded lines of primary value, all values, and their order. Identical before and after. The order is
  the part that matters: it decides the primary barcode and what `$(barcode:1)` and `$(barcode:2)` mean.
  Those files are customer documents and are not in the repository, so this is a harness check rather
  than a test.
- **`NAPS2.Sdk.Tests`**: 722 passed, 24 skipped, 2 failed — the two `PageSizeTests` string-format cases
  that fail on a German locale on `master` as well.
- **`NAPS2.Lib.Tests`**: 395 passed, 8 skipped, 5 failed — the same five that fail on `master`
  (`Naps2ConfigTests`, two `CommandLineIntegrationTests.ScanPdfSettings_*`, the donate prompt).
- **New tests**: `BicubicResamplerTests` (10) compares the banded resampler byte for byte against a plainly
  written reference across 1, 3 and 4 channels, and pins that it does not shift brightness.
  `PostProcessingPipelineTests` (6) pins that pages are handed on in scan order under deliberately
  scrambled processing times, that a page survives a driver which disposes it the moment the callback
  returns, that a dropped blank page does not reorder the rest, that a failed page throws its own
  exception, and that no more pages are held at once than the memory bound allows.
- **Thumbnails** differ from the ones GDI+ produced by an RMSE of 8.8 to 20.1 over the customer's pages.
  The repository's own thumbnail test passes unchanged at its 6.9 threshold, that being a photograph at a
  gentler ratio.
- `ScanMe.sln` builds clean from scratch in `Debug|Any CPU` and `Release|Any CPU`.

## Approaches measured and rejected

Recorded so they are not tried again.

- **A GPU.** The dominant costs were ZXing's branch-heavy multi-barcode search and a lock inside GDI+,
  neither of which a graphics card removes. What is genuinely GPU-shaped in the chain totals under 200 ms
  a page and was cheaper to fix on the CPU.
- **A cheaper downscale for the barcode pass** (box or bilinear instead of bicubic). Loses barcodes on
  three of the customer's pages, one of which only the downscaled pass reads at all.
- **Storing pages as JPEG instead of "whichever is smaller".** PNG wins on 26 of the 35 customer pages,
  by a factor of four to five — the opposite of what a synthetic noisy test page suggests. It would have
  quintupled the archive files and made them lossy.
- **Holding grayscale scans as 8-bit instead of expanding them to RGB.** 620 → 598 ms a page; not worth
  the disruption.
- **ImageSharp** as the replacement resampler. Works and is equivalent, but version 3 is under the Six
  Labors Split License, which is a commercial licensing question for a shipped product. The hand-written
  resampler is faster anyway.

## Still open

Found while measuring, deliberately not changed:

- The PDF export encodes each page inside the document lock (`PdfExporter.WriteToPdfSharpStep`), so that
  part of saving a document is single-threaded. It only bites when pages carry transforms or are
  grayscale; an untransformed colour page is embedded without re-encoding.
- `WinFormsListView.SetSections` reassigns every item's group on every change, which grows with the
  square of the batch size. It affects how the window responds during a large scan, not what is filed.
