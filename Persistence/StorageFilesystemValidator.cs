namespace WebNet.CatalogServer;

public static class StorageFilesystemValidator
{
    public static SelfCheckResponse EnsureAndValidate(StorageDirectoryLayout layout)
    {
        var issues = new List<SelfCheckIssue>();

        ValidateDirectory(layout.DataRoot, "storage.fs.data_root", issues);
        ValidateDirectory(layout.ZoneTreeRoot, "storage.fs.zonetree_root", issues);
        ValidateDirectory(layout.FastDbRoot, "storage.fs.fastdb_root", issues);
        ValidateDirectory(layout.RocksDbRoot, "storage.fs.rocksdb_root", issues);
        ValidateDirectory(Path.GetDirectoryName(layout.SnapshotFilePath)!, "storage.fs.snapshot_root", issues);

        return new SelfCheckResponse(issues.Count == 0, issues.Count, issues);
    }

    private static void ValidateDirectory(string directoryPath, string codePrefix, ICollection<SelfCheckIssue> issues)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
        }
        catch (Exception ex)
        {
            issues.Add(new SelfCheckIssue($"{codePrefix}.create_failed", $"Failed to create '{directoryPath}': {ex.Message}"));
            return;
        }

        var probePath = Path.Combine(directoryPath, $".write-probe-{Guid.NewGuid():N}.tmp");

        try
        {
            using var stream = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.WriteByte(0x42);
        }
        catch (Exception ex)
        {
            issues.Add(new SelfCheckIssue($"{codePrefix}.write_failed", $"Directory '{directoryPath}' is not writable: {ex.Message}"));
            return;
        }
        finally
        {
            try
            {
                if (File.Exists(probePath))
                {
                    File.Delete(probePath);
                }
            }
            catch
            {
            }
        }
    }
}
