# Next steps

The reusable Windows foundation (filesystem, Notepad recipe, Cua Driver
adapter) is complete, reviewed, and qualified. The independent Track A + Track
B + Track C review is done and every confirmed defect in scope was fixed.
Remaining work is Compuse-specific differentiation, application recipes,
reliability, and documented edge cases — not generic capture or input
functions already supplied by the adopted dependencies.

1. When explicitly requested, commit and merge to `main`, then push. Work is
   uncommitted on `cursor/track-a-filesystem-prototype`.
2. First Compuse-specific feature on this foundation: the decision-complete
   `verify_after_action` implementation prompt is prepared at
   `context/prompts/VERIFY-AFTER-ACTION.md`. It runs one driver action, then
   reads back application-owned state (document text, list selection, displayed
   value) and returns `committed` only on an independent observation,
   `indeterminate` on mismatch, and `refused` when the driver reports
   `background_unavailable`/stale state before dispatch.
3. Second application recipe behind the recipe catalog (for example a
   documented `IFileDialog`-based importer or Notepad "Save As" verification),
   keeping recipe-specific labels/IDs in the recipe and generic primitives
   generic.
4. Wire the routing policy into one operation end-to-end (documented API →
   Compuse semantic backend → UIA → Cua Driver background → visual →
   explicitly-permitted foreground) and record the chosen route in evidence.
5. Browser workflows against an existing signed-in profile only after a product
   decision authorizes `GrantExistingBrowserProfile`
   (`--grant existing-profile` is an explicit capability, never a default). The
   disposable driver-owned profile flow is already qualified.
6. Qualification matrix maintenance: record rows for File Explorer (Win32),
   Calculator (WinUI), Notepad (packaged, disposable only), Edge/Chrome
   (Chromium, disposable profile), and an Electron app when one is installed;
   mark unavailable rows "untested" rather than fabricating evidence.
7. M013 qualification harness (`eng/verify-track-a.ps1`) extended to the
   app-surface and driver workflows; M019 release qualification; FINAL
   integrated-system review.

Do not resume Hyper-V, GUI, cloud, networking, or UFO/Computer-Use-Agent
vendoring. Do not add a mouse or focus fallback. Do not copy Cua Driver
source; consume the pinned Windows binary through its public MCP interface.
