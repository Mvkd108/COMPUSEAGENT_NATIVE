# VERIFY-AFTER-ACTION — decision-complete implementation prompt

Status: PREPARED — decision-complete but **not yet implemented**. This is the
first unique Compuse feature to build on the hardened Windows foundation
(filesystem `IFileOperation` + allowlisted Notepad UI Automation recipe + the
schema-exact TryCua Cua Driver adapter). Work is uncommitted on
`cursor/track-a-filesystem-prototype`.

## Outcome: one decision complete feature

Implement `verify_after_action` in `Compuse.Runtime` vocabulary: execute one
Windows computer-control action through `Compuse.CuaDriver`, then independently
observe application-owned state afterward, and return exactly one of the four
outcomes — `committed`, `refused`, `failed`, `indeterminate` — using the
established evidence rules. A `committed` result requires at least one
`ExternalSideEffectObservation` of the requested state, proven by an
independent read-back, never by the action tool's success return.

## Non-goals (do not do these)

- Do not modify `Compuse.Contracts`, `Compuse.Requests`, `Compuse.Protocol`, or
  `Compuse.Runtime` public types. This feature lives in a new product-owned
  module (see below) and consumes the driver surface as-is.
- Do not replace the `IFileOperation` filesystem backend, the allowlisted
  Notepad recipe, or the Cua Driver adapter with generic input simulation.
- Do not add Playwright, Selenium, or another browser runtime.
- Do not synthesize `committed`. Do not treat a driver `Succeeded` action
  result, an `effect: unverifiable`, or an OS/API return as commitment
  evidence. `verify_state` alone is the driver's own predicate evaluation and
  is diagnostic evidence, not product-owned independent observation — use it
  only as corroboration.
- Do not silently escalate a background refusal to foreground. A
  `background_unavailable` refusal before dispatch maps to `refused`.
- Do not auto-retry a possibly-mutating action.
- Do not access an existing signed-in browser profile. If the feature ever
  drives a browser, use a disposable driver-owned isolated profile and require
  an explicit session label.
- Do not download or update the Cua Driver binary.

## Context to read first

- `README.md` (outcome semantics, routing order, Track C surface)
- `context/PRODUCT.md`, `context/CURRENT.md`, `context/DECISIONS.md`,
  `context/NEXT.md`
- `src/Compuse.CuaDriver/IWindowsComputerDriver.cs`,
  `src/Compuse.CuaDriver/DriverActionModels.cs`,
  `src/Compuse.CuaDriver/DriverModels.cs`,
  `src/Compuse.CuaDriver/WindowsComputerDriver.cs`
- `src/Compuse.DropFiles/DropFilesHandler.cs` and
  `src/Compuse.ApplicationSurface/ApplicationRouter.cs` for the established
  evidence/refusal mapping patterns
- `context/sessions/2026-09-11-computer-control-foundation.md` for the
  qualified driver surface and its limitations

## Feature contract

### Request (product-owned, new module `Compuse.VerifyAfterAction`)

A `VerifyAfterActionRequest` names exactly one action and exactly one
application-owned postcondition predicate:

- Correlation identifier (existing `CorrelationId`).
- One action in a narrow, closed vocabulary, e.g.:
  - `element_click` on an exact element target (`ElementTarget`: pid, window_id,
    element_token or snapshot_id+element_index), or a coordinate click within a
    window;
  - `set_value` on an exact element target;
  - `type_text` into an exact element target or window;
  - `press_key` / `hotkey` to a window;
  - `scroll` a window; or
  - a declared no-input action that only observes.
- The requested delivery mode: `background` (default) or explicit `foreground`.
  Foreground must remain explicit and must be recorded.
- One application-owned postcondition, expressed in the typed vocabulary that
  `get_window_state` already parses, e.g.:
  - an element's `value` equals a string;
  - an element's `label` contains a substring;
  - an element `exists` / is `enabled` / is `selected`;
  - document text read-back for a known document element.
  The postcondition references an element by a freshly captured
  `element_token`/`element_index` from a `get_window_state` snapshot taken in
  the same turn (element tokens are session-scoped and short-lived).
- An optional deadline (existing runtime semantics).
- Validation: an incomplete or contradictory element target (bare index
  without snapshot, pid/window mismatch) is rejected before dispatch as an
  invalid request, never as an operation result.

### Execution sequence (one driver session, one turn)

1. `EnsureReady`/`InitializeAsync`.
2. `GetWindowStateAsync(pid, window_id)` — fresh snapshot for the turn.
   Capture the snapshot id. If the window is gone, `refused`
   (`target_not_found`/`stale_identity` style).
3. Resolve the action target against the snapshot (element token preferred).
   Reject before dispatch if the target is incomplete or contradictory, or if
   the postcondition element cannot be found: `refused`
   (`postcondition_target_missing`).
