# foldermatch

Cross-platform **folder compare & sync** utility for **Windows 10/11** and **macOS**. Point it at two folders, see exactly what differs — new, changed, deleted, and conflicting files — then mirror or synchronize them safely with a dry-run preview and one-click undo. Offline and privacy-first: everything runs locally, no cloud account required.

## Overview

foldermatch answers a question every desktop user eventually has: *"What's different between these two folders, and how do I make them match — without clobbering something I care about?"* It scans a **left** and a **right** folder, builds a reconciled diff (by name, then size/modified-time, then optional content hash), and shows a clear, filterable tree of the differences. From there you choose a sync direction and apply changes with full preview, safety guardrails, and an undo journal.

It's built for the common real-world cases:
- Keeping a laptop folder and an external/USB drive in sync
- Reconciling a working copy against a backup
- Comparing two versions of a project or document tree
- Verifying a copy actually completed correctly (content-hash compare)

## Motivation

Built-in tools are either too blunt (drag-and-drop copy that silently overwrites) or too scary (raw `robocopy` / `rsync` flags with no preview). Cloud sync services require accounts, upload your files, and don't help when both folders are local. foldermatch fills the gap with a **visual, safe, local** compare-and-sync tool that:

- Shows you *exactly* what will change **before** anything is touched (dry-run by default)
- Distinguishes **new / updated / deleted / conflict / identical** at a glance
- Detects true content differences via a size → partial-hash → full-hash pipeline (not just timestamps that lie)
- Never deletes without an undo path (Recycle Bin / Trash by default + JSON undo journal)
- Works fully offline; your file names and contents never leave the machine

## Use cases

- **Backup verification** — content-hash compare a source tree against its backup to prove every byte matches.
- **One-way mirror** — make `right` an exact mirror of `left` (adds, updates, and optional deletes), e.g. laptop → external drive.
- **Two-way sync** — reconcile two folders that both changed, with explicit conflict handling (newer-wins / larger-wins / ask).
- **Pre-copy audit** — before overwriting a folder, see which files would actually change and which are already identical.
- **Selective sync** — include/exclude by glob, size, or date; check/uncheck individual files in the diff tree before applying.

## How to use

### Windows 10/11 quickstart

1. Download the latest `foldermatch-win-x64.zip` from Releases and unzip (portable, no install), or install the MSIX package.
2. Launch **foldermatch**.
3. Pick a **Left** folder and a **Right** folder.
4. Choose a **compare mode** (Quick: size + date, or Thorough: content hash) and click **Compare**.
5. Review the diff tree — filter by *New / Updated / Deleted / Conflict / Identical*.
6. Pick a **sync direction** (Mirror L→R, Mirror R→L, or Two-way), review the **dry-run plan**, then **Apply**.
7. Made a mistake? **Undo** restores the previous state from the undo journal.

### macOS quickstart

1. Download `foldermatch-macos-universal.dmg` from Releases, open it, and drag **foldermatch** to Applications.
2. On first launch, right-click → **Open** to clear Gatekeeper (unsigned dev build), then grant folder access when prompted.
3. Follow the same Compare → review → choose direction → dry-run → Apply → Undo flow as above.

### Headless CLI (both platforms)

```
# Compare two folders, print a diff summary
foldermatch compare ./photos /Volumes/Backup/photos --mode hash

# Dry-run a mirror (no changes made)
foldermatch sync ./photos /Volumes/Backup/photos --direction mirror-lr --dry-run

# Apply a two-way sync, sending deletions to Trash and writing an undo journal
foldermatch sync ./docsA ./docsB --direction two-way --conflict newer-wins --trash --journal
```

## Example workflow

```
# 1. Thorough compare of a working copy vs. its backup
foldermatch compare ~/Projects/site /Volumes/Backup/site --mode hash --exclude "**/node_modules/**"

# Output:
#   =  1,204 identical
#   +     18 new on left (not in backup)
#   ~      6 updated (content differs)
#   -      2 deleted on left (still in backup)
#   !      1 conflict (both changed since last known state)

# 2. Preview making the backup match the working copy
foldermatch sync ~/Projects/site /Volumes/Backup/site --direction mirror-lr --dry-run

# 3. Apply for real, safely
foldermatch sync ~/Projects/site /Volumes/Backup/site --direction mirror-lr --trash --journal
```

### Run the desktop app from source

The Avalonia desktop app uses the same .NET 8 codebase on Windows and macOS:

```bash
dotnet run --project src/FolderMatch.App/FolderMatch.App.csproj
```

Choose both folders, compare, check the entries to include, and select **Preview dry run**. The Apply button stays disabled until a plan has been reviewed. Deletes go to Recycle Bin / Trash and each apply writes an undo journal under the per-user app-data folder.

## Local-AI integration (optional)

foldermatch works fully without any AI. When enabled, an optional local-AI assist connects to an **Ollama** or **llama.cpp** OpenAI-compatible endpoint on `localhost` to:

- **Summarize a diff** in plain language ("Mostly new photos from August plus a few edited documents; one config file conflicts").
- **Explain conflicts** and suggest a resolution strategy.
- **Group changes** by theme for large diffs.

Design constraints:
- **Off by default**, opt-in in settings.
- **Local-only** — sends just file *metadata* (names, sizes, dates, change types), never file contents, and only to `localhost`.
- Reachability probe on startup; if no model is reachable, foldermatch silently falls back to the built-in rule-based summary.
- Tiny-model friendly: Llama 3.2 / Qwen2.5 / Phi-3-mini / MiniCPM-class models are sufficient.

## Current status / milestones

🚧 **Early scaffolding.** This repo currently contains the project plan and backlog. See [PLAN.md](./PLAN.md) and the issue tracker.

- [ ] M1 — Core compare engine (diff model + hash pipeline)
- [ ] M2 — Sync engine (mirror / two-way, conflict rules, safe apply + undo)
- [ ] M3 — Desktop UI (diff tree, filters, dry-run preview)
- [ ] M4 — CLI (compare / sync, scripting-friendly)
- [ ] M5 — Optional local-AI diff summaries
- [ ] M6 — Packaging & CI (Windows zip/MSIX, macOS .app/.dmg)
