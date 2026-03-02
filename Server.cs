
using MessagePack;

namespace WebNet.CatalogServer
{
    public class Server
    {
        private readonly Storage storage;
        private readonly ITokenAuthorizer tokenAuthorizer;
        private readonly IClientCertificateValidator clientCertificateValidator;
        private readonly TimeProvider timeProvider;

        private DateTimeOffset startedAtUtc;

        public Server(
            Storage storage,
            ITokenAuthorizer tokenAuthorizer,
            IClientCertificateValidator clientCertificateValidator,
            TimeProvider? timeProvider = null)
        {
            this.storage = storage;
            this.tokenAuthorizer = tokenAuthorizer;
            this.clientCertificateValidator = clientCertificateValidator;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            this.startedAtUtc = DateTimeOffset.MinValue;
        }

        public bool IsRunning { get; private set; }

        public void Start()
        {
            this.startedAtUtc = this.timeProvider.GetUtcNow();
            this.IsRunning = true;
        }

        public void Stop()
        {
            this.IsRunning = false;
        }

        public TcpServerHost CreateTcpHost(TcpServerOptions? options = null)
        {
            var dispatcher = new TcpCommandDispatcher(this);
            return new TcpServerHost(dispatcher, options);
        }

        public async Task<ResponseEnvelope> HandleAsync(
            RequestEnvelope request,
            SecurityContext securityContext,
            CancellationToken cancellationToken = default)
        {
            if (!this.IsRunning)
            {
                return ResponseEnvelope.Error(request.RequestId, "server.not_running", "Server is not running.");
            }

            if (!this.clientCertificateValidator.Validate(securityContext.ClientCertificateThumbprint))
            {
                return ResponseEnvelope.Error(request.RequestId, "auth.invalid_certificate", "Invalid client certificate.");
            }

            if (!this.tokenAuthorizer.Authorize(securityContext, request.Command))
            {
                return ResponseEnvelope.Error(request.RequestId, "auth.forbidden", "Caller is not authorized for this command.");
            }

            return request.Command switch
            {
                CommandKind.CreateDatabase => await this.HandleCreateDatabaseAsync(request, cancellationToken),
                CommandKind.DropDatabase => await this.HandleDropDatabaseAsync(request, cancellationToken),
                CommandKind.ListDatabases => await this.HandleListDatabasesAsync(request, cancellationToken),
                CommandKind.CreateCatalog => await this.HandleCreateCatalogAsync(request, cancellationToken),
                CommandKind.DropCatalog => await this.HandleDropCatalogAsync(request, cancellationToken),
                CommandKind.ListCatalogs => await this.HandleListCatalogsAsync(request, cancellationToken),
                CommandKind.PutDocument => await this.HandlePutDocumentAsync(request, cancellationToken),
                CommandKind.GetDocument => await this.HandleGetDocumentAsync(request, cancellationToken),
                CommandKind.DeleteDocument => await this.HandleDeleteDocumentAsync(request, cancellationToken),
                CommandKind.Health => await this.HandleHealthAsync(request, cancellationToken),
                CommandKind.Metrics => await this.HandleMetricsAsync(request, cancellationToken),
                CommandKind.SelfCheck => await this.HandleSelfCheckAsync(request, cancellationToken),
                CommandKind.MaintenanceDiagnostics => await this.HandleMaintenanceDiagnosticsAsync(request, cancellationToken),
                _ => ResponseEnvelope.Error(request.RequestId, "command.unsupported", $"Unsupported command '{request.Command}'.")
            };
        }

        private async Task<ResponseEnvelope> HandleCreateDatabaseAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<CreateDatabaseRequest>(request.Payload);
            var result = await this.storage.CreateDatabaseAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandleDropDatabaseAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<DropDatabaseRequest>(request.Payload);
            var result = await this.storage.DropDatabaseAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandleListDatabasesAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            _ = MessagePackSerializer.Deserialize<ListDatabasesRequest>(request.Payload);
            var result = await this.storage.ListDatabasesAsync(cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandleCreateCatalogAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<CreateCatalogRequest>(request.Payload);
            var result = await this.storage.CreateCatalogAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandleDropCatalogAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<DropCatalogRequest>(request.Payload);
            var result = await this.storage.DropCatalogAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandleListCatalogsAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<ListCatalogsRequest>(request.Payload);
            var result = await this.storage.ListCatalogsAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandlePutDocumentAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<PutDocumentRequest>(request.Payload);
            var result = await this.storage.PutDocumentAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandleGetDocumentAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<GetDocumentRequest>(request.Payload);
            var result = await this.storage.GetDocumentAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private async Task<ResponseEnvelope> HandleDeleteDocumentAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            var payload = MessagePackSerializer.Deserialize<DeleteDocumentRequest>(request.Payload);
            var result = await this.storage.DeleteDocumentAsync(payload, cancellationToken);
            return result.IsSuccess
                ? ResponseEnvelope.Success(request.RequestId, result.Value)
                : ResponseEnvelope.Error(request.RequestId, result.ErrorCode, result.ErrorMessage);
        }

