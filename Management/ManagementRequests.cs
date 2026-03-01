using MessagePack;

namespace WebNet.CatalogServer;

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
public record HealthRequest;

[MessagePackObject]
public record MetricsRequest;
