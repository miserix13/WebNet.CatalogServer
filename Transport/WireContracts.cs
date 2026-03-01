using MessagePack;

namespace WebNet.CatalogServer;

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
