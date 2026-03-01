using System.Net;

namespace WebNet.CatalogServer;

public sealed record TcpServerOptions(
    IPAddress BindAddress,
    int Port,
    int MaxFrameBytes = 4 * 1024 * 1024)
{
    public static TcpServerOptions Default { get; } = new(IPAddress.Any, 7070);
}
