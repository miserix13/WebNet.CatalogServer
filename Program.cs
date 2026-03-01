using System.Net.Sockets;
using MessagePack;

namespace WebNet.CatalogServer;

internal static class Program
{
    public static async Task Main(string[] args)
    {
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

        var storage = new Storage();
        var server = new Server(
            storage,
            new AllowAllTokenAuthorizer(),
            new AllowAllClientCertificateValidator());

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
}
