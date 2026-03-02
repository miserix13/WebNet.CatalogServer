using System.Net;

namespace WebNet.CatalogClient;

public sealed record CatalogClientOptions
{
    public IPAddress Address { get; init; } = IPAddress.Loopback;
    public int Port { get; init; } = 7070;
    public int MaxFrameBytes { get; init; } = 4 * 1024 * 1024;
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed record CatalogClientAuthContext(
    string Token,
    string ClientCertificateThumbprint,
    string? Subject = null,
    string[]? Roles = null);