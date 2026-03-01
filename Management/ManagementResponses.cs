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
    [property: Key(4)] int CatalogCount);

[MessagePackObject]
public record MetricsResponse([property: Key(0)] IReadOnlyDictionary<string, double> Values);

public readonly record struct StorageStatistics(
    int DatabaseCount,
    int CatalogCount,
    int CatalogItemCount,
    int DocumentCount,
    string? PrimaryDatabaseName);
