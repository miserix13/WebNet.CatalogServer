using MessagePack;

namespace WebNet.CatalogServer;

public sealed class TcpCommandDispatcher
{
    private const int MaxTokenLength = 4096;
    private const int MaxSubjectLength = 256;
    private const int MaxRoles = 32;
    private const int MaxRoleLength = 64;

    private readonly Server server;

    public TcpCommandDispatcher(Server server)
    {
        this.server = server;
    }

    public async Task<byte[]> DispatchAsync(byte[] framePayload, CancellationToken cancellationToken = default)
    {
        if (framePayload is null || framePayload.Length == 0)
        {
            throw new InvalidDataException("Frame payload is empty.");
        }

        var wireRequest = MessagePackSerializer.Deserialize<WireRequest>(framePayload);
        ValidateRequest(wireRequest);

        var securityContext = new SecurityContext(
            wireRequest.Token,
            wireRequest.ClientCertificateThumbprint,
            wireRequest.Subject,
            wireRequest.Roles);

        var response = await this.server.HandleAsync(wireRequest.Request, securityContext, cancellationToken);
        var wireResponse = new WireResponse(response);

        return MessagePackSerializer.Serialize(wireResponse);
    }

    private static void ValidateRequest(WireRequest wireRequest)
    {
        if (wireRequest.Request is null)
        {
            throw new InvalidDataException("Missing request envelope.");
        }

        if (wireRequest.Request.RequestId == Guid.Empty)
        {
            throw new InvalidDataException("RequestId is required.");
        }

        if (wireRequest.Request.Command == CommandKind.Unknown)
        {
            throw new InvalidDataException("Unknown command is not allowed.");
        }

        if (wireRequest.Request.Payload is null || wireRequest.Request.Payload.Length == 0)
        {
            throw new InvalidDataException("Request payload is required.");
        }

        if (string.IsNullOrWhiteSpace(wireRequest.Token) || wireRequest.Token.Length > MaxTokenLength)
        {
            throw new InvalidDataException("Token is missing or exceeds limits.");
        }

        if (string.IsNullOrWhiteSpace(wireRequest.ClientCertificateThumbprint))
        {
            throw new InvalidDataException("Client certificate thumbprint is required.");
        }

        if (!string.IsNullOrWhiteSpace(wireRequest.Subject) && wireRequest.Subject.Length > MaxSubjectLength)
        {
            throw new InvalidDataException("Subject exceeds limits.");
        }

        if (wireRequest.Roles is not null)
        {
            if (wireRequest.Roles.Length > MaxRoles)
            {
                throw new InvalidDataException("Too many roles supplied.");
            }

            foreach (var role in wireRequest.Roles)
            {
                if (string.IsNullOrWhiteSpace(role) || role.Length > MaxRoleLength)
                {
                    throw new InvalidDataException("Invalid role value supplied.");
                }
            }
        }
    }
}
