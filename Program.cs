using System.Net.Sockets;
using MessagePack;

namespace WebNet.CatalogServer;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (HasFlag(args, "--help") || HasFlag(args, "-h") || HasFlag(args, "/?"))
        {
            PrintHelp();
            return;
        }

        if (!TryParseOptions(args, out var options, out var error))
        {
            Console.Error.WriteLine($"Argument error: {error}");
            Console.Error.WriteLine("Run 'dotnet run -- --help' for usage.");
            Environment.ExitCode = 64;
            return;
        }

        if (options.Mode == "client")
        {
            await RunClientSmokeTestAsync(options.HostName!, options.Port);
            return;
        }

        await RunServerAsync(options.Port, options.FailOnSelfCheck, options.SelfCheckOnly, options.DataRoot);
    }

    private static async Task RunServerAsync(int port, bool failOnSelfCheck, bool selfCheckOnly, string? dataRoot)
    {
        var layout = StorageDirectoryLayout.Resolve(dataRoot);
        var fileSystemCheck = StorageFilesystemValidator.EnsureAndValidate(layout);
        var storage = new Storage(new MultiEngineStoragePersistenceAdapter(layout));
        var server = new Server(
            storage,
            new AllowAllTokenAuthorizer(),
            new AllowAllClientCertificateValidator());

        var storageCheck = storage.RunSelfCheck();
        var combinedIssues = fileSystemCheck.Issues.Concat(storageCheck.Issues).ToArray();
        var isHealthy = combinedIssues.Length == 0;

        Console.WriteLine($"Storage roots => data='{layout.DataRoot}', zonetree='{layout.ZoneTreeRoot}', fastdb='{layout.FastDbRoot}', rocksdb='{layout.RocksDbRoot}', snapshot='{layout.SnapshotFilePath}'");
        Console.WriteLine($"Startup SelfCheck => healthy={isHealthy}, issues={combinedIssues.Length}");
        foreach (var issue in combinedIssues)
        {
            Console.WriteLine($"   ! {issue.Code}: {issue.Message}");
        }

        if (selfCheckOnly)
        {
            Environment.ExitCode = isHealthy ? 0 : 2;
            return;
        }

        if (failOnSelfCheck && !isHealthy)
        {
            Console.Error.WriteLine("Startup aborted due to --fail-on-self-check and failing invariants.");
            Environment.ExitCode = 1;
            return;
        }

        server.Start();
        await using var host = server.CreateTcpHost(TcpServerOptions.Default with { Port = port });
        await host.StartAsync();

        Console.WriteLine($"WebNet.CatalogServer listening on tcp://0.0.0.0:{port}");
        Console.WriteLine("Press Ctrl+C to stop.");

        var shutdown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.TrySetResult(true);
        };

        await shutdown.Task;
        server.Stop();
        await host.StopAsync();
    }

    private static async Task RunClientSmokeTestAsync(string hostName, int port)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(hostName, port);
        using var stream = client.GetStream();

        var createDbRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.CreateDatabase,
            new CreateDatabaseRequest("default", ConsistencyLevel.Strong, MakePrimary: true));

        var createDbResponse = await SendAsync(stream, createDbRequest);
        Console.WriteLine($"CreateDatabase => success={createDbResponse.IsSuccess}, error={createDbResponse.ErrorCode}");

        var createCatalogRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.CreateCatalog,
            new CreateCatalogRequest("default", "products"));

        var createCatalogResponse = await SendAsync(stream, createCatalogRequest);
        Console.WriteLine($"CreateCatalog => success={createCatalogResponse.IsSuccess}, error={createCatalogResponse.ErrorCode}");

        var documentId = Guid.NewGuid();
        var putDocumentRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.PutDocument,
            new PutDocumentRequest(
                "default",
                "products",
                new Document
                {
                    DocumentId = documentId,
                    Properties =
                    {
                        ["sku"] = "ABC-123",
                        ["name"] = "Sample Product"
                    }
                }));

        var putDocumentResponse = await SendAsync(stream, putDocumentRequest);
        Console.WriteLine($"PutDocument => success={putDocumentResponse.IsSuccess}, error={putDocumentResponse.ErrorCode}");

        var getDocumentRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.GetDocument,
            new GetDocumentRequest("default", "products", documentId));

        var getDocumentResponse = await SendAsync(stream, getDocumentRequest);
        if (getDocumentResponse.IsSuccess)
        {
            var getPayload = MessagePackSerializer.Deserialize<GetDocumentResponse>(getDocumentResponse.Payload);
            Console.WriteLine($"GetDocument => success=True, name={getPayload.Document.Properties.GetValueOrDefault("name")}");
        }
        else
        {
            Console.WriteLine($"GetDocument => success=False, error={getDocumentResponse.ErrorCode}");
        }

        var deleteDocumentRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.DeleteDocument,
            new DeleteDocumentRequest("default", "products", documentId));

        var deleteDocumentResponse = await SendAsync(stream, deleteDocumentRequest);
        Console.WriteLine($"DeleteDocument => success={deleteDocumentResponse.IsSuccess}, error={deleteDocumentResponse.ErrorCode}");

        var selfCheckRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.SelfCheck,
            new SelfCheckRequest());

        var selfCheckResponse = await SendAsync(stream, selfCheckRequest);
        if (selfCheckResponse.IsSuccess)
        {
            var selfCheckPayload = MessagePackSerializer.Deserialize<SelfCheckResponse>(selfCheckResponse.Payload);
            Console.WriteLine($"SelfCheck => healthy={selfCheckPayload.IsHealthy}, issues={selfCheckPayload.IssueCount}");
            foreach (var issue in selfCheckPayload.Issues)
            {
                Console.WriteLine($"   ! {issue.Code}: {issue.Message}");
            }
        }
        else
        {
            Console.WriteLine($"SelfCheck => success=False, error={selfCheckResponse.ErrorCode}");
        }

        var maintenanceRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.MaintenanceDiagnostics,
            new MaintenanceDiagnosticsRequest());

        var maintenanceResponse = await SendAsync(stream, maintenanceRequest);
        if (maintenanceResponse.IsSuccess)
        {
            var maintenancePayload = MessagePackSerializer.Deserialize<MaintenanceDiagnosticsResponse>(maintenanceResponse.Payload);
            Console.WriteLine($"Maintenance => zonetree(s={maintenancePayload.ZoneTreeSuccesses},f={maintenancePayload.ZoneTreeFailures}), rocksdb(s={maintenancePayload.RocksDbSuccesses},f={maintenancePayload.RocksDbFailures}), fastdb(s={maintenancePayload.FastDbSuccesses},f={maintenancePayload.FastDbFailures})");
        }
        else
        {
            Console.WriteLine($"Maintenance => success=False, error={maintenanceResponse.ErrorCode}");
        }

        var listDbRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.ListDatabases,
            new ListDatabasesRequest());

        var listDbResponse = await SendAsync(stream, listDbRequest);
        if (!listDbResponse.IsSuccess)
        {
            Console.WriteLine($"ListDatabases failed: {listDbResponse.ErrorCode} - {listDbResponse.ErrorMessage}");
            return;
        }

        var listPayload = MessagePackSerializer.Deserialize<ListDatabasesResponse>(listDbResponse.Payload);
        Console.WriteLine($"Databases ({listPayload.Databases.Count}):");
        foreach (var database in listPayload.Databases)
        {
            Console.WriteLine($" - {database.Name} (primary={database.IsPrimary}, consistency={database.Consistency})");
        }
    }

    private static async Task<ResponseEnvelope> SendAsync(NetworkStream stream, RequestEnvelope request)
    {
        var wireRequest = new WireRequest(
            request,
            Token: "dev-token",
            ClientCertificateThumbprint: "dev-thumbprint",
            Subject: "dev-user",
            Roles: ["admin"]);

        var payload = MessagePackSerializer.Serialize(wireRequest);
        await TcpFrameCodec.WriteFrameAsync(stream, payload);

        var frame = await TcpFrameCodec.ReadFrameAsync(stream, 4 * 1024 * 1024);
        if (frame is null)
        {
            throw new InvalidOperationException("Server closed connection before responding.");
        }

        var wireResponse = MessagePackSerializer.Deserialize<WireResponse>(frame);
        return wireResponse.Response;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryParseOptions(string[] args, out CliOptions options, out string error)
    {
        const int defaultPort = 7070;

        var index = 0;
        var mode = "server";

        if (args.Length > 0 && !args[0].StartsWith('-') && args[0] != "/?")
        {
            mode = args[0].Trim().ToLowerInvariant();
            index = 1;
        }

        if (mode != "server" && mode != "client")
        {
            options = CliOptions.Server(defaultPort, failOnSelfCheck: false, selfCheckOnly: false, dataRoot: null);
            error = $"Unknown mode '{mode}'.";
            return false;
        }

        var failOnSelfCheck = false;
        var selfCheckOnly = false;
        string? dataRoot = null;
        var positional = new List<string>();

        for (var i = index; i < args.Length; i++)
        {
            var token = args[i].Trim();
            if (token.StartsWith('-'))
            {
                if (string.Equals(token, "--fail-on-self-check", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode != "server")
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        error = "--fail-on-self-check is only valid in server mode.";
                        return false;
                    }

                    failOnSelfCheck = true;
                    continue;
                }

                if (string.Equals(token, "--self-check-only", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode != "server")
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        error = "--self-check-only is only valid in server mode.";
                        return false;
                    }

                    selfCheckOnly = true;
                    continue;
                }

                if (token.StartsWith("--data-root", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode != "server")
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        error = "--data-root is only valid in server mode.";
                        return false;
                    }

                    if (!TryReadDataRootValue(args, ref i, token, out var value, out error))
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        return false;
                    }

                    dataRoot = value;
                    continue;
                }

                options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                error = $"Unknown flag '{token}'.";
                return false;
            }

            positional.Add(token);
        }

        if (mode == "server")
        {
            if (positional.Count > 1)
            {
                options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                error = "Server mode accepts at most one positional argument: [port].";
                return false;
            }

            var port = defaultPort;
            if (positional.Count == 1 && !TryParsePort(positional[0], out port))
            {
                options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                error = $"Invalid server port '{positional[0]}'. Expected integer in range 1-65535.";
                return false;
            }

            options = CliOptions.Server(port, failOnSelfCheck, selfCheckOnly, dataRoot);
            error = string.Empty;
            return true;
        }

        if (positional.Count > 2)
        {
            options = CliOptions.Client("127.0.0.1", defaultPort);
            error = "Client mode accepts [host] [port].";
            return false;
        }

        var hostName = positional.Count >= 1 ? positional[0] : "127.0.0.1";
        var clientPort = defaultPort;
        if (positional.Count == 2 && !TryParsePort(positional[1], out clientPort))
        {
            options = CliOptions.Client("127.0.0.1", defaultPort);
            error = $"Invalid client port '{positional[1]}'. Expected integer in range 1-65535.";
            return false;
        }

        options = CliOptions.Client(hostName, clientPort);
        error = string.Empty;
        return true;
    }

    private static bool TryParsePort(string value, out int port)
    {
        if (int.TryParse(value, out var parsed) && parsed is > 0 and <= 65535)
        {
            port = parsed;
            return true;
        }

        port = 0;
        return false;
    }

    private static bool TryReadDataRootValue(string[] args, ref int index, string token, out string value, out string error)
    {
        const string prefix = "--data-root=";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = token[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "--data-root requires a non-empty value.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = "--data-root requires a path value.";
            return false;
        }

        value = args[++index].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "--data-root requires a non-empty value.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("WebNet.CatalogServer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- server [port] [--fail-on-self-check] [--self-check-only] [--data-root <path>]");
        Console.WriteLine("  dotnet run -- client [host] [port]");
        Console.WriteLine("  dotnet run -- --help");
        Console.WriteLine();
        Console.WriteLine("Modes:");
        Console.WriteLine("  server              Starts TCP server (default mode).");
        Console.WriteLine("  client              Runs smoke-test client against a server.");
        Console.WriteLine();
        Console.WriteLine("Flags:");
        Console.WriteLine("  --fail-on-self-check  Abort server startup when self-check is unhealthy.");
        Console.WriteLine("  --self-check-only     Run self-check and exit without starting listener.");
        Console.WriteLine("  --data-root <path>    Override storage root (also via WEBNET_DATA_ROOT).\n                        Layout: kv/zonetree, kv/fastdb, kv/rocksdb, snapshots/.");
        Console.WriteLine("  --help, -h, /?        Show this help text.");
    }

}

internal sealed record CliOptions(
    string Mode,
    int Port,
    string? HostName,
    bool FailOnSelfCheck,
    bool SelfCheckOnly,
    string? DataRoot)
{
    public static CliOptions Server(int port, bool failOnSelfCheck, bool selfCheckOnly, string? dataRoot) =>
        new("server", port, null, failOnSelfCheck, selfCheckOnly, dataRoot);

    public static CliOptions Client(string hostName, int port) =>
        new("client", port, hostName, false, false, null);
}
