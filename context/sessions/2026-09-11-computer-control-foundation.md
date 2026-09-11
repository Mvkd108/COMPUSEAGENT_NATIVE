# Session 2026-09-11 — Reusable Windows computer-control foundation (Track C)

## Scope

Fixed the four identified gaps in the application-surface work, then built and
qualified a reusable Windows computer-control foundation that reuses TryCua
Cua Driver as an out-of-process MCP backend and keeps Compuse's
differentiators (observation, evidence, routing, refusal) in product-owned code.

## Corrections (gaps 1–4)

1. Explicit application recipe selection: `IApplicationImportRecipeCatalog` /
   `ApplicationRecipeCatalog` / `IApplicationImportRecipe`; the Notepad recipe
   runs only for a positively identified Notepad target; other applications
   refuse with `unsupported_application_recipe`.
2. `ApplicationImportPlan` now stores the source `FilesystemIdentity` and
   revalidates it (plus length) before dispatch; same-length replacement is
   refused with `stale_identity`.
3. Documentation now distinguishes TryCua Cua Driver, Microsoft
   Computer-Use-Agent, and FlaUI as distinct projects.
4. `IUiaSession` is a generic seam; Notepad-specific labels/control IDs moved
   into `NotepadImportBackend`.

## New module — Compuse.CuaDriver

- `IWindowsComputerDriver` narrow interface with typed DTOs; Cua/MCP payload
  types are internal (`CuaMcpClient`, `McpWire`, `IMcpTransport`,
  `StdioMcpTransport`, `McpToolResult`).
- MCP stdio client: initialize handshake, tools/call correlation by id,
  notifications (`initialized`, `cancelled`), malformed/partial/oversized
  responses, process-exit faulting, cancellation and client-side deadlines,
  bounded message size and stderr ring buffer.
- Process supervision: configured executable, missing binary →
  `DriverUnavailableException`, start failures wrapped, stdin-EOF graceful
  stop then kill, no auto-retry of possibly-mutating actions.
- Capability methods: health, apps, windows, window state (UIA tree + element
  tokens + screenshot), launch, background click (element token), set value,
  type, keys, hotkey, scroll, clipboard, bring-to-front, cursor, desktop state,
  verify. Upstream `background_unavailable`/`stale`/`ambiguous`/`permission`
  refusals map to typed `DriverActionResult` results; `Route` records
  `accessibility`/pixel/foreground.

## Verification

- Full suite: 318 passed / 0 failed with the real driver integration tests
  enabled; 316 passed + 2 skipped when `COMPUSE_CUA_DRIVER_EXE` is unset.
  Build 0 warnings/errors, format 0 changes.
- Unit tests (fake transport): MCP framing, out-of-order correlation, JSON-RPC
  errors, malformed/partial/oversized responses, stderr flood bound, missing
  exe, unsupported version, cancellation, internal-type isolation.
- Driver integration tests against pinned `cua-driver-rs-v0.27.0` (Windows
  x64): initialize/health, discover apps+windows, cursor.
- Live qualification on the real desktop: Cua Driver launched Calculator
  (packaged AUMID), discovered the window, captured the UIA tree (39 elements)
  and screenshot, performed a background accessibility click on the "Seven"
  element token (`route=accessibility`, `delivery=background`), and read back
  "Display is 7". Pointer and foreground were measured unchanged around the
  whole flow.
- Notepad recipe re-validated live after the refactor (copy `committed`,
  `unsupported_application_recipe` refusal for a real Calculator window).

## Findings worth recording

- Cua Driver click requires `element_token` (or `snapshot_id` + element_index),
  not a bare element_index; tokens are session-scoped and short-lived — keep
  one driver session per capture→act→verify turn.
- `list_windows` treats `pid:0` as an explicit "pid 0" filter; the adapter
  omits the field when no filter is given. Structured shapes differ between
  `windows` (nested bounds) and `_legacy_windows` (flat bounds); both are
  parsed.

## Files changed

- `src/Compuse.ApplicationSurface` — recipe catalog/interfaces,
  `NotepadImportRecipe`, generic seam refactor, router recipe selection
- `src/Compuse.Routing` — `ApplicationImportPlan` source identity + recipe
  name; `unsupported_application_recipe` code
- `src/Compuse.DropFiles` — handler recipe catalog wiring + identity
  revalidation
- `src/Compuse.Cli` — recipe catalog wiring
- `src/Compuse.CuaDriver` (new) — driver adapter
- Tests: `tests/Compuse.ApplicationSurface.Tests`, `tests/Compuse.DropFiles.Tests`,
  `tests/Compuse.CuaDriver.Tests` (new)
