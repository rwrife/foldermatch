# PLAN.md — foldermatch

## Scope

A cross-platform desktop **folder compare & sync** utility for Windows 10/11 and macOS.

**In scope**
- Compare two folder trees and produce a reconciled diff (new / updated / deleted / conflict / identical).
- Compare modes: **Quick** (name + size + modified-time) and **Thorough** (content hash: size → partial-hash → full-hash pipeline).
- Filter/scope: include/exclude globs, min/max size, date ranges, hidden/system files toggle, case-sensitivity handling per-OS.
- Sync engines: **Mirror L→R**, **Mirror R→L**, and **Two-way** with configurable conflict resolution (newer-wins / larger-wins / left-wins / right-wins / ask).
- Safety: **dry-run by default**, deletions to Recycle Bin (Windows) / Trash (macOS), JSON **undo journal**, never-delete-without-backup invariant.
- Desktop UI: two-pane folder pickers, virtualized diff tree with per-item check/uncheck, status filters, dry-run plan view, progress + cancellation.
- Headless CLI for scripting and CI verification.
- Optional local-AI diff summaries (metadata-only, localhost, off by default).

**Out of scope (see Non-goals)** — cloud sync, real-time background watching (initial release is on-demand), 3-way merge of file *contents*, version history/snapshots.

## Architecture / tech approach

- **Runtime:** .NET 8.
- **UI:** Avalonia UI (MVVM) — chosen for true cross-platform Windows + macOS from a single codebase (WPF rejected as Windows-only).
- **Core library (`FolderMatch.Core`, UI-free, unit-tested):**
  - `IFolderScanner` — parallel `System.IO.Enumeration` walk building a `FileEntry` tree (relative path, size, mtime, attributes), with access-denied and reparse-point/symlink safety and cancellation/progress.
  - `IHasher` — streaming hash pipeline: bucket by size → partial (head/tail) hash → full hash (xxHash64 fast path, SHA-256 verify option).
  - `IDiffEngine` — reconciles left/right trees into a `DiffResult` of `DiffItem { RelativePath, ChangeType, LeftInfo, RightInfo }` where `ChangeType ∈ {New, Updated, Deleted, Conflict, Identical}`.
  - `ISyncPlanner` — turns a `DiffResult` + `SyncOptions` (direction, conflict rule, delete policy) into an ordered `SyncPlan` of `SyncAction { Copy, Overwrite, Delete, Skip }`.
  - `ISyncExecutor` — applies a `SyncPlan` with dry-run mode, Recycle Bin/Trash deletion, atomic copy-to-temp-then-rename, and an `IUndoJournal` (JSON) enabling rollback.
  - `IChangeSummarizer` / `IDiffAiService` — rule-based summary + optional local-AI backend.
- **Platform adapters:** Recycle Bin via `Microsoft.VisualBasic.FileIO` / `SHFileOperation` (Windows) and `NSFileManager trashItem` / `osascript` fallback (macOS); per-OS path case-sensitivity + long-path handling.
- **Persistence:** JSON settings + saved compare "profiles" + undo journals under `%APPDATA%\foldermatch` (Windows) / `~/Library/Application Support/foldermatch` (macOS).
- **CLI:** `FolderMatch.Cli` (`compare`, `sync`) sharing `FolderMatch.Core`; exit codes suitable for scripting/CI.
- **Local-AI:** `IDiffAiService` → Ollama / llama.cpp OpenAI-compatible endpoint at `http://localhost`; sends only diff *metadata*; reachability probe + graceful fallback; off by default.
- **Testing:** xUnit on `FolderMatch.Core` with temp-directory fixtures (deterministic, isolated); golden diffs and round-trip sync+undo tests.

## Milestones

- **M1 — Core compare engine:** `FileEntry` model, `IFolderScanner`, `IHasher` pipeline, `IDiffEngine` → `DiffResult`; xUnit coverage.
- **M2 — Sync engine:** `ISyncPlanner` (all directions + conflict rules), `ISyncExecutor` with dry-run, Trash/Recycle Bin, atomic copy, `IUndoJournal` + rollback.
- **M3 — Desktop UI:** Avalonia two-pane shell, virtualized diff tree with filters + per-item selection, dry-run plan view, progress/cancel.
- **M4 — CLI:** `compare` and `sync` verbs, glob/size/date filters, machine-readable output, scripting exit codes.
- **M5 — Local-AI summaries:** `IDiffAiService`, settings toggle, reachability probe, rule-based fallback.
- **M6 — Packaging & CI:** Windows portable zip + MSIX, macOS universal `.app` + `.dmg`, GitHub Actions matrix (windows-latest + macos-latest), release artifacts.

## Non-goals

- No cloud storage, account, or network sync — both folders are local (or locally-mounted network/USB paths).
- No real-time/continuous background watching in the initial release (on-demand compare/sync first; a watcher may come later).
- No 3-way content merge or line-level diff of file *contents* (foldermatch reconciles at the file level, not inside files).
- No version history / snapshot store — undo covers the last applied sync, not arbitrary history.
- No mobile/Linux targets in the initial scope (Windows + macOS only).

## Packaging / distribution target

- **Windows:** self-contained `win-x64` portable zip **and** MSIX package; CI on `windows-latest`.
- **macOS:** universal (`arm64` + `x64`) `.app` bundled into a `.dmg`; CI on `macos-latest`.
- **CI:** GitHub Actions matrix builds + runs `FolderMatch.Core` tests on both OSes; attaches artifacts to tagged releases.
