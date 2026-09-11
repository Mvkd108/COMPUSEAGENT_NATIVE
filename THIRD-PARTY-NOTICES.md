# Third-party notices and reuse matrix

This file records every external dependency that CompuseAgent Native consumes at
build or runtime, its exact version or pinned revision, its license, and how it
is updated. CompuseAgent Native is Windows-only (Windows 11 x64); nothing here
adds a macOS or Linux backend, a cross-platform product abstraction, a cloud
sandbox, a virtual-machine orchestrator, training infrastructure, or an
unrelated agent framework.

## Open-source reuse matrix

| Capability | Existing CompuseAgent implementation | Candidate upstream implementation | Selected implementation | Reason for selection | License | Version / pinned revision | Remaining limitation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Windows UI Automation semantic actions (Notepad recipe) | `Compuse.ApplicationSurface.FlaUiSession` (UIA3 via FlaUI) | FlaUI; Microsoft Windows UI Automation (CUIAutomation); Cua Driver UIA route | FlaUI (`FlaUI.Core` + `FlaUI.UIA3`) | Maintained, Windows-only, MIT, thin wrapper over Microsoft UIA COM; proven in the retained Notepad recipe | MIT | 5.0.0 (NuGet) | WinUI "Pop-up" menu-host search is a WinUI structural detail; app recipes must be explicit (allowlisted) |
| Window discovery (packaged/Win32 top-level windows) | `Compuse.ApplicationSurface.FlaUiSession` (`EnumWindows` + `FromHandle`) | Cua Driver `list_windows`; FlaUI; Win32 `EnumWindows` | `EnumWindows` + UIA `FromHandle` in the recipe path; Cua Driver `list_windows` in the generic driver path | Fast, no dependency for the recipe; driver supplies discovery for the generic layer | MIT (our code); MIT (driver) | our code; Cua Driver 0.27.0 | HWND/PID are hints; identity must be revalidated before acting |
| Generic computer-control layer (apps, windows, capture, accessibility tree, background semantic + general input, browser tools, recording, health) | none before this change | TryCua Cua Driver (`cua-driver mcp`, stdio MCP); Microsoft UFO; Microsoft Computer-Use-Agent | TryCua Cua Driver | Active, Windows-supported (Win32 + UIA), background-first delivery with structured `background_unavailable` refusals, typed browser tools, MCP over stdio (no Python/Node in our runtime), MIT | MIT | 0.27.0 (GitHub release `cua-driver-rs-v0.27.0`, asset `cua-driver-rs-0.27.0-windows-x86_64.zip`) | Element tokens and browser page refs are session-scoped and short-lived; packaged Notepad file launches reuse an open window (qualification refuses while any Notepad window is open); existing signed-in browser profiles require the explicit `--grant existing-profile` capability; elevated-integrity and some background Chromium gestures are unproven upstream; image payloads are bounded by our adapter, not by the driver |
| MCP stdio client + process supervisor | none before this change | Cua Driver's own SDK bindings (UniFFI Python/TypeScript) | Our own C# adapter (`Compuse.CuaDriver`) over the documented MCP stdio interface | Keeps the Rust driver out-of-process, avoids Python/Node in the product runtime, and keeps Cua payloads behind a narrow `IWindowsComputerDriver` | MIT (our code) | our code against Cua Driver 0.27.0 MCP protocol `2025-06-18` | None known |
| Filesystem copy/move | `Compuse.Filesystem` (`IFileOperation` on a dedicated STA) | Cua Driver input simulation; shell `IFileOperation` | Shell `IFileOperation` (retained) | Reliable native semantics with collision refusal, identity checks, cancellation-safe COM; never simulated with input | Microsoft Windows SDK (system) | Windows 11 | none |
| Test framework | MSTest | — | MSTest | existing project standard | MIT | 4.3.3 (NuGet) | none |
| Protobuf | Google.Protobuf / Grpc.Tools | — | retained | existing project standard | BSD-3-Clause (Google) | 3.35.1 / 2.83.0 | none |

## Explicitly not adopted

- **Microsoft UFO (MIT)**: a Python/LLM agent framework with multi-device and
  cross-platform orchestration. Adopting it would add a second runtime and an
  unrelated agent framework; its Windows ideas are already served by FlaUI and
  Cua Driver.
- **Microsoft Computer-Use-Agent**: a distinct project from TryCua Cua Driver.
  It does not publish a stable, license-uniform, separable .NET or C# component
  for Windows computer control, so nothing is consumed from it.
- **Cua Driver optional components**: OmniParser (CC-BY-4.0), Ultralytics
  (AGPL-3.0), model weights, cloud Fleets, Lume VMs, Cua Bench training/
  benchmark infrastructure, and the macOS/Linux backends are not distributed or
  referenced. The adapter talks only to the Windows `cua-driver.exe` MCP
  process using its documented public interface.

The three names are different projects: **TryCua Cua Driver** (open-source
Windows/macOS/Linux background automation driver, MIT, consumed here for
Windows), **Microsoft Computer-Use-Agent** (a separate Microsoft agent project,
not consumed), and **FlaUI** (a .NET UIA wrapper, MIT, consumed for the
Notepad recipe).

## Pinned Cua Driver

- Project: TryCua Cua Driver, upstream `https://github.com/trycua/cua`
- Release: `cua-driver-rs-v0.27.0`
- Windows x64 asset: `cua-driver-rs-0.27.0-windows-x86_64.zip`
  (`cua-driver.exe`, `cua-driver-uia.exe`, `cua_driver_abi.h`, SDK/native
  bindings, cursor-theme helper)
- License: MIT (repository). The release zip does not bundle a separate
  LICENSE text; the MIT license is the repository license.
- Checksums: GitHub release assets do not publish per-asset SHA-256 digests at
  this time; the installer pins an exact stable version and downloads only
  from `https://github.com/trycua/cua/releases/download/<tag>/`. We pin the
  exact tag and the exact asset name.
- Update procedure: bump `DriverConnectionOptions`/documentation to a newer
  `cua-driver-rs-vX.Y.Z`, re-run the driver integration tests
  (`COMPUSE_CUA_DRIVER_EXE=<path>`), and re-run the live qualification. The
  adapter refuses versions older than the tested minimum (`0.27.0`) and any
  `1.0.0` or newer until revalidated.
- Install (operator): `irm https://cua.ai/driver/install.ps1 | iex` (pins the
  current stable channel), or download the pinned release zip and point the
  adapter at the absolute path of `cua-driver.exe`.
- Diagnose: `cua-driver doctor`, `cua-driver --version`,
  `cua-driver telemetry status`, and the adapter's `GetHealthAsync`
  (`health_report`).
- Disable: do not install it, or remove/rename the executable; the adapter then
  returns `DriverUnavailableException` (a clear refusal/diagnostic) on
  `InitializeAsync` and never downloads or updates the binary on its own.

## Windows-only dependency boundary

All product-owned code and every adopted dependency in this file is Windows-
relevant. `Compuse.Contracts`, `Compuse.Requests`, `Compuse.Protocol`, and
`Compuse.Runtime` remain platform-neutral `net10.0`. Windows projects
(`Compuse.Discovery`, `Compuse.Routing`, `Compuse.Filesystem`,
`Compuse.ApplicationSurface`, `Compuse.CuaDriver`, `Compuse.DropFiles`,
`Compuse.Cli`, and their tests) target `net10.0-windows`. The Cua Driver
adapter (`Compuse.CuaDriver`) supervises a Windows executable over stdin/stdout
and contains no macOS/Linux backend.
