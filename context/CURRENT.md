# Current state

Last updated: 2026-08-22

## M001–M004 — Contract and runtime foundation

Status: `APPROVED`. Local `main` fast-forwarded to M004
`56b35fcab253df3049aa514dd1e0e2c42bc122a1`. `origin/main` may still be at
M003 until that merge is pushed.

- SDK `10.0.302`
- Contracts, Protobuf result/request, and `OperationRuntime` public types
  were not modified.

## Track A — Filesystem `drop_files` prototype

Status: implemented on `cursor/track-a-filesystem-prototype`, including a
usable CLI loop (relative paths, evidence output, timeout, cancel-safe
transfer). M012 repairs are in the working tree and request independent
review; they are not an approval.

- Prompts: `context/prompts/M005-FS-IMPLEMENTATION-PROMPT.md`,
  `context/prompts/TRACK-A-VERTICAL.md`
- Repair: architect roadmap M012 (async execution, advise integrity,
  least-privilege discovery)
- OLE gate: `context/OLE-IDROPTARGET-SPIKE.md`
- Discovery: `Compuse.Discovery` (`WindowsFilesystemDiscovery`)
- Router: `Compuse.Routing` (`DropFilesRouter`, refusal catalog)
- Transfer: `Compuse.Filesystem` (`ITransferBackend.ExecuteAsync` on a
  dedicated foreground STA with an `IFileOperationProgressSink` cancellation
  sink)
- Orchestration: `Compuse.DropFiles` (`DropFilesHandler` awaits the backend)
- CLI: `Compuse.Cli` assembly name `compuse`

Prototype success: copy or move physical files into a real directory and
return `committed` only after destination observation. Application-surface
targets refuse with `unsupported_target_kind`. Overwrite is refused
(`collision`). Multi-file preflight is all-or-nothing. Destination length
mismatch after transfer is `indeterminate` with `integrity_mismatch`
evidence. Uninspectable destinations after transfer are `failed` with
`verification_unavailable` when no side effect was observed.

CLI: relative paths resolve against the current directory. Execute stdout
includes `evidence=` / `artifact=` lines. `--timeout <seconds>` sets the
runtime deadline. Pre-cancelled tokens do not mutate. Ctrl+C or deadline
after dispatch is `indeterminate` without waiting for `PerformOperations`;
the STA transfer is not killed mid-copy.

Destination capability discovery opens with exactly `FILE_ADD_FILE` and
creates no probe file. Failed `IFileOperation.Advise` skips flags, item
creation, queue, and perform.

## Intentionally absent

No OLE application drop, private C ABI, GUI, Hyper-V, networking, CUA,
installers, pointer/mouse fallback, or overwrite/recycle.

## Known operational notes

- SDK `10.0.302` is installed user-locally under
  `%LOCALAPPDATA%\Microsoft\dotnet`.
- Track A Windows projects target `net10.0-windows`.
- `IFileOperation` UI is suppressed. Elevation prompts are not used; access
  denied becomes a non-committed outcome.
- Runtime cancellation or deadline can win the first-terminal-result race and
  report `indeterminate` while the Shell operation finishes. The process waits
  for the foreground STA thread rather than aborting COM.

Verified on SDK `10.0.302`: Release 0 warnings, format 0 changes, 258 tests
passed (was 250). Real copy/move tests observed destination payloads and
source removal.
