namespace WebNet.CatalogServer.Tests;

using LiteGraph.GraphRepositories.Sqlite;
using Xunit;

public sealed class LiteGraphTokenAuthorizerTests
{
    [Fact]
    public void Authorize_BootstrapAdmin_AllowsAllCommands()
    {
        var path = NewAuthDbPath();
        var options = NewOptions(path, "admin", "token-admin");

        using var authorizer = new LiteGraphTokenAuthorizer(options);
        var security = new SecurityContext("token-admin", "dev-thumbprint", "dev-user", ["admin"]);

        Assert.True(authorizer.Authorize(security, CommandKind.CreateDatabase));
        Assert.True(authorizer.Authorize(security, CommandKind.MaintenanceDiagnostics));
    }

    [Fact]
    public void Authorize_ReaderRole_DeniesWriteCommands()
    {
        var path = NewAuthDbPath();
        var options = NewOptions(path, "reader", "token-reader");

        using var authorizer = new LiteGraphTokenAuthorizer(options);
        var security = new SecurityContext("token-reader", "dev-thumbprint", "dev-user", ["reader"]);

        Assert.True(authorizer.Authorize(security, CommandKind.GetDocument));
        Assert.True(authorizer.Authorize(security, CommandKind.SelfCheck));
        Assert.False(authorizer.Authorize(security, CommandKind.PutDocument));
        Assert.False(authorizer.Authorize(security, CommandKind.CreateCatalog));
    }

    [Fact]
    public async Task Authorize_InactiveCredential_DeniesAccess()
    {
        var path = NewAuthDbPath();
        var options = NewOptions(path, "admin", "token-inactive");
        var security = new SecurityContext("token-inactive", "dev-thumbprint", "dev-user", ["admin"]);

        using (var authorizer = new LiteGraphTokenAuthorizer(options))
        {
            Assert.True(authorizer.Authorize(security, CommandKind.Health));
        }

        var repository = new SqliteGraphRepository(path, true);
        repository.InitializeRepository();
        var credential = await repository.Credential.ReadByBearerToken("token-inactive", CancellationToken.None);
        Assert.NotNull(credential);
        credential!.Active = false;
        credential.LastUpdateUtc = DateTime.UtcNow;
        _ = await repository.Credential.Update(credential, CancellationToken.None);

        using var reloaded = new LiteGraphTokenAuthorizer(options);
        Assert.False(reloaded.Authorize(security, CommandKind.Health));
    }

    [Fact]
    public void Authorize_CustomCommandPolicy_EnforcesOverrides()
    {
        var path = NewAuthDbPath();
        var customPolicy = new Dictionary<CommandKind, IReadOnlyCollection<string>>
        {
            [CommandKind.GetDocument] = ["admin"],
            [CommandKind.Health] = ["reader", "admin"]
        };

        var options = new LiteGraphAuthOptions(
            Provider: AuthProvider.LiteGraph,
            path,
            BootstrapCredentialEnabled: true,
            BootstrapCredentialName: "reader",
            BootstrapBearerToken: "token-custom",
            AllowedWindowsSubjects: [],
            AllowedCertificateThumbprints: ["dev-thumbprint"],
            CommandRolePolicy: customPolicy);

        using var authorizer = new LiteGraphTokenAuthorizer(options);
        var security = new SecurityContext("token-custom", "dev-thumbprint", "dev-user", ["reader"]);

        Assert.False(authorizer.Authorize(security, CommandKind.GetDocument));
        Assert.True(authorizer.Authorize(security, CommandKind.Health));
    }

    private static string NewAuthDbPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "auth", "litegraph-auth.db");
    }

    private static LiteGraphAuthOptions NewOptions(string dbPath, string credentialName, string token)
    {
        var policy = new Dictionary<CommandKind, IReadOnlyCollection<string>>
        {
            [CommandKind.CreateDatabase] = ["admin", "writer"],
            [CommandKind.DropDatabase] = ["admin", "writer"],
            [CommandKind.ListDatabases] = ["admin", "writer", "reader"],
            [CommandKind.CreateCatalog] = ["admin", "writer"],
            [CommandKind.DropCatalog] = ["admin", "writer"],
            [CommandKind.ListCatalogs] = ["admin", "writer", "reader"],
            [CommandKind.PutDocument] = ["admin", "writer"],
            [CommandKind.GetDocument] = ["admin", "writer", "reader"],
            [CommandKind.DeleteDocument] = ["admin", "writer"],
            [CommandKind.Health] = ["admin", "writer", "reader"],
            [CommandKind.Metrics] = ["admin", "writer", "reader"],
            [CommandKind.SelfCheck] = ["admin", "writer", "reader"],
            [CommandKind.MaintenanceDiagnostics] = ["admin", "writer", "reader"]
        };

        return new LiteGraphAuthOptions(
            Provider: AuthProvider.LiteGraph,
            dbPath,
            BootstrapCredentialEnabled: true,
            BootstrapCredentialName: credentialName,
            BootstrapBearerToken: token,
            AllowedWindowsSubjects: [],
            AllowedCertificateThumbprints: ["dev-thumbprint"],
            CommandRolePolicy: policy);
    }
}
