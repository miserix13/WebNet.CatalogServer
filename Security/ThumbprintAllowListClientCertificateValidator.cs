namespace WebNet.CatalogServer;

public sealed class ThumbprintAllowListClientCertificateValidator : IClientCertificateValidator
{
    private readonly HashSet<string> allowlist;

    public ThumbprintAllowListClientCertificateValidator(IEnumerable<string> allowedThumbprints)
    {
        this.allowlist = new HashSet<string>(
            allowedThumbprints
                .Select(Normalize)
                .Where(value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.Ordinal);
    }

    public bool Validate(string thumbprint)
    {
        if (this.allowlist.Count == 0)
        {
            return false;
        }

        var normalized = Normalize(thumbprint);
        return this.allowlist.Contains(normalized);
    }

    public static string Normalize(string? thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return string.Empty;
        }

        var chars = thumbprint
            .Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();

        return new string(chars);
    }
}
