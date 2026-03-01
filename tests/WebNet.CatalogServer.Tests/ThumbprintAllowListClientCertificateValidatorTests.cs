namespace WebNet.CatalogServer.Tests;

using Xunit;

public sealed class ThumbprintAllowListClientCertificateValidatorTests
{
    [Fact]
    public void Validate_UsesNormalizedThumbprints()
    {
        var validator = new ThumbprintAllowListClientCertificateValidator(["aa-bb-11"]);

        Assert.True(validator.Validate("AA:BB:11"));
        Assert.True(validator.Validate("aabb11"));
        Assert.False(validator.Validate("CCDD22"));
    }

    [Fact]
    public void Validate_WithEmptyAllowlist_DeniesAll()
    {
        var validator = new ThumbprintAllowListClientCertificateValidator([]);

        Assert.False(validator.Validate("dev-thumbprint"));
    }
}
