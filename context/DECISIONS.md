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

## 2026-08-20 — CLI is the Track A product surface

The filesystem prototype is usable from `compuse` without requiring already-
absolute paths. The CLI resolves relative source and `--to` paths with
`Path.GetFullPath` against the current directory, then constructs M003
request types. That resolution is CLI-only; request types still forbid
relative paths.

Execute stdout stays key=value and adds `evidence=<kind>:<code>` plus
`artifact=` when an evidence item carries a path. `--timeout <seconds>`
(1–86400) is passed to `OperationRuntime.RunAsync` as the deadline.

## 2026-08-20 — Cancel-safe IFileOperation

`IFileOperation` runs on a foreground STA thread so process exit does not
kill COM mid-copy. A managed `IFileOperationProgressSink` returns `E_ABORT`
when the cancellation token is signaled. After `PerformOperations` starts,
the backend always inspects destinations with an uncancellable token.
`committed` still requires independent observation; a length mismatch is
`indeterminate` with `integrity_mismatch` evidence. Cancellation before
mutation does not copy or move files.

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

## 2026-08-22 — M012 async Shell execution and least-privilege probe

`ITransferBackend.ExecuteAsync` returns an incomplete task. `IFileOperation`
always runs on a dedicated foreground STA, including when the caller is
already STA. The caller path does not `Join`, `.Result`, or `.Wait()`.
Post-dispatch deadline and cancellation can complete the runtime result
while the STA finishes and releases COM. Failed `Advise` skips
`SetOperationFlags`, Shell-item creation, queue, and perform, and is never
unadvised. Successful `Advise` is paired with exactly one `Unadvise`.
Destination add-file discovery requests exactly `FILE_ADD_FILE` and creates
no probe child. M001–M004 public types were not changed.

## 2026-09-11 — Reuse FlaUI for Windows UI Automation; reject UFO and CUA wholesale

The earlier blanket "no CUA" restriction is revised: it never justified
refusing the same documented, maintained Windows components that a CUA-style
backend would use. The reusable, license-clean, Windows-only piece is FlaUI
(`FlaUI.Core` + `FlaUI.UIA3`, v5.0.0, MIT, upstream
`https://github.com/FlaUI/FlaUI`, release `v5.0.0`). It wraps Microsoft UI
Automation COM and supports `net8.0-windows7.0`, consumed from our
`net10.0-windows` projects. Updates are NuGet-pinned via
`Directory.Packages.props` and per-project `packages.lock.json` in locked
mode; no source is vendored or forked.

- Microsoft UFO (MIT) is not adopted: UFO² is a Python/LLM agent framework
  with multi-device and cross-platform orchestration. Using it would add a
  second runtime and an unrelated agent framework, which the reuse rules
  forbid. Its Windows interaction ideas are served by FlaUI directly.
- Microsoft's Computer-Use-Agent repository does not publish a stable,
  license-uniform, separable .NET component; its Windows control surface is
  built on UIA/FlaUI. We use the underlying maintained component instead of
  the agent framework.
- All Windows-only usage. No macOS/Linux backends, containers, cloud
  orchestration, or unrelated agent frameworks are imported.

## 2026-09-11 — Application-surface import is UI Automation, not OLE drop

`drop_files` against an `ApplicationSurface` target is implemented as a
documented UI Automation file-dialog import for an allowlisted application
recipe (Windows 11 Notepad: File > Open menu item, file-name edit `1148`,
primary `SplitButton` `1`, document text read-back). This is reported
accurately as a UI Automation file-dialog import, never as an OLE drop.
The OLE `IDropTarget` gate in `context/OLE-IDROPTARGET-SPIKE.md` stays in
force for a future true OLE drop; this module does not use `DoDragDrop`,
`WM_DROPFILES`, or pointer/focus synthesis.

## 2026-09-11 — No pointer movement or input synthesis in the app surface

The `FlaUiSession` uses only UIA patterns: `ExpandCollapsePattern`,
`InvokePattern`, `ValuePattern`, `TextPattern`, and UIA `FromHandle`/property
reads. It never calls FlaUI `Mouse`, `Keyboard`, `SetCursorPos`,
`SendInput`, `mouse_event`, or `SetForegroundWindow`. A tight real-app check
measured the pointer position unchanged across a single import
(before == after) and the foreground window staying on the target app.
Opening the modal Open dialog can activate the target window (the app's own
modal behavior); that is documented, not hidden.

