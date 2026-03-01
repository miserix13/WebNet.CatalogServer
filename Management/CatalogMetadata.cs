using MessagePack;

namespace WebNet.CatalogServer;

[MessagePackObject]
public record CatalogMetadata(
    [property: Key(0)] Guid Id,
    [property: Key(1)] string Name,
    [property: Key(2)] string DatabaseName,
    [property: Key(3)] DateTimeOffset CreatedAtUtc);
