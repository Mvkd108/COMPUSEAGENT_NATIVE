# Track A remaining modules (decision-complete)

These decisions apply to M006-FS, M008, M010-FS, and M011-lite. Do not modify M001–M004 public types or proto field numbers.

## Shared frozen policy

- Overwrite: refuse. No recycle, no rename-on-collision, no silent replace.
- Multi-file: all-or-nothing preflight. If any source or destination child is not acceptable, refuse the whole request before mutation.
- Duplicate destination file names across sources: `collision`.
- Transfer API: `IFileOperation` with `FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_NOCONFIRMMKDIR | FOFX_EARLYFAILURE`. Run the COM calls on an STA thread. No `File.Copy` fallback.
- Verification: destination exists, size matches snapshot length. For move, source path must no longer exist (or no longer have the pre-move file index). `committed` requires `ExternalSideEffectObservation`. HRESULT alone is `OsApiReturn`.
- Partial mutation after a failed `PerformOperations`: `indeterminate` if any destination appeared or any source disappeared; `failed` if nothing changed.
- Revalidation: rediscover immediately before execute; refuse `stale_identity` if volume serial, file index, or size changed.
- OLE, C ABI, mouse, focus, and elevation prompts are out of Track A.
- Application-surface: `unsupported_target_kind` refusal.

## Refusal code catalog

`unsupported_target_kind`, `source_not_found`, `source_not_file`, `source_inaccessible`, `destination_missing`, `destination_not_container`, `destination_inaccessible`, `collision`, `integrity_mismatch`, `ambiguous_target`, `verification_unavailable`, `stale_identity`.

Precedence: unsupported target, then destination statuses, then source statuses in request order, then collision.

## M006-FS — Deterministic policy and mechanism router

Project: `Compuse.Routing` (`net10.0-windows` is unnecessary; `net10.0` is fine).
No filesystem or COM calls. Inputs are snapshots. Identical inputs produce identical plans.
Plans: `backend=filesystem`, effect copy or move, `verification=size_and_file_id`.
Exhaustive decision-table tests. No mouse route.

## M008 — Shell/filesystem transfer backend

Project: `Compuse.Filesystem` on `net10.0-windows`.
Execute an `ExecutionPlan` via `IFileOperation`. Return API HRESULT plus independent `PathInspection` observations. Do not construct `OperationResult`.
Real temp-file tests. Cleanup fixtures.

## M010-FS — Integrated drop_files orchestration

Project: `Compuse.DropFiles`.
`DropFilesHandler : IOperationHandler<DropFilesRequest>` discovers, routes, revalidates, executes, maps evidence to `OperationResult`.
Never synthesize `committed` from HRESULT alone.
End-to-end temp-directory copy and move tests through `OperationRuntime`.

## M011-lite — CLI

Project: `Compuse.Cli`, assembly name `compuse`, `net10.0-windows`.
`CliApplication.RunAsync` is the testable entry.

```
compuse drop-files --copy|--move --to <dir> [--plan] [--correlation <guid>] <file>...
compuse drop-files --proto [--plan]
```

- Invalid request construction: stderr, exit `1`, no `OperationResult`.
- `--plan`: discover + route only; stdout plan text; exit `0` on a plan, `2` on refusal.
- Execute: stdout text `outcome=` / `correlation=` / optional `code=`, or `--proto` binary `OperationResult` envelope.
- Diagnostics on stderr. Exit `0` committed, `2` refused, `3` failed, `4` indeterminate.
- Ctrl+C cancels the runtime token.
