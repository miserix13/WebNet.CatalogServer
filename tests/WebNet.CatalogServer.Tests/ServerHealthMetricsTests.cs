namespace WebNet.CatalogServer.Tests;

using MessagePack;
using Xunit;

public sealed class ServerHealthMetricsTests
{
    [Fact]
    public async Task HealthCommand_ReturnsExpandedOperationalFields()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"), "store.snapshot.mpk");
        var storage = new Storage(new FileStoragePersistenceAdapter(snapshotPath));
        var server = new Server(storage, new AllowAllTokenAuthorizer(), new AllowAllClientCertificateValidator());
        var security = new SecurityContext("token", "thumbprint", "test", ["admin"]);

        try
        {
            server.Start();

            var createDb = RequestEnvelope.FromPayload(
                Guid.NewGuid(),
                CommandKind.CreateDatabase,
                new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));
            var createDbResponse = await server.HandleAsync(createDb, security);
            Assert.True(createDbResponse.IsSuccess);

            var healthRequest = RequestEnvelope.FromPayload(Guid.NewGuid(), CommandKind.Health, new HealthRequest());
            var healthResponse = await server.HandleAsync(healthRequest, security);

            Assert.True(healthResponse.IsSuccess);
            var payload = MessagePackSerializer.Deserialize<HealthResponse>(healthResponse.Payload);
            Assert.Equal(ServerHealthStatus.Healthy, payload.Status);
            Assert.True(payload.IsRunning);
            Assert.True(payload.Uptime >= TimeSpan.Zero);
            Assert.Equal(1, payload.DatabaseCount);
            Assert.Equal(0, payload.CatalogCount);
            Assert.Equal(0, payload.DocumentCount);
            Assert.Equal("default", payload.PrimaryDatabaseName);
            Assert.Equal(0, payload.SelfCheckIssueCount);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task MetricsCommand_IncludesLifecycleAndTransportKeys()
    {
        TransportAbuseDiagnostics.Reset();
        KvMaintenanceDiagnostics.Reset();

        var snapshotPath = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"), "store.snapshot.mpk");
        var storage = new Storage(new FileStoragePersistenceAdapter(snapshotPath));
        var server = new Server(storage, new AllowAllTokenAuthorizer(), new AllowAllClientCertificateValidator());
        var security = new SecurityContext("token", "thumbprint", "test", ["admin"]);

        try
        {
            server.Start();

            var metricsRequest = RequestEnvelope.FromPayload(Guid.NewGuid(), CommandKind.Metrics, new MetricsRequest());
            var metricsResponse = await server.HandleAsync(metricsRequest, security);

            Assert.True(metricsResponse.IsSuccess);
            var payload = MessagePackSerializer.Deserialize<MetricsResponse>(metricsResponse.Payload);

            Assert.True(payload.Values.ContainsKey("server.uptime.seconds"));
            Assert.True(payload.Values.ContainsKey("server.running"));
            Assert.True(payload.Values.ContainsKey("selfcheck.healthy"));
            Assert.True(payload.Values.ContainsKey("selfcheck.issue.count"));
            Assert.True(payload.Values.ContainsKey("maintenance.failures.total"));
            Assert.True(payload.Values.ContainsKey("transport.abuse.total"));
            Assert.True(payload.Values.ContainsKey("transport.rate_limited.total"));
        }
        finally
        {
            server.Stop();
        }
    }
}