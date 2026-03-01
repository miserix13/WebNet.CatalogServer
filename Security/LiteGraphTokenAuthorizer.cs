using LiteGraph;
using LiteGraph.GraphRepositories.Sqlite;

namespace WebNet.CatalogServer;

public sealed class LiteGraphTokenAuthorizer : ITokenAuthorizer, IDisposable
{
    private static readonly HashSet<CommandKind> ReadCommands =
    [
        CommandKind.GetDocument,
        CommandKind.ListDatabases,
        CommandKind.ListCatalogs,
        CommandKind.Health,
        CommandKind.Metrics,
        CommandKind.SelfCheck,
        CommandKind.MaintenanceDiagnostics
    ];

    private static readonly HashSet<CommandKind> WriteCommands =
    [
        CommandKind.CreateDatabase,
        CommandKind.DropDatabase,
        CommandKind.CreateCatalog,
        CommandKind.DropCatalog,
        CommandKind.PutDocument,
        CommandKind.DeleteDocument
    ];

    private readonly SqliteGraphRepository repository;

    public LiteGraphTokenAuthorizer(LiteGraphAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var directory = Path.GetDirectoryName(options.DatabaseFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        this.repository = new SqliteGraphRepository(options.DatabaseFilePath, true);
        this.repository.InitializeRepository();

        if (options.BootstrapCredentialEnabled)
        {
            this.EnsureBootstrapCredential(options);
        }
    }

    public bool Authorize(string token, CommandKind command)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        Credential? credential;
        try
        {
            credential = this.repository.Credential
                .ReadByBearerToken(token, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return false;
        }

        if (credential is null || !credential.Active)
        {
            return false;
        }

        UserMaster? user;
        try
        {
            user = this.repository.User
                .ReadByGuid(credential.TenantGUID, credential.UserGUID, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            return false;
        }

        if (user is null || !user.Active)
        {
            return false;
        }

        return IsCommandAllowed(credential.Name, command);
    }

    public void Dispose()
    {
        if (this.repository is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static bool IsCommandAllowed(string? credentialName, CommandKind command)
    {
        var roles = ParseRoles(credentialName);
        if (roles.Count == 0)
        {
            return false;
        }

        if (roles.Contains("admin", StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (roles.Contains(command.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var allowRead = roles.Contains("reader", StringComparer.OrdinalIgnoreCase)
            || roles.Contains("read", StringComparer.OrdinalIgnoreCase);

        var allowWrite = roles.Contains("writer", StringComparer.OrdinalIgnoreCase)
            || roles.Contains("write", StringComparer.OrdinalIgnoreCase);

        if (allowRead && ReadCommands.Contains(command))
        {
            return true;
        }

        if (allowWrite && (WriteCommands.Contains(command) || ReadCommands.Contains(command)))
        {
            return true;
        }

        return false;
    }

    private static HashSet<string> ParseRoles(string? credentialName)
    {
        if (string.IsNullOrWhiteSpace(credentialName))
        {
            return [];
        }

        return credentialName
            .Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureBootstrapCredential(LiteGraphAuthOptions options)
    {
        var existing = this.repository.Credential
            .ReadByBearerToken(options.BootstrapBearerToken, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (existing is not null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var tenant = this.repository.Tenant.Create(new TenantMetadata
        {
            GUID = Guid.NewGuid(),
            Name = "default",
            Active = true,
            CreatedUtc = now,
            LastUpdateUtc = now
        }, CancellationToken.None).GetAwaiter().GetResult();

        var user = this.repository.User.Create(new UserMaster
        {
            GUID = Guid.NewGuid(),
            TenantGUID = tenant.GUID,
            FirstName = "Bootstrap",
            LastName = "User",
            Email = "bootstrap@webnet.local",
            Password = "not-used",
            Active = true,
            CreatedUtc = now,
            LastUpdateUtc = now
        }, CancellationToken.None).GetAwaiter().GetResult();

        _ = this.repository.Credential.Create(new Credential
        {
            GUID = Guid.NewGuid(),
            TenantGUID = tenant.GUID,
            UserGUID = user.GUID,
            Name = options.BootstrapCredentialName,
            BearerToken = options.BootstrapBearerToken,
            Active = true,
            CreatedUtc = now,
            LastUpdateUtc = now
        }, CancellationToken.None).GetAwaiter().GetResult();
    }
}
