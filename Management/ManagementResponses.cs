using MessagePack;

namespace WebNet.CatalogServer;

[MessagePackObject]
public record DropDatabaseResponse(
    [property: Key(0)] string Name,
    [property: Key(1)] bool Dropped);

[MessagePackObject]
public record ListDatabasesResponse([property: Key(0)] IReadOnlyCollection<DatabaseMetadata> Databases);

[MessagePackObject]
public record DropCatalogResponse(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName,
    [property: Key(2)] bool Dropped);

[MessagePackObject]
public record ListCatalogsResponse(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] IReadOnlyCollection<CatalogMetadata> Catalogs);

[MessagePackObject]
public record PutDocumentResponse(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName,
    [property: Key(2)] Guid DocumentId,
    [property: Key(3)] bool ReplacedExisting);

[MessagePackObject]
public record GetDocumentResponse(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName,
    [property: Key(2)] Document Document);

[MessagePackObject]
public record DeleteDocumentResponse(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName,
    [property: Key(2)] Guid DocumentId,
    [property: Key(3)] bool Deleted);

public enum ServerHealthStatus
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2
}

[MessagePackObject]
public record HealthResponse(
    [property: Key(0)] ServerHealthStatus Status,
    [property: Key(1)] DateTimeOffset StartedAtUtc,
    [property: Key(2)] TimeSpan Uptime,
    [property: Key(3)] int DatabaseCount,
    [property: Key(4)] int CatalogCount,
    [property: Key(5)] int DocumentCount,
    [property: Key(6)] string? PrimaryDatabaseName,
    [property: Key(7)] int SelfCheckIssueCount,
    [property: Key(8)] bool IsRunning,
    [property: Key(9)] bool ClusterEnabled,
    [property: Key(10)] bool ClusterRunning,
    [property: Key(11)] string? ClusterSystemName,
    [property: Key(12)] string? ClusterHostname,
    [property: Key(13)] int ClusterPort,
    [property: Key(14)] int ClusterMemberCount);

[MessagePackObject]
public record MetricsResponse([property: Key(0)] IReadOnlyDictionary<string, double> Values);

[MessagePackObject]
public record SelfCheckIssue(
    [property: Key(0)] string Code,
    [property: Key(1)] string Message);

[MessagePackObject]
public record SelfCheckResponse(
    [property: Key(0)] bool IsHealthy,
    [property: Key(1)] int IssueCount,
    [property: Key(2)] IReadOnlyCollection<SelfCheckIssue> Issues);

[MessagePackObject]
public record MaintenanceDiagnosticsResponse(
    [property: Key(0)] long ZoneTreeSuccesses,
    [property: Key(1)] long ZoneTreeFailures,
    [property: Key(2)] long RocksDbSuccesses,
    [property: Key(3)] long RocksDbFailures,
    [property: Key(4)] long FastDbSuccesses,
    [property: Key(5)] long FastDbFailures,
    [property: Key(6)] long TransportRateLimitedRequests,
    [property: Key(7)] long TransportRejectedConnections,
    [property: Key(8)] long TransportReadTimeouts,
    [property: Key(9)] long TransportInvalidFrames,
    [property: Key(10)] long TransportInvalidRequests,
    [property: Key(11)] long TransportDispatchErrors,
    [property: Key(12)] long TransportProtocolDisconnects);

public readonly record struct StorageStatistics(
    int DatabaseCount,
    int CatalogCount,
    int CatalogItemCount,
    int DocumentCount,
    string? PrimaryDatabaseName);
