using MessagePack;

namespace WebNet.CatalogServer;

public sealed class TcpCommandDispatcher
{
    private readonly Server server;

    public TcpCommandDispatcher(Server server)
    {
        this.server = server;
    }

    public async Task<byte[]> DispatchAsync(byte[] framePayload, CancellationToken cancellationToken = default)
    {
        var wireRequest = MessagePackSerializer.Deserialize<WireRequest>(framePayload);
        var securityContext = new SecurityContext(
            wireRequest.Token,
            wireRequest.ClientCertificateThumbprint,
            wireRequest.Subject,
            wireRequest.Roles);

        var response = await this.server.HandleAsync(wireRequest.Request, securityContext, cancellationToken);
        var wireResponse = new WireResponse(response);

        return MessagePackSerializer.Serialize(wireResponse);
    }
}
