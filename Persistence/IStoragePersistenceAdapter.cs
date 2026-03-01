namespace WebNet.CatalogServer;

public interface IStoragePersistenceAdapter
{
    StoragePersistentState Load();

    void Save(StoragePersistentState state);
}
