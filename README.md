# WebNet.CatalogServer #

## A document / catalog (as in a collection of related items) database server built on .NET 10 and written in C# ##

## For storage, uses <https://github.com/koculu/ZoneTree>, <https://github.com/stonstad/Stellar.FastDB> and <https://github.com/curiosity-ai/rocksdb-sharp> key-value stores for both documents and catalogs

## Uses <https://github.com/MessagePack-CSharp/MessagePack-CSharp> for serialization

## Uses <https://github.com/Azure/DotNetty> and <https://github.com/beetlex-io/BeetleX> for networking

## Uses <https://github.com/akkadotnet/akka.net> for clustering support

## Uses <https://github.com/litegraphdb/litegraph> for auth

## Support multiple databases with a master / primary database

## For data model: Document, CatalogItem and Catalog

## Persistence

- Storage is now durable and automatically persisted to a MessagePack snapshot file.
- Default storage root: `./data` (relative to the process working directory).
- Configurable storage root: `--data-root <path>` or environment variable `WEBNET_DATA_ROOT`.
- Filesystem layout is validated at startup and these directories are created if missing:
  - `kv/zonetree`
  - `kv/fastdb`
  - `kv/rocksdb`
  - `snapshots/storage.snapshot.mpk`
- Persistence is written through all configured KV engines (ZoneTree, FastDB, RocksDB) and a snapshot fallback file.
- After each successful persisted mutation, automated maintenance runs per engine:
  - ZoneTree maintainer merge/background maintenance pass
  - RocksDB `CompactRange` compaction pass
  - FastDB `DefragmentMemoryAsync` + flush pass
- On startup, state recovery loads from engine-backed persisted bytes (with snapshot fallback).
- Every successful state mutation (create/drop db, create/drop catalog, put/delete document) is atomically persisted across engines.

## Security

- Runtime auth provider is configurable via `WEBNET_AUTH_PROVIDER`:
  - `litegraph` (default): LiteGraph SQLite-backed credentials
  - `windows`: caller `Subject` + `Roles` are authorized against command-role policy
- Default auth DB path: `./data/auth/litegraph-auth.db` (override with `WEBNET_AUTH_DB_PATH`).
- Default bootstrap credential is created if missing:
  - token: `dev-token` (override `WEBNET_AUTH_BOOTSTRAP_BEARER_TOKEN`)
  - role/name: `admin` (override `WEBNET_AUTH_BOOTSTRAP_CREDENTIAL_NAME`)
  - bootstrap enabled by default (`WEBNET_AUTH_BOOTSTRAP_ENABLED=true|false`)
- Optional Windows subject allowlist (`windows` mode only):
  - env: `WEBNET_AUTH_WINDOWS_ALLOWED_SUBJECTS`
  - format: comma/semicolon separated values (example: `CORP\\alice;CORP\\catalog-admins`)
  - when empty, any non-empty `Subject` is eligible and command access is controlled by roles/policy
- Client certificate thumbprints are validated against allowlist:
  - env: `WEBNET_ALLOWED_CERT_THUMBPRINTS` (comma/semicolon separated)
  - default includes `dev-thumbprint` for local smoke tests
- Command authorization uses explicit command->role policy:
  - default policy: readers for read/health commands, writers for read+write commands, admins for all configured commands
  - override env: `WEBNET_AUTH_COMMAND_ROLE_POLICY`
  - format: `CommandKind=role1,role2;CommandKind=role3`
  - example: `GetDocument=admin,reader;PutDocument=admin,writer;SelfCheck=admin`
  - optional strict coverage mode: `WEBNET_AUTH_REQUIRE_FULL_COMMAND_POLICY=true`
    - when enabled, startup fails unless override policy explicitly maps every command (except `Unknown`)
  - startup fails fast with clear config error if command names are unknown or entries are malformed

## Run

- Start server (default port 7070):

```powershell
dotnet run -- server
```

- Start server with explicit storage root (and strict startup checks):

```powershell
dotnet run -- server --data-root C:\catalog-data --fail-on-self-check
```

- Start server on custom port:

```powershell
dotnet run -- server 7071
```

- Start server and fail startup if self-check has issues:

```powershell
dotnet run -- server --fail-on-self-check
```

- Start server on custom port with strict self-check:

```powershell
dotnet run -- server 7071 --fail-on-self-check
```

- Start server using Windows auth provider (CLI override):

```powershell
dotnet run -- server --auth-provider windows
```

- Run internal self-check only (no TCP listener):

```powershell
dotnet run -- server --self-check-only
```

- Show CLI help:

```powershell
dotnet run -- --help
```

Invalid modes/flags/ports now fail fast with a non-zero exit code and a help hint.

- Run automated tests:

```powershell
dotnet test WebNet.CatalogServer.slnx
```

- Run TCP smoke-test client against local server:

```powershell
dotnet run -- client 127.0.0.1 7070
```

The smoke-test client now includes internal `SelfCheck` and `MaintenanceDiagnostics` management commands.
`MaintenanceDiagnostics` now reports both KV-engine maintenance counters and transport abuse counters (rate-limited requests, rejected connections, read timeouts, invalid frames/requests, dispatch errors, and protocol disconnects).
