namespace WebNet.CatalogServer
{
    public class Storage
    {
        private readonly Lock sync = new();
        private readonly Dictionary<string, DatabaseState> databases = new(StringComparer.OrdinalIgnoreCase);
        private string? primaryDatabaseName;

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

                database.Catalogs[request.CatalogName] = catalog;
                database.DocumentsByCatalog[request.CatalogName] = new Dictionary<Guid, Document>();

                return Task.FromResult(OperationResult<CatalogMetadata>.Ok(new CatalogMetadata(
                    catalog.Id,
                    request.CatalogName,
                    request.DatabaseName,
                    DateTimeOffset.UtcNow)));
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
                    .Select(kvp => new CatalogMetadata(kvp.Value.Id, kvp.Key, request.DatabaseName, DateTimeOffset.UtcNow))
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
                    catalogItemCount += database.Catalogs.Values.Sum(catalog => catalog.Count);
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

        private sealed class DatabaseState
        {
            public DatabaseState(DatabaseMetadata metadata)
            {
                this.Metadata = metadata;
                this.Catalogs = new Dictionary<string, Catalog>(StringComparer.OrdinalIgnoreCase);
                this.DocumentsByCatalog = new Dictionary<string, Dictionary<Guid, Document>>(StringComparer.OrdinalIgnoreCase);
            }

            public DatabaseMetadata Metadata { get; set; }

            public Dictionary<string, Catalog> Catalogs { get; }

            public Dictionary<string, Dictionary<Guid, Document>> DocumentsByCatalog { get; }
        }

    }
}
