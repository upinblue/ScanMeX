# Review: ScanMe main window and profiles (Windows, Eto.Forms on WinForms)

- **Date:** 2026-11-23
- **Reviewer:** Claude Code, with the Leonardo skill (review mode)
- **Scope:** main window (toolbar, scan sidebar, page canvas with document sections, document list and
  inspector), Profiles window; `ColorScheme` and the Eto layout code behind them.
- **Main tasks walked through:** scan a stack with barcode separation → check documents → correct an
  identification → upload; manage profiles.
- **Basis:** release screenshots `docs/releases/assets/1.1.2.0/main-window.png` and
  `1.1.1.0/profiles.png`, plus the source (`NAPS2.Lib/EtoForms/ColorScheme.cs`, `Desktop/DocumentInspector.cs`,
  `Layout/LayoutController.cs`, `Desktop/Sidebar.cs`). Leonardo `design-language.md`, `platforms/windows.md`,
  `leonardo.tokens.json` as shipped with the skill.

## Summary

ScanMe already looks more like a Windows 11 app than its NAPS2 origins: Fluent icons, Fluent colour tokens
in one place (`ColorScheme`), a drawn Fluent progress bar, dark mode and Windows 11 window corners. The
largest problem is structural, not visual: **Leonardo on Windows is defined for WinUI 3 and WPF, ScanMe is
WinForms**, and its Fluent look is drawn by ScanMe itself (`FluentToolStripRenderer`, drawn section
headings, drawn progress bar) — which Leonardo calls a rebuilt system component, and which is also why
ScanMe ignores Windows contrast themes entirely. Beyond that, the main window shows too many commands at
one level, permanent green for "done", undersized remove targets in the inspector, and spacing/type that
are off the Leonardo grid and roles.

## Fix first

1. **Respect Windows contrast themes** (R1). No code path reads `SystemInformation.HighContrast`; every
   drawn surface uses fixed `ColorScheme` values. Map `ColorScheme` to `SystemColors` when a contrast
   theme is active. This is the only finding that excludes users, and it is needed under any migration path.
2. **Put `ColorScheme`, spacing and type on Leonardo tokens** (R8, R9, R10, R11). One file for colours
   already exists; add the same for spacing and type roles, generated from `leonardo.tokens.json`.
3. **Thin the command bar** (R3, R4). Group the 20 toolbar items into what the current view acts on;
   Console, Language, Settings and About belong elsewhere.
4. **Make "done" calm and statuses shaped** (R5, R6). Green only briefly, neutral afterwards; failure
   and attention as shape + colour + text.
5. **Decide the shell** (see "Becoming a Leonardo app" below) before investing in more drawn Fluent controls.

## Findings

