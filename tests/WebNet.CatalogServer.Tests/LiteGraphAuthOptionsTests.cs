namespace WebNet.CatalogServer.Tests;

using Xunit;

public sealed class LiteGraphAuthOptionsTests
{
    [Fact]
    public void Resolve_InvalidCommandName_ThrowsConfigurationException()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "NotACommand=admin");

        try
        {
            var exception = Assert.Throws<LiteGraphAuthOptions.AuthConfigurationException>(() => LiteGraphAuthOptions.Resolve());
            Assert.Contains("Unknown command", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [Fact]
    public void Resolve_InvalidEntryFormat_ThrowsConfigurationException()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "GetDocument-admin");

        try
        {
            var exception = Assert.Throws<LiteGraphAuthOptions.AuthConfigurationException>(() => LiteGraphAuthOptions.Resolve());
            Assert.Contains("Expected format", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [Fact]
    public void Resolve_EmptyRoleList_ThrowsConfigurationException()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "GetDocument=|");

        try
        {
            var exception = Assert.Throws<LiteGraphAuthOptions.AuthConfigurationException>(() => LiteGraphAuthOptions.Resolve());
            Assert.Contains("at least one role", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }

    [Fact]
    public void Resolve_ValidOverride_UpdatesPolicy()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, "GetDocument=admin,reader;PutDocument=admin,writer");

        try
        {
            var options = LiteGraphAuthOptions.Resolve();
            Assert.Contains("reader", options.CommandRolePolicy[CommandKind.GetDocument]);
            Assert.Contains("writer", options.CommandRolePolicy[CommandKind.PutDocument]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
        }
    }
}
