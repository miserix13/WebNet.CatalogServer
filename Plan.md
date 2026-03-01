# Next Milestones

M1: Durable storage wiring — replace in-memory maps in Storage.cs with a persistence adapter (ZoneTree/RocksDB/FastDB), add startup recovery, and  preserve SelfCheck behavior.
M2: Real authn/authz — replace allow-all validators in AllowAllTokenAuthorizer.cs and AllowAllClientCertificateValidator.cs with token scope checks + certificate trust chain/thumbprint policy.
M3: Protocol hardening — add request size/rate limits, command-level authorization rules, and stricter error mapping in Server.cs and TcpServerHost.cs.
M4: Production readiness — structured logging, startup/shutdown lifecycle checks, and expanded health/metrics surface in Program.cs and ManagementResponses.cs.
M5: Test coverage expansion — add integration tests for TCP command flow, auth failures, and storage recovery under WebNet.CatalogServer.Tests.
M6: Clustering support through Akka.Net.

Definition of Done (per milestone)

Green tests, no CLI regressions in CliParsingTests.cs, and README run instructions updated in README.md