| ID | Severity | Where | Problem | Why it matters | Recommendation | Source |
| --- | --- | --- | --- | --- | --- | --- |
| R1 | 3 | Whole app | Contrast themes are ignored: no reference to `HighContrast` anywhere; toolbar, section headings, progress bar and thumbnails are drawn in fixed colours. | Users who rely on a contrast theme get a low-contrast or unreadable window. | When `SystemInformation.HighContrast` is set, return `SystemColors` from every `ColorScheme` property and listen to `UserPreferenceChanged`. Status dots get a symbol and text. | WCAG 2.2 SC 1.4.3/1.4.11; Leonardo, Accessibility and honest fallbacks, MUST |
| R2 | 2–3 | Inspector › Detected barcodes | The remove "×" per barcode is a ~10 px glyph in link colour. | Hard to hit, easy to miss; link colour says "this leads somewhere", not "remove". | 24×24 px minimum target, neutral glyph (Fluent `Dismiss` 16), tooltip and accessible name "Remove barcode PA4711002". | WCAG 2.2 SC 2.5.8; Leonardo, Color as signal (Link: never for buttons), MUST |
| R3 | 2 | Toolbar | 20 commands at one level, including app-level ones (Console, Language, Settings, About) next to page commands. | "What can I do here?" has no quick answer; long German labels already wrap ("Upload documents"). | Primary group: Scan, Import, Upload documents, Save PDF. Page group: Rotate, Move, Reorder, Delete. Overflow ("…") for the rest. Settings/About/Language/Console into a settings page or overflow. | Heuristic 8 (minimalist design); Windows: command design basics; Leonardo, Orientation, MUST |
| R4 | 2 | Toolbar › Upload documents | Disabled without a way to learn why. | Operators cannot tell "nothing to upload" from "upload broken". | Tooltip on the disabled button naming the reason ("No document waits for upload"), or keep it enabled and say so inline. | Leonardo, Forms and input, NEVER disabled controls without reason |
| R5 | 2 | Section headings, document list, inspector | "Filed locally" and the green dot stay green permanently. | Green is reserved for a brief confirmation; a day of scanning paints the window green and a real problem no longer stands out. | Finished = neutral (no dot, meta text "Filed locally"); confirm completion briefly (Arrive accent bar or a short green that fades). | Leonardo, Color as signal: green "never for permanent states", SHOULD |
| R6 | 2 | Document list | Status is a small coloured dot only; done and failed differ by colour alone in the list. | Colour-only status fails for colour-blind users and in contrast themes. | Leonardo status shapes: ring (caution/attention), circle with "!" (critical/failed), plus status text in the row's accessible name. | WCAG 2.2 SC 1.4.1; Leonardo, Color as signal, MUST (shape + text) |
| R7 | 2 | Scan sidebar | "Scan Configuration" sits mid-height with a large empty area above it. | Wasted space at the top, where the eye starts; the sidebar looks unfinished. | Top-align; give the sidebar a place header (profile name) and the scan button as the one accent action. | Leonardo, Grid and geometry (asymmetric balance), SHOULD |
| R8 | 1 | Whole app | Spacing is off-grid: `DefaultSpacing = 6`, paddings 10, spacings 1/3/4/5/6/20. | Uneven rhythm; nothing to check values against. | `LeonardoSpace` constants (4/8/16/24/40); page margin 24; `DefaultSpacing` 8. | Leonardo, Grid and geometry, MUST |
| R9 | 1 | Panel headings | "Documents", "Selected document", "Scan Configuration" are body size in bold. | Hierarchy only from weight; three panels, no clear "where am I". | Type roles: `section` 20/28 Semibold for panel headings, `meta` 12/16 tertiary for counts, `body` 14/20 for everything else. Segoe UI Variable explicitly. | Leonardo, Typography as architecture, MUST |
| R10 | 1 | `ColorScheme` | Neutrals are Fluent greys (#1B1B1B, #F3F3F3, #202020). | Leonardo's quiet signature is cool, slightly blue neutrals. | Replace with `theme.light/dark.*` (surface #FAFBFD / #1A2030, surfaceAlt, text primary/secondary/tertiary, stroke subtle/strong). Keep the Fluent status colours — they are identical to Leonardo's. | Leonardo, Blue scale, SHOULD |
| R11 | 1 | Accent | The accent follows the user's Windows accent (fallback #0078D4). | Leonardo: the app's accent is brand blue (#1B6CE0), far (#7FB2F0) on dark ground. | Fixed accent from the tokens; drop the luma nudging. `AccentForegroundColor` = onAccent (white / depth). | Leonardo, Color as signal, MUST |
| R12 | 1 | Profiles window | 3D NAPS2 scanner icon with a check badge next to flat Fluent glyphs; buttons mix icon+text and text-only; Done and Scan as large framed tiles. | Two icon families; not "authentically digital". | List with Fluent `Scanner` 16 icon per row, default marked by text ("Default"), command bar above the list (New, Edit, Delete, Import, Export), one accent action. | Leonardo, Attitude (authentically digital), Dialects: Fluent icons |
| R13 | 1 | Labels throughout | "3 page(s)", Title Case headings ("Scan Configuration"), colons after labels ("Profile:"). | Microsoft Writing Style Guide uses sentence case and real plurals. | "3 pages" / "1 page" (resx with singular/plural), sentence case, no colons on field headers. | Leonardo, Writing, MUST (platform writing guidelines) |
| R14 | 1 | Canvas | Section heading = green dot + bold file name + meta, plus a divider line under it. | Lines for hierarchy. | Drop the rule; heading in `body` Semibold on the alignment line, meta in `meta`. | Leonardo, Typography: NEVER lines for hierarchy |

## What works well

- **One place for colour.** `ColorScheme` with named, sourced tokens is exactly the shape Leonardo wants;
  swapping its values is cheap.
- **List plus inspector** in the document panel, with radio buttons for the identification: the right
  pattern, and in Leonardo terms content before chrome.
- **Feedback discipline.** The console rule, the "say what was refused" notifications, the honest SAP
  wait ("Waiting for SAP … (24 s)") match Leonardo's "loading, saving, syncing and errors are visible
  where they happen" better than most apps.
- **Fluent icons, generated** from a mapping — Leonardo's Windows dialect is "Fluent icons", nothing to change.
- **The single accent action** "Scan" in the sidebar already follows the one-accent-per-view rule.

## Limitations

- Read from the source and from release screenshots; the app was not run for this review. Dark mode,
  contrast themes, 225 % text size, Narrator and keyboard-only (F6 between panes) were not tested.
- The profile edit dialog, settings, console and SAP/SharePoint dialogs were not reviewed.
- Official Microsoft guidance was not re-fetched for this review; platform statements rely on the Leonardo
  Windows platform file. Treat them as unverified until checked against learn.microsoft.com.

## Becoming a Leonardo app

Leonardo's Windows implementation exists for **WinUI 3** (`UpInBlue.Leonardo.WinUI`, incl. `PlaceHeader`,
`SummaryLine`, `StatusDot`, motion helpers) and **WPF** with the .NET 9 Fluent theme
(`UpInBlue.Leonardo.Wpf`, tokens + text styles, no controls). ScanMe is Eto.Forms on WinForms with a
custom layout engine. Three paths:

| | A: Leonardo tokens in WinForms | B: WPF shell (Fluent theme) | C: WinUI 3 shell |
| --- | --- | --- | --- |
| What | Generate `ColorScheme`, spacing and type constants from `leonardo.tokens.json`; fix R1–R14. | Main window and dialogs in WPF with `ThemeMode` Fluent + `UpInBlue.Leonardo.Wpf`; the pipeline (`DocumentPipeline`, queue, upload) stays. | Same, in WinUI 3 with Mica, `NavigationView`/`CommandBar`, the three Leonardo controls and motion. |
| Leonardo conformity | Look and content layer yes; "platform controls unchanged" no — the Fluent look stays self-drawn. | Full for WPF (Leonardo's WPF level has no controls or motion beyond stagger). | Full, including `StatusDot`, `SummaryLine` ("Since your last scan: 3 filed · 1 failed"), Arrive/Leave. |
| Effort | Weeks | Months | Months+ |
| Risk | Low; no workflow change | All the WinForms canvas know-how (ListView groups, drop anchors, drawn headings — see CLAUDE.md) must be rebuilt | Same as B, plus packaging (Windows App SDK runtime) and the MSI |

**Recommendation:** A now, then decide between B and C with a short spike.

- **A first** because it pays off under every path: tokens, contrast themes, status semantics, command
  structure and wording are all shell-independent. Most of it is `ColorScheme`, `LayoutController.DefaultSpacing`,
  a `LeonardoType` helper for fonts and resx edits.
- **Then a spike (2–3 days)** that rebuilds only the page canvas with document sections in the candidate
  shell: grouped thumbnails, drag between documents with the drop anchor, 500 pages. The canvas is the
  one part of ScanMe that is genuinely hard; if it works there, the rest of the window is routine.
  WinUI 3 is the target that makes ScanMe a full Leonardo app; WPF is the lower-risk fallback.
- Eto.Forms has a WPF backend (`Eto.Platform.Wpf`), which could in theory host the existing forms under
  the WPF Fluent theme. Unverified: Eto's WPF controls may not pick up `ThemeMode` styles, and every
  `NAPS2.Lib.WinForms` control (list view, toolbar renderer, drop indicator) would need a WPF version
  anyway. Worth one afternoon inside the spike, not a plan on its own.

## Open questions

1. Is a WinUI 3 / WPF rewrite of the shell on the roadmap at all, or should ScanMe stay WinForms and get
   Leonardo as far as tokens reach (path A only)?
2. Should the accent really ignore the user's Windows accent? Leonardo says brand blue; some customers may
   have set a corporate accent on purpose.
3. Who decides on the toolbar regrouping (R3)? Operators rely on today's positions; this changes a
   workflow existing users know.
4. Is `UpInBlue.Leonardo.Wpf`/`.WinUI` available on our NuGet feed, and which Leonardo version should be pinned?

## Status after the first pass (2026-11-23)

Decided with the product owner: no shell change (path A only), brand blue replaces the Windows accent,
the toolbar is regrouped as proposed. Implemented and checked in the running app (light, dark, toolbar,
"More" menu, tooltip, settings dialog):

- Done: R1 (contrast themes -- in code; not switched on screen, see CLAUDE.md), R2, R3, R4, R5, R6,
  R7, R8, R9 (panel headings; body text deliberately stays 9 pt), R10, R11, R14.
- Partly: R13 -- "page(s)" and "document(s)" in the main window are real plurals now; Title Case
  labels and colons after field labels elsewhere are unchanged.
- Open: R12 (the Profiles window). Not verified on screen: the canvas section headings with real
  documents, which need the fake scanner from the UI-verification notes.
