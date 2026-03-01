namespace WebNet.CatalogServer.Tests;

using Xunit;

public sealed class StorageFilesystemValidatorTests
{
    [Fact]
    public void Resolve_UsesExpectedDirectoryStructure()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));
        var layout = StorageDirectoryLayout.Resolve(tempRoot);

        Assert.Equal(Path.GetFullPath(tempRoot), layout.DataRoot);
        Assert.Equal(Path.Combine(layout.DataRoot, "kv", "zonetree"), layout.ZoneTreeRoot);
        Assert.Equal(Path.Combine(layout.DataRoot, "kv", "fastdb"), layout.FastDbRoot);
        Assert.Equal(Path.Combine(layout.DataRoot, "kv", "rocksdb"), layout.RocksDbRoot);
        Assert.Equal(Path.Combine(layout.DataRoot, "snapshots", "storage.snapshot.mpk"), layout.SnapshotFilePath);
    }

    [Fact]
    public void EnsureAndValidate_CreatesAllConfiguredDirectories()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));

        try
        {
            var layout = StorageDirectoryLayout.Resolve(tempRoot);
            var result = StorageFilesystemValidator.EnsureAndValidate(layout);

            Assert.True(result.IsHealthy);
            Assert.Equal(0, result.IssueCount);

            Assert.True(Directory.Exists(layout.DataRoot));
            Assert.True(Directory.Exists(layout.ZoneTreeRoot));
            Assert.True(Directory.Exists(layout.FastDbRoot));
            Assert.True(Directory.Exists(layout.RocksDbRoot));
            Assert.True(Directory.Exists(Path.GetDirectoryName(layout.SnapshotFilePath)!));
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
