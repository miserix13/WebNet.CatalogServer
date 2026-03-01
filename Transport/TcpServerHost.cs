using System.Net;
using System.Net.Sockets;
using MessagePack;

namespace WebNet.CatalogServer;

public sealed class TcpServerHost : IAsyncDisposable
{
    private readonly TcpServerOptions options;
    private readonly TcpCommandDispatcher dispatcher;

    private TcpListener? listener;
    private CancellationTokenSource? acceptLoopCts;
    private Task? acceptLoop;

    public TcpServerHost(TcpCommandDispatcher dispatcher, TcpServerOptions? options = null)
    {
        this.dispatcher = dispatcher;
        this.options = options ?? TcpServerOptions.Default;
    }

    public bool IsRunning => this.acceptLoop is not null && !this.acceptLoop.IsCompleted;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (this.listener is not null)
        {
            throw new InvalidOperationException("TCP host is already started.");
        }

        this.listener = new TcpListener(this.options.BindAddress, this.options.Port);
        this.listener.Start();
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

            _ = Task.Run(() => this.ProcessClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var _ = client;
        using var stream = client.GetStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            byte[]? requestPayload;
            try
            {
                requestPayload = await TcpFrameCodec.ReadFrameAsync(stream, this.options.MaxFrameBytes, cancellationToken);
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
            catch (Exception ex)
            {
                var fallback = ResponseEnvelope.Error(Guid.Empty, "transport.dispatch_error", ex.Message);
                responsePayload = MessagePackSerializer.Serialize(new WireResponse(fallback));
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
    }
}
