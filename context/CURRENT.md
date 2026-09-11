# Current state

Last updated: 2026-09-11

## M001–M004 — Contract and runtime foundation

Status: `APPROVED`. Local `main` fast-forwarded to M004
`56b35fcab253df3049aa514dd1e0e2c42bc122a1`.

- SDK `10.0.302`
- Contracts, Protobuf result/request, and `OperationRuntime` public types
  were not modified.

## Track A — Filesystem `drop_files` prototype

Status: implemented and verified. Copy or move physical files into a real
directory and return `committed` only after destination observation (and, for
move, source removal). Overwrite is refused (`collision`); multi-file preflight
is all-or-nothing; destination length mismatch is `indeterminate`
(`integrity_mismatch`); uninspectable destinations are `failed`
(`verification_unavailable`) when no side effect was observed. `IFileOperation`
runs on a dedicated foreground STA; post-dispatch deadline or Ctrl+C returns
`indeterminate` without killing the COM transfer.

## Track B — Application-surface `drop_files` (UI Automation)

Status: implemented and verified. Windows 11 Notepad is an allowlisted recipe.

- Reusable component: FlaUI `FlaUI.Core` + `FlaUI.UIA3` v5.0.0 (MIT) behind
  narrow seams.
- Module: `Compuse.ApplicationSurface` with FlaUI-free seams `IUiaSession`,
  `IWindowDiscovery`, `IApplicationImportBackend`, recipe interfaces, and the
  concrete `FlaUiSession`, `WindowsWindowDiscovery`, `NotepadImportBackend`,
  `NotepadImportRecipe`, `ApplicationRouter`, `ApplicationRecipeCatalog`.
- Explicit recipe selection: only a positively identified Notepad target runs
  the Notepad recipe; other applications refuse with
  `unsupported_application_recipe`. The CLI wires the catalog.
- Source identity is stored in `ApplicationImportPlan` and revalidated
  (`SameObjectAs`) plus length before dispatch; same-length replacement is
  refused with `stale_identity`.
- Verified live: copy import `committed` only after UIA read-back matches;
  move import `committed` with observed source removal; `refused` for
  unsupported recipe, ambiguous window, stale HWND, locked source; `indeterminate`
  for real deadline and verification mismatch; pointer and foreground unchanged.

## Track C — Reusable Windows computer-control foundation (Cua Driver)

Status: implemented, hardened, and verified against the pinned TryCua Cua
Driver (`cua-driver-rs-v0.27.0`, Windows x64, MIT) as a child
`cua-driver mcp` process speaking MCP JSON-RPC over stdin/stdout. The adapter
matches the exact schemas captured from the pinned driver (contract fixtures in
`tests/Compuse.CuaDriver.Tests/WindowsComputerDriverContractTests.cs`).

- Narrow interface: `IWindowsComputerDriver` with typed DTOs. Cua/MCP payloads
  (`CuaMcpClient`, `McpWire`, `IMcpTransport`, `StdioMcpTransport`,
  `McpToolResult`) are internal to the module.
- Transport: `StdioMcpTransport.ReadLineAsync` is a persistent buffered reader
  that retains all unread bytes (multiple frames per OS read, split frames,
  CRLF/LF, EOF unterminated frames, oversized frames, cancellation with a
  buffered partial frame). Verified with a real child process emitting many
  frames in one write.
- Capabilities: initialize/version handshake (refuses < 0.27.0 and >= 1.0.0),
  `health_report`, `list_apps`, `list_windows`, `get_window_state` (UIA tree +
  element tokens + element frames + screenshot + snapshot id), `launch_app`,
  background `click`/`right_click`/`double_click`/`drag`, `set_value`,
  `type_text`, `press_key`, `hotkey`, window-targeted `scroll`, clipboard
  read/write, `bring_to_front`, `get_cursor_position`, `get_desktop_state`
  (a `DesktopState` capture model, not a window list), `verify_state` (the
  documented `expect` predicate array; verdict read from `status`).
- Element identity: element-targeted actions pass `element_token`, or the
  matching `snapshot_id` + `element_index` + `window_id`. Bare indices without
  a snapshot, and pid/window contradictions, are rejected before dispatch.
