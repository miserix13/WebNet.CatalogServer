namespace WebNet.CatalogServer.Tests;

using MessagePack;
using System.Net;
using System.Net.Sockets;
using Xunit;

[Collection("DiagnosticsCounters")]
public sealed class MaintenanceDiagnosticsCommandTests
{
    [Fact]
    public async Task MaintenanceDiagnosticsCommand_ReturnsRuntimeCounters()
    {
        KvMaintenanceDiagnostics.Reset();
        TransportAbuseDiagnostics.Reset();
        ClusterRuntimeDiagnostics.Configure(false, "webnet-catalog", "127.0.0.1", 8110);
        ClusterRuntimeDiagnostics.SetRunning(false, 0);

        var tempRoot = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var layout = StorageDirectoryLayout.Resolve(tempRoot);
            var storage = new Storage(new MultiEngineStoragePersistenceAdapter(layout));
            var server = new Server(storage, new AllowAllTokenAuthorizer(), new AllowAllClientCertificateValidator());
            var security = new SecurityContext("token", "thumbprint", "test", ["admin"]);

            server.Start();

            var createDb = RequestEnvelope.FromPayload(
                Guid.NewGuid(),
                CommandKind.CreateDatabase,
                new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));

            var createDbResponse = await server.HandleAsync(createDb, security);
            Assert.True(createDbResponse.IsSuccess);

            var diagnostics = RequestEnvelope.FromPayload(
                Guid.NewGuid(),
                CommandKind.MaintenanceDiagnostics,
                new MaintenanceDiagnosticsRequest());

            var diagnosticsResponse = await server.HandleAsync(diagnostics, security);
            Assert.True(diagnosticsResponse.IsSuccess);

            var payload = MessagePackSerializer.Deserialize<MaintenanceDiagnosticsResponse>(diagnosticsResponse.Payload);
            Assert.True(payload.ZoneTreeSuccesses > 0);
            Assert.True(payload.RocksDbSuccesses > 0);
            Assert.True(payload.FastDbSuccesses > 0);
            Assert.Equal(0, payload.ZoneTreeFailures);
            Assert.Equal(0, payload.RocksDbFailures);
            Assert.Equal(0, payload.FastDbFailures);
            Assert.True(payload.TransportRateLimitedRequests >= 0);
            Assert.True(payload.TransportRejectedConnections >= 0);
            Assert.True(payload.TransportReadTimeouts >= 0);
            Assert.True(payload.TransportInvalidFrames >= 0);
            Assert.True(payload.TransportInvalidRequests >= 0);
            Assert.True(payload.TransportDispatchErrors >= 0);
            Assert.True(payload.TransportProtocolDisconnects >= 0);
            Assert.False(payload.ClusterEnabled);
            Assert.False(payload.ClusterRunning);
            Assert.Equal("webnet-catalog", payload.ClusterSystemName);
            Assert.Equal("127.0.0.1", payload.ClusterHostname);
            Assert.Equal(8110, payload.ClusterPort);
            Assert.Equal(0, payload.ClusterMemberCount);

            server.Stop();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TransportAbuseDiagnostics_ResetSnapshot_StartsAtZero()
    {
        TransportAbuseDiagnostics.Reset();

        var snapshot = TransportAbuseDiagnostics.Snapshot();

        Assert.Equal(0, snapshot.RateLimitedRequests);
        Assert.Equal(0, snapshot.RejectedConnections);
        Assert.Equal(0, snapshot.ReadTimeouts);
        Assert.Equal(0, snapshot.InvalidFrames);
        Assert.Equal(0, snapshot.InvalidRequests);
        Assert.Equal(0, snapshot.DispatchErrors);
        Assert.Equal(0, snapshot.ProtocolDisconnects);
    }

    [Fact]
    public async Task MaintenanceDiagnosticsCommand_ReportsTransportRateLimitedRequests()
    {
        TransportAbuseDiagnostics.Reset();
        ClusterRuntimeDiagnostics.Configure(false, "webnet-catalog", "127.0.0.1", 8110);
        ClusterRuntimeDiagnostics.SetRunning(false, 0);

        var tempRoot = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));
        var port = GetFreeTcpPort();

        var layout = StorageDirectoryLayout.Resolve(tempRoot);
        var storage = new Storage(new MultiEngineStoragePersistenceAdapter(layout));
        var server = new Server(storage, new AllowAllTokenAuthorizer(), new AllowAllClientCertificateValidator());

        await using var host = server.CreateTcpHost(
            TcpServerOptions.Default with
            {
                BindAddress = IPAddress.Loopback,
                Port = port,
                MaxRequestsPerSecondPerClient = 1,
                MaxBurstRequestsPerClient = 1,
                DisconnectOnRateLimit = false,
                ClientReadTimeout = TimeSpan.FromSeconds(5)
            });

        try
        {
            server.Start();
            await host.StartAsync();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();

            var burstResponses = new List<ResponseEnvelope>();
            for (var i = 0; i < 5; i++)
            {
                burstResponses.Add(await SendWireAsync(stream, CommandKind.Health, new HealthRequest()));
            }

            Assert.Contains(burstResponses, response => !response.IsSuccess && response.ErrorCode == "transport.rate_limited");

            var diagnosticsRequest = RequestEnvelope.FromPayload(
                Guid.NewGuid(),
                CommandKind.MaintenanceDiagnostics,
                new MaintenanceDiagnosticsRequest());
            var diagnostics = await server.HandleAsync(diagnosticsRequest, new SecurityContext("dev-token", "dev-thumbprint", "test", ["admin"]));

            Assert.True(diagnostics.IsSuccess);
            var payload = MessagePackSerializer.Deserialize<MaintenanceDiagnosticsResponse>(diagnostics.Payload);
            Assert.True(payload.TransportRateLimitedRequests > 0);
            Assert.False(payload.ClusterEnabled);
            Assert.False(payload.ClusterRunning);
        }
        finally
        {
            await host.StopAsync();
            server.Stop();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
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

    private static async Task<ResponseEnvelope> SendWireAsync<TPayload>(NetworkStream stream, CommandKind command, TPayload payload)
    {
        var request = RequestEnvelope.FromPayload(Guid.NewGuid(), command, payload!);
        var wire = new WireRequest(request, Token: "dev-token", ClientCertificateThumbprint: "dev-thumbprint", Subject: "test", Roles: ["admin"]);

        await TcpFrameCodec.WriteFrameAsync(stream, MessagePackSerializer.Serialize(wire));
        var frame = await TcpFrameCodec.ReadFrameAsync(stream, maxFrameBytes: 4 * 1024 * 1024);
        if (frame is null)
        {
            throw new InvalidOperationException("Connection closed before response frame was received.");
        }

        return MessagePackSerializer.Deserialize<WireResponse>(frame).Response;
    }
}
