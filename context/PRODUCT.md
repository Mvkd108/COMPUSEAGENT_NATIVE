# Product context

## Thesis

COMPUSEAGENT_NATIVE is independent Windows semantic-automation software. It is
not a fork of TryCua Cua Driver, Microsoft Computer-Use-Agent, or any other
agent framework, and it does not copy or vendor their source. It reuses
maintained open-source Windows components behind narrow product-owned seams:
FlaUI for UI Automation in the retained Notepad recipe, and TryCua Cua Driver
as the out-of-process generic computer-control backend (MCP over stdio).

CompuseAgent's differentiators are its own: semantic Windows operations,
independent observation of requested results, evidence-backed outcomes, safe
cancellation and retry behavior, application-specific verification, and
explicit routing between native APIs, accessibility actions, and visual
interaction. The first product capability is a reliable `drop_files` semantic
operation with the correct Windows mechanisms instead of treating every file
transfer as a mouse gesture.

## Settled stack

- C# / .NET 10 for contracts, runtime, routing, policy, CLI, diagnostics,
  managed tests, and the Cua Driver MCP adapter.
- Windows-only. `Compuse.Contracts`, `Compuse.Requests`, `Compuse.Protocol`,
  and `Compuse.Runtime` are platform-neutral `net10.0`; every other project
  targets `net10.0-windows`. No macOS/Linux backends, cross-platform product
  abstractions, cloud sandboxes, VM orchestration, training infrastructure, or
  unrelated agent frameworks.
- Protobuf binary package `compuse.v1` is the canonical external result
  contract.
- Filesystem copy/move uses Shell `IFileOperation`; application import uses
  UI Automation (FlaUI) behind an allowlisted recipe catalog.
- Generic computer control (windows, capture, accessibility state, background
  input, browser tools) is delegated to TryCua Cua Driver 0.27.0 (MIT) through
  a supervised `cua-driver mcp` child process, isolated behind
  `IWindowsComputerDriver`.
- A later narrow C++/WinRT component only where native COM/OLE/Shell is
  justified. OLE application drop remains gated.

## Outcome vocabulary

Exactly `committed`, `refused`, `failed`, and `indeterminate`.

An OS or API success return is diagnostic evidence only. A `committed` result
requires at least one `ExternalSideEffectObservation`.

## Background and foreground

Background delivery is attempted first. The pointer is never moved by
background operations. Foreground interaction (`SetForegroundWindow`,
`SendInput`, the system input queue) is a separately authorized mode and is
recorded when used; it is never a silent fallback.

## Out of scope until separately prepared

- Hyper-V, GUI, cloud platform, networking, installers. Browser tooling is
  reused from Cua Driver only through the typed adapter surface; existing
  signed-in profiles require the explicit `GrantExistingBrowserProfile`
  capability and are never a default. No Playwright, Selenium, or other
  browser runtime is added unless a demonstrated Cua gap requires it.
- CUA/UFO vendoring or source copying; Cua Driver optional vision components
  (OmniParser, Ultralytics), model weights, cloud Fleets, Lume VMs, and
  training infrastructure
- The superseded C++/Go/Hyper-V platform plan from 2026-08-15