4. Record the requested delivery mode and route in the evidence.
5. Dispatch the action. Map the driver result:
   - `Refused` with `background_unavailable`/`background_occluded` →
     `refused` (`background_unavailable`). Never fall back to foreground.
   - `Refused` with stale/ambiguous/permission codes → `refused` with the
     mapped code.
   - `Failed`/protocol error → `failed`.
   - `Succeeded` → proceed to observation. Record the driver-reported
     `route`, delivery mode, `effect`, and whether dispatch may have occurred
     (`DispatchMayHaveOccurred`) as diagnostic evidence
     (`OsApiReturn`/`DiagnosticArtifact`), never as commitment.
6. Observe independently: after the action, take a fresh `get_window_state`
   and read the application-owned state that the postcondition targets (the
   element's `value`, `label`, `exists`/`enabled`/`selected`, or document
   text). This read-back is product-owned independent observation.
7. Decide the outcome:
   - Postcondition proven by the independent read-back →
     `committed` with an `ExternalSideEffectObservation` (kind
     `document_text_observed`/`element_value_observed`/similar, code + detail
     + optional artifact path). Note: this feature proves the requested state,
     not an arbitrary user-visible side effect.
   - The read-back contradicts the postcondition →
     `indeterminate` (`verification_failed`).
   - The read-back is unavailable (window exited, element gone, unreadable) →
     `indeterminate` (`verification_unavailable`) when dispatch may have
     occurred; `refused` (`target_exited`) when the target was already gone.
   - Cancellation/deadline after dispatch → `indeterminate`
     (`deadline_expired`/`cancelled`); before dispatch → `refused`
     (`cancelled`).
8. Recording (optional, caller-selected): if a recording was started through
   `StartRecordingAsync`, the recorded `before/after` artifacts are
   `DiagnosticArtifact` evidence and never change the outcome.

### Evidence vocabulary

Follow `README.md` exactly:
- `OsApiReturn` — the driver action's success return (diagnostic only).
- `ExternalSideEffectObservation` — the independent read-back proving the
  requested state (required for `committed`).
- `DiagnosticArtifact` — screenshots, tree snapshots, driver `verify_state`
  verdicts, recordings (never commitment).

### Refusal/failure codes (open strings on `RefusalInfo`/`FailureInfo`)

`background_unavailable`, `stale_identity`, `ambiguous_target`,
`target_not_found`, `target_exited`, `postcondition_target_missing`,
`verification_unavailable`, `verification_failed`, `invalid_action`,
`driver_permission_denied`, `cancelled`.

### Where it lives

- New module `src/Compuse.VerifyAfterAction` (`net10.0-windows`), with a typed
  handler `IVerifyAfterActionHandler` (or a direct runtime handler) that the
  CLI/`compuse` can wire, plus `tests/Compuse.VerifyAfterAction.Tests`. Keep
  it narrow: one action + one postcondition per request. Do not expand the
  Cua Driver public surface unless the pinned driver's schema requires it.
- The `compuse` CLI gains a `verify-after-action` command with explicit
  `--background`/`--foreground` and a structured postcondition selector. Exit
  codes follow the existing convention (`0` committed, `1` invalid request,
  `2` refused, `3` failed, `4` indeterminate).

## Tests required

- Unit/fake-transport tests: the outcome mapping table above is exhaustively
  covered (driver Succeeded without proof → not committed; driver Refused
  background → refused; stale/ambiguous/permission → refused codes; read-back
  match → committed with `ExternalSideEffectObservation`; mismatch →
  indeterminate; unreadable → indeterminate; cancellation before/after
  dispatch). Invalid request validation tests (incomplete element target).
- Real-driver integration tests (gated on `COMPUSE_CUA_DRIVER_EXE`): a
  Calculator round trip (`set_value` or `click` on a digit then read the
  `Display is N` element and commit only when the read-back proves it), a
  disposable Notepad `set_value`/document-text round trip (refusing to run
  while any Notepad window is open), and a stale-target refusal. Reuse the
  disposable-profile browser flow for any browser case.
- Run the full gate: locked restore, Release build 0/0, format 0 changes,
  full suite 0 failures (374 with driver / 362+12 skipped without).

## Definition of done

- The four-outcome mapping is implemented with product-owned independent
  observation; `committed` requires an `ExternalSideEffectObservation` and can
  never come from the action tool's success return.
- Background refusals never escalate to foreground; possibly-mutating actions
  are never auto-retried.
- All tests above pass; the full gate is green; documentation
  (`README.md`, `context/CURRENT.md`, `context/DECISIONS.md`, session notes)
  is updated to match; the working tree stays uncommitted unless an explicit
  commit/merge instruction is given.
