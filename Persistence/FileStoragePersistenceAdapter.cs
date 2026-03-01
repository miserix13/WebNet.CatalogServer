using MessagePack;

namespace WebNet.CatalogServer;

public sealed class FileStoragePersistenceAdapter : IStoragePersistenceAdapter
{
    private readonly string filePath;

    public FileStoragePersistenceAdapter(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Persistence file path is required.", nameof(filePath));
        }

        this.filePath = Path.GetFullPath(filePath);
    }

    public static FileStoragePersistenceAdapter CreateDefault(string? dataRootOverride = null)
    {
        var layout = StorageDirectoryLayout.Resolve(dataRootOverride);
        return new FileStoragePersistenceAdapter(layout.SnapshotFilePath);
    }

    public StoragePersistentState Load()
    {
        if (!File.Exists(this.filePath))
        {
            return StoragePersistentState.Empty;
        }

        var bytes = File.ReadAllBytes(this.filePath);
        if (bytes.Length == 0)
        {
            return StoragePersistentState.Empty;
        }

        try
        {
            return MessagePackSerializer.Deserialize<StoragePersistentState>(bytes) ?? StoragePersistentState.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Unable to load persisted storage snapshot '{this.filePath}'.", ex);
        }
    }

    public void Save(StoragePersistentState state)
    {
        var directory = Path.GetDirectoryName(this.filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = MessagePackSerializer.Serialize(state);
        var tempPath = this.filePath + ".tmp";

        File.WriteAllBytes(tempPath, bytes);
        if (File.Exists(this.filePath))
        {
            File.Replace(tempPath, this.filePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, this.filePath);
        }
    }
}
