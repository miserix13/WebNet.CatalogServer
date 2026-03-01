namespace WebNet.CatalogServer.Tests;

using Xunit;

public sealed class LiteGraphAuthOptionsTests
{
    [Fact]
    public void Resolve_InvalidCommandName_ThrowsConfigurationException()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        const string strictKey = "WEBNET_AUTH_REQUIRE_FULL_COMMAND_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        var originalStrict = Environment.GetEnvironmentVariable(strictKey);
        Environment.SetEnvironmentVariable(key, "NotACommand=admin");
        Environment.SetEnvironmentVariable(strictKey, null);

        try
        {
            var exception = Assert.Throws<LiteGraphAuthOptions.AuthConfigurationException>(() => LiteGraphAuthOptions.Resolve());
            Assert.Contains("Unknown command", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
            Environment.SetEnvironmentVariable(strictKey, originalStrict);
        }
    }

    [Fact]
    public void Resolve_InvalidEntryFormat_ThrowsConfigurationException()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        const string strictKey = "WEBNET_AUTH_REQUIRE_FULL_COMMAND_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        var originalStrict = Environment.GetEnvironmentVariable(strictKey);
        Environment.SetEnvironmentVariable(key, "GetDocument-admin");
        Environment.SetEnvironmentVariable(strictKey, null);

        try
        {
            var exception = Assert.Throws<LiteGraphAuthOptions.AuthConfigurationException>(() => LiteGraphAuthOptions.Resolve());
            Assert.Contains("Expected format", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
            Environment.SetEnvironmentVariable(strictKey, originalStrict);
        }
    }

    [Fact]
    public void Resolve_EmptyRoleList_ThrowsConfigurationException()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        const string strictKey = "WEBNET_AUTH_REQUIRE_FULL_COMMAND_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        var originalStrict = Environment.GetEnvironmentVariable(strictKey);
        Environment.SetEnvironmentVariable(key, "GetDocument=|");
        Environment.SetEnvironmentVariable(strictKey, null);

        try
        {
            var exception = Assert.Throws<LiteGraphAuthOptions.AuthConfigurationException>(() => LiteGraphAuthOptions.Resolve());
            Assert.Contains("at least one role", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
            Environment.SetEnvironmentVariable(strictKey, originalStrict);
        }
    }

    [Fact]
    public void Resolve_ValidOverride_UpdatesPolicy()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        const string strictKey = "WEBNET_AUTH_REQUIRE_FULL_COMMAND_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        var originalStrict = Environment.GetEnvironmentVariable(strictKey);
        Environment.SetEnvironmentVariable(key, "GetDocument=admin,reader;PutDocument=admin,writer");
        Environment.SetEnvironmentVariable(strictKey, null);

        try
        {
            var options = LiteGraphAuthOptions.Resolve();
            Assert.Contains("reader", options.CommandRolePolicy[CommandKind.GetDocument]);
            Assert.Contains("writer", options.CommandRolePolicy[CommandKind.PutDocument]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
            Environment.SetEnvironmentVariable(strictKey, originalStrict);
        }
    }

    [Fact]
    public void Resolve_StrictModeMissingCommands_ThrowsConfigurationException()
    {
        const string key = "WEBNET_AUTH_COMMAND_ROLE_POLICY";
        const string strictKey = "WEBNET_AUTH_REQUIRE_FULL_COMMAND_POLICY";
        var original = Environment.GetEnvironmentVariable(key);
        var originalStrict = Environment.GetEnvironmentVariable(strictKey);
        Environment.SetEnvironmentVariable(key, "GetDocument=admin,reader;PutDocument=admin,writer");
        Environment.SetEnvironmentVariable(strictKey, "true");

        try
        {
            var exception = Assert.Throws<LiteGraphAuthOptions.AuthConfigurationException>(() => LiteGraphAuthOptions.Resolve());
            Assert.Contains("requires WEBNET_AUTH_COMMAND_ROLE_POLICY to define all commands", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, original);
            Environment.SetEnvironmentVariable(strictKey, originalStrict);
        }
    }
}
