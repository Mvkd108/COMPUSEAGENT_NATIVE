# OLE IDropTarget spike gate (deferred)

Track A does not implement M007 (native ABI) or M009 (OLE application drop).
This note is the architecture gate, not an implementation.

## Why OLE is deferred

Filesystem-container transfers have a documented non-input mechanism: `IFileOperation`.
Application-surface drops do not. `DoDragDrop` is a drag loop that consults cursor
position. Synthesizing `SetCursorPos`, `mouse_event`, `SendInput`, or
`SetForegroundWindow` would violate the product invariant that no silent input
fallback may steal focus or move the pointer.

## What a future spike must prove

A documented path is allowed into M009 only if all of the following are shown
on Windows 11 x64, same integrity level, without pointer or focus changes:

1. A documented API that acquires `IDropTarget` for an explicitly identified
   target (not “window under cursor”).
2. `IDropTarget::Drop` (or equivalent) can be invoked with an `IDataObject`
   that includes `CF_HDROP`.
3. UIPI: a lower-integrity caller is refused, not prompted to elevate.
4. Commitment is based on the instrumented target observing the payload, not
   on `DROPEFFECT` / `Drop` HRESULT alone.
5. Cancellation, target-exit, and rejected-format paths return
   `refused` / `failed` / `indeterminate` without leftover COM refs.

If any item cannot be met, application-surface stays `unsupported_target_kind`
in v1.

## What is already known (not sufficient to start M009)

- Folder PIDL `IDropTarget` / `IFileOperation` is a filesystem mechanism. It
  must not be used as a fake “drop on Explorer HWND.”
- `WM_DROPFILES` is a posted message, not OLE, and is blocked across integrity
  levels. It is not a v1 application-drop mechanism.
- A private C ABI (M007) is justified only if the spike shows managed COM
  interop cannot own STA pumping or crash isolation. Do not introduce M007
  speculatively.

## Next action when Track A works

Run a separate spike against an instrumented in-process `IDropTarget`, then
against one allowlisted real application. Record the verdict in `DECISIONS.md`
before writing an M009 prompt.
