namespace WebNet.CatalogServer;

public enum CommandKind
{
    Unknown = 0,
    CreateDatabase = 1,
    DropDatabase = 2,
    ListDatabases = 3,
    CreateCatalog = 10,
    DropCatalog = 11,
    ListCatalogs = 12,
    PutDocument = 20,
    GetDocument = 21,
    DeleteDocument = 22,
    Health = 100,
    Metrics = 101,
    SelfCheck = 102,
    MaintenanceDiagnostics = 103
}
