# CompuseAgent Native

CompuseAgent Native is independent Windows software. C# and .NET 10 own contracts, runtime, routing, policy, CLI, diagnostics, managed tests, and the Cua Driver MCP adapter. A later narrow C++/WinRT component may own justified OLE/Shell operations. This repository currently contains the typed contract foundation, its canonical Protobuf v1 result representation, the v1 `drop_files` request contract, a managed operation runtime kernel, a filesystem-first `drop_files` prototype (discovery, routing, `IFileOperation` transfer, orchestration, and CLI), an application-surface `drop_files` prototype that imports a file into Windows 11 Notepad through UI Automation, and a reusable Windows computer-control foundation that supervises the pinned TryCua Cua Driver as an out-of-process MCP backend for windows, capture, accessibility, and background input.

## Module boundary

M001 establishes the executable contract vocabulary and repository bootstrap. M002 adds the versioned `compuse.v1` Protobuf binary representation of operation results and a lossless, validating mapper onto the M001 types. M003 adds the managed `drop_files` request types, a separate `compuse.v1` request schema, and a lossless, validating mapper. Invalid requests cannot be constructed or mapped into the domain model. M004 adds `Compuse.Runtime`: a managed kernel that accepts an already-validated request, assigns or preserves a correlation identifier, dispatches a typed async handler, and returns exactly one terminal `OperationResult`.

M004 adds `Compuse.Runtime`: a managed kernel that accepts an already-validated request, assigns or preserves a correlation identifier, dispatches a typed async handler, and returns exactly one terminal `OperationResult`. Track A adds filesystem discovery, a deterministic router, an `IFileOperation` backend, `DropFilesHandler`, and the `compuse` CLI. Track B adds UI Automation window discovery, a FlaUI-backed import backend, and an allowlisted Notepad file-dialog import recipe. Track C adds `Compuse.CuaDriver`: a narrow `IWindowsComputerDriver` over the pinned TryCua Cua Driver MCP stdio process. The runtime kernel never synthesizes `committed`.

Windows 11 x64 is the initial product platform. `Compuse.Contracts`, `Compuse.Requests`, `Compuse.Protocol`, and `Compuse.Runtime` target `net10.0` with no platform API dependency. All other projects target `net10.0-windows`.

## Settled architecture

- Outcome vocabulary is exactly `committed`, `refused`, `failed`, and `indeterminate`.
- An OS or API success return is diagnostic evidence only. It is never proof that an external side effect occurred.
- A `committed` result requires at least one `ExternalSideEffectObservation`.
- Protobuf binary package `compuse.v1` is the canonical external contract for operation results and `drop_files` requests. M001 JSON mapping exists only to stabilize the four outcome tokens for local typed-contract tests. ProtoJSON is not an external contract.
- Mapping a parsed Protobuf message into domain types does not preserve unknown fields. Rematerializing Protobuf from the domain model therefore drops unknown fields. Unknown fields are tolerated on parse when all known fields are valid.
- Invalid, unspecified, unknown, or internally inconsistent Protobuf messages are rejected. They are never converted to another outcome or a guessed request.
- v1 `drop_files` sources are physical files only. Supported transfer effects are copy and move. Directories, virtual files, and link creation are out of this module.
- Request paths are absolute Windows paths, lexically normalized without filesystem access or reparse-point resolution. HWND and PID values on an application-surface target are hints, never identity. Window identity is a process id plus HWND plus the UI Automation runtime-id key, revalidated before acting.
- The managed runtime owns cancellation, deadline, timeout, exception translation, cleanup, concurrency, and shutdown. Caller cancellation, deadline expiry, and shutdown after dispatch are terminal results, not `OperationCanceledException` from `RunAsync`.
- Deadlines and timeouts are enforced against an injected `IOperationClock`. The kernel does not use wall-clock `CancelAfter`.
- The future native boundary is a private, versioned C ABI. Track A does not introduce it. OLE application-drop is gated on `context/OLE-IDROPTARGET-SPIKE.md`.
- The application-surface import is a documented UI Automation file-dialog import for the Windows 11 Notepad recipe, selected explicitly by recipe; it is never described as an OLE drop. No pointer movement or input synthesis is used.
- Reusable Windows UI Automation comes from FlaUI (`FlaUI.Core` + `FlaUI.UIA3` v5.0.0, MIT) behind narrow seams in `Compuse.ApplicationSurface`. Generic computer control (windows, capture, accessibility, background input, browser tools) comes from TryCua Cua Driver (`cua-driver-rs-v0.27.0`, MIT) supervised by `Compuse.CuaDriver` over MCP stdio. Microsoft Computer-Use-Agent is a distinct project and is not consumed; Microsoft UFO remains rejected as an unrelated Python/LLM agent framework.
- Windows routing order for semantic operations: documented application/OS API → Compuse semantic backend (`IFileOperation`) → UI Automation semantic pattern → Cua Driver background action → visual/coordinate action → explicitly-permitted foreground interaction. Background is attempted first; `background_unavailable` maps to `refused`; foreground activation is recorded, never a silent fallback.
- The Cua Driver binary is never downloaded or updated by the product; all Cua/MCP payloads stay inside the adapter module.

