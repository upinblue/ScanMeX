---
name: release
description: Cut a new ScanMe release. Shows the current version and everything that changed since the last one, asks which kind of version to publish (major/minor/patch/hotfix), then bumps the version, writes a customer-shareable changelog, builds the MSI, tags master and creates the GitHub release with the MSI and a source archive attached. Use when the user asks to release, ship, publish a version, cut a release, or says "neue Version".
---

# Cutting a ScanMe release

Work through the phases in order. **Phases 1-3 change nothing** — they gather facts and get a decision.
Everything that writes, commits, pushes or publishes is in phases 4-8, and each outward-facing step is
confirmed before it runs.

Do not skip the report in phase 2 even if the user already said which bump they want: seeing what is
actually in the release is the point of the tool, and a "patch" that turns out to contain a new upload
target is a minor.

## Phase 1 — Establish where we are

```bash
git status --porcelain
```

- **The working tree must be clean** apart from build output (`bin/`, `obj/`, `publish/`). If real source
  changes are uncommitted, stop and tell the user what they are — a release built from an uncommitted
  tree cannot be reproduced from its own tag.
- **The branch must be `master`.** If it isn't, stop and say so.

Then read the facts:

- Current version: the `<Version>` and `<VersionName>` elements in
  `NAPS2.Setup/targets/VersionTargets.targets`. These are the single source of truth — never edit a
  version anywhere else.
- The previous release: `git tag --list "v*" --sort=-v:refname` (first entry). If there are no tags yet,
  this is the first release — say so, and take the whole history as the change set.
- What changed since then: `git log <previous>..HEAD --format='%h %s'`.

## Phase 2 — Report, in the user's language

Show, compactly:

1. The current version, and the date and version of the previous release.
2. Every commit since then, one line each, in their own words.
3. Your own reading of what kind of release this is, with the reason — "three of these are new
   behaviour, so this looks like a minor" — and what the resulting number would be for each choice.

Group the commits by what they touch (scanning/barcodes, documents, upload, UI, installer, internals) if
there are more than about eight. Do not editorialise the commit messages here; this list is for the user
to recognise their own work in.

## Phase 3 — Ask which version

Ask with `AskUserQuestion`, and put your recommendation first, labelled. The scheme, from a current
version of `A.B.C.D`:

| Choice | Meaning | Next version |
| --- | --- | --- |
| **Major** | Large steps, breaking changes, a reworked workflow | `A+1.0.0.0` |
| **Minor** | New features, or an accumulation of bug fixes | `A.B+1.0.0` |
| **Patch** | Bug fixes and small changes | `A.B.C+1.0` |
| **Hotfix** | An urgent fix on top of a release that is already out | `A.B.C.D+1` |

Always show the concrete number for each option, computed from the current version — not the template.

**A hotfix is the one that needs a caveat.** Windows Installer compares only the first three fields of a
version, so `1.0.15.0 -> 1.0.15.1` is not an upgrade as far as MSI is concerned. It works here only
because `setup.template.wxs` sets `AllowSameVersionUpgrades="yes"`, and it means the installer replaces
rather than upgrades. Mention this if the user picks hotfix.

**Never go backwards.** Machines in the field carry the current version, and a lower number is not an
upgrade — Windows Installer refuses it outright with the `DowngradeErrorMessage`. If the user asks for a
number below the current one (wanting a tidy "1.0.0" for a first public release, say), explain that and
offer the next number up instead.

## Phase 4 — Write the version and the changelog

### The version

Set both `<Version>` and `<VersionName>` in `NAPS2.Setup/targets/VersionTargets.targets` to the new
number. They are always the same value for ScanMe.

**Check the copyright year.** `AppBranding.CopyrightEndYear` in `NAPS2.Lib/AppBranding.cs` is a constant
so that a build states the years it was published in rather than reading the clock at runtime. If the
current year is later than that constant, raise it, and say that you did.

### The changelog

Two files, same content:

- **`docs/releases/<version>.md`** — the file the user hands to customers. Self-contained: it names the
  product and version, so it still makes sense as an email attachment with no surrounding page.
- **`CHANGELOG.md`** — the same entry, prepended above the previous ones.

Write the entry **in German first, then English**, under `## Deutsch` / `## English` headings. The
customers are German; the GitHub release page is read in English. One file with both beats two files
that drift.

Turn the commits into what changed *for the operator*, not what changed in the code:

- Group under `### Neu` / `### Verbessert` / `### Behoben` (`### New` / `### Improved` / `### Fixed`).
  Drop empty groups.
- One bullet per user-visible change. Several commits that together produced one change are one bullet.
- Name the thing the operator sees — the button, the dialog, the setting, the file — not the class.
- **Leave out anything invisible from outside**: refactors, test-only changes, comment and
  documentation edits. If a release contains nothing else, say that it contains internal improvements
  only, and do not invent user-facing bullets to pad it.
