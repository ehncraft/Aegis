using Aegis.Expressions;
using Xunit;

namespace Aegis.Tests;

public class ExpressionEngineTests
{
    private static EvaluationContext ContextWith(
        Dictionary<string, object?> principal, Dictionary<string, object?> resource) =>
        new EvaluationContext()
            .WithScope("principal", principal)
            .WithScope("resource", resource);

    [Fact]
    public void MemberEquality_TrueWhenAttributesMatch()
    {
        var context = ContextWith(
            new Dictionary<string, object?> { ["department"] = "finance" },
            new Dictionary<string, object?> { ["department"] = "finance" });

        var expr = CompiledExpression.Parse("principal.department == resource.department");

        Assert.True(expr.EvaluateBoolean(context));
    }

    [Fact]
    public void MemberEquality_FalseWhenAttributesDiffer()
    {
        var context = ContextWith(
            new Dictionary<string, object?> { ["department"] = "finance" },
            new Dictionary<string, object?> { ["department"] = "engineering" });

        var expr = CompiledExpression.Parse("principal.department == resource.department");

        Assert.False(expr.EvaluateBoolean(context));
    }

    [Theory]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 2", true)]
    [InlineData("3 > 2", true)]
    [InlineData("2 >= 3", false)]
    [InlineData("2 == 2", true)]
    [InlineData("2 != 2", false)]
    public void NumericComparisons(string source, bool expected)
    {
        var expr = CompiledExpression.Parse(source);
        Assert.Equal(expected, expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void LogicalAnd_ShortCircuitsCorrectly()
    {
        var expr = CompiledExpression.Parse("true && false");
        Assert.False(expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void LogicalOr_TrueWhenEitherSideTrue()
    {
        var expr = CompiledExpression.Parse("false || true");
        Assert.True(expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void Negation_InvertsBoolean()
    {
        var expr = CompiledExpression.Parse("!false");
        Assert.True(expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void Parentheses_OverridePrecedence()
    {
        // Without parens, && binds tighter than ||, so this would be
        // false || (false && true) == false.
        var expr = CompiledExpression.Parse("(false || false) && true");
        Assert.False(expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void UnresolvedMember_EvaluatesToNullNotError()
    {
        var context = ContextWith(new Dictionary<string, object?>(), new Dictionary<string, object?>());
        var expr = CompiledExpression.Parse("principal.department == resource.department");

        Assert.False(expr.EvaluateBoolean(context));
    }

    [Fact]
    public void InvalidSyntax_ThrowsExpressionSyntaxException()
    {
        Assert.Throws<ExpressionSyntaxException>(() => CompiledExpression.Parse("principal.department =="));
    }
}
