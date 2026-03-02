using System.Net;
using System.Net.Sockets;
using WebNet.CatalogClient;
using Xunit;

namespace WebNet.CatalogServer.Tests;

public sealed class CatalogClientIntegrationTests
{
    [Fact]
    public async Task Client_EndToEndFlow_Succeeds()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "WebNet.CatalogClient.Tests", Guid.NewGuid().ToString("N"), "client-flow.snapshot.mpk");
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

        var options = new CatalogClientOptions
        {
            Address = IPAddress.Loopback,
            Port = port
        };

        await using var client = new WebNet.CatalogClient.CatalogClient(options, _ =>
            ValueTask.FromResult(new CatalogClientAuthContext("dev-token", "dev-thumbprint", "test-user", ["admin"])));

        try
        {
            var db = await client.CreateDatabaseAsync(new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));
            Assert.Equal("default", db.Name);

            var catalog = await client.CreateCatalogAsync(new CreateCatalogRequest("default", "products"));
            Assert.Equal("products", catalog.Name);

            var documentId = Guid.NewGuid();
            var put = await client.PutDocumentAsync(new PutDocumentRequest(
                "default",
                "products",
                new Document
                {
                    DocumentId = documentId,
                    Properties =
                    {
                        ["name"] = "Catalog Client",
                        ["sku"] = "SKU-1"
                    }
                }));

            Assert.Equal(documentId, put.DocumentId);

            var get = await client.GetDocumentAsync(new GetDocumentRequest("default", "products", documentId));
            Assert.Equal("Catalog Client", get.Document.Properties["name"]);

            var health = await client.HealthAsync();
            Assert.True(health.IsRunning);

            var list = await client.ListDatabasesAsync();
            Assert.Contains(list.Databases, entry => entry.Name == "default");
        }
        finally
        {
            await host.StopAsync();
            server.Stop();
        }
    }

    [Fact]
    public async Task Client_AuthFailure_ThrowsCatalogClientException()
    {
        var snapshotPath = Path.Combine(Path.GetTempPath(), "WebNet.CatalogClient.Tests", Guid.NewGuid().ToString("N"), "client-auth.snapshot.mpk");
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

        var options = new CatalogClientOptions
        {
            Address = IPAddress.Loopback,
            Port = port
        };

        await using var client = new WebNet.CatalogClient.CatalogClient(options, _ =>
            ValueTask.FromResult(new CatalogClientAuthContext("dev-token", "untrusted-thumbprint", "test-user", ["admin"])));

        try
        {
            var exception = await Assert.ThrowsAsync<CatalogClientException>(() => client.HealthAsync());
            Assert.Equal("auth.invalid_certificate", exception.ErrorCode);
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

    private sealed class ExactThumbprintValidator(string allowedThumbprint) : IClientCertificateValidator
    {
        public bool Validate(string thumbprint)
        {
            return string.Equals(allowedThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase);
        }
    }
}