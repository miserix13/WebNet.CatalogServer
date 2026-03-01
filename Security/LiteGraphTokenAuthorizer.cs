using LiteGraph;
using LiteGraph.GraphRepositories.Sqlite;

namespace WebNet.CatalogServer;

public sealed class LiteGraphTokenAuthorizer : ITokenAuthorizer, IDisposable
{
    private readonly SqliteGraphRepository repository;
    private readonly IReadOnlyDictionary<CommandKind, HashSet<string>> commandRolePolicy;

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
        this.commandRolePolicy = options.CommandRolePolicy
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Select(role => role.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal));

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

        return IsCommandAllowed(credential.Name, command, this.commandRolePolicy);
    }

    public void Dispose()
    {
        if (this.repository is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static bool IsCommandAllowed(
        string? credentialName,
        CommandKind command,
        IReadOnlyDictionary<CommandKind, HashSet<string>> commandRolePolicy)
    {
        var roles = ParseRoles(credentialName);
        if (roles.Count == 0)
        {
            return false;
        }

        if (!commandRolePolicy.TryGetValue(command, out var allowedRoles) || allowedRoles.Count == 0)
        {
            return false;
        }

        return roles.Any(role => allowedRoles.Contains(role));
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
