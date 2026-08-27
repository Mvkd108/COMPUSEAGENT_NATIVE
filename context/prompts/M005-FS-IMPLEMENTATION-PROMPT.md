### ROLE

You are the implementation agent for one bounded module of COMPUSEAGENT_NATIVE.

Implement only M005-FS. Inspect the repository before changing anything. Run verification yourself.
Do not modify `Compuse.Contracts`, `Compuse.Requests`, `Compuse.Protocol`, `Compuse.Runtime` public types, or proto schemas.
Do not commit unless the user explicitly asks.

### MODULE ID

`M005-FS — Windows filesystem target identity and capability discovery`

### OBJECTIVE

Resolve an M003 target selector and physical-file sources into short-lived typed snapshots that later routing and transfer modules can consume. Discover capabilities relevant to filesystem copy/move. Perform no transfer, pointer movement, focus change, or elevation.

Application-surface selectors are recorded as unsupported. Window/HWND/PID enumeration is out of this module.

### CURRENT STATE

- M001–M004 are approved. Local `main` includes M004 at `56b35fcab253df3049aa514dd1e0e2c42bc122a1`.
- `DropFilesRequest` sources are physical files; targets are `FilesystemContainer` or `ApplicationSurface`.
- Paths on the request are already absolute and lexically normalized. Do not re-implement `WindowsPath`.
- HWND and PID hints are not identity. Do not treat them as identity here either.

### DECISIONS (FROZEN)

- Track A prototype is filesystem-only. Full window discovery is deferred.
- Discovery is managed C# on `net10.0-windows`. No native C ABI.
- Identity tuple for a filesystem object: normalized path (locator) plus volume serial plus file index from `GetFileInformationByHandle`. Open with `FILE_FLAG_OPEN_REPARSE_POINT` so the named object is identified, not a reparse target.
- Writable probe for a directory: open with `FILE_ADD_FILE` without creating a file. Access denied means inaccessible, not a silent downgrade.
- Missing, not-a-file, not-a-container, and inaccessible are snapshot statuses. Discovery does not throw those as business outcomes and does not produce `OperationResult`.

### FILES AND OWNERSHIP

May create:

- `src/Compuse.Discovery/**`
- `tests/Compuse.Discovery.Tests/**`

May modify: `CompuseAgent.Native.slnx`, `README.md`, `context/*`.

Must not implement routing, `IFileOperation`, CLI, OLE, or `drop_files` execution.

### INTERFACES

Public types in `Compuse.Discovery`:

- `FilesystemIdentity` (normalized path, volume serial, file index)
- `PathPresence` enum: `Missing=1`, `File=2`, `Directory=3`, `Inaccessible=4`
- `PathInspection` (path, presence, optional identity, byte length, `CanAddFiles`)
- `SourceStatus` enum: `PhysicalFile=1`, `Missing=2`, `NotAFile=3`, `Inaccessible=4`
- `SourceSnapshot`
- `DestinationStatus` enum: `FilesystemContainer=1`, `ApplicationSurfaceUnsupported=2`, `Missing=3`, `NotAContainer=4`, `Inaccessible=5`
- `DestinationSnapshot`
- `IFilesystemDiscovery`
- `WindowsFilesystemDiscovery`

`IFilesystemDiscovery`:

```
PathInspection Inspect(string absolutePath, CancellationToken cancellationToken);
SourceSnapshot DiscoverSource(string absolutePath, CancellationToken cancellationToken);
IReadOnlyList<SourceSnapshot> DiscoverSources(IReadOnlyList<string> absolutePaths, CancellationToken cancellationToken);
DestinationSnapshot DiscoverDestination(TargetSelector target, CancellationToken cancellationToken);
```

Application-surface targets return `DestinationStatus.ApplicationSurfaceUnsupported` with no Win32 calls.

### ACCEPTANCE

- Real temp-directory tests on Windows: file, directory, missing path, file-as-destination, identity stable across two inspects of the same file, distinct files have distinct identities.
- Discovery never copies, moves, creates, or deletes caller files (temp fixtures created by tests are allowed).
- No HWND enumeration, `SendInput`, `SetCursorPos`, or focus APIs.
- Locked restore, Release 0 warnings, format verify-no-changes, tests pass.

### OUT OF SCOPE

Routing, transfer, CLI, OLE, C ABI, overwrite policy, operation results.
