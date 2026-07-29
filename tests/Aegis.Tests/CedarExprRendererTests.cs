using Aegis.Cedar;

using Xunit;

namespace Aegis.Tests;

/// <summary>
/// Direct tests of <see cref="CedarExprRenderer"/> -- <see cref="CedarPolicySetLowererTests"/>
/// only exercises the narrow subset of node kinds that actually appear in
/// its driving-scenario fixtures (binary ops, attr access, entity refs,
/// <c>.contains()</c>); this file drives every node kind directly to prove
/// the round-trip claim in the type's own doc comment: render, re-parse,
/// evaluate behaves identically to evaluating the original tree.
/// </summary>
public class CedarExprRendererTests
{
    /// <summary>Parses <paramref name="expression"/>, renders it, then re-parses the rendered text -- the exact round-trip <see cref="PolicyEvaluator"/> relies on.</summary>
    private static bool RenderAndReevaluate(string expression, CedarEvaluationContext context)
    {
        var original = ParseBody(expression);
        var rendered = CedarExprRenderer.Render(original);
        var reparsed = CedarParser.ParseCondition(rendered);
        return CedarConditionEvaluator.EvaluateBoolean(reparsed, context);
    }

    private static CedarExpr ParseBody(string expression) =>
        Assert.Single(Assert.Single(CedarParser.Parse($"permit(principal, action, resource) when {{ {expression} }};")).Conditions).Body;

    private static CedarEvaluationContext Context(
        AegisPrincipal? principal = null,
        AegisResource? resource = null,
        string action = "view",
        IReadOnlyDictionary<string, object?>? context = null) =>
        new()
        {
            Principal = principal ?? AegisPrincipal.Create("alice"),
            Resource = resource ?? AegisResource.Create("document", "doc-1"),
            Action = action,
            Context = context,
            PrincipalEntityType = "User",
        };

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1 == 1", true)]
    [InlineData("\"a\" == \"a\"", true)]
    [InlineData("principal.id == \"alice\"", true)]
    [InlineData("1 != 2", true)]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 2", true)]
    [InlineData("3 > 2", true)]
    [InlineData("3 >= 3", true)]
    [InlineData("1 + 2 == 3", true)]
    [InlineData("5 - 2 == 3", true)]
    [InlineData("2 * 3 == 6", true)]
    [InlineData("-5 == 0 - 5", true)]
    [InlineData("!false", true)]
    [InlineData("(if true then 1 else 2) == 1", true)]
    [InlineData("[1, 2, 3].contains(2)", true)]
    [InlineData("{ x: 1, y: 2 }.x == 1", true)]
    [InlineData("principal has id", true)]
    [InlineData("\"hello\" like \"hel*\"", true)]
    [InlineData("\"a*b\" like \"a\\*b\"", true)]
    public void Render_ThenReparse_EvaluatesIdenticallyToOriginal(string expression, bool expected)
    {
        Assert.Equal(expected, RenderAndReevaluate(expression, Context()));
    }

    [Fact]
    public void Render_EntityLiteralEquality_RoundTrips()
    {
        var context = new CedarEvaluationContext
        {
            Principal = AegisPrincipal.Create("alice"),
            Resource = AegisResource.Create("document", "doc-1"),
            Action = "view",
            PrincipalEntityType = "User",
        };

        Assert.True(RenderAndReevaluate("principal == User::\"alice\"", context));
    }

    [Fact]
    public void Render_IsExpression_RoundTrips()
    {
        Assert.True(RenderAndReevaluate("principal is User", Context()));
    }

    [Fact]
    public void Render_InExpression_RoundTrips()
    {
        var graph = new Aegis.Relationships.RelationshipGraph(
        [
            new Aegis.Relationships.EntityParent
            {
                Child = new Aegis.Relationships.EntityUid("User", "alice"),
                Parent = new Aegis.Relationships.EntityUid("Group", "admins"),
            },
        ]);
        var context = new CedarEvaluationContext
        {
            Principal = AegisPrincipal.Create("alice"),
            Resource = AegisResource.Create("document", "doc-1"),
            Action = "view",
            RelationshipGraph = graph,
            PrincipalEntityType = "User",
        };

        Assert.True(RenderAndReevaluate("principal in Group::\"admins\"", context));
    }

    [Fact]
    public void Render_MethodCallWithMultipleArguments_RoundTrips()
    {
        Assert.True(RenderAndReevaluate("[1, 2, 3].containsAll([1, 2])", Context()));
    }

    [Fact]
    public void Render_ExtensionCall_RoundTrips()
    {
        Assert.True(RenderAndReevaluate("ip(\"10.0.0.1\").isIpv4()", Context()));
    }

    [Fact]
    public void Render_ContextAttribute_RoundTrips()
    {
        var context = Context(context: new Dictionary<string, object?> { ["reason"] = "urgent" });

        Assert.True(RenderAndReevaluate("context.reason == \"urgent\"", context));
    }

    [Fact]
    public void Render_StringLiteralWithSpecialCharacters_RoundTrips()
    {
        var original = ParseBody("\"a\\nb\\tc\\rd\\\\e\\\"f\"");
        var rendered = CedarExprRenderer.Render(original);
        var reparsed = (CedarLiteralExpr)CedarParser.ParseCondition(rendered);

        Assert.Equal("a\nb\tc\rd\\e\"f", reparsed.Value);
    }

    [Fact]
    public void Render_RecordWithMultipleFields_RoundTrips()
    {
        Assert.True(RenderAndReevaluate("{ a: 1, b: 2, c: 3 }.b == 2", Context()));
    }

    [Fact]
    public void Render_BareActionVar_RoundTrips()
    {
        Assert.True(RenderAndReevaluate("action == Action::\"view\"", Context(action: "view")));
    }

    [Fact]
    public void Render_LikePatternWithSpecialCharacters_RoundTrips()
    {
        var original = (CedarLikeExpr)ParseBody("\"anything\" like \"a\\\"b\\\\c\\nd\\te\\rf*g\\*h\"");
        var rendered = CedarExprRenderer.Render(original);
        var reparsed = (CedarLikeExpr)CedarParser.ParseCondition(rendered);

        Assert.Equal(original.Pattern, reparsed.Pattern);
    }
}