- Capability enforcement: `DriverPermissionMode.Bounded` sets the documented
  `CUA_DRIVER_PERMISSION_MODE`/`CUA_DRIVER_CAPABILITY_MANIFEST_FILE`/
  `CUA_DRIVER_CAPABILITY_MANIFEST_APPROVED` child environment, validates the
  manifest as an absolute existing file, and fails closed at startup.
  `GrantExistingBrowserProfile` appends `--grant existing-profile`. Ambient
  `CUA_DRIVER_*` permission variables are never inherited; the effective mode
  is visible via `EffectivePermissionMode` / `GetDiagnostics()`.
- Browser surface (disposable driver-owned profiles only): `PrepareBrowserAsync`,
  `BindBrowserAsync`, `SnapshotBrowserAsync`, `NavigateBrowserAsync`,
  `BrowserClickAsync`, `BrowserTypeAsync`, `BrowserScrollAsync`,
  `BrowserSetInputFilesAsync`. Stale page refs refuse with
  `browser_input_trust_unavailable`; browser operations require a session
  label.
- Recording: `StartRecordingAsync` (absolute path + approved-root validation),
  `GetRecordingStateAsync`, `StopRecordingAsync`. Recording is diagnostic
  evidence, never `committed`.
- Process supervision: configured executable path, missing binary →
  `DriverUnavailableException`, start failures wrapped, bounded stderr
  diagnostics, bounded message size (16 MiB default), graceful stdin-EOF
  shutdown then kill, cancellation via `notifications/cancelled` plus
  client-side deadlines, correlation by JSON-RPC id, enforced request-id
  ceiling, no automatic retry of possibly-mutating actions.
- Upstream refusal mapping prefers the driver's structured `code` field:
  `background_unavailable`, `background_occluded`,
  `browser_input_trust_unavailable`, `browser_ref_stale`, `ambiguous_target`,
  stale/expired targets, and permission denials map to `refused`-typed
  results. `DriverActionResult` records the delivery route, delivery mode,
  `Effect`, and `DispatchMayHaveOccurred`. A success without structured action
  content is a protocol error, never a successful dispatch.
- Real-driver qualification (Windows 11): Calculator launch → discovery →
  screenshot → UIA tree → background element-token click (`route=accessibility`,
  `delivery=background`) → independent read-back → `verify_state` satisfied;
  pointer and foreground unchanged around the dispatch. Stale-snapshot
  refusal, background title-bar `drag` refusal (`background_unavailable`),
  cancellation, disposable Notepad set-value round trip, File Explorer
  discovery, bounded-mode deny-by-default, disposable-profile browser flow
  (prepare → bind → navigate → snapshot → stale-ref refusal), and recording
  start/query/stop all pass against the real binary.
- Driver integration tests are gated on `COMPUSE_CUA_DRIVER_EXE`; without it
  the twelve real-driver tests are skipped (inconclusive).

## Intentionally absent

No OLE application drop, private C ABI, GUI, Hyper-V, networking, installers,
pointer/mouse fallback, or overwrite/recycle. No Cua Driver optional vision
components, cloud Fleets, Lume VMs, or training infrastructure. Foreground
input is not a silent fallback.

## Known operational notes

- SDK `10.0.302` is installed user-locally under
  `%LOCALAPPDATA%\Microsoft\dotnet`.
- Windows 11 Notepad routes `notepad.exe` launches into the existing packaged
  `Notepad` process; window discovery matches by process name and HWND
  enumeration, never by assuming the launcher PID owns a window.
- Cua Driver element tokens are session-scoped and short-lived: keep one driver
  session per capture→act→verify turn and refresh `get_window_state` before
  acting after any gap.
- The Cua Driver binary is never downloaded or updated by the product; an
  operator installs it once (`irm https://cua.ai/driver/install.ps1 | iex` or
  the pinned release zip) and points the adapter at the executable path.
- The full automated suite runs the driver integration tests only when
  `COMPUSE_CUA_DRIVER_EXE` is set; otherwise they are skipped.

Verified on SDK `10.0.302`: Release 0 warnings, format 0 changes, 374 tests
passed with the real-driver integration tests enabled (362 passed + 12 skipped
when the driver is not configured), 0 failed. Real Calculator, disposable
Notepad, File Explorer, and disposable-profile Edge qualification all observed
external side effects and unchanged pointer/foreground for background actions.

Operational note: packaged Notepad launches with a file argument reuse an
already-open Notepad window and replace its document. The qualification test
refuses to run while any Notepad window is open; the user's
`maisha cheklist.txt` file is intact on disk, but its open Notepad window was
closed during qualification and has not been restored.
