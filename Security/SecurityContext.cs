namespace WebNet.CatalogServer;

public sealed record SecurityContext(
    string Token,
    string ClientCertificateThumbprint,
    string? Subject = null,
    IReadOnlyCollection<string>? Roles = null);
