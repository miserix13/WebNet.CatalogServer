using System.Diagnostics;
using System.Net.Sockets;
using MessagePack;
using Microsoft.Extensions.Logging;

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

        await RunServerAsync(options.Port, options.FailOnSelfCheck, options.SelfCheckOnly, options.DataRoot, options.AuthProviderOverride, options.EnableCluster, options.ClusterPort);
    }

    private static async Task RunServerAsync(int port, bool failOnSelfCheck, bool selfCheckOnly, string? dataRoot, AuthProvider? authProviderOverride, bool enableCluster, int clusterPort)
    {
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddJsonConsole(options =>
            {
                options.TimestampFormat = "O";
                options.IncludeScopes = false;
            });
        });
        var logger = loggerFactory.CreateLogger("WebNet.CatalogServer.Program");

        var startupStopwatch = Stopwatch.StartNew();
        var layout = StorageDirectoryLayout.Resolve(dataRoot);
        var fileSystemCheck = StorageFilesystemValidator.EnsureAndValidate(layout);
        var storage = new Storage(new MultiEngineStoragePersistenceAdapter(layout));
        LiteGraphAuthOptions authOptions;
        try
        {
            authOptions = LiteGraphAuthOptions.Resolve(layout.DataRoot, authProviderOverride);
        }
        catch (LiteGraphAuthOptions.AuthConfigurationException ex)
        {
            logger.LogError("Configuration error: {ErrorMessage}", ex.Message);
            Environment.ExitCode = 64;
            return;
        }

        var tokenAuthorizer = CreateTokenAuthorizer(authOptions);
        var certificateValidator = new ThumbprintAllowListClientCertificateValidator(authOptions.AllowedCertificateThumbprints);
        var server = new Server(
            storage,
            tokenAuthorizer,
            certificateValidator);

        var storageCheck = storage.RunSelfCheck();
        var combinedIssues = fileSystemCheck.Issues.Concat(storageCheck.Issues).ToArray();
        var isHealthy = combinedIssues.Length == 0;

        logger.LogInformation(
            "Storage roots configured: data={DataRoot}, zonetree={ZoneTreeRoot}, fastdb={FastDbRoot}, rocksdb={RocksDbRoot}, snapshot={SnapshotFilePath}",
            layout.DataRoot,
            layout.ZoneTreeRoot,
            layout.FastDbRoot,
            layout.RocksDbRoot,
            layout.SnapshotFilePath);
        logger.LogInformation(
            "Auth configured: provider={Provider}, litegraph={DatabaseFilePath}, bootstrap={BootstrapEnabled}, windowsAllowListCount={WindowsAllowListCount}, certAllowListCount={CertAllowListCount}, strictPolicy={StrictPolicy}, overrideMappedCommands={OverrideMappedCommands}",
            authOptions.Provider,
            authOptions.DatabaseFilePath,
            authOptions.BootstrapCredentialEnabled,
            authOptions.AllowedWindowsSubjects.Count,
            authOptions.AllowedCertificateThumbprints.Count,
            authOptions.RequireFullCommandPolicy,
            authOptions.OverrideMappedCommandCount);
        logger.LogInformation("Startup self-check completed: healthy={IsHealthy}, issueCount={IssueCount}", isHealthy, combinedIssues.Length);
        foreach (var issue in combinedIssues)
        {
            logger.LogWarning("Startup issue detected: code={IssueCode}, message={IssueMessage}", issue.Code, issue.Message);
        }

        if (selfCheckOnly)
        {
            Environment.ExitCode = isHealthy ? 0 : 2;
            logger.LogInformation("Self-check-only mode exit: code={ExitCode}, startupDurationMs={StartupDurationMs}", Environment.ExitCode, startupStopwatch.Elapsed.TotalMilliseconds);
            return;
        }

        if (failOnSelfCheck && !isHealthy)
        {
            logger.LogError("Startup aborted due to fail-on-self-check with failing invariants.");
            Environment.ExitCode = 1;
            return;
        }

        ConsoleCancelEventHandler? onCancel = null;
        AkkaClusterRuntime? clusterRuntime = null;
        try
        {
            if (enableCluster)
            {
                clusterRuntime = new AkkaClusterRuntime(AkkaClusterOptions.CreateDefault(clusterPort));
                await clusterRuntime.StartAsync();
                logger.LogInformation("Akka cluster runtime started: enabled={ClusterEnabled}, system={SystemName}, host={Host}, port={Port}",
                    true,
                    "webnet-catalog",
                    "127.0.0.1",
                    clusterPort);
            }

            server.Start();
            await using var host = server.CreateTcpHost(TcpServerOptions.Default with { Port = port });
            await host.StartAsync();

            if (!host.IsRunning)
            {
                logger.LogError("Lifecycle check failed: TCP host not running after StartAsync.");
                Environment.ExitCode = 1;
                server.Stop();
                return;
            }

            startupStopwatch.Stop();
            logger.LogInformation("Server startup completed: endpoint=tcp://0.0.0.0:{Port}, startupDurationMs={StartupDurationMs}", port, startupStopwatch.Elapsed.TotalMilliseconds);

            Console.WriteLine($"WebNet.CatalogServer listening on tcp://0.0.0.0:{port}");
            Console.WriteLine("Press Ctrl+C to stop.");

            var shutdown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            onCancel = (_, e) =>
            {
                e.Cancel = true;
                shutdown.TrySetResult(true);
                logger.LogInformation("Shutdown signal received from console.");
            };
            Console.CancelKeyPress += onCancel;

            await shutdown.Task;

            var shutdownStopwatch = Stopwatch.StartNew();
            server.Stop();

            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(shutdownTimeout.Token);

            shutdownStopwatch.Stop();
            logger.LogInformation("Server shutdown completed: serverRunning={ServerRunning}, hostRunning={HostRunning}, shutdownDurationMs={ShutdownDurationMs}", server.IsRunning, host.IsRunning, shutdownStopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Shutdown lifecycle check failed: host did not stop within timeout.");
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled server lifecycle exception.");
            Environment.ExitCode = 1;
        }
        finally
        {
            if (onCancel is not null)
            {
                Console.CancelKeyPress -= onCancel;
            }

            if (tokenAuthorizer is IDisposable disposableAuthorizer)
            {
                disposableAuthorizer.Dispose();
            }

            if (clusterRuntime is not null)
            {
                await clusterRuntime.DisposeAsync();
                logger.LogInformation("Akka cluster runtime stopped.");
            }
        }
    }

    private static ITokenAuthorizer CreateTokenAuthorizer(LiteGraphAuthOptions authOptions)
    {
        return authOptions.Provider switch
        {
            AuthProvider.LiteGraph => new LiteGraphTokenAuthorizer(authOptions),
            AuthProvider.Windows => new WindowsTokenAuthorizer(authOptions),
            _ => throw new InvalidOperationException($"Unsupported auth provider '{authOptions.Provider}'.")
        };
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

        var healthRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.Health,
            new HealthRequest());

        var healthResponse = await SendAsync(stream, healthRequest);
        if (healthResponse.IsSuccess)
        {
            var healthPayload = MessagePackSerializer.Deserialize<HealthResponse>(healthResponse.Payload);
            Console.WriteLine($"Health => status={healthPayload.Status}, running={healthPayload.IsRunning}, db={healthPayload.DatabaseCount}, catalogs={healthPayload.CatalogCount}, docs={healthPayload.DocumentCount}, primary={healthPayload.PrimaryDatabaseName}, selfcheck_issues={healthPayload.SelfCheckIssueCount}, uptime={healthPayload.Uptime}");
        }
        else
        {
            Console.WriteLine($"Health => success=False, error={healthResponse.ErrorCode}");
        }

        var metricsRequest = RequestEnvelope.FromPayload(
            Guid.NewGuid(),
            CommandKind.Metrics,
            new MetricsRequest());

        var metricsResponse = await SendAsync(stream, metricsRequest);
        if (metricsResponse.IsSuccess)
        {
            var metricsPayload = MessagePackSerializer.Deserialize<MetricsResponse>(metricsResponse.Payload);
            Console.WriteLine($"Metrics => keys={metricsPayload.Values.Count}, uptime_seconds={metricsPayload.Values.GetValueOrDefault("server.uptime.seconds")}, selfcheck_issues={metricsPayload.Values.GetValueOrDefault("selfcheck.issue.count")}, transport_abuse_total={metricsPayload.Values.GetValueOrDefault("transport.abuse.total")}");
        }
        else
        {
            Console.WriteLine($"Metrics => success=False, error={metricsResponse.ErrorCode}");
        }

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
            Console.WriteLine($"Maintenance => zonetree(s={maintenancePayload.ZoneTreeSuccesses},f={maintenancePayload.ZoneTreeFailures}), rocksdb(s={maintenancePayload.RocksDbSuccesses},f={maintenancePayload.RocksDbFailures}), fastdb(s={maintenancePayload.FastDbSuccesses},f={maintenancePayload.FastDbFailures}), transport(rate_limited={maintenancePayload.TransportRateLimitedRequests}, rejected={maintenancePayload.TransportRejectedConnections}, read_timeouts={maintenancePayload.TransportReadTimeouts}, invalid_frames={maintenancePayload.TransportInvalidFrames}, invalid_requests={maintenancePayload.TransportInvalidRequests}, dispatch_errors={maintenancePayload.TransportDispatchErrors}, protocol_disconnects={maintenancePayload.TransportProtocolDisconnects})");
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
        var enableCluster = false;
        var clusterPort = 8110;
        string? dataRoot = null;
        AuthProvider? authProviderOverride = null;
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

                if (string.Equals(token, "--enable-cluster", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode != "server")
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        error = "--enable-cluster is only valid in server mode.";
                        return false;
                    }

                    enableCluster = true;
                    continue;
                }

                if (token.StartsWith("--cluster-port", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode != "server")
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        error = "--cluster-port is only valid in server mode.";
                        return false;
                    }

                    if (!TryReadClusterPortValue(args, ref i, token, out var clusterPortValue, out error))
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        return false;
                    }

                    clusterPort = clusterPortValue;
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

                if (token.StartsWith("--auth-provider", StringComparison.OrdinalIgnoreCase))
                {
                    if (mode != "server")
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        error = "--auth-provider is only valid in server mode.";
                        return false;
                    }

                    if (!TryReadAuthProviderValue(args, ref i, token, out var providerValue, out error))
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        return false;
                    }

                    if (!TryParseAuthProvider(providerValue, out var parsedProvider))
                    {
                        options = CliOptions.Server(defaultPort, false, false, dataRoot: null);
                        error = $"Invalid auth provider '{providerValue}'. Expected 'litegraph' or 'windows'.";
                        return false;
                    }

                    authProviderOverride = parsedProvider;
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

            options = CliOptions.Server(port, failOnSelfCheck, selfCheckOnly, dataRoot, authProviderOverride, enableCluster, clusterPort);
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

    private static bool TryReadAuthProviderValue(string[] args, ref int index, string token, out string value, out string error)
    {
        const string prefix = "--auth-provider=";
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = token[prefix.Length..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "--auth-provider requires a non-empty value.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = "--auth-provider requires a value.";
            return false;
        }

        value = args[++index].Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "--auth-provider requires a non-empty value.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadClusterPortValue(string[] args, ref int index, string token, out int clusterPort, out string error)
    {
        const string prefix = "--cluster-port=";
        string rawValue;
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            rawValue = token[prefix.Length..].Trim();
        }
        else
        {
            if (index + 1 >= args.Length)
            {
                clusterPort = 0;
                error = "--cluster-port requires a value.";
                return false;
            }

            rawValue = args[++index].Trim();
        }

        if (!TryParsePort(rawValue, out clusterPort))
        {
            error = $"Invalid cluster port '{rawValue}'. Expected integer in range 1-65535.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryParseAuthProvider(string value, out AuthProvider provider)
    {
        if (string.Equals(value, "litegraph", StringComparison.OrdinalIgnoreCase))
        {
            provider = AuthProvider.LiteGraph;
            return true;
        }

        if (string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase))
        {
            provider = AuthProvider.Windows;
            return true;
        }

        provider = default;
        return false;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("WebNet.CatalogServer");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- server [port] [--fail-on-self-check] [--self-check-only] [--data-root <path>] [--auth-provider <litegraph|windows>] [--enable-cluster] [--cluster-port <port>]");
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
        Console.WriteLine("  --auth-provider <v>   Override runtime auth provider: 'litegraph' or 'windows'.");
        Console.WriteLine("  --enable-cluster      Enable Akka.NET cluster bootstrap runtime.");
        Console.WriteLine("  --cluster-port <port> Cluster transport port (default: 8110). Requires server mode.");
        Console.WriteLine("Environment auth options:");
        Console.WriteLine("  WEBNET_AUTH_PROVIDER                  'litegraph' (default) or 'windows'.");
        Console.WriteLine("  WEBNET_AUTH_WINDOWS_ALLOWED_SUBJECTS  Optional CSV/semicolon allow-list for Windows mode.");
        Console.WriteLine("  --help, -h, /?        Show this help text.");
    }

}

internal sealed record CliOptions(
    string Mode,
    int Port,
    string? HostName,
    bool FailOnSelfCheck,
    bool SelfCheckOnly,
    string? DataRoot,
    AuthProvider? AuthProviderOverride,
    bool EnableCluster,
    int ClusterPort)
{
    public static CliOptions Server(int port, bool failOnSelfCheck, bool selfCheckOnly, string? dataRoot, AuthProvider? authProviderOverride = null, bool enableCluster = false, int clusterPort = 8110) =>
        new("server", port, null, failOnSelfCheck, selfCheckOnly, dataRoot, authProviderOverride, enableCluster, clusterPort);

    public static CliOptions Client(string hostName, int port) =>
        new("client", port, hostName, false, false, null, null, false, 8110);
}
