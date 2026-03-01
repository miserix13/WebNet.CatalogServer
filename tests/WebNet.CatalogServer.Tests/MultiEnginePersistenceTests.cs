namespace WebNet.CatalogServer.Tests;

using Xunit;

public sealed class MultiEnginePersistenceTests
{
    [Fact]
    public async Task MultiEnginePersistence_RoundTripsDataModelAcrossRestart_AndWritesAllKvRoots()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));
        var layout = StorageDirectoryLayout.Resolve(tempRoot);
        var adapter = new MultiEngineStoragePersistenceAdapter(layout);

        try
        {
            var first = new Storage(adapter);

            var createDatabase = await first.CreateDatabaseAsync(new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));
            Assert.True(createDatabase.IsSuccess);

            var createCatalog = await first.CreateCatalogAsync(new CreateCatalogRequest("default", "products"));
            Assert.True(createCatalog.IsSuccess);

            var docId = Guid.NewGuid();
            var put = await first.PutDocumentAsync(new PutDocumentRequest(
                "default",
                "products",
                new Document
                {
                    DocumentId = docId,
                    Properties =
                    {
                        ["sku"] = "SKU-001",
                        ["name"] = "Starter Product",
                        ["category"] = "test-data"
                    }
                }));
            Assert.True(put.IsSuccess);

            var second = new Storage(new MultiEngineStoragePersistenceAdapter(layout));

            var listDatabases = await second.ListDatabasesAsync();
            Assert.True(listDatabases.IsSuccess);
            Assert.Single(listDatabases.Value!.Databases);
            Assert.Equal("default", listDatabases.Value.Databases.Single().Name);

            var listCatalogs = await second.ListCatalogsAsync(new ListCatalogsRequest("default"));
            Assert.True(listCatalogs.IsSuccess);
            Assert.Single(listCatalogs.Value!.Catalogs);
            Assert.Equal("products", listCatalogs.Value.Catalogs.Single().Name);

            var loaded = await second.GetDocumentAsync(new GetDocumentRequest("default", "products", docId));
            Assert.True(loaded.IsSuccess);
            Assert.Equal("SKU-001", loaded.Value!.Document.Properties["sku"]);
            Assert.Equal("Starter Product", loaded.Value.Document.Properties["name"]);

            Assert.True(DirectoryHasAnyFile(layout.ZoneTreeRoot));
            Assert.True(DirectoryHasAnyFile(layout.FastDbRoot));
            Assert.True(DirectoryHasAnyFile(layout.RocksDbRoot));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static bool DirectoryHasAnyFile(string root)
    {
        return Directory.Exists(root) && Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any();
    }
}