        private Task<ResponseEnvelope> HandleHealthAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            _ = MessagePackSerializer.Deserialize<HealthRequest>(request.Payload);
            var stats = this.storage.GetStatistics();
            var selfCheck = this.storage.RunSelfCheck();
            var status = selfCheck.IsHealthy ? ServerHealthStatus.Healthy : ServerHealthStatus.Degraded;
            var cluster = ClusterRuntimeDiagnostics.Snapshot();
            var response = new HealthResponse(
                status,
                this.startedAtUtc,
                this.timeProvider.GetUtcNow() - this.startedAtUtc,
                stats.DatabaseCount,
                stats.CatalogCount,
                stats.DocumentCount,
                stats.PrimaryDatabaseName,
                selfCheck.IssueCount,
                this.IsRunning,
                cluster.Enabled,
                cluster.Running,
                cluster.SystemName,
                cluster.Hostname,
                cluster.Port,
                cluster.MemberCount);

            return Task.FromResult(ResponseEnvelope.Success(request.RequestId, response));
        }

        private Task<ResponseEnvelope> HandleMetricsAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            _ = MessagePackSerializer.Deserialize<MetricsRequest>(request.Payload);
            var stats = this.storage.GetStatistics();
            var selfCheck = this.storage.RunSelfCheck();
            var maintenance = KvMaintenanceDiagnostics.Snapshot();
            var transport = TransportAbuseDiagnostics.Snapshot();
            var cluster = ClusterRuntimeDiagnostics.Snapshot();
            var now = this.timeProvider.GetUtcNow();
            var uptime = this.startedAtUtc == DateTimeOffset.MinValue ? TimeSpan.Zero : now - this.startedAtUtc;
            var response = new MetricsResponse(
                new Dictionary<string, double>
                {
                    ["database.count"] = stats.DatabaseCount,
                    ["catalog.count"] = stats.CatalogCount,
                    ["catalog.items.total"] = stats.CatalogItemCount,
                    ["documents.total"] = stats.DocumentCount,
                    ["server.uptime.seconds"] = Math.Max(0, uptime.TotalSeconds),
                    ["server.running"] = this.IsRunning ? 1 : 0,
                    ["selfcheck.healthy"] = selfCheck.IsHealthy ? 1 : 0,
                    ["selfcheck.issue.count"] = selfCheck.IssueCount,
                    ["maintenance.zonetree.failures"] = maintenance.ZoneTreeFailures,
                    ["maintenance.rocksdb.failures"] = maintenance.RocksDbFailures,
                    ["maintenance.fastdb.failures"] = maintenance.FastDbFailures,
                    ["maintenance.failures.total"] = maintenance.ZoneTreeFailures + maintenance.RocksDbFailures + maintenance.FastDbFailures,
                    ["transport.rate_limited.total"] = transport.RateLimitedRequests,
                    ["transport.rejected_connections.total"] = transport.RejectedConnections,
                    ["transport.read_timeouts.total"] = transport.ReadTimeouts,
                    ["transport.invalid_frames.total"] = transport.InvalidFrames,
                    ["transport.invalid_requests.total"] = transport.InvalidRequests,
                    ["transport.dispatch_errors.total"] = transport.DispatchErrors,
                    ["transport.protocol_disconnects.total"] = transport.ProtocolDisconnects,
                    ["transport.abuse.total"] = transport.RateLimitedRequests + transport.RejectedConnections + transport.ReadTimeouts + transport.InvalidFrames + transport.InvalidRequests + transport.DispatchErrors + transport.ProtocolDisconnects,
                    ["cluster.enabled"] = cluster.Enabled ? 1 : 0,
                    ["cluster.running"] = cluster.Running ? 1 : 0,
                    ["cluster.members.count"] = cluster.MemberCount
                });

            return Task.FromResult(ResponseEnvelope.Success(request.RequestId, response));
        }

        private Task<ResponseEnvelope> HandleSelfCheckAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            _ = MessagePackSerializer.Deserialize<SelfCheckRequest>(request.Payload);
            var response = this.storage.RunSelfCheck();
            return Task.FromResult(ResponseEnvelope.Success(request.RequestId, response));
        }

        private Task<ResponseEnvelope> HandleMaintenanceDiagnosticsAsync(RequestEnvelope request, CancellationToken cancellationToken)
        {
            _ = MessagePackSerializer.Deserialize<MaintenanceDiagnosticsRequest>(request.Payload);
            var snapshot = KvMaintenanceDiagnostics.Snapshot();
            var transportSnapshot = TransportAbuseDiagnostics.Snapshot();
            var clusterSnapshot = ClusterRuntimeDiagnostics.Snapshot();
            var response = new MaintenanceDiagnosticsResponse(
                snapshot.ZoneTreeSuccesses,
                snapshot.ZoneTreeFailures,
                snapshot.RocksDbSuccesses,
                snapshot.RocksDbFailures,
                snapshot.FastDbSuccesses,
                snapshot.FastDbFailures,
                transportSnapshot.RateLimitedRequests,
                transportSnapshot.RejectedConnections,
                transportSnapshot.ReadTimeouts,
                transportSnapshot.InvalidFrames,
                transportSnapshot.InvalidRequests,
                transportSnapshot.DispatchErrors,
                transportSnapshot.ProtocolDisconnects,
                clusterSnapshot.Enabled,
                clusterSnapshot.Running,
                clusterSnapshot.SystemName,
                clusterSnapshot.Hostname,
                clusterSnapshot.Port,
                clusterSnapshot.MemberCount);

            return Task.FromResult(ResponseEnvelope.Success(request.RequestId, response));
        }
    }
}
