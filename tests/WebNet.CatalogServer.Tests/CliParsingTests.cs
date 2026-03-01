namespace WebNet.CatalogServer.Tests;

using Xunit;

public class CliParsingTests
{
    [Fact]
    public void TryParseOptions_ServerDefaults_ParsesSuccessfully()
    {
        var result = Program.TryParseOptions([], out var options, out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
        Assert.Equal("server", options.Mode);
        Assert.Equal(7070, options.Port);
        Assert.False(options.FailOnSelfCheck);
        Assert.False(options.SelfCheckOnly);
        Assert.Null(options.DataRoot);
    }

    [Fact]
    public void TryParseOptions_ServerWithFlagsAndPort_ParsesSuccessfully()
    {
        var result = Program.TryParseOptions(["server", "7071", "--fail-on-self-check", "--self-check-only"], out var options, out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
        Assert.Equal("server", options.Mode);
        Assert.Equal(7071, options.Port);
        Assert.True(options.FailOnSelfCheck);
        Assert.True(options.SelfCheckOnly);
        Assert.Null(options.DataRoot);
    }

    [Fact]
    public void TryParseOptions_ClientDefaults_ParsesSuccessfully()
    {
        var result = Program.TryParseOptions(["client"], out var options, out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
        Assert.Equal("client", options.Mode);
        Assert.Equal("127.0.0.1", options.HostName);
        Assert.Equal(7070, options.Port);
        Assert.Null(options.DataRoot);
    }

    [Fact]
    public void TryParseOptions_UnknownFlag_Fails()
    {
        var result = Program.TryParseOptions(["server", "--bad-flag"], out _, out var error);

        Assert.False(result);
        Assert.Contains("Unknown flag", error);
    }

    [Fact]
    public void TryParseOptions_ServerFlagInClientMode_Fails()
    {
        var result = Program.TryParseOptions(["client", "--self-check-only"], out _, out var error);

        Assert.False(result);
        Assert.Contains("only valid in server mode", error);
    }

    [Fact]
    public void TryParseOptions_InvalidPort_Fails()
    {
        var result = Program.TryParseOptions(["server", "70000"], out _, out var error);

        Assert.False(result);
        Assert.Contains("Invalid server port", error);
    }

    [Fact]
    public void TryParseOptions_ServerDataRootWithEquals_ParsesSuccessfully()
    {
        var result = Program.TryParseOptions(["server", "--data-root=C:\\catalog-data"], out var options, out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
        Assert.Equal("C:\\catalog-data", options.DataRoot);
    }

    [Fact]
    public void TryParseOptions_ServerDataRootWithSeparatedValue_ParsesSuccessfully()
    {
        var result = Program.TryParseOptions(["server", "--data-root", "C:\\catalog-data"], out var options, out var error);

        Assert.True(result);
        Assert.Equal(string.Empty, error);
        Assert.Equal("C:\\catalog-data", options.DataRoot);
    }

    [Fact]
    public void TryParseOptions_ServerDataRootWithoutValue_Fails()
    {
        var result = Program.TryParseOptions(["server", "--data-root"], out _, out var error);

        Assert.False(result);
        Assert.Contains("requires a path value", error);
    }

    [Fact]
    public void TryParseOptions_ClientDataRoot_Fails()
    {
        var result = Program.TryParseOptions(["client", "--data-root", "C:\\catalog-data"], out _, out var error);

        Assert.False(result);
        Assert.Contains("only valid in server mode", error);
    }
}
