namespace WebNet.CatalogServer;

public sealed record LiteGraphAuthOptions(
    string DatabaseFilePath,
    bool BootstrapCredentialEnabled,
    string BootstrapCredentialName,
    string BootstrapBearerToken,
    IReadOnlyCollection<string> AllowedCertificateThumbprints,
    IReadOnlyDictionary<CommandKind, IReadOnlyCollection<string>> CommandRolePolicy)
{
    public static LiteGraphAuthOptions Resolve(string? dataRoot = null)
    {
        var root = string.IsNullOrWhiteSpace(dataRoot)
            ? StorageDirectoryLayout.Resolve().DataRoot
            : StorageDirectoryLayout.Resolve(dataRoot).DataRoot;

        var authDbPath = Environment.GetEnvironmentVariable("WEBNET_AUTH_DB_PATH");
        if (string.IsNullOrWhiteSpace(authDbPath))
        {
            authDbPath = Path.Combine(root, "auth", "litegraph-auth.db");
        }

        var bootstrapEnabled = ParseBoolEnvironment("WEBNET_AUTH_BOOTSTRAP_ENABLED", true);
        var bootstrapCredentialName = ReadEnvironmentOrDefault("WEBNET_AUTH_BOOTSTRAP_CREDENTIAL_NAME", "admin");
        var bootstrapBearerToken = ReadEnvironmentOrDefault("WEBNET_AUTH_BOOTSTRAP_BEARER_TOKEN", "dev-token");
        var allowlistRaw = ReadEnvironmentOrDefault("WEBNET_ALLOWED_CERT_THUMBPRINTS", "dev-thumbprint");

        var thumbprints = allowlistRaw
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ThumbprintAllowListClientCertificateValidator.Normalize)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var commandRolePolicy = ResolveCommandRolePolicy();

        return new LiteGraphAuthOptions(
            Path.GetFullPath(authDbPath),
            bootstrapEnabled,
            bootstrapCredentialName,
            bootstrapBearerToken,
            thumbprints,
            commandRolePolicy);
    }

    private static IReadOnlyDictionary<CommandKind, IReadOnlyCollection<string>> ResolveCommandRolePolicy()
    {
        var defaults = BuildDefaultCommandRolePolicy();
        var overrideRaw = Environment.GetEnvironmentVariable("WEBNET_AUTH_COMMAND_ROLE_POLICY");
        if (string.IsNullOrWhiteSpace(overrideRaw))
        {
            return defaults;
        }

        var policy = defaults.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, EqualityComparer<CommandKind>.Default);
        var entries = overrideRaw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!Enum.TryParse<CommandKind>(parts[0], ignoreCase: true, out var command))
            {
                continue;
            }

            var roles = parts[1]
                .Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => role.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (roles.Length > 0)
            {
                policy[command] = roles;
            }
        }

        return policy;
    }

    private static IReadOnlyDictionary<CommandKind, IReadOnlyCollection<string>> BuildDefaultCommandRolePolicy()
    {
        return new Dictionary<CommandKind, IReadOnlyCollection<string>>
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
    }

    private static string ReadEnvironmentOrDefault(string key, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    private static bool ParseBoolEnvironment(string key, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value.Trim(), out var parsed) ? parsed : defaultValue;
    }
}
