Plan: CatalogClient v1 Design (DRAFT)
Design a new in-repo .NET client project focused on typed operations only, targeting net10.0, with pluggable auth delegates per request. The client will mirror the existing TCP + MessagePack wire contract exactly, preserve server error semantics, and add a small, explicit transport/configuration layer for reconnect, timeouts, and frame limits. This minimizes protocol drift while keeping enough extension points for future generic APIs or multi-targeting.

Steps

Define public contract and package boundaries in WebNet.CatalogServer.slnx, adding a new client project (for example src/WebNet.CatalogClient/WebNet.CatalogClient.csproj) targeting net10.0 with dependencies aligned to WebNet.CatalogServer.csproj.
Model protocol-facing DTO compatibility by reusing/mirroring request-response shapes from ManagementRequests.cs, ManagementResponses.cs, RequestEnvelope.cs, and ResponseEnvelope.cs, with a strict mapping to CommandKind from CommandKind.cs.
Design transport core interfaces/classes around frame and wire contracts in TcpFrameCodec.cs and WireContracts.cs: connection lifecycle, request correlation (RequestId), read/write timeouts, max-frame guardrails, and optional reconnect policy.
Define typed client surface (CatalogClient) with one method per command (CreateDatabaseAsync, PutDocumentAsync, HealthAsync, etc.) matching server dispatch expectations in Server.cs and behavior in Storage.cs; keep no generic send API in v1.
Define auth delegate model (AuthContextProvider) matching WireRequest fields (Token, ClientCertificateThumbprint, Subject, Roles) from WireContracts.cs, and document trust/security assumptions from ThumbprintAllowListClientCertificateValidator.cs.
Standardize client error taxonomy from server/host responses: envelope business errors plus transport errors (transport.rate_limited, transport.read_timeout, etc.) observed in TcpServerHost.cs, including handling for Guid.Empty request IDs.
Add client-focused tests in a new test project added to the solution, using patterns from TcpIntegrationTests.cs and TransportHardeningTests.cs to validate wire compatibility, auth propagation, error mapping, and reconnect/rate-limit behavior.
Document consumer usage and operational constraints in README.md: connection config, auth delegate examples, supported commands, retry expectations, and protocol compatibility notes.
Verification

Run targeted tests for protocol compatibility and typed operations in the new client test project.
Run existing server integration tests to confirm no wire contract regressions.
Manual smoke: start server, execute typed client flow (CreateDatabase → CreateCatalog → PutDocument → GetDocument → Health), then trigger an auth failure and a rate-limit scenario to validate error handling.
Decisions

Target framework: net10.0 only (v1).
API style: typed operations only.
Location: new project in the same solution.
Auth: pluggable per-request delegate model.
If this draft matches what you want, I can refine it into an execution-ready checklist with proposed namespaces/types and test case matrix.