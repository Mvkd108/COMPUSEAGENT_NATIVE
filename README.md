# CompuseAgent Native

CompuseAgent Native is independent Windows software. C# and .NET 10 own contracts, runtime, routing, policy, CLI, diagnostics, and managed tests. A later narrow C++/WinRT component may own justified OLE/Shell operations. This repository currently contains the typed contract foundation, its canonical Protobuf v1 result representation, the v1 `drop_files` request contract, a managed operation runtime kernel, and a filesystem-first `drop_files` prototype (discovery, routing, `IFileOperation` transfer, orchestration, and CLI).

## Module boundary

M001 establishes the executable contract vocabulary and repository bootstrap. M002 adds the versioned `compuse.v1` Protobuf binary representation of operation results and a lossless, validating mapper onto the M001 types. M003 adds the managed `drop_files` request types, a separate `compuse.v1` request schema, and a lossless, validating mapper. Invalid requests cannot be constructed or mapped into the domain model. M004 adds `Compuse.Runtime`: a managed kernel that accepts an already-validated request, assigns or preserves a correlation identifier, dispatches a typed async handler, and returns exactly one terminal `OperationResult`.

M004 adds `Compuse.Runtime`: a managed kernel that accepts an already-validated request, assigns or preserves a correlation identifier, dispatches a typed async handler, and returns exactly one terminal `OperationResult`. Track A adds filesystem discovery, a deterministic router, an `IFileOperation` backend, `DropFilesHandler`, and the `compuse` CLI. Application-surface targets are refused. The runtime kernel never synthesizes `committed`.

Windows 11 x64 is the initial product platform. `Compuse.Contracts`, `Compuse.Requests`, `Compuse.Protocol`, and `Compuse.Runtime` target `net10.0` with no platform API dependency. Track A Windows projects target `net10.0-windows`.

## Settled architecture

- Outcome vocabulary is exactly `committed`, `refused`, `failed`, and `indeterminate`.
- An OS or API success return is diagnostic evidence only. It is never proof that an external side effect occurred.
- A `committed` result requires at least one `ExternalSideEffectObservation`.
- Protobuf binary package `compuse.v1` is the canonical external contract for operation results and `drop_files` requests. M001 JSON mapping exists only to stabilize the four outcome tokens for local typed-contract tests. ProtoJSON is not an external contract.
- Mapping a parsed Protobuf message into domain types does not preserve unknown fields. Rematerializing Protobuf from the domain model therefore drops unknown fields. Unknown fields are tolerated on parse when all known fields are valid.
- Invalid, unspecified, unknown, or internally inconsistent Protobuf messages are rejected. They are never converted to another outcome or a guessed request.
- v1 `drop_files` sources are physical files only. Supported transfer effects are copy and move. Directories, virtual files, and link creation are out of this module.
- Request paths are absolute Windows paths, lexically normalized without filesystem access or reparse-point resolution. HWND and PID values on an application-surface target are hints, never identity.
- The managed runtime owns cancellation, deadline, timeout, exception translation, cleanup, concurrency, and shutdown. Caller cancellation, deadline expiry, and shutdown after dispatch are terminal results, not `OperationCanceledException` from `RunAsync`.
- Deadlines and timeouts are enforced against an injected `IOperationClock`. The kernel does not use wall-clock `CancelAfter`.
- The future native boundary is a private, versioned C ABI. Track A does not introduce it. OLE application-drop is gated on `context/OLE-IDROPTARGET-SPIKE.md`.
- CUA is independent software. This repository does not copy, vendor, modify, reference, download, or integrate CUA. A later module may consume a pinned external backend solely through its public interface.

## Prerequisites

- Git 2.53 or later, with support for `git init --initial-branch`.
- Exact .NET SDK `10.0.302`. `global.json` sets `"rollForward": "disable"` and `"allowPrerelease": false`.
- NuGet access for the first restore. Later restores use checked-in lock files in locked mode.
- MSTest `4.3.3` via the `MSTest` meta-package, pinned in `Directory.Packages.props`.
- Google.Protobuf `3.35.1` and Grpc.Tools `2.83.0` for schema compilation. Generated C# stays in ignored `obj/` directories and is not committed.

Do not use SDK 8, another .NET 10 feature band, or a preview SDK.

## Repository layout

```text
.editorconfig
.gitattributes
.gitignore
global.json
Directory.Build.props
Directory.Packages.props
CompuseAgent.Native.slnx
README.md
proto/compuse/v1/operation_result.proto
proto/compuse/v1/drop_files.proto
src/Compuse.Contracts/
src/Compuse.Requests/
src/Compuse.Protocol/
src/Compuse.Runtime/
src/Compuse.Discovery/
src/Compuse.Routing/
src/Compuse.Filesystem/
src/Compuse.DropFiles/
src/Compuse.Cli/
tests/Compuse.Contracts.Tests/
tests/Compuse.Requests.Tests/
tests/Compuse.Protocol.Tests/
tests/Compuse.Runtime.Tests/
tests/Compuse.Discovery.Tests/
tests/Compuse.Routing.Tests/
tests/Compuse.Filesystem.Tests/
tests/Compuse.DropFiles.Tests/
tests/Compuse.Cli.Tests/
```

