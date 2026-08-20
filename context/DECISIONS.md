# Decisions

## 2026-08-15 — Independent product, not a CUA fork

Do not copy, vendor, modify, or integrate CUA source. A later module may
consume a pinned external backend solely through its public interface.

## 2026-08-15 — C# / .NET 10 owns the managed product

Contracts, runtime, routing, policy, CLI, diagnostics, and managed tests are C#
on .NET 10. C++/WinRT is reserved for a later narrow OLE/Shell boundary. The
earlier C++-throughout plus Go control-plane plan is superseded.

## 2026-08-15 — Four outcomes, evidence-backed commitment

Outcome vocabulary is exactly `committed`, `refused`, `failed`, and
`indeterminate`. An OS/API return cannot by itself produce `committed`.

## 2026-08-15 — First capability is reliable drop_files

Use the correct Windows file-transfer mechanism. Do not treat every transfer
as a mouse gesture. Do not implement `drop_files` until a dedicated module is
prepared.

## 2026-08-15 — Module protocol

Codex (or another architect) prepares bounded implementation prompts and
reviews actual trees. Implementation agents own one module, run verification
themselves, and do not commit unless the current plan explicitly allows it.

## 2026-08-18 — M001 is the committed baseline

`main` begins at `42e5d0ca368939d436f5d0a8cc25d82316dc1bb7`. Later modules
must not modify `Compuse.Contracts` unless a replacement prompt says so.

## 2026-08-18 — Protobuf binary is the canonical external result contract

Package `compuse.v1` is the versioned result schema. ProtoJSON is not an
external contract. Unknown fields are tolerated on parse; mapping into M001
types does not preserve them. Field and enum numbers in
`operation_result.proto` are immutable after M002 approval.

## 2026-08-18 — M002 is the committed result-contract baseline

`main` includes M002 at `0d8e66d538d019237268549c80bcdec753b9b9dd`. Existing
result field and enum numbers remain immutable.

## 2026-08-18 — v1 drop_files request contract is descriptive only

M003 describes one `drop_files` operation without executing it. v1 sources
are physical files only; effects are copy and move; targets are a filesystem
container path or an application-surface selector. Paths are absolute and
lexically normalized without resolving reparse points or touching the
filesystem. HWND and PID values are optional hints, never identity. Request
validation failures cannot become operation results.

## 2026-08-18 — Managed runtime owns lifecycle, not commitment

M004 adds `OperationRuntime` as the shared handler kernel. It never
synthesizes `committed`. Caller cancellation, deadline expiry, and shutdown
after dispatch are terminal `OperationResult` values, not
`OperationCanceledException` from `RunAsync`. Deadlines and timeouts use
`IOperationClock.Delay`; wall-clock `CancelAfter` is forbidden. A request
deadline is enforced only when the caller passes it explicitly; the kernel
does not read fields from `TRequest`. Admission refusals do not occupy an
in-flight slot. The first successful completion wins, including discarding a
late valid `committed`. Cleanup is LIFO and cannot replace the terminal
result. `Compuse.Runtime` references only `Compuse.Contracts`.

## 2026-08-18 — M004 is the committed runtime baseline

`main` includes M004 at `56b35fcab253df3049aa514dd1e0e2c42bc122a1` (merged
from `cursor/m004-operation-runtime`). Independent review found no defects
against the M004 prompt. Later modules must not modify `Compuse.Runtime`
public types unless a replacement prompt says so.

## 2026-08-20 — Track A is filesystem-first

The original M005–M013 sequence put native ABI and OLE on the critical path
before any file could move. Track A is the prototype path: filesystem
discovery, deterministic routing, `IFileOperation` transfer, orchestration,
and a CLI. Application-surface window discovery, M007, and M009 wait until
Track A works.

See `context/prompts/M005-FS-IMPLEMENTATION-PROMPT.md` and
`context/prompts/TRACK-A-VERTICAL.md`.

## 2026-08-20 — Filesystem identity tuple

A filesystem object is identified by volume serial plus file index from
`GetFileInformationByHandle`, with the normalized path as locator only. Opens
use `FILE_FLAG_OPEN_REPARSE_POINT`. HWND and PID remain hints, never identity.

## 2026-08-20 — Prototype transfer policy

- Overwrite is `refused` (`collision`). No recycle and no rename-on-collision.
- Multi-file operations are all-or-nothing at preflight.
- Filesystem destinations always route to the Shell `IFileOperation` backend
  with UI suppressed. There is no mouse/pointer route.
- `IFileOperation` runs on an STA thread. Managed COM is sufficient for Track
  A; a private C ABI is not introduced yet.
- Application-surface targets refuse with `unsupported_target_kind`.

## 2026-08-20 — v1 refusal code catalog

`unsupported_target_kind`, `source_not_found`, `source_not_file`,
`source_inaccessible`, `destination_missing`, `destination_not_container`,
`destination_inaccessible`, `collision`, `integrity_mismatch`,
`ambiguous_target`, `verification_unavailable`, `stale_identity`.

Codes remain open strings on `RefusalInfo`; this catalog is the Track A
vocabulary so callers can branch without guessing.

## 2026-08-20 — OLE and native ABI remain gated

Do not implement M007 or M009 until the spike in
`context/OLE-IDROPTARGET-SPIKE.md` proves a documented no-pointer,
no-focus `IDropTarget` path with independent observation evidence. If that
spike fails, application-surface stays refused in v1.
