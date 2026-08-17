# CompuseAgent Native

CompuseAgent Native is independent Windows software. C# and .NET 10 own contracts, runtime, routing, policy, CLI, diagnostics, and managed tests. A later narrow C++/WinRT component may own justified OLE/Shell operations. This repository currently contains only the typed contract foundation.

## Module boundary

M001 establishes the executable contract vocabulary and repository bootstrap. It does **not** implement runtime services, platform integration, file transfer, CLI, GUI, native projects, protobuf schemas, or placeholders for later work.

Windows 11 x64 is the initial product platform. `Compuse.Contracts` targets `net10.0` with no platform API dependency so later runtime and native projects can own Windows-specific behavior.

## Settled architecture

- Outcome vocabulary is exactly `committed`, `refused`, `failed`, and `indeterminate`.
- An OS or API success return is diagnostic evidence only. It is never proof that an external side effect occurred.
- A `committed` result requires at least one `ExternalSideEffectObservation`.
- Protobuf will later become the canonical external contract. M001 JSON mapping exists only to stabilize the four outcome tokens for local typed-contract tests.
- The future native boundary is a private, versioned C ABI. This module does not define C ABI details.
- CUA is independent software. This repository does not copy, vendor, modify, reference, download, or integrate CUA. A later module may consume a pinned external backend solely through its public interface.

## Prerequisites

- Git 2.53 or later, with support for `git init --initial-branch`.
- Exact .NET SDK `10.0.302`. `global.json` sets `"rollForward": "disable"` and `"allowPrerelease": false`.
- NuGet access for the first restore. Later restores use checked-in lock files in locked mode.
- MSTest `4.3.3` via the `MSTest` meta-package, pinned in `Directory.Packages.props`.

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
src/Compuse.Contracts/
tests/Compuse.Contracts.Tests/
```

`CompuseAgent.Native.slnx` contains exactly:

- `src/Compuse.Contracts/Compuse.Contracts.csproj`
- `tests/Compuse.Contracts.Tests/Compuse.Contracts.Tests.csproj`

Build outputs under `bin/`, `obj/`, `.artifacts/`, and `TestResults/` are ignored. Both `packages.lock.json` files are tracked.

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

## Verification

Run every command from the repository root. Restore lock files once after changing package versions, then use locked mode.

```powershell
dotnet --version
git status --short --branch
git rev-parse --verify HEAD
dotnet restore .\CompuseAgent.Native.slnx --locked-mode --verbosity minimal
dotnet sln .\CompuseAgent.Native.slnx list
dotnet build .\CompuseAgent.Native.slnx --configuration Release --no-restore
dotnet format .\CompuseAgent.Native.slnx --verify-no-changes --no-restore --verbosity diagnostic
dotnet test .\CompuseAgent.Native.slnx --configuration Release --no-build --no-restore --logger "console;verbosity=normal"
git diff --check --no-index -- NUL .\README.md
git check-ignore -q --no-index .\src\Compuse.Contracts\bin\probe.dll
git check-ignore -q --no-index .\tests\Compuse.Contracts.Tests\packages.lock.json
```

Expected results:

1. `dotnet --version` prints `10.0.302`.
2. Git reports an unborn `main` branch. Do not create a commit during M001 review.
3. `git rev-parse --verify HEAD` exits `128` because HEAD cannot be resolved.
4. Locked restore succeeds for both projects.
5. Solution list contains exactly the two projects above.
6. Release build succeeds with 0 warnings and 0 errors.
7. Format check reports no required changes.
8. Tests pass at least 30 cases with 0 failed and 0 skipped.
9. `git diff --check --no-index -- NUL .\README.md` exits `1` with added README lines and no whitespace-error diagnostic.
10. `bin\probe.dll` is ignored (exit `0`). `packages.lock.json` is not ignored (exit `1`).

If SDK `10.0.302` is unavailable, verification is blocked. Do not roll forward to another SDK.
