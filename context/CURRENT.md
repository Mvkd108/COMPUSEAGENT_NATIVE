# Current state

Last updated: 2026-08-20

## M001–M004 — Contract and runtime foundation

Status: `APPROVED`. Local `main` fast-forwarded to M004
`56b35fcab253df3049aa514dd1e0e2c42bc122a1`. `origin/main` may still be at
M003 until that merge is pushed.

- SDK `10.0.302`
- Contracts, Protobuf result/request, and `OperationRuntime` are unchanged
  by Track A. Public types were not modified.

## Track A — Filesystem `drop_files` prototype

Status: implemented on `cursor/track-a-filesystem-prototype`.

- Prompts: `context/prompts/M005-FS-IMPLEMENTATION-PROMPT.md`,
  `context/prompts/TRACK-A-VERTICAL.md`
- OLE gate: `context/OLE-IDROPTARGET-SPIKE.md`
- Discovery: `Compuse.Discovery` (`WindowsFilesystemDiscovery`)
- Router: `Compuse.Routing` (`DropFilesRouter`, refusal catalog)
- Transfer: `Compuse.Filesystem` (`IFileOperation` on an STA thread)
- Orchestration: `Compuse.DropFiles` (`DropFilesHandler`)
- CLI: `Compuse.Cli` assembly name `compuse`

Prototype success: copy or move physical files into a real directory and
return `committed` only after destination observation. Application-surface
targets refuse with `unsupported_target_kind`. Overwrite is refused
(`collision`). Multi-file preflight is all-or-nothing.

## Intentionally absent

No OLE application drop, private C ABI, GUI, Hyper-V, networking, CUA,
installers, pointer/mouse fallback, or overwrite/recycle.

## Known operational notes

- SDK `10.0.302` is installed user-locally under
  `%LOCALAPPDATA%\Microsoft\dotnet`.
- Track A Windows projects target `net10.0-windows`.
- `IFileOperation` UI is suppressed. Elevation prompts are not used; access
  denied becomes a non-committed outcome.
