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

Server lifecycle events (startup checks, ready state, shutdown path) are now emitted as structured JSON logs.

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

- Start server with Akka.NET cluster bootstrap enabled:

```powershell
dotnet run -- server --enable-cluster
```

- Start server with Akka.NET cluster bootstrap and explicit cluster port:

```powershell
dotnet run -- server --enable-cluster --cluster-port 8210
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
`MaintenanceDiagnostics` now reports KV-engine maintenance counters, transport abuse counters (rate-limited requests, rejected connections, read timeouts, invalid frames/requests, dispatch errors, and protocol disconnects), and a cluster diagnostics section (enabled/running/system/host/port/member count).
The smoke-test client also reports expanded `Health` and `Metrics` output, including lifecycle and self-check signals.
When cluster bootstrap is enabled, `Health` and `Metrics` also expose cluster runtime status (`cluster.enabled`, `cluster.running`, `cluster.members.count`).

Integration test coverage includes TCP end-to-end command flow, TCP auth failure paths, and storage recovery across process restarts.

## CatalogClient (phase 2) ##

A typed .NET client library is available in `src/WebNet.CatalogClient`.

- Target framework: `net10.0`
- Protocol: TCP + length-prefixed MessagePack frames
- Auth model: caller-provided delegate supplies token/thumbprint/subject/roles per request

Example:

```csharp
using System.Net;
using WebNet.CatalogClient;

var options = new CatalogClientOptions
{
  Address = IPAddress.Loopback,
  Port = 7070,
  MaxFrameBytes = 4 * 1024 * 1024,
  ConnectTimeout = TimeSpan.FromSeconds(10),
  ReadTimeout = TimeSpan.FromSeconds(30),
  ConnectionRetryCount = 2,
  ConnectionRetryDelay = TimeSpan.FromMilliseconds(200),
  RateLimitRetryCount = 2,
  RateLimitRetryDelay = TimeSpan.FromMilliseconds(250)
};

await using var client = new CatalogClient(
  options,
  _ => ValueTask.FromResult(new CatalogClientAuthContext(
    Token: "dev-token",
    ClientCertificateThumbprint: "dev-thumbprint",
    Subject: "dev-user",
    Roles: ["admin"])));

var database = await client.CreateDatabaseAsync(new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));
var catalog = await client.CreateCatalogAsync(new CreateCatalogRequest("default", "products"));

var put = await client.PutDocumentAsync(new PutDocumentRequest(
  "default",
  "products",
  new Document
  {
    Properties = { ["sku"] = "SKU-100", ["name"] = "Product 100" }
  }));

var get = await client.GetDocumentAsync(new GetDocumentRequest("default", "products", put.DocumentId));
var health = await client.HealthAsync();
```

Error handling notes:

- Business/authorization failures are surfaced as `CatalogClientException` with server `ErrorCode` values (for example `auth.forbidden`).
- `transport.rate_limited` responses are retried up to `RateLimitRetryCount` with `RateLimitRetryDelay` between attempts.
- Socket/stream failures are retried up to `ConnectionRetryCount` with `ConnectionRetryDelay`, reconnecting automatically.
