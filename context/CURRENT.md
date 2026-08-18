# Current state

Last updated: 2026-08-18

## M001 — Repository bootstrap and executable contract foundation

Status: `APPROVED` and committed.

- Revision: `42e5d0ca368939d436f5d0a8cc25d82316dc1bb7` on `main`
- SDK pin: `10.0.302` (`rollForward=disable`, `allowPrerelease=false`)
- Projects: `Compuse.Contracts`, `Compuse.Contracts.Tests`
- Correlation IDs reject surrounding whitespace around an otherwise valid GUID
  `D` form (`Length != 36` before `Guid.TryParseExact`)
- Independent verification: locked restore, Release 0 warnings, format
  verify-no-changes, 80 tests passed / 0 failed / 0 skipped

## M002 — Canonical Protobuf v1 operation-result contract

Status: `APPROVED` and committed.

- Revision: `0d8e66d538d019237268549c80bcdec753b9b9dd` on `main`
- Schema: `proto/compuse/v1/operation_result.proto`, package `compuse.v1`,
  C# namespace `Compuse.Protocol.V1`
- Mapper: `OperationResultProtoMapper.ToProto` / `FromProto`
- Packages: Google.Protobuf `3.35.1`, Grpc.Tools `2.83.0` (private),
  MSTest `4.3.3` preserved
- Generated C# lives only under ignored `obj/` (`OperationResult.cs`)
- Canonical wire vector
  `0a2461626364656630312d323334352d363738392d616263642d6566303132333435363738391004`

## M003 — `drop_files` request contract and canonical request schema

Status: implemented in the working tree. Not independently reviewed. Not
committed.

- Domain: `Compuse.Requests` (`DropFilesRequest`, physical-file sources, copy
  and move, filesystem-container and application-surface targets)
- Schema: `proto/compuse/v1/drop_files.proto`, package `compuse.v1`
- Mapper: `DropFilesRequestProtoMapper.ToProto` / `FromProto`
- No filesystem, Win32, discovery, routing, CLI, or native code
- `Compuse.Contracts` public types were not modified
- M001 lock files remain unchanged
- Implementer verification: locked restore, Release 0 warnings, format
  0 files, 13 request tests, 63 protocol tests, 156 solution tests passed /
  0 failed / 0 skipped; canonical copy wire vector
  `0a2461626364656630312d323334352d363738392d616263642d65663031323334353637383912100a0e0a0c433a5c7372635c612e7478741801220a0a080a06433a5c647374`

## Intentionally absent

No runtime, CLI, native COM, C ABI, transport, GUI, Hyper-V, networking, CUA,
or `drop_files` execution.

## Known operational notes

- SDK `10.0.302` is installed user-locally under
  `%LOCALAPPDATA%\Microsoft\dotnet`. Shells that prefer
  `C:\Program Files\dotnet\dotnet.exe` (8.x) will fail verification until that
  SDK is on `PATH`.
- MSTest transitively restores a test-only telemetry extension.
  `Compuse.Contracts` has no package dependencies.
