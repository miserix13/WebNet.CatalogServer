namespace WebNet.CatalogServer.Tests;

using LiteGraph.GraphRepositories.Sqlite;
using Xunit;

public sealed class LiteGraphTokenAuthorizerTests
{
    [Fact]
    public void Authorize_BootstrapAdmin_AllowsAllCommands()
    {
        var path = NewAuthDbPath();
        var options = new LiteGraphAuthOptions(path, true, "admin", "token-admin", ["dev-thumbprint"]);

        using var authorizer = new LiteGraphTokenAuthorizer(options);

        Assert.True(authorizer.Authorize("token-admin", CommandKind.CreateDatabase));
        Assert.True(authorizer.Authorize("token-admin", CommandKind.MaintenanceDiagnostics));
    }

    [Fact]
    public void Authorize_ReaderRole_DeniesWriteCommands()
    {
        var path = NewAuthDbPath();
        var options = new LiteGraphAuthOptions(path, true, "reader", "token-reader", ["dev-thumbprint"]);

        using var authorizer = new LiteGraphTokenAuthorizer(options);

        Assert.True(authorizer.Authorize("token-reader", CommandKind.GetDocument));
        Assert.True(authorizer.Authorize("token-reader", CommandKind.SelfCheck));
        Assert.False(authorizer.Authorize("token-reader", CommandKind.PutDocument));
        Assert.False(authorizer.Authorize("token-reader", CommandKind.CreateCatalog));
    }

    [Fact]
    public async Task Authorize_InactiveCredential_DeniesAccess()
    {
        var path = NewAuthDbPath();
        var options = new LiteGraphAuthOptions(path, true, "admin", "token-inactive", ["dev-thumbprint"]);

        using (var authorizer = new LiteGraphTokenAuthorizer(options))
        {
            Assert.True(authorizer.Authorize("token-inactive", CommandKind.Health));
        }

        var repository = new SqliteGraphRepository(path, true);
        repository.InitializeRepository();
        var credential = await repository.Credential.ReadByBearerToken("token-inactive", CancellationToken.None);
        Assert.NotNull(credential);
        credential!.Active = false;
        credential.LastUpdateUtc = DateTime.UtcNow;
        _ = await repository.Credential.Update(credential, CancellationToken.None);

        using var reloaded = new LiteGraphTokenAuthorizer(options);
        Assert.False(reloaded.Authorize("token-inactive", CommandKind.Health));
    }

    private static string NewAuthDbPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "WebNet.CatalogServer.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "auth", "litegraph-auth.db");
    }
}
