# Decisions

## 2026-08-15 — Independent product, not a CUA fork

Do not copy, vendor, modify, or integrate CUA source. A later module may
consume a pinned external backend solely through its public interface.

## 2026-08-15 — C# / .NET 10 owns the managed product

Contracts, runtime, routing, policy, CLI, diagnostics, and tests are C# on
.NET 10. C++/WinRT is reserved for a later narrow OLE/Shell boundary. The
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