- Docs: `README.md`, `context/PRODUCT.md`, `context/CURRENT.md`,
  `context/DECISIONS.md`, `context/NEXT.md`,
  `context/OLE-IDROPTARGET-SPIKE.md`, `THIRD-PARTY-NOTICES.md` (new)

## First action next session

Independent review of Track A + Track B + Track C. Commit/merge only when
explicitly requested. The first Compuse-specific feature to build on this
foundation is a `verify_after_action` semantic operation (see `context/NEXT.md`).

# Session 2026-09-11 (second) — Independent review and hardening of the foundation

## Scope

Independent review and final hardening of Track A + Track B + Track C against
the pinned Cua Driver 0.27.0. The MCP contract was captured from the real
binary (list-tools, describe, and live responses) and every wrapped tool
was corrected. Full gate: locked restore, Release build (0 warnings/errors),
format (0 changes), full suite zero failures.

## Defects fixed (all confirmed against the real driver)

1. StdioMcpTransport.ReadLineAsync discarded bytes after the first newline
   in a read buffer. Replaced with a persistent buffered reader; real-child-
   process tests cover multiple frames per read, split frames, complete+partial
   frames, CRLF/LF, EOF unterminated frames, oversized frames, and cancellation
   with a buffered partial frame.
2. clipboard_read never requested text (include_text was absent).
3. verify_state sent a string expect (schema requires a predicate array)
   and parsed verdict/result; the real field is status.
4. get_desktop_state was parsed as a window list; it returns a desktop
   capture. Replaced with the DesktopState model (screenshot + display
   metadata) with image-content parsing and dimension enrichment.
5. Element-targeted set_value/	ype_text/press_key/hotkey/click
   omitted element_token/snapshot_id; bare indices without a snapshot are
   now rejected before dispatch; pid/window contradictions are rejected.
6. hotkey did not require two keys and did not carry window/element
   addressing; scroll had no window id; right-click/double-click/drag were
   missing (now call the dedicated ight_click/double_click/drag tools).
7. launch_app sent additional_arguments as a string (schema: array).
8. Element frame shape is {x,y,w,h}; the adapter only read width/height.
9. Screenshots arrive as MCP image content, not structuredContent.screenshot;
   dimensions come from structuredContent.screenshot_width/height.
10. Optional arguments were sent as JSON 
ull; the driver treats explicit
    
ull as missing-required for strict fields. Argument builders now omit
    unset fields.
11. DriverConnectionOptions.CapabilityManifestFile/GrantExistingBrowserProfile
    had no effect. Bounded mode now sets the documented
    CUA_DRIVER_PERMISSION_MODE/CUA_DRIVER_CAPABILITY_MANIFEST_FILE/
    CUA_DRIVER_CAPABILITY_MANIFEST_APPROVED child environment, validates the
    manifest as an absolute existing file, and fails closed at startup; the
    grant appends --grant existing-profile. Ambient CUA_DRIVER_*
    permission variables are never inherited. The effective mode is visible in
    diagnostics.
12. The MaxRequestId option was not enforced; NextId now fails at the
    ceiling (and request ids now start at 1).
13. A successful action response without structured content was treated as a
    success; it is now a protocol error. Refusals prefer the structured
    code field; DriverActionResult records Effect and
    DispatchMayHaveOccurred.

## New product-owned surfaces

- Browser: PrepareBrowserAsync (disposable isolated profile),
  BindBrowserAsync, SnapshotBrowserAsync, NavigateBrowserAsync,
  BrowserClickAsync, BrowserTypeAsync, BrowserScrollAsync,
  BrowserSetInputFilesAsync; stale refs refuse with
  browser_input_trust_unavailable; session label required.
- Recording: StartRecordingAsync/GetRecordingStateAsync/StopRecordingAsync
  with absolute-path + approved-root validation and \\?\ normalization.
- Basic actions: RightClickAsync, DoubleClickAsync, DragAsync, and
  window-targeted scroll/press_key/hotkey.

## Verification

- Full suite with real driver: 374 passed / 0 failed / 0 skipped.
- Unconfigured: 362 passed / 12 skipped (real-driver integration tests) /
  0 failed.
- Real-driver integration suite: Calculator (discovery, screenshot, a11y,
  background element-token click, read-back, verify_state, pointer/foreground,
  stale-snapshot refusal, background drag refusal, cancellation), disposable
  Notepad set-value round trip, File Explorer discovery, bounded-mode
  deny-by-default, disposable-profile Edge flow, recording start/query/stop.
- The user's maisha cheklist.txt file is intact on disk. Its open Notepad
  window was closed during qualification (packaged Notepad file launches reuse
  an open window and replace its document); the qualification test now refuses
  to run while any Notepad window is open, and the limitation is documented.

## First action next session

Implement the prepared verify_after_action feature from
context/prompts/VERIFY-AFTER-ACTION.md, then commit/merge only when
explicitly requested.
