# Session 2026-09-11 — Application-surface import via UI Automation (Track B)

## Scope

Integrated FlaUI into `Compuse.ApplicationSurface` and implemented a real
end-to-end `drop_files` workflow that imports a file into Windows 11 Notepad
using UI Automation, with evidence-based `committed` and explicit
refusals/indeterminate outcomes. Track A filesystem behavior was preserved
(unchanged tests still pass).

## Decisions recorded

See `context/DECISIONS.md` 2026-09-11 entries:
- Reuse FlaUI `FlaUI.Core` + `FlaUI.UIA3` v5.0.0 (MIT); reject UFO and CUA
  wholesale.
- App-surface import is a documented UI Automation file-dialog import, not an
  OLE drop; the OLE spike gate stays.
- No pointer movement or input synthesis (only UIA patterns).
- `EnumWindows` + `FromHandle` window discovery (replaced a 5 s full-desktop
  traversal per process).
- Dedicated foreground thread for the import backend so the runtime deadline
  can fire mid-operation (a synchronous backend never let `RunHandlerAsync`
  yield, so the deadline watcher was never started).
- Outcome mapping mirrors filesystem semantics; single source file per app
  operation.

## Files changed

- `Directory.Packages.props` — FlaUI.Core / FlaUI.UIA3 5.0.0
- `CompuseAgent.Native.slnx` — new project + tests
- `src/Compuse.Routing` — `WindowIdentity`, `ApplicationImportPlan`,
  `RouteDecision.ApplicationPlan`, new refusal codes
- `src/Compuse.ApplicationSurface` (new) — `IUiaSession`, `FlaUiSession`,
  `IUiaSessionFactory`, `IWindowDiscovery`, `WindowsWindowDiscovery`,
  `IApplicationImportBackend`, `NotepadImportBackend`, `ApplicationRouter`,
  execution/observation/phase types
- `src/Compuse.DropFiles/DropFilesHandler.cs` — app-surface routing,
  revalidation, mapping
- `src/Compuse.Cli` — `--app`, `--window-class`, `--window-title`, `--pid`,
  `--hwnd`; app plan output; service wiring
- Tests: `tests/Compuse.ApplicationSurface.Tests` (new), added cases in
  `tests/Compuse.DropFiles.Tests`
- `README.md`, `context/CURRENT.md`, `context/DECISIONS.md`, `context/NEXT.md`

## Verification (SDK 10.0.302)

- Locked restore, Release build 0 warnings / 0 errors, format 0 changes.
- Full solution: 289 tests passed / 0 failed / 0 skipped (was 259).
- Real Windows 11 Notepad, disposable temp files, one fresh Notepad window
  per run, every window closed afterward:
  - Copy import committed with `document_text_observed`; independent UIA
    read-back matched the source bytes.
  - Move import committed and removed the source.
  - `target_not_found`, `ambiguous_target`, stale HWND, and locked-source
    refusals verified.
  - 1-second deadline on a ~25 MB file returned `indeterminate`
    `deadline_expired`.
  - A binary file Notepad cannot round-trip returned `indeterminate`
    `verification_failed`.
  - Pointer check: cursor identical before/after a single import; foreground
    stayed on the target window.
- The user's pre-existing Notepad window ("maisha cheklist.txt") was never
  touched and was confirmed intact at the end.

## First action next session

Independent review of Track A + M012 + Track B. Commit/merge only when
explicitly requested. Do not start M013, M014, M007, or M009.
