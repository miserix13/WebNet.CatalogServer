namespace WebNet.CatalogServer;

public sealed record StorageDirectoryLayout(
    string DataRoot,
    string ZoneTreeRoot,
    string FastDbRoot,
    string RocksDbRoot,
    string SnapshotFilePath)
{
    private const string DataRootEnvironmentVariable = "WEBNET_DATA_ROOT";

    public static StorageDirectoryLayout Resolve(string? dataRootOverride = null)
    {
        var resolvedRoot = string.IsNullOrWhiteSpace(dataRootOverride)
            ? Environment.GetEnvironmentVariable(DataRootEnvironmentVariable)
            : dataRootOverride;

        if (string.IsNullOrWhiteSpace(resolvedRoot))
        {
            resolvedRoot = Path.Combine(Environment.CurrentDirectory, "data");
        }

        var fullRoot = Path.GetFullPath(resolvedRoot);
        var kvRoot = Path.Combine(fullRoot, "kv");

        return new StorageDirectoryLayout(
            DataRoot: fullRoot,
            ZoneTreeRoot: Path.Combine(kvRoot, "zonetree"),
            FastDbRoot: Path.Combine(kvRoot, "fastdb"),
            RocksDbRoot: Path.Combine(kvRoot, "rocksdb"),
            SnapshotFilePath: Path.Combine(fullRoot, "snapshots", "storage.snapshot.mpk"));
    }
}
