namespace WebNet.CatalogServer.Tests;

using Xunit;

public sealed class WindowsTokenAuthorizerTests
{
    [Fact]
    public void Authorize_AdminRole_AllowsWriteCommand()
    {
        var options = NewOptions();
        var authorizer = new WindowsTokenAuthorizer(options);
        var security = new SecurityContext("unused", "thumb", "CORP\\alice", ["admin"]);

        Assert.True(authorizer.Authorize(security, CommandKind.PutDocument));
    }

    [Fact]
    public void Authorize_ReaderRole_DeniesWriteCommand()
    {
        var options = NewOptions();
        var authorizer = new WindowsTokenAuthorizer(options);
        var security = new SecurityContext("unused", "thumb", "CORP\\bob", ["reader"]);

        Assert.True(authorizer.Authorize(security, CommandKind.GetDocument));
        Assert.False(authorizer.Authorize(security, CommandKind.DeleteDocument));
    }

    [Fact]
    public void Authorize_SubjectNotInAllowList_DeniesAccess()
    {
        var options = NewOptions(allowedSubjects: ["CORP\\alice"]);
        var authorizer = new WindowsTokenAuthorizer(options);
        var security = new SecurityContext("unused", "thumb", "CORP\\bob", ["admin"]);

        Assert.False(authorizer.Authorize(security, CommandKind.Health));
    }

    [Fact]
    public void Authorize_MissingSubject_DeniesAccess()
    {
        var options = NewOptions();
        var authorizer = new WindowsTokenAuthorizer(options);
        var security = new SecurityContext("unused", "thumb", null, ["admin"]);

        Assert.False(authorizer.Authorize(security, CommandKind.Health));
    }

    private static LiteGraphAuthOptions NewOptions(IReadOnlyCollection<string>? allowedSubjects = null)
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
            Provider: AuthProvider.Windows,
            DatabaseFilePath: Path.Combine(Path.GetTempPath(), "unused", "auth.db"),
            BootstrapCredentialEnabled: false,
            BootstrapCredentialName: "admin",
            BootstrapBearerToken: "unused",
            AllowedWindowsSubjects: allowedSubjects ?? [],
            AllowedCertificateThumbprints: ["dev-thumbprint"],
            CommandRolePolicy: policy);
    }
}
