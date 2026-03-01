namespace WebNet.CatalogServer;

public sealed class AllowAllTokenAuthorizer : ITokenAuthorizer
{
    public bool Authorize(string token, CommandKind command) => true;
}
