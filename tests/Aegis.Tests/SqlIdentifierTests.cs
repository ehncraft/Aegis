using Aegis.Sql;

using Xunit;

namespace Aegis.Tests;

public class SqlIdentifierTests
{
    [Theory]
    [InlineData("Users", "[Users]")]
    [InlineData("UserId", "[UserId]")]
    [InlineData("_leadingUnderscore", "[_leadingUnderscore]")]
    [InlineData("Column1", "[Column1]")]
    public void Quote_SafeIdentifier_BracketsIt(string identifier, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.Quote(identifier));
    }

    [Theory]
    [InlineData("Users; DROP TABLE Users;--")]
    [InlineData("Users]; DROP TABLE Users;--")]
    [InlineData("Users -- comment")]
    [InlineData("Users'")]
    [InlineData("Users Table")]
    [InlineData("1Users")]
    [InlineData("")]
    public void Quote_UnsafeIdentifier_ThrowsArgumentException(string identifier)
    {
        Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(identifier));
    }

    [Fact]
    public void Quote_WithSchema_BracketsBothAndJoinsWithDot()
    {
        Assert.Equal("[tenant_123].[CedarPolicies]", SqlIdentifier.Quote("tenant_123", "CedarPolicies"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Quote_NullOrEmptySchema_ReturnsUnqualifiedTable(string? schema)
    {
        Assert.Equal("[CedarPolicies]", SqlIdentifier.Quote(schema, "CedarPolicies"));
    }

    [Fact]
    public void Quote_UnsafeSchema_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote("tenant; DROP TABLE Users;--", "CedarPolicies"));
    }
}