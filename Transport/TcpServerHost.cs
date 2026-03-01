using System.Net;
using System.Net.Sockets;
using MessagePack;

namespace WebNet.CatalogServer;

public sealed class TcpServerHost : IAsyncDisposable
{
    private readonly TcpServerOptions options;
    private readonly TcpCommandDispatcher dispatcher;
    private readonly SemaphoreSlim connectionSlots;
    private readonly Lock sync = new();
    private readonly Dictionary<string, RequestRateLimiter> requestLimiters = new(StringComparer.OrdinalIgnoreCase);

    private TcpListener? listener;
    private CancellationTokenSource? acceptLoopCts;
    private Task? acceptLoop;

    public TcpServerHost(TcpCommandDispatcher dispatcher, TcpServerOptions? options = null)
    {
        this.dispatcher = dispatcher;
        this.options = options ?? TcpServerOptions.Default;
        this.connectionSlots = new SemaphoreSlim(this.options.MaxConcurrentConnections, this.options.MaxConcurrentConnections);
    }

    public bool IsRunning => this.acceptLoop is not null && !this.acceptLoop.IsCompleted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (this.listener is not null)
        {
            throw new InvalidOperationException("TCP host is already started.");
        }

        this.listener = new TcpListener(this.options.BindAddress, this.options.Port);
        this.listener.Start(this.options.Backlog);
        this.acceptLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this.acceptLoop = Task.Run(() => this.AcceptLoopAsync(this.acceptLoopCts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (this.listener is null)
        {
            return;
        }

        this.acceptLoopCts?.Cancel();
        this.listener.Stop();

        if (this.acceptLoop is not null)
        {
            await this.acceptLoop.WaitAsync(cancellationToken);
        }

        this.listener = null;
        this.acceptLoop = null;
        this.acceptLoopCts?.Dispose();
        this.acceptLoopCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        await this.StopAsync();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await this.listener!.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (!this.connectionSlots.Wait(0))
            {
                using var rejected = client;
                TransportAbuseDiagnostics.RecordRejectedConnection();
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await this.ProcessClientAsync(client, cancellationToken);
                }
                finally
                {
                    this.connectionSlots.Release();
                }
            }, CancellationToken.None);
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        using var stream = client.GetStream();
        var clientKey = GetClientKey(client);
        var limiter = this.GetRateLimiter(clientKey);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!limiter.TryConsume(DateTimeOffset.UtcNow))
            {
                TransportAbuseDiagnostics.RecordRateLimitedRequest();
                await TryWriteErrorAsync(stream, "transport.rate_limited", "Too many requests.", cancellationToken);
                if (this.options.DisconnectOnRateLimit)
                {
                    TransportAbuseDiagnostics.RecordProtocolDisconnect();
                    break;
                }

                continue;
            }

            byte[]? requestPayload;
            try
            {
                using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (this.options.ClientReadTimeout > TimeSpan.Zero)
                {
                    readTimeoutCts.CancelAfter(this.options.ClientReadTimeout);
                }

                requestPayload = await TcpFrameCodec.ReadFrameAsync(stream, this.options.MaxFrameBytes, readTimeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TransportAbuseDiagnostics.RecordReadTimeout();
                await TryWriteErrorAsync(stream, "transport.read_timeout", "Client read timed out.", cancellationToken);
                TransportAbuseDiagnostics.RecordProtocolDisconnect();
                break;
            }
            catch (InvalidDataException ex)
            {
                TransportAbuseDiagnostics.RecordInvalidFrame();
                await TryWriteErrorAsync(stream, "transport.invalid_frame", ex.Message, cancellationToken);
                if (this.options.DisconnectOnProtocolViolation)
                {
                    TransportAbuseDiagnostics.RecordProtocolDisconnect();
                    break;
                }

                continue;
            }
            catch
            {
                break;
            }

            if (requestPayload is null)
            {
                break;
            }

            byte[] responsePayload;
            try
            {
                responsePayload = await this.dispatcher.DispatchAsync(requestPayload, cancellationToken);
            }
            catch (InvalidDataException ex)
            {
                TransportAbuseDiagnostics.RecordInvalidRequest();
                responsePayload = SerializeError("transport.invalid_request", ex.Message);
                if (this.options.DisconnectOnProtocolViolation)
                {
                    await TcpFrameCodec.WriteFrameAsync(stream, responsePayload, cancellationToken);
                    TransportAbuseDiagnostics.RecordProtocolDisconnect();
                    break;
                }
            }
            catch (Exception ex)
            {
                TransportAbuseDiagnostics.RecordDispatchError();
                responsePayload = SerializeError("transport.dispatch_error", ex.Message);
            }

            try
            {
                await TcpFrameCodec.WriteFrameAsync(stream, responsePayload, cancellationToken);
            }
            catch
            {
                break;
            }
        }

        lock (this.sync)
        {
            this.requestLimiters.Remove(clientKey);
        }
    }

    private RequestRateLimiter GetRateLimiter(string clientKey)
    {
        lock (this.sync)
        {
            if (!this.requestLimiters.TryGetValue(clientKey, out var limiter))
            {
                limiter = new RequestRateLimiter(this.options.MaxRequestsPerSecondPerClient, this.options.MaxBurstRequestsPerClient);
                this.requestLimiters[clientKey] = limiter;
            }

            return limiter;
        }
    }

    private static byte[] SerializeError(string code, string message)
    {
        var fallback = ResponseEnvelope.Error(Guid.Empty, code, message);
        return MessagePackSerializer.Serialize(new WireResponse(fallback));
    }

    private static string GetClientKey(TcpClient client)
    {
        if (client.Client.RemoteEndPoint is IPEndPoint endpoint)
        {
            return endpoint.Address.ToString();
        }

        return "unknown";
    }

    private static async Task TryWriteErrorAsync(NetworkStream stream, string code, string message, CancellationToken cancellationToken)
    {
        try
        {
            await TcpFrameCodec.WriteFrameAsync(stream, SerializeError(code, message), cancellationToken);
        }
        catch
        {
        }
    }
}
