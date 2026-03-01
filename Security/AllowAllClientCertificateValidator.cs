namespace WebNet.CatalogServer;

public sealed class AllowAllClientCertificateValidator : IClientCertificateValidator
{
    public bool Validate(string thumbprint) => true;
}
