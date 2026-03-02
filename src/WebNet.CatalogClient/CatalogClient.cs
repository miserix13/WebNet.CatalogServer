using System.Net.Sockets;
using MessagePack;

namespace WebNet.CatalogClient;

public sealed class CatalogClient : IAsyncDisposable
{
    private readonly CatalogClientOptions options;
    private readonly Func<CancellationToken, ValueTask<CatalogClientAuthContext>> authContextProvider;
    private readonly SemaphoreSlim requestGate = new(1, 1);
    private TcpClient? tcpClient;
    private NetworkStream? stream;

    public CatalogClient(
        CatalogClientOptions options,
        Func<CancellationToken, ValueTask<CatalogClientAuthContext>> authContextProvider)
    {
        this.options = options;
        this.authContextProvider = authContextProvider;
    }

    public Task<DatabaseMetadata> CreateDatabaseAsync(CreateDatabaseRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<CreateDatabaseRequest, DatabaseMetadata>(CommandKind.CreateDatabase, request, cancellationToken);

    public Task<DropDatabaseResponse> DropDatabaseAsync(DropDatabaseRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<DropDatabaseRequest, DropDatabaseResponse>(CommandKind.DropDatabase, request, cancellationToken);

    public Task<ListDatabasesResponse> ListDatabasesAsync(CancellationToken cancellationToken = default)
        => this.SendAsync<ListDatabasesRequest, ListDatabasesResponse>(CommandKind.ListDatabases, new ListDatabasesRequest(), cancellationToken);

    public Task<CatalogMetadata> CreateCatalogAsync(CreateCatalogRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<CreateCatalogRequest, CatalogMetadata>(CommandKind.CreateCatalog, request, cancellationToken);

    public Task<DropCatalogResponse> DropCatalogAsync(DropCatalogRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<DropCatalogRequest, DropCatalogResponse>(CommandKind.DropCatalog, request, cancellationToken);

    public Task<ListCatalogsResponse> ListCatalogsAsync(ListCatalogsRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<ListCatalogsRequest, ListCatalogsResponse>(CommandKind.ListCatalogs, request, cancellationToken);

    public Task<PutDocumentResponse> PutDocumentAsync(PutDocumentRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<PutDocumentRequest, PutDocumentResponse>(CommandKind.PutDocument, request, cancellationToken);

    public Task<GetDocumentResponse> GetDocumentAsync(GetDocumentRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<GetDocumentRequest, GetDocumentResponse>(CommandKind.GetDocument, request, cancellationToken);

    public Task<DeleteDocumentResponse> DeleteDocumentAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
        => this.SendAsync<DeleteDocumentRequest, DeleteDocumentResponse>(CommandKind.DeleteDocument, request, cancellationToken);

    public Task<HealthResponse> HealthAsync(CancellationToken cancellationToken = default)
        => this.SendAsync<HealthRequest, HealthResponse>(CommandKind.Health, new HealthRequest(), cancellationToken);

    public Task<MetricsResponse> MetricsAsync(CancellationToken cancellationToken = default)
        => this.SendAsync<MetricsRequest, MetricsResponse>(CommandKind.Metrics, new MetricsRequest(), cancellationToken);

    public Task<SelfCheckResponse> SelfCheckAsync(CancellationToken cancellationToken = default)
        => this.SendAsync<SelfCheckRequest, SelfCheckResponse>(CommandKind.SelfCheck, new SelfCheckRequest(), cancellationToken);

    public Task<MaintenanceDiagnosticsResponse> MaintenanceDiagnosticsAsync(CancellationToken cancellationToken = default)
        => this.SendAsync<MaintenanceDiagnosticsRequest, MaintenanceDiagnosticsResponse>(CommandKind.MaintenanceDiagnostics, new MaintenanceDiagnosticsRequest(), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await this.requestGate.WaitAsync();
        try
        {
            this.stream?.Dispose();
            this.stream = null;
            this.tcpClient?.Dispose();
            this.tcpClient = null;
        }
        finally
        {
            this.requestGate.Release();
            this.requestGate.Dispose();
        }
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(CommandKind command, TRequest request, CancellationToken cancellationToken)
    {
        await this.requestGate.WaitAsync(cancellationToken);
        try
        {
            await this.EnsureConnectedAsync(cancellationToken);

            if (this.stream is null)
            {
                throw new InvalidOperationException("Client stream is not available.");
            }

            var requestId = Guid.NewGuid();
            var envelope = RequestEnvelope.FromPayload(requestId, command, request!);
            var authContext = await this.authContextProvider(cancellationToken);
            var wire = new WireRequest(
                envelope,
                authContext.Token,
                authContext.ClientCertificateThumbprint,
                authContext.Subject,
                authContext.Roles);

            var bytes = MessagePackSerializer.Serialize(wire);
            await TcpFrameCodec.WriteFrameAsync(this.stream, bytes, cancellationToken);

            var frame = await TcpFrameCodec.ReadFrameAsync(this.stream, this.options.MaxFrameBytes, cancellationToken);
            if (frame is null)
            {
                this.DisposeConnection();
                throw new IOException("Connection closed before response frame was received.");
            }

            var response = MessagePackSerializer.Deserialize<WireResponse>(frame).Response;
            if (!response.IsSuccess)
            {
                throw new CatalogClientException(
                    response.RequestId,
                    response.ErrorCode ?? "unknown",
                    response.ErrorMessage ?? "Catalog server returned an unknown error.");
            }

            if (response.RequestId != requestId && response.RequestId != Guid.Empty)
            {
                this.DisposeConnection();
                throw new CatalogClientException(response.RequestId, "protocol.request_id_mismatch", "Response request ID did not match request ID.");
            }

            return MessagePackSerializer.Deserialize<TResponse>(response.Payload);
        }
        catch (SocketException)
        {
            this.DisposeConnection();
            throw;
        }
        catch (IOException)
        {
            this.DisposeConnection();
            throw;
        }
        finally
        {
            this.requestGate.Release();
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (this.tcpClient is { Connected: true } && this.stream is not null)
        {
            return;
        }

        this.DisposeConnection();

        this.tcpClient = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(this.options.ConnectTimeout);
        await this.tcpClient.ConnectAsync(this.options.Address, this.options.Port, timeoutCts.Token);
        this.stream = this.tcpClient.GetStream();
        this.stream.ReadTimeout = (int)this.options.ReadTimeout.TotalMilliseconds;
        this.stream.WriteTimeout = (int)this.options.ReadTimeout.TotalMilliseconds;
    }

    private void DisposeConnection()
    {
        this.stream?.Dispose();
        this.stream = null;
        this.tcpClient?.Dispose();
        this.tcpClient = null;
    }
}