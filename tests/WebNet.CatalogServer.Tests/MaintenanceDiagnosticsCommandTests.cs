namespace WebNet.CatalogServer.Tests;

using MessagePack;
using Xunit;

public sealed class MaintenanceDiagnosticsCommandTests
{
    [Fact]
    public async Task MaintenanceDiagnosticsCommand_ReturnsRuntimeCounters()
    {
        KvMaintenanceDiagnostics.Reset();

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
}
