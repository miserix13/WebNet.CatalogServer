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

        var mode = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : "server";

        if (mode == "client")
        {
            await RunClientSmokeTestAsync(args);
            return;
        }

        await RunServerAsync(args);
    }

    private static async Task RunServerAsync(string[] args)
    {
        var port = TryReadPort(args, defaultPort: 7070);
        var failOnSelfCheck = HasFlag(args, "--fail-on-self-check");
        var selfCheckOnly = HasFlag(args, "--self-check-only");

        var storage = new Storage();
        var server = new Server(
            storage,
            new AllowAllTokenAuthorizer(),
            new AllowAllClientCertificateValidator());

        var selfCheck = storage.RunSelfCheck();
        Console.WriteLine($"Startup SelfCheck => healthy={selfCheck.IsHealthy}, issues={selfCheck.IssueCount}");
        foreach (var issue in selfCheck.Issues)
        {
            Console.WriteLine($"   ! {issue.Code}: {issue.Message}");
        }

        if (selfCheckOnly)
        {
            Environment.ExitCode = selfCheck.IsHealthy ? 0 : 2;
            return;
        }

        if (failOnSelfCheck && !selfCheck.IsHealthy)
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

    private static async Task RunClientSmokeTestAsync(string[] args)
    {
        var hostName = args.Length > 1 ? args[1] : "127.0.0.1";
        var port = TryReadPort(args, defaultPort: 7070);

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

    private static int TryReadPort(string[] args, int defaultPort)
    {
        if (args.Length > 1 && int.TryParse(args[^1], out var parsed) && parsed is > 0 and <= 65535)
        {
            return parsed;
        }

        return defaultPort;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        return args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintHelp()
    {
        Console.WriteLine("WebNet.CatalogServer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- server [port] [--fail-on-self-check] [--self-check-only]");
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
        Console.WriteLine("  --help, -h, /?        Show this help text.");
    }
}
