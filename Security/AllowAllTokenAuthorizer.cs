namespace WebNet.CatalogServer;

public sealed class AllowAllTokenAuthorizer : ITokenAuthorizer
{
    public bool Authorize(SecurityContext securityContext, CommandKind command) => true;
}