`CompuseAgent.Native.slnx` contains the four foundation projects, five Track A projects, and their tests (eighteen projects).

Build outputs under `bin/`, `obj/`, `.artifacts/`, and `TestResults/` are ignored. All `packages.lock.json` files are tracked. Generated protocol C# is not tracked.

## Outcome semantics

| Outcome | Meaning | Typed detail | Commitment evidence |
| --- | --- | --- | --- |
| `committed` | The requested external side effect was observed. | none | Non-empty evidence including at least one `ExternalSideEffectObservation` |
| `refused` | An intentional decision not to attempt or continue. | `RefusalInfo` | Optional |
| `failed` | A technical failure while attempting the operation. | `FailureInfo` | Optional |
| `indeterminate` | The outcome could not be established. | none | Optional |

`VerificationEvidenceKind`:

- `OsApiReturn` records an API or OS call result. It cannot by itself prove an external side effect.
- `ExternalSideEffectObservation` records observation of the requested state outside the invoking API return value.
- `DiagnosticArtifact` references a diagnostic artifact and does not prove commitment.

## Protocol mapping

`OperationResultProtoMapper` maps operation results. `DropFilesRequestProtoMapper` maps `drop_files` requests. Each exposes `ToProto` and `FromProto`. Correlation identifiers must be the exact 36-character lowercase GUID `D` form. Protobuf zero/unspecified enums are invalid at the managed boundary. Timestamp nanoseconds must be divisible by 100; sub-tick values are rejected rather than truncated.

## `drop_files` request

A v1 request names one correlation identity, 1 to 1024 unique physical-file sources, a copy or move effect, and exactly one target:

- `FilesystemContainer` — an absolute Windows directory path
- `ApplicationSurface` — a process image file name plus optional window class, title, HWND hint, and PID hint

Paths are normalized lexically (`/` to `\`, `.` / `..` collapse) without touching the filesystem. Relative paths, device prefixes (`\\?\`, `\\.`, `\??\`), duplicate sources, reserved DOS device names, and zero HWND/PID hints are rejected. An optional UTC deadline is request metadata on the M003 type. Callers that pass `request.DeadlineUtc` into `OperationRuntime.RunAsync` have that deadline enforced by the M004 kernel.

## Operation runtime

`OperationRuntime` dispatches `IOperationHandler<TRequest>` implementations. It references only `Compuse.Contracts`. Correlation is passed explicitly; the kernel never reads it from `TRequest`. Admission refusals occupy no in-flight slot. After dispatch, the first terminal result wins, including when a later handler return is a valid `committed`. Cleanup runs LIFO after that result is chosen and cannot replace it.

## Filesystem prototype

Track A copies or moves physical files into a filesystem container using Shell `IFileOperation` with UI suppressed. `committed` requires observed destination files (and, for move, observed source removal). Overwrite is refused. Application-surface selectors return `unsupported_target_kind`.

```powershell
compuse drop-files --copy --to C:\dst C:\src\a.txt
compuse drop-files --plan --copy --to C:\dst C:\src\a.txt
compuse drop-files --proto
```

`--plan` writes the route to stdout and does not mutate files. Execute writes `outcome=` / `correlation=` on stdout (or a Protobuf `OperationResult` envelope with `--proto`). Diagnostics go to stderr. Exit codes: `0` committed or successful plan, `1` invalid request, `2` refused, `3` failed, `4` indeterminate.

## Verification

Run every command from the repository root. Restore lock files once after changing package versions, then use locked mode.

```powershell
dotnet --version
git status --short --branch
git rev-parse HEAD
dotnet restore .\CompuseAgent.Native.slnx --locked-mode --verbosity minimal
dotnet sln .\CompuseAgent.Native.slnx list
dotnet build .\CompuseAgent.Native.slnx --configuration Release --no-restore
dotnet format .\CompuseAgent.Native.slnx --verify-no-changes --no-restore --verbosity diagnostic
dotnet test .\tests\Compuse.Runtime.Tests\Compuse.Runtime.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=normal"
dotnet test .\CompuseAgent.Native.slnx --configuration Release --no-build --no-restore --logger "console;verbosity=normal"
git check-ignore -q --no-index .\src\Compuse.Runtime\bin\probe.dll
git check-ignore -q --no-index .\src\Compuse.Runtime\packages.lock.json
```

Expected results:

1. `dotnet --version` prints `10.0.302`.
2. Git reports a committed `main` branch.
3. `git rev-parse HEAD` prints the current baseline revision.
4. Locked restore succeeds for all eighteen projects.
5. Solution list contains the eighteen projects in `CompuseAgent.Native.slnx`.
6. Release build succeeds with 0 warnings and 0 errors.
7. Format check reports no required changes.
8. Runtime tests pass at least 51 cases with 0 failed and 0 skipped.
9. Full solution tests pass at least 238 cases with 0 failed and 0 skipped.
10. `bin\probe.dll` is ignored (exit `0`). Runtime `packages.lock.json` is not ignored (exit `1`).

If SDK `10.0.302` is unavailable, verification is blocked. Do not roll forward to another SDK.
