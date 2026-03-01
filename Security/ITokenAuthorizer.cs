namespace WebNet.CatalogServer;

public interface ITokenAuthorizer
{
    bool Authorize(SecurityContext securityContext, CommandKind command);
}
