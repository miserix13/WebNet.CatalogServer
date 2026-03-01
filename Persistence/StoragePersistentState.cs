using MessagePack;

namespace WebNet.CatalogServer;

[MessagePackObject]
public sealed record StoragePersistentState(
    [property: Key(0)] string? PrimaryDatabaseName,
    [property: Key(1)] IReadOnlyCollection<PersistedDatabaseState> Databases)
{
    public static StoragePersistentState Empty { get; } = new(null, []);
}

[MessagePackObject]
public sealed record PersistedDatabaseState(
    [property: Key(0)] DatabaseMetadata Metadata,
    [property: Key(1)] IReadOnlyCollection<PersistedCatalogState> Catalogs);

[MessagePackObject]
public sealed record PersistedCatalogState(
    [property: Key(0)] Catalog Catalog,
    [property: Key(1)] DateTimeOffset CreatedAtUtc,
    [property: Key(2)] IReadOnlyCollection<Document> Documents);
