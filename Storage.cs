namespace WebNet.CatalogServer
{
    public class Storage
    {
        private readonly Lock sync = new();
        private readonly IStoragePersistenceAdapter persistence;
        private readonly Dictionary<string, DatabaseState> databases = new(StringComparer.OrdinalIgnoreCase);
        private string? primaryDatabaseName;

        public Storage(IStoragePersistenceAdapter? persistence = null)
        {
            this.persistence = persistence ?? FileStoragePersistenceAdapter.CreateDefault();
            this.LoadPersistedState();
        }

        public Task<OperationResult<DatabaseMetadata>> CreateDatabaseAsync(CreateDatabaseRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Task.FromResult(OperationResult<DatabaseMetadata>.Fail("db.invalid_name", "Database name is required."));
            }

            lock (this.sync)
            {
                if (this.databases.ContainsKey(request.Name))
                {
                    return Task.FromResult(OperationResult<DatabaseMetadata>.Fail("db.already_exists", $"Database '{request.Name}' already exists."));
                }

                var metadata = new DatabaseMetadata(
                    Guid.NewGuid(),
                    request.Name,
                    DateTimeOffset.UtcNow,
                    request.Consistency,
                    false);

                this.databases[request.Name] = new DatabaseState(metadata);

                if (request.MakePrimary || this.primaryDatabaseName is null)
                {
                    this.SetPrimaryInternal(request.Name);
                }

                this.PersistStateUnderLock();

                return Task.FromResult(OperationResult<DatabaseMetadata>.Ok(this.databases[request.Name].Metadata));
            }
        }

        public Task<OperationResult<DropDatabaseResponse>> DropDatabaseAsync(DropDatabaseRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Task.FromResult(OperationResult<DropDatabaseResponse>.Fail("db.invalid_name", "Database name is required."));
            }

            lock (this.sync)
            {
                var removed = this.databases.Remove(request.Name);
                if (!removed)
                {
                    return Task.FromResult(OperationResult<DropDatabaseResponse>.Fail("db.not_found", $"Database '{request.Name}' was not found."));
                }

                if (string.Equals(this.primaryDatabaseName, request.Name, StringComparison.OrdinalIgnoreCase))
                {
                    this.primaryDatabaseName = this.databases.Keys.FirstOrDefault();
                    if (this.primaryDatabaseName is not null)
                    {
                        this.SetPrimaryInternal(this.primaryDatabaseName);
                    }
                }

                this.PersistStateUnderLock();

                return Task.FromResult(OperationResult<DropDatabaseResponse>.Ok(new DropDatabaseResponse(request.Name, true)));
            }
        }

        public Task<OperationResult<ListDatabasesResponse>> ListDatabasesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (this.sync)
            {
                var databases = this.databases.Values
                    .Select(state => state.Metadata)
                    .OrderBy(metadata => metadata.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return Task.FromResult(OperationResult<ListDatabasesResponse>.Ok(new ListDatabasesResponse(databases)));
            }
        }

        public Task<OperationResult<CatalogMetadata>> CreateCatalogAsync(CreateCatalogRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryGetDatabase(request.DatabaseName, out var database, out var error))
            {
                return Task.FromResult(OperationResult<CatalogMetadata>.Fail(error!.ErrorCode, error.ErrorMessage));
            }

            lock (this.sync)
            {
                if (database!.Catalogs.ContainsKey(request.CatalogName))
                {
                    return Task.FromResult(OperationResult<CatalogMetadata>.Fail("catalog.already_exists", $"Catalog '{request.CatalogName}' already exists in database '{request.DatabaseName}'."));
                }

                var catalog = new Catalog
                {
                    Name = request.CatalogName
                };

                database.Catalogs[request.CatalogName] = new CatalogState(catalog, DateTimeOffset.UtcNow);
                database.DocumentsByCatalog[request.CatalogName] = new Dictionary<Guid, Document>();

                this.PersistStateUnderLock();

                return Task.FromResult(OperationResult<CatalogMetadata>.Ok(new CatalogMetadata(
                    catalog.Id,
                    request.CatalogName,
                    request.DatabaseName,
                    database.Catalogs[request.CatalogName].CreatedAtUtc)));
            }
        }

        public Task<OperationResult<DropCatalogResponse>> DropCatalogAsync(DropCatalogRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryGetDatabase(request.DatabaseName, out var database, out var error))
            {
                return Task.FromResult(OperationResult<DropCatalogResponse>.Fail(error!.ErrorCode, error.ErrorMessage));
            }

            lock (this.sync)
            {
                var removed = database!.Catalogs.Remove(request.CatalogName);
                if (!removed)
                {
                    return Task.FromResult(OperationResult<DropCatalogResponse>.Fail("catalog.not_found", $"Catalog '{request.CatalogName}' was not found in database '{request.DatabaseName}'."));
                }

                database.DocumentsByCatalog.Remove(request.CatalogName);

                this.PersistStateUnderLock();

                return Task.FromResult(OperationResult<DropCatalogResponse>.Ok(new DropCatalogResponse(request.DatabaseName, request.CatalogName, true)));
            }
        }

        public Task<OperationResult<ListCatalogsResponse>> ListCatalogsAsync(ListCatalogsRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryGetDatabase(request.DatabaseName, out var database, out var error))
            {
                return Task.FromResult(OperationResult<ListCatalogsResponse>.Fail(error!.ErrorCode, error.ErrorMessage));
            }

            lock (this.sync)
            {
                var catalogs = database!.Catalogs
                    .Select(kvp => new CatalogMetadata(kvp.Value.Catalog.Id, kvp.Key, request.DatabaseName, kvp.Value.CreatedAtUtc))
                    .OrderBy(metadata => metadata.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return Task.FromResult(OperationResult<ListCatalogsResponse>.Ok(new ListCatalogsResponse(request.DatabaseName, catalogs)));
            }
        }

        public Task<OperationResult<PutDocumentResponse>> PutDocumentAsync(PutDocumentRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Document is null)
            {
                return Task.FromResult(OperationResult<PutDocumentResponse>.Fail("doc.invalid", "Document is required."));
            }

            if (!this.TryGetCatalog(request.DatabaseName, request.CatalogName, out var documentMap, out var error))
            {
                return Task.FromResult(OperationResult<PutDocumentResponse>.Fail(error!.ErrorCode, error.ErrorMessage));
            }

            lock (this.sync)
            {
                var replacedExisting = documentMap!.ContainsKey(request.Document.DocumentId);
                documentMap[request.Document.DocumentId] = request.Document;

                this.PersistStateUnderLock();

                return Task.FromResult(OperationResult<PutDocumentResponse>.Ok(new PutDocumentResponse(
                    request.DatabaseName,
                    request.CatalogName,
                    request.Document.DocumentId,
                    replacedExisting)));
            }
        }

        public Task<OperationResult<GetDocumentResponse>> GetDocumentAsync(GetDocumentRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryGetCatalog(request.DatabaseName, request.CatalogName, out var documentMap, out var error))
            {
                return Task.FromResult(OperationResult<GetDocumentResponse>.Fail(error!.ErrorCode, error.ErrorMessage));
            }

            lock (this.sync)
            {
                if (!documentMap!.TryGetValue(request.DocumentId, out var document))
                {
                    return Task.FromResult(OperationResult<GetDocumentResponse>.Fail("doc.not_found", $"Document '{request.DocumentId}' was not found in catalog '{request.CatalogName}'."));
                }

                return Task.FromResult(OperationResult<GetDocumentResponse>.Ok(new GetDocumentResponse(
                    request.DatabaseName,
                    request.CatalogName,
                    document)));
            }
        }

        public Task<OperationResult<DeleteDocumentResponse>> DeleteDocumentAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.TryGetCatalog(request.DatabaseName, request.CatalogName, out var documentMap, out var error))
            {
                return Task.FromResult(OperationResult<DeleteDocumentResponse>.Fail(error!.ErrorCode, error.ErrorMessage));
            }

            lock (this.sync)
            {
                var removed = documentMap!.Remove(request.DocumentId);
                if (!removed)
                {
                    return Task.FromResult(OperationResult<DeleteDocumentResponse>.Fail("doc.not_found", $"Document '{request.DocumentId}' was not found in catalog '{request.CatalogName}'."));
                }

                this.PersistStateUnderLock();

                return Task.FromResult(OperationResult<DeleteDocumentResponse>.Ok(new DeleteDocumentResponse(
                    request.DatabaseName,
                    request.CatalogName,
                    request.DocumentId,
                    true)));
            }
        }

        public StorageStatistics GetStatistics()
        {
            lock (this.sync)
            {
                var catalogCount = 0;
                var catalogItemCount = 0;
                var documentCount = 0;

                foreach (var database in this.databases.Values)
                {
                    catalogCount += database.Catalogs.Count;
                    catalogItemCount += database.Catalogs.Values.Sum(catalog => catalog.Catalog.Count);
                    documentCount += database.DocumentsByCatalog.Values.Sum(map => map.Count);
                }

                return new StorageStatistics(
                    this.databases.Count,
                    catalogCount,
                    catalogItemCount,
                    documentCount,
                    this.primaryDatabaseName);
            }
        }

        public SelfCheckResponse RunSelfCheck()
        {
            lock (this.sync)
            {
                var issues = new List<SelfCheckIssue>();

                if (this.databases.Count == 0)
                {
                    return new SelfCheckResponse(true, 0, []);
                }

                if (string.IsNullOrWhiteSpace(this.primaryDatabaseName))
                {
                    issues.Add(new SelfCheckIssue("storage.primary.missing", "Primary database pointer is not set while databases exist."));
                }
                else if (!this.databases.ContainsKey(this.primaryDatabaseName))
                {
                    issues.Add(new SelfCheckIssue("storage.primary.invalid", $"Primary database '{this.primaryDatabaseName}' does not exist."));
                }

                var primaryCount = this.databases.Values.Count(state => state.Metadata.IsPrimary);
                if (primaryCount != 1)
                {
                    issues.Add(new SelfCheckIssue("storage.primary.count", $"Expected exactly one primary database, found {primaryCount}."));
                }

                foreach (var (databaseName, state) in this.databases)
                {
                    foreach (var catalogName in state.Catalogs.Keys)
                    {
                        if (!state.DocumentsByCatalog.ContainsKey(catalogName))
                        {
                            issues.Add(new SelfCheckIssue(
                                "storage.catalog.documents.missing",
                                $"Catalog '{catalogName}' in database '{databaseName}' has no document map."));
                        }
                    }

                    foreach (var catalogName in state.DocumentsByCatalog.Keys)
                    {
                        if (!state.Catalogs.ContainsKey(catalogName))
                        {
                            issues.Add(new SelfCheckIssue(
                                "storage.catalog.documents.orphaned",
                                $"Document map for catalog '{catalogName}' in database '{databaseName}' is orphaned."));
                        }
                    }
                }

                return new SelfCheckResponse(issues.Count == 0, issues.Count, issues);
            }
        }

        private bool TryGetCatalog(string databaseName, string catalogName, out Dictionary<Guid, Document>? documentMap, out OperationError? error)
        {
            documentMap = null;

            if (!this.TryGetDatabase(databaseName, out var database, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(catalogName))
            {
                error = new OperationError("catalog.invalid_name", "Catalog name is required.");
                return false;
            }

            lock (this.sync)
            {
                if (!database!.Catalogs.ContainsKey(catalogName))
                {
                    error = new OperationError("catalog.not_found", $"Catalog '{catalogName}' was not found in database '{databaseName}'.");
                    return false;
                }

                if (!database.DocumentsByCatalog.TryGetValue(catalogName, out documentMap))
                {
                    error = new OperationError(
                        "storage.invariant_violation",
                        $"Catalog '{catalogName}' in database '{databaseName}' is missing its document map.");
                    return false;
                }
            }

            error = null;
            return true;
        }

        private bool TryGetDatabase(string databaseName, out DatabaseState? state, out OperationError? error)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                state = null;
                error = new OperationError("db.invalid_name", "Database name is required.");
                return false;
            }

            lock (this.sync)
            {
                if (!this.databases.TryGetValue(databaseName, out state))
                {
                    error = new OperationError("db.not_found", $"Database '{databaseName}' was not found.");
                    return false;
                }
            }

            error = null;
            return true;
        }

        private void SetPrimaryInternal(string databaseName)
        {
            foreach (var key in this.databases.Keys)
            {
                var state = this.databases[key];
                state.Metadata = state.Metadata with { IsPrimary = string.Equals(key, databaseName, StringComparison.OrdinalIgnoreCase) };
                this.databases[key] = state;
            }

            this.primaryDatabaseName = databaseName;
        }

        private void LoadPersistedState()
        {
            lock (this.sync)
            {
                this.databases.Clear();
                this.primaryDatabaseName = null;

                var persisted = this.persistence.Load();
                foreach (var persistedDatabase in persisted.Databases)
                {
                    var state = new DatabaseState(persistedDatabase.Metadata);

                    foreach (var persistedCatalog in persistedDatabase.Catalogs)
                    {
                        state.Catalogs[persistedCatalog.Catalog.Name] = new CatalogState(persistedCatalog.Catalog, persistedCatalog.CreatedAtUtc);
                        state.DocumentsByCatalog[persistedCatalog.Catalog.Name] = persistedCatalog.Documents
                            .ToDictionary(document => document.DocumentId, document => document);
                    }

                    this.databases[persistedDatabase.Metadata.Name] = state;
                }

                if (!string.IsNullOrWhiteSpace(persisted.PrimaryDatabaseName)
                    && this.databases.ContainsKey(persisted.PrimaryDatabaseName))
                {
                    this.SetPrimaryInternal(persisted.PrimaryDatabaseName);
                }
                else if (this.databases.Count > 0)
                {
                    var preferredPrimary = this.databases.Values
                        .FirstOrDefault(db => db.Metadata.IsPrimary)
                        ?.Metadata.Name;

                    if (!string.IsNullOrWhiteSpace(preferredPrimary) && this.databases.ContainsKey(preferredPrimary))
                    {
                        this.SetPrimaryInternal(preferredPrimary);
                    }
                    else
                    {
                        this.SetPrimaryInternal(this.databases.Keys.First());
                    }
                }
            }
        }

        private void PersistStateUnderLock()
        {
            var snapshot = new StoragePersistentState(
                this.primaryDatabaseName,
                this.databases.Values
                    .Select(database => new PersistedDatabaseState(
                        database.Metadata,
                        database.Catalogs.Values
                            .Select(catalog => new PersistedCatalogState(
                                catalog.Catalog,
                                catalog.CreatedAtUtc,
                                database.DocumentsByCatalog.TryGetValue(catalog.Catalog.Name, out var documents)
                                    ? documents.Values.ToArray()
                                    : []))
                            .ToArray()))
                    .ToArray());

            this.persistence.Save(snapshot);
        }

        private sealed class DatabaseState
        {
            public DatabaseState(DatabaseMetadata metadata)
            {
                this.Metadata = metadata;
                this.Catalogs = new Dictionary<string, CatalogState>(StringComparer.OrdinalIgnoreCase);
                this.DocumentsByCatalog = new Dictionary<string, Dictionary<Guid, Document>>(StringComparer.OrdinalIgnoreCase);
            }

            public DatabaseMetadata Metadata { get; set; }

            public Dictionary<string, CatalogState> Catalogs { get; }

            public Dictionary<string, Dictionary<Guid, Document>> DocumentsByCatalog { get; }
        }

        private sealed record CatalogState(Catalog Catalog, DateTimeOffset CreatedAtUtc);

    }
}
