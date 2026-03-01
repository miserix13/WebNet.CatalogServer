namespace WebNet.CatalogServer.Tests;

using Xunit;

public sealed class StoragePersistenceTests
{
    [Fact]
    public async Task Storage_ReloadsPersistedStateAcrossInstances()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var snapshotPath = Path.Combine(tempDirectory, "snapshot.mpk");
        var adapter = new FileStoragePersistenceAdapter(snapshotPath);

        try
        {
            var createdDocumentId = Guid.NewGuid();

            var firstStorage = new Storage(adapter);
            await firstStorage.CreateDatabaseAsync(new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));
            await firstStorage.CreateCatalogAsync(new CreateCatalogRequest("default", "products"));
            await firstStorage.PutDocumentAsync(new PutDocumentRequest(
                "default",
                "products",
                new Document
                {
                    DocumentId = createdDocumentId,
                    Properties =
                    {
                        ["sku"] = "ABC-123",
                        ["name"] = "Persisted Product"
                    }
                }));

            var secondStorage = new Storage(adapter);
            var databases = await secondStorage.ListDatabasesAsync();
            var catalogs = await secondStorage.ListCatalogsAsync(new ListCatalogsRequest("default"));
            var document = await secondStorage.GetDocumentAsync(new GetDocumentRequest("default", "products", createdDocumentId));
            var selfCheck = secondStorage.RunSelfCheck();

            Assert.True(databases.IsSuccess);
            Assert.Single(databases.Value!.Databases);
            Assert.Equal("default", databases.Value.Databases.Single().Name);
            Assert.True(databases.Value.Databases.Single().IsPrimary);

            Assert.True(catalogs.IsSuccess);
            Assert.Single(catalogs.Value!.Catalogs);
            Assert.Equal("products", catalogs.Value.Catalogs.Single().Name);

            Assert.True(document.IsSuccess);
            Assert.Equal("Persisted Product", document.Value!.Document.Properties["name"]);

            Assert.True(selfCheck.IsHealthy);
            Assert.Equal(0, selfCheck.IssueCount);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Storage_WithoutSnapshot_StartsEmpty()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(tempDirectory, "snapshot.mpk");
        var storage = new Storage(new FileStoragePersistenceAdapter(snapshotPath));

        var databases = await storage.ListDatabasesAsync();
        var selfCheck = storage.RunSelfCheck();

        Assert.True(databases.IsSuccess);
        Assert.Empty(databases.Value!.Databases);
        Assert.True(selfCheck.IsHealthy);
        Assert.Equal(0, selfCheck.IssueCount);
    }
}
