using MessagePack;
using System.Reflection;

namespace WebNet.CatalogServer;

public sealed class MultiEngineStoragePersistenceAdapter : IStoragePersistenceAdapter
{
    private readonly FileStoragePersistenceAdapter snapshotAdapter;
    private readonly IReadOnlyList<IEngineBlobStore> blobStores;

    public MultiEngineStoragePersistenceAdapter(StorageDirectoryLayout layout)
    {
        this.snapshotAdapter = new FileStoragePersistenceAdapter(layout.SnapshotFilePath);
        this.blobStores =
        [
            new ZoneTreeBlobStore(layout.ZoneTreeRoot),
            new FastDbBlobStore(layout.FastDbRoot),
            new RocksDbBlobStore(layout.RocksDbRoot)
        ];
    }

    public StoragePersistentState Load()
    {
        foreach (var store in this.blobStores)
        {
            if (!store.TryRead(out var bytes) || bytes is null || bytes.Length == 0)
            {
                continue;
            }

            try
            {
                return MessagePackSerializer.Deserialize<StoragePersistentState>(bytes) ?? StoragePersistentState.Empty;
            }
            catch
            {
            }
        }

        return this.snapshotAdapter.Load();
    }

    public void Save(StoragePersistentState state)
    {
        var bytes = MessagePackSerializer.Serialize(state);
        var failures = new List<Exception>();

        foreach (var store in this.blobStores)
        {
            try
            {
                store.Write(bytes);
                store.RunMaintenance();
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"Failed to persist snapshot to '{store.Name}'.", ex));
            }
        }

        try
        {
            this.snapshotAdapter.Save(state);
        }
        catch (Exception ex)
        {
            failures.Add(new InvalidOperationException("Failed to persist fallback snapshot file.", ex));
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("One or more persistence engines failed to store snapshot state.", failures);
        }
    }

    private interface IEngineBlobStore
    {
        string Name { get; }

        bool TryRead(out byte[]? bytes);

        void Write(byte[] bytes);

        void RunMaintenance();
    }

    private sealed class ZoneTreeBlobStore : IEngineBlobStore
    {
        private const long SnapshotKey = 1;
        private readonly string root;

        public ZoneTreeBlobStore(string root)
        {
            this.root = root;
        }

        public string Name => "ZoneTree";

        public bool TryRead(out byte[]? bytes)
        {
            Directory.CreateDirectory(this.root);

            using var tree = CreateTree(this.root);
            var found = tree.TryGet(SnapshotKey, out var base64);
            bytes = found && base64 is not null ? Convert.FromBase64String(base64) : null;
            return found;
        }

        public void Write(byte[] bytes)
        {
            Directory.CreateDirectory(this.root);

            using var tree = CreateTree(this.root);
            tree.Upsert(SnapshotKey, Convert.ToBase64String(bytes));
        }

        public void RunMaintenance()
        {
            Directory.CreateDirectory(this.root);

            using var tree = CreateTree(this.root);
            var maintainer = tree.CreateMaintainer();
            if (maintainer is null)
            {
                KvMaintenanceDiagnostics.RecordZoneTreeSuccess();
                return;
            }

            try
            {
                var maintainerType = maintainer.GetType();
                foreach (var property in maintainerType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.CanWrite && property.PropertyType == typeof(bool) && property.Name.StartsWith("EnableJobFor", StringComparison.Ordinal))
                    {
                        property.SetValue(maintainer, true);
                    }
                }

                var mergeMethod = maintainerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.GetParameters().Length == 0 && m.Name.Contains("Merge", StringComparison.OrdinalIgnoreCase));
                mergeMethod?.Invoke(maintainer, null);

                var waitMethod = maintainerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.GetParameters().Length == 0 && string.Equals(m.Name, "WaitForBackgroundThreads", StringComparison.Ordinal));
                waitMethod?.Invoke(maintainer, null);

                KvMaintenanceDiagnostics.RecordZoneTreeSuccess();
            }
            catch
            {
                KvMaintenanceDiagnostics.RecordZoneTreeFailure();
                throw;
            }
            finally
            {
                if (maintainer is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static Tenray.ZoneTree.IZoneTree<long, string> CreateTree(string path)
        {
            var factory = new Tenray.ZoneTree.ZoneTreeFactory<long, string>()
                .SetDataDirectory(path)
                .SetKeySerializer(new Tenray.ZoneTree.Serializers.Int64Serializer())
                .SetValueSerializer(new Tenray.ZoneTree.Serializers.Utf8StringSerializer());

            return factory.OpenOrCreate();
        }
    }

    private sealed class RocksDbBlobStore : IEngineBlobStore
    {
        private static readonly byte[] SnapshotKey = "storage-snapshot"u8.ToArray();
        private readonly string root;

        public RocksDbBlobStore(string root)
        {
            this.root = root;
        }

        public string Name => "RocksDB";

        public bool TryRead(out byte[]? bytes)
        {
            Directory.CreateDirectory(this.root);

            var options = new RocksDbSharp.DbOptions().SetCreateIfMissing(true);
            using var db = RocksDbSharp.RocksDb.Open(options, this.root);
            bytes = db.Get(SnapshotKey);
            return bytes is not null && bytes.Length > 0;
        }

        public void Write(byte[] bytes)
        {
            Directory.CreateDirectory(this.root);

            var options = new RocksDbSharp.DbOptions().SetCreateIfMissing(true);
            using var db = RocksDbSharp.RocksDb.Open(options, this.root);
            db.Put(SnapshotKey, bytes);
        }

        public void RunMaintenance()
        {
            Directory.CreateDirectory(this.root);

            try
            {
                var options = new RocksDbSharp.DbOptions().SetCreateIfMissing(true);
                using var db = RocksDbSharp.RocksDb.Open(options, this.root);
                db.CompactRange(Array.Empty<byte>(), Array.Empty<byte>(), null!);
                KvMaintenanceDiagnostics.RecordRocksDbSuccess();
            }
            catch
            {
                KvMaintenanceDiagnostics.RecordRocksDbFailure();
                throw;
            }
        }
    }

    private sealed class FastDbBlobStore : IEngineBlobStore
    {
        private readonly string root;

        public FastDbBlobStore(string root)
        {
            this.root = root;
        }

        public string Name => "Stellar.FastDB";

        public bool TryRead(out byte[]? bytes)
        {
            Directory.CreateDirectory(this.root);
            using var context = FastDbReflectionContext.Open(this.root);

            if (context.TryGetBytes(1, out var value))
            {
                bytes = value;
                return true;
            }

            bytes = null;
            return false;
        }

        public void Write(byte[] bytes)
        {
            Directory.CreateDirectory(this.root);
            using var context = FastDbReflectionContext.Open(this.root);
            context.SetBytes(1, bytes);
        }

        public void RunMaintenance()
        {
            Directory.CreateDirectory(this.root);

            try
            {
                using var context = FastDbReflectionContext.Open(this.root);
                context.RunMaintenance();
                KvMaintenanceDiagnostics.RecordFastDbSuccess();
            }
            catch
            {
                KvMaintenanceDiagnostics.RecordFastDbFailure();
                throw;
            }
        }

        private sealed class FastDbReflectionContext : IDisposable
        {
            private readonly object database;
            private readonly object collection;
            private readonly MethodInfoInvoker methodInvoker;
            private readonly Action? disposeAction;

            private FastDbReflectionContext(object database, object collection, Action? disposeAction)
            {
                this.database = database;
                this.collection = collection;
                this.disposeAction = disposeAction;
                this.methodInvoker = new MethodInfoInvoker(collection.GetType());
            }

            public static FastDbReflectionContext Open(string root)
            {
                var assembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(x => string.Equals(x.GetName().Name, "Stellar.FastDB", StringComparison.Ordinal));

                if (assembly is null)
                {
                    try
                    {
                        assembly = Assembly.Load("Stellar.FastDB");
                    }
                    catch
                    {
                    }
                }

                if (assembly is null)
                {
                    var localAssemblyPath = Path.Combine(AppContext.BaseDirectory, "Stellar.FastDB.dll");
                    if (File.Exists(localAssemblyPath))
                    {
                        assembly = Assembly.LoadFrom(localAssemblyPath);
                    }
                }

                if (assembly is null)
                {
                    throw new InvalidOperationException("Stellar.FastDB assembly could not be loaded.");
                }

                var dbType = assembly.GetType("Stellar.Collections.FastDB")
                    ?? throw new InvalidOperationException("Stellar.Collections.FastDB type not found.");

                var optionsType = assembly.GetType("Stellar.Collections.FastDBOptions");
                var options = optionsType is null ? null : Activator.CreateInstance(optionsType);

                if (options is not null)
                {
                    SetPathProperty(options, root);
                }

                var database = options is null
                    ? Activator.CreateInstance(dbType)
                    : Activator.CreateInstance(dbType, options);

                if (database is null)
                {
                    throw new InvalidOperationException("Unable to initialize FastDB instance.");
                }

                var getCollection = dbType
                    .GetMethods()
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, "GetCollection", StringComparison.Ordinal)
                        && m.IsGenericMethodDefinition
                        && m.GetGenericArguments().Length == 2
                        && m.GetParameters().Length == 2)
                    ?? throw new InvalidOperationException("FastDB GetCollection<TKey, TValue>(name, options) method not found.");

                var collectionOptions = optionsType is null ? null : Activator.CreateInstance(optionsType);
                if (collectionOptions is not null)
                {
                    SetPathProperty(collectionOptions, root);
                }

                var typed = getCollection.MakeGenericMethod(typeof(int), typeof(byte[])).Invoke(database, ["catalog-state", collectionOptions])
                    ?? throw new InvalidOperationException("FastDB collection instance could not be created.");

                Action? close = null;
                var closeMethod = dbType.GetMethod("Close", Type.EmptyTypes);
                if (closeMethod is not null)
                {
                    close = () => closeMethod.Invoke(database, null);
                }

                return new FastDbReflectionContext(database, typed, close);
            }

            public bool TryGetBytes(int key, out byte[] value)
            {
                if (this.methodInvoker.TryInvokeTryGet(this.collection, key, out value))
                {
                    return true;
                }

                if (this.methodInvoker.TryInvokeGet(this.collection, key, out value))
                {
                    return true;
                }

                value = Array.Empty<byte>();
                return false;
            }

            public void SetBytes(int key, byte[] value)
            {
                if (this.methodInvoker.TryInvoke(this.collection, "AddOrUpdate", key, value)
                    || this.methodInvoker.TryInvoke(this.collection, "set_Item", key, value)
                    || this.methodInvoker.TryInvoke(this.collection, "Update", key, value)
                    || this.methodInvoker.TryInvoke(this.collection, "Add", key, value))
                {
                    this.methodInvoker.TryInvoke(this.collection, "Flush");
                    return;
                }

                throw new InvalidOperationException("Unable to determine compatible FastDB write method for collection<int, byte[]>.");
            }

            public void RunMaintenance()
            {
                var dbType = this.database.GetType();

                var flush = dbType.GetMethod("Flush", Type.EmptyTypes);
                flush?.Invoke(this.database, null);

                var defragmentAsync = dbType.GetMethod("DefragmentMemoryAsync", Type.EmptyTypes);
                var task = defragmentAsync?.Invoke(this.database, null) as Task;
                task?.GetAwaiter().GetResult();
            }

            public void Dispose()
            {
                this.disposeAction?.Invoke();
                if (this.database is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            private static void SetPathProperty(object options, string root)
            {
                var candidates = options.GetType()
                    .GetProperties()
                    .Where(p => p.PropertyType == typeof(string) && p.CanWrite)
                    .Where(p =>
                        p.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("Directory", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("Folder", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var property in candidates)
                {
                    property.SetValue(options, root);
                }
            }
        }

        private sealed class MethodInfoInvoker
        {
            private readonly Type collectionType;

            public MethodInfoInvoker(Type collectionType)
            {
                this.collectionType = collectionType;
            }

            public bool TryInvoke(object instance, string methodName, params object[] args)
            {
                var methods = this.collectionType
                    .GetMethods()
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                    .ToArray();

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                    {
                        continue;
                    }

                    if (!CanMapParameters(parameters, args))
                    {
                        continue;
                    }

                    method.Invoke(instance, args);
                    return true;
                }

                return false;
            }

            public bool TryInvoke(object instance, string methodName, object arg, out object? result)
            {
                result = null;
                var method = this.collectionType
                    .GetMethods()
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, methodName, StringComparison.Ordinal)
                        && m.GetParameters().Length == 1
                        && IsCompatible(m.GetParameters()[0].ParameterType, arg.GetType()));

                if (method is null)
                {
                    return false;
                }

                result = method.Invoke(instance, [arg]);
                return true;
            }

            public bool TryInvokeTryGet(object instance, int key, out byte[] value)
            {
                foreach (var methodName in new[] { "TryGet", "TryGetValue" })
                {
                    var method = this.collectionType
                        .GetMethods()
                        .FirstOrDefault(m =>
                            string.Equals(m.Name, methodName, StringComparison.Ordinal)
                            && m.GetParameters().Length == 2
                            && IsCompatible(m.GetParameters()[0].ParameterType, typeof(int))
                            && m.GetParameters()[1].IsOut);

                    if (method is null)
                    {
                        continue;
                    }

                    var args = new object?[] { key, null };
                    var ok = method.Invoke(instance, args);
                    if (ok is bool found && found && args[1] is byte[] bytes)
                    {
                        value = bytes;
                        return true;
                    }
                }

                value = Array.Empty<byte>();
                return false;
            }

            public bool TryInvokeGet(object instance, int key, out byte[] value)
            {
                var method = this.collectionType
                    .GetMethods()
                    .FirstOrDefault(m =>
                        string.Equals(m.Name, "Get", StringComparison.Ordinal)
                        && m.GetParameters().Length == 1
                        && IsCompatible(m.GetParameters()[0].ParameterType, typeof(int))
                        && m.ReturnType == typeof(byte[]));

                if (method is null)
                {
                    value = Array.Empty<byte>();
                    return false;
                }

                var bytes = method.Invoke(instance, [key]) as byte[];
                if (bytes is null)
                {
                    value = Array.Empty<byte>();
                    return false;
                }

                value = bytes;
                return true;
            }

            private static bool CanMapParameters(ParameterInfo[] parameters, object[] args)
            {
                for (var i = 0; i < parameters.Length; i++)
                {
                    var arg = args[i];
                    if (arg is null)
                    {
                        continue;
                    }

                    if (!IsCompatible(parameters[i].ParameterType, arg.GetType()))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool IsCompatible(Type target, Type source)
            {
                var nonByRefTarget = target.IsByRef ? target.GetElementType()! : target;
                return nonByRefTarget.IsAssignableFrom(source);
            }
        }
    }
}

public sealed record KvMaintenanceSnapshot(
    long ZoneTreeSuccesses,
    long ZoneTreeFailures,
    long RocksDbSuccesses,
    long RocksDbFailures,
    long FastDbSuccesses,
    long FastDbFailures)
{
    public MaintenanceDiagnosticsResponse ToResponse() => new(
        this.ZoneTreeSuccesses,
        this.ZoneTreeFailures,
        this.RocksDbSuccesses,
        this.RocksDbFailures,
        this.FastDbSuccesses,
        this.FastDbFailures);
}

public static class KvMaintenanceDiagnostics
{
    private static long zoneTreeSuccesses;
    private static long zoneTreeFailures;
    private static long rocksDbSuccesses;
    private static long rocksDbFailures;
    private static long fastDbSuccesses;
    private static long fastDbFailures;

    public static void Reset()
    {
        Interlocked.Exchange(ref zoneTreeSuccesses, 0);
        Interlocked.Exchange(ref zoneTreeFailures, 0);
        Interlocked.Exchange(ref rocksDbSuccesses, 0);
        Interlocked.Exchange(ref rocksDbFailures, 0);
        Interlocked.Exchange(ref fastDbSuccesses, 0);
        Interlocked.Exchange(ref fastDbFailures, 0);
    }

    public static KvMaintenanceSnapshot Snapshot() => new(
        Interlocked.Read(ref zoneTreeSuccesses),
        Interlocked.Read(ref zoneTreeFailures),
        Interlocked.Read(ref rocksDbSuccesses),
        Interlocked.Read(ref rocksDbFailures),
        Interlocked.Read(ref fastDbSuccesses),
        Interlocked.Read(ref fastDbFailures));

    internal static void RecordZoneTreeSuccess() => Interlocked.Increment(ref zoneTreeSuccesses);

    internal static void RecordZoneTreeFailure() => Interlocked.Increment(ref zoneTreeFailures);

    internal static void RecordRocksDbSuccess() => Interlocked.Increment(ref rocksDbSuccesses);

    internal static void RecordRocksDbFailure() => Interlocked.Increment(ref rocksDbFailures);

    internal static void RecordFastDbSuccess() => Interlocked.Increment(ref fastDbSuccesses);

    internal static void RecordFastDbFailure() => Interlocked.Increment(ref fastDbFailures);
}
