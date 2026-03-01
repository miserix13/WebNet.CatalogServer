using MessagePack;

namespace WebNet.CatalogServer;

[MessagePackObject]
public record DatabaseMetadata(
    [property: Key(0)] Guid Id,
    [property: Key(1)] string Name,
    [property: Key(2)] DateTimeOffset CreatedAtUtc,
    [property: Key(3)] ConsistencyLevel Consistency,
    [property: Key(4)] bool IsPrimary);