## 2026-09-11 — Window discovery uses EnumWindows plus UIA FromHandle

Window resolution enumerates processes by image-name-derived process name,
finds top-level HWNDs with `EnumWindows` + `GetWindowThreadProcessId`, then
attaches UIA elements via `FromHandle`. This replaced an O(processes × full
desktop traversal) `FindAllDescendants` loop that took seconds. HWND, PID,
window class, and window title remain hints; `WindowIdentity` (process id,
HWND, UIA runtime-id key) is revalidated before acting and refuses with
`target_identity_changed` when it changes. Multiple candidate windows refuse
with `ambiguous_target`.

## 2026-09-11 — Application import runs on a dedicated foreground thread

`NotepadImportBackend.ExecuteAsync` starts a dedicated foreground thread that
owns the `IUiaSession` (created and disposed on that thread) and returns an
incomplete `ValueTask`, mirroring the filesystem STA backend. A synchronous
backend that never yields blocked the M004 runtime from ever starting its
deadline watcher (`RunHandlerAsync` never reached an await), so a 1-second
deadline was silently ignored. The thread fix restores deadline and caller
cancellation: a post-dispatch deadline returns `indeterminate` while the UIA
thread finishes and disposes its COM session. Cancellation during the dialog
flow best-effort invokes the dialog Cancel button and dismisses the Notepad
"cannot find file" error dialog (`DismissErrorDialog`) so a cancelled import
does not leave a modal dialog behind.

## 2026-09-11 — App-surface outcome mapping mirrors filesystem semantics

`committed` requires an `ExternalSideEffectObservation` of the target
document text matching the source file (plus observed source removal for
move). A successful UIA invoke alone is diagnostic evidence
(`uia_file_dialog_import`, `OsApiReturn`) and can never commit. Text mismatch
is `indeterminate` (`verification_failed`); unreadable text is
`indeterminate` (`verification_unavailable`); target exit is `indeterminate`
(`target_exited`); menu not drivable is `failed` (`ui_automation_unsupported`)
or `failed` (`import_dialog_failed`); stale identity, no window, ambiguous
window, and unreadable source are `refused`. The app-surface prototype
accepts exactly one source file; more is `refused`
(`single_source_required`).

## 2026-09-11 — Application recipes are allowlisted

The `--app` path is driven by an explicit recipe catalog
(`IApplicationImportRecipeCatalog`, `ApplicationRecipeCatalog`,
`IApplicationImportRecipe`). The Windows 11 Notepad recipe
(`NotepadImportRecipe`) runs only for a positively identified Notepad target
(process image name derived from the selector after window resolution
succeeded). Any other application refuses with `unsupported_application_recipe`
before dispatch. The CLI wires the catalog; a handler without a catalog
refuses app surfaces with `unsupported_target_kind`.

## 2026-09-11 — Source identity is part of the application import plan

`ApplicationImportPlan` retains the source `FilesystemIdentity` and the
handler revalidates it (`SameObjectAs`) in addition to byte length before
dispatch, so a same-length replacement file at the same path is refused with
`stale_identity`. This mirrors the filesystem plan's identity check.

## 2026-09-11 — Generic primitives carry no recipe labels

`IUiaSession` is a generic UI Automation seam: menu expand/invoke by name,
wait for a window by title, set an Edit's value by automation id, invoke a
SplitButton by automation id and name, close a dialog by button name, dismiss a
modal dialog by title and button name, wait for a window to close, read
document text. Notepad-specific labels and control IDs (`File`, `Open`,
dialog `Open`, edit `1148`, split button `1`/`Open`, `Cancel`, error dialog
`Notepad`/`OK`) live in `NotepadImportBackend`, not in the generic code.

## 2026-09-11 — TryCua Cua Driver, Microsoft Computer-Use-Agent, and FlaUI are distinct

Documentation now names all three correctly. We consume (a) FlaUI
(`FlaUI.Core` + `FlaUI.UIA3` 5.0.0, MIT) for the retained Notepad recipe's UI
Automation, and (b) TryCua Cua Driver (0.27.0, MIT) as the generic
computer-control backend over MCP stdio. Microsoft Computer-Use-Agent is a
separate project and is not consumed. Microsoft UFO (MIT) remains rejected as
an unrelated Python/LLM agent framework.

## 2026-09-11 — Cua Driver is a supervised out-of-process MCP backend

