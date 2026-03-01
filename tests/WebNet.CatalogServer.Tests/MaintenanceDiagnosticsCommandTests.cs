namespace WebNet.CatalogServer.Tests;

using MessagePack;
using Xunit;

public sealed class MaintenanceDiagnosticsCommandTests
{
    [Fact]
    public async Task MaintenanceDiagnosticsCommand_ReturnsRuntimeCounters()
    {
        KvMaintenanceDiagnostics.Reset();
        TransportAbuseDiagnostics.Reset();

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
}