- No commit hashes, no file paths, no issue numbers in the customer text.

Then show the draft to the user and let them correct it before anything is committed. This is the file
that goes to a customer; it is worth one round of review.

### Screenshots

The user wants screenshots in the release notes. After the changelog text is agreed:

1. Launch the app: `dotnet run --project NAPS2.App.WinForms -c Debug`
2. Capture the main window, and any dialog this release actually changed — a release that touched the
   profile dialog gets a shot of the profile dialog, not a generic gallery.
3. Save them to `docs/releases/assets/<version>/` with descriptive names
   (`main-window.png`, `document-inspector.png`), and reference them from
   `docs/releases/<version>.md` with relative paths so the file works both on GitHub and locally.

**Say plainly what the screenshots show.** Without a scanner attached the window comes up empty, so a
shot of the document list is a shot of an empty list. If a change cannot be shown without real paper, do
not fake it — tell the user which shot is missing and offer to take it from a real scan on their machine.
A release note illustrated with empty states is worse than one with no pictures.

Remember that a Debug build shows English, not German: only Release builds compile the `de` satellite
assembly. If the screenshots need to be German, build `-c Release` instead.

## Phase 5 — Commit and tag

```bash
git add -A
git commit -m "Release ScanMe <version>"
git tag -a v<version> -m "ScanMe <version>"
```

Keep the changelog body out of the commit message; the tag and the release page carry that.

Do **not** push yet.

## Phase 6 — Build the MSI

```bash
dotnet run --project NAPS2.Tools -- pkg msi
```

- This publishes NAPS2.App.WinForms, NAPS2.App.Console and NAPS2.App.Worker itself. It is the whole
  build; nothing needs to run before it.
- **Never run `build msi` first**, and never build `ScanMe.sln` as a whole: that pulls in NAPS2.App.Mac
  and fails with NETSDK1147 unless the macos workload is installed.
- It needs WiX Toolset v3.14 on PATH.
- Output: `NAPS2.Setup/publish/<version>/ScanMe-<version>-win-x64.msi`. Confirm the file exists and
  report its size before going on.

If the build fails, stop. The version bump and changelog commit are already made, which is fine — fix
the build, add a commit, move the tag, and re-run this phase. Never publish a release whose MSI did not
build.

### Verify the installer before it goes out

The failure this project has hit repeatedly is an MSI that installs but leaves the Start menu entry out,
so check the artifact rather than trusting it:

```bash
pwsh -NoProfile -File tools/setup/Test-MsiPackage.ps1 -MsiPath NAPS2.Setup/publish/<version>/ScanMe-<version>-win-x64.msi
```

It checks the Start menu shortcut, the product name and version, the publisher and the upgrade code, and
exits non-zero if any of them is wrong. If it fails, do not release.

## Phase 7 — Source archive

ScanMe is a GPL fork, so a recipient of the binary is entitled to the corresponding source. Attaching it
to the release is the simplest way to be done with that obligation permanently:

```bash
git archive --format=zip --prefix=ScanMe-<version>/ -o NAPS2.Setup/publish/<version>/ScanMe-<version>-src.zip v<version>
```

The repository is public, so the tag alone would satisfy the licence. Attach the archive anyway — it pins
exactly what the MSI was built from, and it survives the repository later being made private.

## Phase 8 — Push and publish

**Confirm with the user before this phase.** It is the first irreversible, outward-facing step: pushing
the tag and publishing the release page. Show what is about to happen — version, tag, files to attach —
and wait for a clear yes.

```bash
git push origin master
git push origin v<version>
```

```bash
gh release create v<version> --title "ScanMe <version>" --notes-file docs/releases/<version>.md NAPS2.Setup/publish/<version>/ScanMe-<version>-win-x64.msi NAPS2.Setup/publish/<version>/ScanMe-<version>-src.zip
```

If `gh` is missing or not authenticated, do not try to work around it with raw API calls and a token
pasted into a command line — tell the user to install it (`winget install --id GitHub.cli`) and run
`gh auth login` themselves, then continue. Everything up to this point is already committed and tagged,
so stopping here costs nothing.

Then report, with the release URL, the MSI's name and size, and the path to
`docs/releases/<version>.md` as the file they can send to a customer.

## Notes

- `NAPS2.Setup/publish/` is gitignored, which is why the MSI reaches customers as a release asset rather
  than as a commit. Do not commit MSIs — they are ~100 MB each.
- The `docs/releases/<version>.md` files *are* committed. They are the customer-facing record and have to
  survive independently of GitHub's release API.
- If the user asks for a release with nothing to release — no commits since the last tag — say so and
  stop rather than publishing an empty version.
