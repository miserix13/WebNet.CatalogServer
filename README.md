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
- On startup, state recovery loads from engine-backed persisted bytes (with snapshot fallback).
- Every successful state mutation (create/drop db, create/drop catalog, put/delete document) is atomically persisted across engines.

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

The smoke-test client now includes an internal `SelfCheck` management command that reports storage invariant issues.