## Prerequisites

- Git 2.53 or later, with support for `git init --initial-branch`.
- Exact .NET SDK `10.0.302`. `global.json` sets `"rollForward": "disable"` and `"allowPrerelease": false`.
- NuGet access for the first restore. Later restores use checked-in lock files in locked mode.
- MSTest `4.3.3` via the `MSTest` meta-package, pinned in `Directory.Packages.props`.
- Google.Protobuf `3.35.1` and Grpc.Tools `2.83.0` for schema compilation. Generated C# stays in ignored `obj/` directories and is not committed.
- FlaUI.Core `5.0.0` and FlaUI.UIA3 `5.0.0` for Windows UI Automation, pinned in `Directory.Packages.props`. MIT license, upstream `https://github.com/FlaUI/FlaUI`, release `v5.0.0`.
- TryCua Cua Driver Windows x64 release `cua-driver-rs-v0.27.0` for the generic computer-control adapter. Install with `irm https://cua.ai/driver/install.ps1 | iex`, or download the pinned release zip, and set `COMPUSE_CUA_DRIVER_EXE` (or the configured executable path) to `cua-driver.exe`. MIT license, upstream `https://github.com/trycua/cua`. See `THIRD-PARTY-NOTICES.md`.

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
src/Compuse.ApplicationSurface/
src/Compuse.CuaDriver/
src/Compuse.DropFiles/
src/Compuse.Cli/
tests/Compuse.Contracts.Tests/
tests/Compuse.Requests.Tests/
tests/Compuse.Protocol.Tests/
tests/Compuse.Runtime.Tests/
tests/Compuse.Discovery.Tests/
tests/Compuse.Routing.Tests/
tests/Compuse.Filesystem.Tests/
tests/Compuse.ApplicationSurface.Tests/
tests/Compuse.CuaDriver.Tests/
tests/Compuse.DropFiles.Tests/
tests/Compuse.Cli.Tests/
```

`CompuseAgent.Native.slnx` contains the four foundation projects, Track A, Track B (`Compuse.ApplicationSurface`), Track C (`Compuse.CuaDriver`), and their tests (twenty-two projects).

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

Track A copies or moves physical files into a filesystem container using Shell `IFileOperation` with UI suppressed. `committed` requires observed destination files (and, for move, observed source removal). Overwrite is refused. Application-surface selectors are handled by Track B when the app backend is configured, otherwise they refuse with `unsupported_target_kind`.

```powershell
compuse drop-files --copy --to C:\dst C:\src\a.txt
compuse drop-files --copy --to dst .\a.txt
compuse drop-files --plan --copy --to C:\dst C:\src\a.txt
compuse drop-files --copy --timeout 30 --to C:\dst C:\src\a.txt
compuse drop-files --proto
```

Relative source and destination paths are resolved against the current directory before the request is constructed. `--plan` writes the route to stdout and does not mutate files. Execute writes `outcome=` / `correlation=` / optional `code=` plus `evidence=` and `artifact=` lines on stdout (or a Protobuf `OperationResult` envelope with `--proto`). Protobuf stdin is limited to 4 MiB and oversized input is rejected before parsing. Diagnostics go to stderr. Exit codes: `0` committed or successful plan, `1` invalid request, `2` refused, `3` failed, `4` indeterminate. `IFileOperation` runs on a dedicated foreground STA and is awaited asynchronously, so a post-dispatch deadline or Ctrl+C can return `indeterminate` without joining the Shell call. The STA still finishes and releases COM; it is not killed mid-copy.

## Application-surface prototype (UI Automation)

Track B imports exactly one source file into a matching window of an
allowlisted application recipe using UI Automation (`FlaUI.UIA3`). The only
recipe today is Windows 11 Notepad, selected explicitly by
`IApplicationImportRecipeCatalog`: the recipe runs only for a positively
identified Notepad target, and any other application refuses with
`unsupported_application_recipe`. The import drives the application's own
File > Open dialog: expand the File menu, invoke Open, set the file-name edit,
invoke the primary Open button, then read the document text back through the
`TextPattern` and compare it to the source content. The plan retains the source
file's `FilesystemIdentity` and revalidates it before dispatch.

```powershell
compuse drop-files --copy --app notepad.exe --window-title "Untitled*" C:\src\a.txt
compuse drop-files --copy --app notepad.exe --pid 4242 --hwnd 0x1A2B3C C:\src\a.txt
compuse drop-files --plan --copy --app notepad.exe C:\src\a.txt
compuse drop-files --copy --timeout 30 --app notepad.exe C:\src\a.txt
```

- The process image name is required; `--window-class`, `--window-title`,
  `--pid`, and `--hwnd` are hints that disambiguate. A single matching window
  is required — multiple candidates refuse with `ambiguous_target`.
- `committed` requires the target document text to match the source (and, for
  move, observed source removal). A successful UIA invoke is diagnostic
  evidence only.
- No pointer movement or input synthesis: the backend uses UIA patterns
  (`ExpandCollapse`, `Invoke`, `Value`, `Text`) only. Opening the modal Open
  dialog can activate the target window as the app's own modal behavior; this
  is documented, not hidden.
- The import backend runs on a dedicated foreground thread, so a post-dispatch
  deadline or Ctrl+C returns `indeterminate` while the thread finishes and
  releases its UI Automation session.
- Unsupported recipes refuse with `unsupported_application_recipe`; unsupported
  app surfaces fail cleanly (`ui_automation_unsupported`,
  `import_dialog_failed`) or refuse (`target_not_found`, `ambiguous_target`,
  `target_identity_changed`, `stale_identity`, `single_source_required`).

## Computer-control foundation (Cua Driver)

Track C (`Compuse.CuaDriver`) supervises the pinned TryCua Cua Driver
(`cua-driver-rs-v0.27.0`, Windows x64, MIT) as a child `cua-driver mcp`
process and speaks MCP JSON-RPC over stdin/stdout. It is a thin adapter: every
Cua/MCP payload stays inside the module, and callers see the narrow
`IWindowsComputerDriver` interface with typed DTOs. Every wrapped tool matches
the exact schema captured from the pinned driver (see the contract fixtures in
`tests/Compuse.CuaDriver.Tests/WindowsComputerDriverContractTests.cs`).

- Transport: a persistent buffered `StdioMcpTransport.ReadLineAsync` retains
  all unread bytes. Multiple frames in one OS read, frames split across reads,
  a complete frame followed by a partial frame, CRLF and LF framing, EOF after
  an unterminated frame, oversized frames, and cancellation with a buffered
  partial frame are all covered by tests against a real child process.
- Runtime/diagnostics: initialize + version handshake (refuses < 0.27.0 and
  >= 1.0.0), `health_report`, missing/incompatible binary refusals, bounded
  stderr diagnostics, clean stdin-EOF shutdown then kill, and an enforced
  request-id ceiling (`MaxRequestId`, default 1,000,000).
- Applications/windows: `list_apps`, `list_windows`, `launch_app`, cursor
  position; PID/HWND are hints and identity is revalidated before acting.
- Desktop capture: `get_desktop_state` is modeled as the product-owned
  `DesktopState` — a full-display screenshot plus display metadata
  (screen/screenshot size, scale factor, display id, platform). It is a
  capture, never a list of application windows. Screenshot payloads are parsed
  from the MCP image content with the driver's reported dimensions.
- Observation: `get_window_state` returns the UIA tree (element-index and
  element-token addressing, element frames parsed from the driver's
  `{x,y,w,h}` shape), a screenshot, capture-coverage, and a snapshot id that
  fails closed when stale.
- Element-targeted actions (`click`, `set_value`, `type_text`, `press_key`,
  `hotkey`) pass either the `element_token` or the matching
  `snapshot_id` + `element_index` + `window_id`. An element index without its
  snapshot, or any pid/window contradiction, is rejected before dispatch.
- Basic action surface: background `click` (token or coordinates), the
  dedicated `right_click`, `double_click`, and `drag` tools, window-targeted
  `scroll`, window-targeted `press_key`/`hotkey`, clipboard read/write, and
  `verify_state` (which sends the driver's documented `expect` predicate array
  and reads the real `status` field).
- Every action records the requested delivery mode, the actual `route`, the
  actual delivery mode, the driver's `effect` (`verified`/`unverifiable`/
  `refused`), and `DispatchMayHaveOccurred`. Foreground delivery is explicit;
  a background refusal is never retried as foreground input.
- Capability enforcement: `DriverPermissionMode.Bounded` passes the documented
  `CUA_DRIVER_PERMISSION_MODE`/`CUA_DRIVER_CAPABILITY_MANIFEST_FILE`/
  `CUA_DRIVER_CAPABILITY_MANIFEST_APPROVED` environment variables to the child,
  resolves and validates the manifest as an absolute existing file, and fails
  closed at startup when a bounded half is missing. `GrantExistingBrowserProfile`
  appends the official `--grant existing-profile` launch grant. Ambient
  `CUA_DRIVER_*` permission variables are never inherited. The effective mode
  is exposed on `EffectivePermissionMode` and in `GetDiagnostics()`.
- Browser surface (Cua Driver's typed browser tools): `PrepareBrowserAsync`
  with a driver-owned disposable isolated profile, `BindBrowserAsync`,
  `SnapshotBrowserAsync` (page refs + screenshot), `NavigateBrowserAsync`,
  `BrowserClickAsync`, `BrowserTypeAsync`, `BrowserScrollAsync`, and
  `BrowserSetInputFilesAsync`. Page refs are session-scoped; stale refs and
  `browser_ref_stale` refusals map to `refused` with
  `browser_input_trust_unavailable`. Browser operations require an explicit
  session label. Existing signed-in profiles are only reachable through
  `GrantExistingBrowserProfile`; automated and live tests use disposable
  driver-owned profiles only.
- Recording: `StartRecordingAsync` (caller-selected output directory, absolute
  path validation, restricted to the explicitly approved
  `ApprovedRecordingRoot`), `GetRecordingStateAsync`, and
  `StopRecordingAsync`, with bounded turn accounting and `\\?\`-prefix
  normalization. Recording is diagnostic evidence and never establishes
  `committed`.
- Cancellation/deadlines: `notifications/cancelled` plus client-side
  deadlines; process exit, malformed frames, oversized lines, and stderr
  floods are handled; no automatic retry of possibly-mutating actions.
- Refusal mapping: `background_unavailable`, `background_occluded`,
  `browser_input_trust_unavailable`, `browser_ref_stale`, `ambiguous_target`,
  stale/expired targets, and driver permission denials map to typed
  `DriverActionResult` results (refused semantics), preferring the driver's
  structured `code` field when present. A successful response without
  structured action content is treated as a protocol error, never as a
  successful dispatch.

Driver integration tests run when `COMPUSE_CUA_DRIVER_EXE` points at the
pinned `cua-driver.exe`; otherwise they are skipped. The real-driver suite
covers Calculator (discovery, screenshot, accessibility tree, background
element-token click, independent read-back, `verify_state`, pointer/foreground
measured around the dispatch, stale-snapshot refusal, background `drag`
refusal, cancellation), a disposable Notepad document flow, File Explorer
discovery, bounded-mode denial, disposable-profile browser flow, and
recording start/query/stop. See `THIRD-PARTY-NOTICES.md` for the reuse matrix,
license, pin, and update procedure.

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
4. Locked restore succeeds for all twenty-two projects.
5. Solution list contains the twenty-two projects in `CompuseAgent.Native.slnx`.
6. Release build succeeds with 0 warnings and 0 errors.
7. Format check reports no required changes.
8. Runtime tests pass 51 cases with 0 failed and 0 skipped.
9. Full solution tests pass 374 cases with 0 failed and 0 skipped when
   `COMPUSE_CUA_DRIVER_EXE` points at the pinned `cua-driver.exe`; without the
   driver the twelve real-driver integration tests are skipped
   (362 passed + 12 skipped, 0 failed).
10. `bin\probe.dll` is ignored (exit `0`). Runtime `packages.lock.json` is not ignored (exit `1`).

If SDK `10.0.302` is unavailable, verification is blocked. Do not roll forward to another SDK.
