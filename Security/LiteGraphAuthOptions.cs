namespace WebNet.CatalogServer;

public sealed record LiteGraphAuthOptions(
    string DatabaseFilePath,
    bool BootstrapCredentialEnabled,
    string BootstrapCredentialName,
    string BootstrapBearerToken,
    IReadOnlyCollection<string> AllowedCertificateThumbprints)
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

        return new LiteGraphAuthOptions(
            Path.GetFullPath(authDbPath),
            bootstrapEnabled,
            bootstrapCredentialName,
            bootstrapBearerToken,
            thumbprints);
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
