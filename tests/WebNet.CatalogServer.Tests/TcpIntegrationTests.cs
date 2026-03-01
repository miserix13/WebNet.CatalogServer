namespace WebNet.CatalogServer.Tests;

using MessagePack;
using System.Net;
using System.Net.Sockets;
using Xunit;

public sealed class TcpIntegrationTests
{
    [Fact]
    public async Task TcpHost_EndToEndCommandFlow_Succeeds()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"), "tcp-flow.snapshot.mpk");
        var storage = new Storage(new FileStoragePersistenceAdapter(snapshotPath));
        var server = new Server(storage, new AllowAllTokenAuthorizer(), new AllowAllClientCertificateValidator());
        var port = GetFreeTcpPort();

        await using var host = server.CreateTcpHost(TcpServerOptions.Default with
        {
            BindAddress = IPAddress.Loopback,
            Port = port,
            MaxRequestsPerSecondPerClient = 1000,
            MaxBurstRequestsPerClient = 1000
        });

        server.Start();
        await host.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();

            var createDb = await SendWireAsync(stream, CommandKind.CreateDatabase, new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));
            Assert.True(createDb.IsSuccess);

            var createCatalog = await SendWireAsync(stream, CommandKind.CreateCatalog, new CreateCatalogRequest("default", "products"));
            Assert.True(createCatalog.IsSuccess);

            var documentId = Guid.NewGuid();
            var put = await SendWireAsync(stream, CommandKind.PutDocument, new PutDocumentRequest(
                "default",
                "products",
                new Document
                {
                    DocumentId = documentId,
                    Properties =
                    {
                        ["sku"] = "SKU-100",
                        ["name"] = "Integration Product"
                    }
                }));
            Assert.True(put.IsSuccess);

            var get = await SendWireAsync(stream, CommandKind.GetDocument, new GetDocumentRequest("default", "products", documentId));
            Assert.True(get.IsSuccess);
            var getPayload = MessagePackSerializer.Deserialize<GetDocumentResponse>(get.Payload);
            Assert.Equal("Integration Product", getPayload.Document.Properties["name"]);

            var delete = await SendWireAsync(stream, CommandKind.DeleteDocument, new DeleteDocumentRequest("default", "products", documentId));
            Assert.True(delete.IsSuccess);

            var listDatabases = await SendWireAsync(stream, CommandKind.ListDatabases, new ListDatabasesRequest());
            Assert.True(listDatabases.IsSuccess);
            var listPayload = MessagePackSerializer.Deserialize<ListDatabasesResponse>(listDatabases.Payload);
            Assert.Contains(listPayload.Databases, db => db.Name == "default" && db.IsPrimary);
        }
        finally
        {
            await host.StopAsync();
            server.Stop();
        }
    }

    [Fact]
    public async Task TcpHost_InvalidCertificate_ReturnsInvalidCertificateError()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"), "tcp-auth-cert.snapshot.mpk");
        var storage = new Storage(new FileStoragePersistenceAdapter(snapshotPath));
        var server = new Server(storage, new AllowAllTokenAuthorizer(), new ExactThumbprintValidator("trusted-thumbprint"));
        var port = GetFreeTcpPort();

        await using var host = server.CreateTcpHost(TcpServerOptions.Default with
        {
            BindAddress = IPAddress.Loopback,
            Port = port,
            MaxRequestsPerSecondPerClient = 1000,
            MaxBurstRequestsPerClient = 1000
        });

        server.Start();
        await host.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();

            var response = await SendWireAsync(
                stream,
                CommandKind.Health,
                new HealthRequest(),
                clientCertificateThumbprint: "untrusted-thumbprint");

            Assert.False(response.IsSuccess);
            Assert.Equal("auth.invalid_certificate", response.ErrorCode);
        }
        finally
        {
            await host.StopAsync();
            server.Stop();
        }
    }

    [Fact]
    public async Task TcpHost_UnauthorizedCommand_ReturnsForbiddenError()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"), "tcp-authz.snapshot.mpk");
        var storage = new Storage(new FileStoragePersistenceAdapter(snapshotPath));
        var server = new Server(storage, new DenyAllTokenAuthorizer(), new AllowAllClientCertificateValidator());
        var port = GetFreeTcpPort();

        await using var host = server.CreateTcpHost(TcpServerOptions.Default with
        {
            BindAddress = IPAddress.Loopback,
            Port = port,
            MaxRequestsPerSecondPerClient = 1000,
            MaxBurstRequestsPerClient = 1000
        });

        server.Start();
        await host.StartAsync();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();

            var response = await SendWireAsync(stream, CommandKind.Health, new HealthRequest());

            Assert.False(response.IsSuccess);
            Assert.Equal("auth.forbidden", response.ErrorCode);
        }
        finally
        {
            await host.StopAsync();
            server.Stop();
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<ResponseEnvelope> SendWireAsync<TPayload>(
        NetworkStream stream,
        CommandKind command,
        TPayload payload,
        string token = "dev-token",
        string clientCertificateThumbprint = "dev-thumbprint",
        string subject = "test-user",
        string[]? roles = null)
    {
        var request = RequestEnvelope.FromPayload(Guid.NewGuid(), command, payload!);
        var wire = new WireRequest(
            request,
            token,
            clientCertificateThumbprint,
            subject,
            roles ?? ["admin"]);

        await TcpFrameCodec.WriteFrameAsync(stream, MessagePackSerializer.Serialize(wire));
        var frame = await TcpFrameCodec.ReadFrameAsync(stream, maxFrameBytes: 4 * 1024 * 1024);
        if (frame is null)
        {
            throw new InvalidOperationException("Connection closed before response frame was received.");
        }

        return MessagePackSerializer.Deserialize<WireResponse>(frame).Response;
    }

    private sealed class DenyAllTokenAuthorizer : ITokenAuthorizer
    {
        public bool Authorize(SecurityContext securityContext, CommandKind command) => false;
    }

    private sealed class ExactThumbprintValidator(string allowedThumbprint) : IClientCertificateValidator
    {
        public bool Validate(string thumbprint)
        {
            return string.Equals(allowedThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase);
        }
    }
}