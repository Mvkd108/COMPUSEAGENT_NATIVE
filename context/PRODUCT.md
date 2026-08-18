# Product context

## Thesis

COMPUSEAGENT_NATIVE is independent Windows semantic-automation software. It is
not a CUA fork and must not copy, vendor, modify, reference, download, or
integrate CUA source. A later module may consume a pinned external backend
solely through CUA’s public interface.

The first product capability is a reliable `drop_files` semantic operation. It
must use the correct Windows mechanism instead of treating every file transfer
as a mouse gesture.

## Settled stack

- C# / .NET 10 for contracts, runtime, routing, policy, CLI, diagnostics, and
  managed tests.
- A later narrow C++/WinRT component only where native COM/OLE/Shell is
  justified.
- Protobuf binary package `compuse.v1` is the canonical external result
  contract.
- The native boundary will be a private, versioned C ABI. That ABI is not
  defined yet.
- Initial platform: Windows 11 x64.

## Outcome vocabulary

Exactly `committed`, `refused`, `failed`, and `indeterminate`.

An OS or API success return is diagnostic evidence only. A `committed` result
requires at least one `ExternalSideEffectObservation`.

## Out of scope until separately prepared

- Hyper-V, GUI, cloud platform, networking, installers, browsers
- Runtime, CLI, C ABI, OLE/Shell, or actual `drop_files` execution
- CUA vendoring or integration
- The superseded C++/Go/Hyper-V platform plan from 2026-08-15
