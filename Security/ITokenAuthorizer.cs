namespace WebNet.CatalogServer;

public interface ITokenAuthorizer
{
    bool Authorize(string token, CommandKind command);
}