`Compuse.CuaDriver` starts the pinned `cua-driver mcp` executable as a child
process and speaks MCP JSON-RPC over stdin/stdout (newline-delimited, bounded
message size). It never downloads or updates the executable, never shells out,
never auto-retries a possibly-mutating action, and isolates every Cua/MCP
payload behind the narrow `IWindowsComputerDriver` interface. Missing or
incompatible binaries surface `DriverUnavailableException` /
`DriverIncompatibleException` (clear refusals/diagnostics before dispatch).
Cancellation sends `notifications/cancelled` and completes the pending call;
deadlines are enforced client-side; process exit, malformed frames, oversized
lines, and stderr floods are handled and bounded.

## 2026-09-11 — Windows routing policy and foreground authorization

The explicit Windows routing order for a semantic operation is:
(1) documented application/OS API, (2) Compuse semantic backend such as
`IFileOperation`, (3) UI Automation semantic pattern, (4) Cua Driver background
action, (5) visual/coordinate action, (6) foreground interaction only when
separately permitted. Each action records which route was used
(`DriverActionResult.Route`). Background delivery is attempted first for
background-requested operations; when the target cannot accept it the driver
returns a structured `background_unavailable` refusal that maps to `refused`,
and the product never silently falls back to `SetForegroundWindow`,
`SendInput`, or the system input queue. Foreground interaction is a separate
authorized mode and records that foreground activation occurred.

## 2026-09-11 — Element and window state are token- and identity-protected

Cua Driver element tokens are session-scoped and short-lived; a client keeps
one driver session for a capture→act→verify turn and must refresh
`get_window_state` before acting after any gap. `WindowIdentity` (PID + HWND +
UIA runtime-id) is revalidated before acting; stale windows, stale element
tokens, and stale snapshots refuse with `stale_identity`-style codes. A
snapshot is an opaque token that fails closed after the underlying state
changes.

## 2026-09-11 — Cua Driver adapter is schema-exact and hardened (Track C review)

The pinned `cua-driver-rs-v0.27.0` MCP contract was captured from the real
binary (`list-tools`/`describe` plus live responses) and every wrapped tool
was corrected to that contract. Confirmed corrections: `clipboard_read` must
request `include_text:true`; `verify_state` takes an `expect` predicate array
and reports the verdict in `status` (not `verdict`/`result`); `launch_app`
takes `additional_arguments` as an array; `get_desktop_state` returns a
desktop capture (screenshot + display metadata), not a window list; element
frames use `{x,y,w,h}`; screenshots arrive as MCP image content with
`structuredContent.screenshot_width/height`; optional arguments must be
omitted (never `null`) because the driver treats explicit `null` as "missing
required" for some strict fields; refusals prefer the structured `code` field.

- Element-targeted actions pass `element_token`, or the matching
  `snapshot_id` + `element_index` + `window_id`. A bare index without a
  snapshot, or any pid/window contradiction, is rejected before dispatch.
- Right-click, double-click, and drag use the dedicated `right_click`,
  `double_click`, and `drag` tools; scroll, press_key, and hotkey carry an
  explicit `window_id`. Every action records delivery mode, route, effect, and
  `DispatchMayHaveOccurred`; a success without structured action content is a
  protocol error, never a successful dispatch.
- Capability enforcement works: bounded mode passes the documented
  `CUA_DRIVER_PERMISSION_MODE`/`CUA_DRIVER_CAPABILITY_MANIFEST_FILE`/
  `CUA_DRIVER_CAPABILITY_MANIFEST_APPROVED` child environment, validates the
  manifest as an absolute existing file, and fails closed at startup.
  `GrantExistingBrowserProfile` appends `--grant existing-profile`. Ambient
  `CUA_DRIVER_*` permission variables are never inherited.
- The MCP transport is a persistent buffered reader; bytes after the first
  newline in a read are never discarded. Multiple frames per read, split
  frames, CRLF/LF, EOF unterminated frames, oversized frames, and cancellation
  with a buffered partial frame are covered by real-child-process tests.
- Browser and recording support are now product-owned typed surfaces
  (disposable driver-owned profiles only for qualification). Recording is
  diagnostic evidence and never establishes `committed`.

## 2026-09-11 — Packaged Notepad file launches reuse an open window

`launch_app` for packaged Notepad with a file argument routes into an
already-running Notepad process and opens the file in an existing window,
replacing that window's document. The qualification test therefore refuses to
run while any Notepad window is open. The user's `maisha cheklist.txt` file is
intact on disk; its open Notepad window was closed during qualification.
