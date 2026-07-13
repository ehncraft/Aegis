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

    [Fact]
    public void Variable_ResolvesFromScope()
    {
        var isFinance = CompiledExpression.Parse("principal.department == 'finance'");
        var scope = new VariableScope(new Dictionary<string, CompiledExpression> { ["isFinance"] = isFinance });
        var context = ContextWith(
                new Dictionary<string, object?> { ["department"] = "finance" },
                new Dictionary<string, object?>())
            .WithVariables(scope);

        var expr = CompiledExpression.Parse("${isFinance}");

        Assert.True(expr.EvaluateBoolean(context));
    }

    [Fact]
    public void Variable_CanReferenceAnotherVariable()
    {
        var inner = CompiledExpression.Parse("true");
        var outer = CompiledExpression.Parse("${inner} && true");
        var scope = new VariableScope(new Dictionary<string, CompiledExpression>
        {
            ["inner"] = inner,
            ["outer"] = outer,
        });
        var context = new EvaluationContext().WithVariables(scope);

        var expr = CompiledExpression.Parse("${outer}");

        Assert.True(expr.EvaluateBoolean(context));
    }

    [Fact]
    public void Variable_UnknownName_Throws()
    {
        var expr = CompiledExpression.Parse("${missing}");

        Assert.Throws<ExpressionEvaluationException>(() => expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void Variable_CircularReference_Throws()
    {
        var a = CompiledExpression.Parse("${b}");
        var b = CompiledExpression.Parse("${a}");
        var scope = new VariableScope(new Dictionary<string, CompiledExpression> { ["a"] = a, ["b"] = b });
        var context = new EvaluationContext().WithVariables(scope);

        Assert.Throws<ExpressionEvaluationException>(() => a.EvaluateBoolean(context));
    }

    [Fact]
    public void Variable_MalformedReference_ThrowsSyntaxException()
    {
        Assert.Throws<ExpressionSyntaxException>(() => CompiledExpression.Parse("${1abc}"));
        Assert.Throws<ExpressionSyntaxException>(() => CompiledExpression.Parse("${abc"));
    }

    [Fact]
    public void ReferencedVariableNames_CollectsAllReferences()
    {
        var expr = CompiledExpression.Parse("${a} && (!${b} || ${a})");

        Assert.Equal(["a", "b", "a"], expr.ReferencedVariableNames);
    }

    [Fact]
    public void ReferencedVariableNames_EmptyWhenNoVariables()
    {
        var expr = CompiledExpression.Parse("principal.department == 'finance'");

        Assert.Empty(expr.ReferencedVariableNames);
    }

    [Theory]
    [InlineData("!5")]
    [InlineData("!'x'")]
    public void SemanticAnalysis_RejectsNotOfNonBooleanLiteral(string source)
    {
        Assert.Throws<ExpressionSyntaxException>(() => CompiledExpression.Parse(source));
    }

    [Theory]
    [InlineData("'a' && true")]
    [InlineData("true && 5")]
    [InlineData("1 || false")]
    public void SemanticAnalysis_RejectsLogicalOperatorOnNonBooleanLiteral(string source)
    {
        Assert.Throws<ExpressionSyntaxException>(() => CompiledExpression.Parse(source));
    }

    [Theory]
    [InlineData("1 < 'x'")]
    [InlineData("'a' > 5")]
    public void SemanticAnalysis_RejectsIncomparableLiteralConstants(string source)
    {
        Assert.Throws<ExpressionSyntaxException>(() => CompiledExpression.Parse(source));
    }

    [Fact]
    public void SemanticAnalysis_StringOrderingComparisonIsAllowed()
    {
        var expr = CompiledExpression.Parse("'a' < 'b'");

        Assert.True(expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void SemanticAnalysis_DoesNotRejectDynamicOperandsStatically()
    {
        // `principal.active` is a member path, not a literal -- its type is
        // only known at request time, so parsing must succeed even though
        // it could turn out non-boolean.
        var expr = CompiledExpression.Parse("principal.active && true");

        var context = ContextWith(
            new Dictionary<string, object?> { ["active"] = true }, new Dictionary<string, object?>());
        Assert.True(expr.EvaluateBoolean(context));
    }

    [Fact]
    public void ConstantFolding_DeeplyNestedConstantsStillEvaluateCorrectly()
    {
        var expr = CompiledExpression.Parse("(1 < 2) && (3 > 2) || false");

        Assert.True(expr.EvaluateBoolean(new EvaluationContext()));
    }

    [Fact]
    public void ConstantFolding_DoesNotSkipEvaluatingNonConstantLeftOperandOfAnd()
    {
        // The right side folds to a constant, but the left side is a
        // member path that turns out non-boolean at runtime -- folding must
        // not have dropped its evaluation (and the exception it raises).
        var expr = CompiledExpression.Parse("principal.active && false");

        var context = ContextWith(
            new Dictionary<string, object?> { ["active"] = "not-a-bool" }, new Dictionary<string, object?>());
        Assert.Throws<ExpressionEvaluationException>(() => expr.EvaluateBoolean(context));
    }

    [Fact]
    public void ConstantFolding_DoesNotSkipEvaluatingNonConstantLeftOperandOfOr()
    {
        var expr = CompiledExpression.Parse("principal.active || true");

        var context = ContextWith(
            new Dictionary<string, object?> { ["active"] = "not-a-bool" }, new Dictionary<string, object?>());
        Assert.Throws<ExpressionEvaluationException>(() => expr.EvaluateBoolean(context));
    }

    [Fact]
    public void ConstantFolding_StillCollectsVariableReferencesInsideFoldedBranch()
    {
        // The `&&` folds away entirely at compile time (left is a constant
        // `false`), but PolicyValidator still needs to see the reference to
        // catch a typo'd variable name -- and evaluation must not need
        // `${x}` to be defined, proving the branch really is skipped.
        var expr = CompiledExpression.Parse("false && ${x}");

        Assert.Equal(["x"], expr.ReferencedVariableNames);
        Assert.False(expr.EvaluateBoolean(new EvaluationContext()));
    }
}