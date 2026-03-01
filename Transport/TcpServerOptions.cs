using System.Net;

namespace WebNet.CatalogServer;

public sealed record TcpServerOptions(
    IPAddress BindAddress,
    int Port,
    int MaxFrameBytes = 4 * 1024 * 1024,
    int Backlog = 256,
    int MaxConcurrentConnections = 256,
    int MaxRequestsPerSecondPerClient = 100,
    int MaxBurstRequestsPerClient = 200,
    TimeSpan ClientReadTimeout = default,
    bool DisconnectOnRateLimit = true,
    bool DisconnectOnProtocolViolation = true)
{
    public static TcpServerOptions Default { get; } = new(
        IPAddress.Any,
        7070,
        MaxFrameBytes: 4 * 1024 * 1024,
        Backlog: 256,
        MaxConcurrentConnections: 256,
        MaxRequestsPerSecondPerClient: 100,
        MaxBurstRequestsPerClient: 200,
        ClientReadTimeout: TimeSpan.FromSeconds(30),
        DisconnectOnRateLimit: true,
        DisconnectOnProtocolViolation: true);
}
