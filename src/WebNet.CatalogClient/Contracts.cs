using MessagePack;

namespace WebNet.CatalogClient;

public enum CommandKind
{
    Unknown = 0,
    CreateDatabase = 1,
    DropDatabase = 2,
    ListDatabases = 3,
    CreateCatalog = 10,
    DropCatalog = 11,
    ListCatalogs = 12,
    PutDocument = 20,
    GetDocument = 21,
    DeleteDocument = 22,
    Health = 100,
    Metrics = 101,
    SelfCheck = 102,
    MaintenanceDiagnostics = 103
}

public enum ConsistencyLevel
{
    Strong = 0,
    Eventual = 1
}

[MessagePackObject]
public record RequestEnvelope(
    [property: Key(0)] Guid RequestId,
    [property: Key(1)] CommandKind Command,
    [property: Key(2)] byte[] Payload,
    [property: Key(3)] DateTimeOffset CreatedAtUtc)
{
    public static RequestEnvelope FromPayload<T>(Guid requestId, CommandKind command, T payload)
    {
        return new RequestEnvelope(
            requestId,
            command,
            MessagePackSerializer.Serialize(payload),
            DateTimeOffset.UtcNow);
    }
}

[MessagePackObject]
public record ResponseEnvelope(
    [property: Key(0)] Guid RequestId,
    [property: Key(1)] bool IsSuccess,
    [property: Key(2)] string? ErrorCode,
    [property: Key(3)] string? ErrorMessage,
    [property: Key(4)] byte[] Payload,
    [property: Key(5)] DateTimeOffset CreatedAtUtc);

[MessagePackObject]
public sealed record WireRequest(
    [property: Key(0)] RequestEnvelope Request,
    [property: Key(1)] string Token,
    [property: Key(2)] string ClientCertificateThumbprint,
    [property: Key(3)] string? Subject = null,
    [property: Key(4)] string[]? Roles = null);

[MessagePackObject]
public sealed record WireResponse(
    [property: Key(0)] ResponseEnvelope Response);

[MessagePackObject]
public record Document
{
    [Key(0)] public Guid DocumentId { get; set; } = Guid.NewGuid();
    [Key(1)] public Dictionary<string, string> Properties { get; set; } = [];
}

[MessagePackObject]
public record CatalogMetadata(
    [property: Key(0)] Guid Id,
    [property: Key(1)] string Name,
    [property: Key(2)] string DatabaseName,
    [property: Key(3)] DateTimeOffset CreatedAtUtc);

[MessagePackObject]
public record DatabaseMetadata(
    [property: Key(0)] Guid Id,
    [property: Key(1)] string Name,
    [property: Key(2)] DateTimeOffset CreatedAtUtc,
    [property: Key(3)] ConsistencyLevel Consistency,
    [property: Key(4)] bool IsPrimary);

[MessagePackObject]
public record CreateDatabaseRequest(
    [property: Key(0)] string Name,
    [property: Key(1)] ConsistencyLevel Consistency,
    [property: Key(2)] bool MakePrimary = false);

[MessagePackObject]
public record DropDatabaseRequest([property: Key(0)] string Name);

[MessagePackObject]
public record ListDatabasesRequest;

[MessagePackObject]
public record CreateCatalogRequest(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName);

[MessagePackObject]
public record DropCatalogRequest(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName);

[MessagePackObject]
public record ListCatalogsRequest([property: Key(0)] string DatabaseName);

[MessagePackObject]
public record PutDocumentRequest(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName,
    [property: Key(2)] Document Document);

[MessagePackObject]
public record GetDocumentRequest(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName,
    [property: Key(2)] Guid DocumentId);

[MessagePackObject]
public record DeleteDocumentRequest(
    [property: Key(0)] string DatabaseName,
    [property: Key(1)] string CatalogName,
    [property: Key(2)] Guid DocumentId);

[MessagePackObject]
public record HealthRequest;

[MessagePackObject]
public record MetricsRequest;

[MessagePackObject]
public record SelfCheckRequest;

[MessagePackObject]
public record MaintenanceDiagnosticsRequest;

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
    [property: Key(12)] long TransportProtocolDisconnects,
    [property: Key(13)] bool ClusterEnabled,
    [property: Key(14)] bool ClusterRunning,
    [property: Key(15)] string? ClusterSystemName,
    [property: Key(16)] string? ClusterHostname,
    [property: Key(17)] int ClusterPort,
    [property: Key(18)] int ClusterMemberCount);