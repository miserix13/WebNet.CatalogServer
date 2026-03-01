namespace WebNet.CatalogServer;

public interface IClientCertificateValidator
{
    bool Validate(string thumbprint);
}
