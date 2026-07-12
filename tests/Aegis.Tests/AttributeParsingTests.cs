using Aegis.Cli;

using Xunit;

namespace Aegis.Tests;

public class AttributeParsingTests
{
    [Fact]
    public void Parse_PlainText_KeepsAsString()
    {
        var result = AttributeParsing.Parse(["department=finance"]);

        Assert.Equal("finance", result["department"]);
    }

    [Fact]
    public void Parse_IntegerLookingValue_InfersLong()
    {
        var result = AttributeParsing.Parse(["approvalLimit=500000"]);

        Assert.Equal(500_000L, result["approvalLimit"]);
    }

    [Fact]
    public void Parse_DecimalLookingValue_InfersDecimal()
    {
        var result = AttributeParsing.Parse(["amount=250000.50"]);

        Assert.Equal(250_000.50m, result["amount"]);
    }

    [Theory]
    [InlineData("isSenior=true", true)]
    [InlineData("isSenior=false", false)]
    public void Parse_BooleanLookingValue_InfersBool(string pair, bool expected)
    {
        var result = AttributeParsing.Parse([pair]);

        Assert.Equal(expected, result["isSenior"]);
    }

    [Fact]
    public void Parse_MalformedPair_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AttributeParsing.Parse(["no-equals-sign"]));
    }

    [Fact]
    public void Parse_MultiplePairs_ReturnsAllOfThem()
    {
        var result = AttributeParsing.Parse(["department=finance", "branch=nairobi-cbd"]);

        Assert.Equal(2, result.Count);
        Assert.Equal("finance", result["department"]);
        Assert.Equal("nairobi-cbd", result["branch"]);
    }
}