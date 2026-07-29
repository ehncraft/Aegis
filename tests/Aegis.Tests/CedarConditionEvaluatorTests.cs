using Aegis.Cedar;
using Aegis.Relationships;

using Xunit;

namespace Aegis.Tests;

public class CedarConditionEvaluatorTests
{
    private static CedarEvaluationContext Context(
        AegisPrincipal? principal = null,
        AegisResource? resource = null,
        string action = "view",
        IReadOnlyDictionary<string, object?>? actionProperties = null,
        IReadOnlyDictionary<string, object?>? context = null,
        RelationshipGraph? relationshipGraph = null,
        string principalEntityType = "User") =>
        new()
        {
            Principal = principal ?? AegisPrincipal.Create("alice"),
            Resource = resource ?? AegisResource.Create("document", "doc-1"),
            Action = action,
            ActionProperties = actionProperties,
            Context = context,
            RelationshipGraph = relationshipGraph ?? RelationshipGraph.Empty,
            PrincipalEntityType = principalEntityType,
        };

    private static bool Eval(string expression, CedarEvaluationContext context) =>
        CedarConditionEvaluator.EvaluateBoolean(ParseBody(expression), context);

    private static CedarExpr ParseBody(string expression) =>
        Assert.Single(Assert.Single(CedarParser.Parse($"permit(principal, action, resource) when {{ {expression} }};")).Conditions).Body;

    // -- literals, comparisons, arithmetic --------------------------------

    [Theory]
    [InlineData("1 == 1", true)]
    [InlineData("1 == 2", false)]
    [InlineData("1 != 2", true)]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 2", true)]
    [InlineData("3 > 2", true)]
    [InlineData("3 >= 3", true)]
    [InlineData("1 + 2 == 3", true)]
    [InlineData("5 - 2 == 3", true)]
    [InlineData("2 * 3 == 6", true)]
    [InlineData("-5 == 0 - 5", true)]
    [InlineData("true && true", true)]
    [InlineData("true && false", false)]
    [InlineData("false || true", true)]
    [InlineData("false || false", false)]
    [InlineData("!false", true)]
    [InlineData("\"a\" == \"a\"", true)]
    [InlineData("\"a\" == \"b\"", false)]
    public void EvaluateBoolean_ArithmeticAndComparisonOperators_MatchExpected(string expression, bool expected)
    {
        Assert.Equal(expected, Eval(expression, Context()));
    }

    [Fact]
    public void EvaluateBoolean_Or_ShortCircuits_RightNeverEvaluated()
    {
        // "context.missing" would throw if evaluated (no such key) -- the
        // left side being true must prevent that.
        Assert.True(Eval("true || context.missing", Context()));
    }

    [Fact]
    public void EvaluateBoolean_And_ShortCircuits_RightNeverEvaluated()
    {
        Assert.False(Eval("false && context.missing", Context()));
    }

    [Fact]
    public void EvaluateBoolean_If_TakesThenBranchWhenConditionTrue()
    {
        Assert.True(Eval("(if 1 == 1 then true else false)", Context()));
    }

    [Fact]
    public void EvaluateBoolean_If_TakesElseBranchWhenConditionFalse()
    {
        Assert.False(Eval("(if 1 == 2 then true else false)", Context()));
    }

    // -- principal/resource/action/context attribute access ---------------

    [Fact]
    public void EvaluateBoolean_PrincipalId_ResolvesToPrincipalId()
    {
        Assert.True(Eval("principal.id == \"alice\"", Context(principal: AegisPrincipal.Create("alice"))));
    }

    [Fact]
    public void EvaluateBoolean_PrincipalCustomAttribute_ResolvesFromAttributes()
    {
        var principal = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        Assert.True(Eval("principal.departmentId == \"dept-1\"", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_ResourceCustomAttribute_ResolvesFromAttributes()
    {
        var resource = AegisResource.Create("document", "doc-1", new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        Assert.True(Eval("resource.departmentId == \"dept-1\"", Context(resource: resource)));
    }

    [Fact]
    public void EvaluateBoolean_ActionName_ResolvesToActionName()
    {
        Assert.True(Eval("action.name == \"approveLeaveRequest\"", Context(action: "approveLeaveRequest")));
    }

    [Fact]
    public void EvaluateBoolean_ContextAttribute_ResolvesFromContextDictionary()
    {
        var context = Context(context: new Dictionary<string, object?> { ["reason"] = "urgent" });
        Assert.True(Eval("context.reason == \"urgent\"", context));
    }

    [Fact]
    public void EvaluateBoolean_UnknownAttribute_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("principal.doesNotExist == \"x\"", Context()));
    }

    // -- has ----------------------------------------------------------------

    [Fact]
    public void EvaluateBoolean_Has_PresentAttribute_ReturnsTrue()
    {
        var principal = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        Assert.True(Eval("principal has departmentId", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_Has_AbsentAttribute_ReturnsFalse()
    {
        Assert.False(Eval("principal has departmentId", Context()));
    }

    // -- like -----------------------------------------------------------

    [Theory]
    [InlineData("\"hello\" like \"hel*\"", true)]
    [InlineData("\"hello\" like \"*llo\"", true)]
    [InlineData("\"hello\" like \"*ell*\"", true)]
    [InlineData("\"hello\" like \"*\"", true)]
    [InlineData("\"hello\" like \"hello\"", true)]
    [InlineData("\"hello\" like \"goodbye\"", false)]
    [InlineData("\"hello\" like \"h*z\"", false)]
    public void EvaluateBoolean_Like_WildcardMatching_MatchesExpected(string expression, bool expected)
    {
        Assert.Equal(expected, Eval(expression, Context()));
    }

    [Fact]
    public void EvaluateBoolean_Like_EscapedStar_MatchesLiteralStarOnly()
    {
        Assert.True(Eval("\"a*b\" like \"a\\*b\"", Context()));
        Assert.False(Eval("\"axb\" like \"a\\*b\"", Context()));
    }

    // -- is / in ----------------------------------------------------------

    [Fact]
    public void EvaluateBoolean_Is_MatchingType_ReturnsTrue()
    {
        Assert.True(Eval("principal is User", Context(principalEntityType: "User")));
    }

    [Fact]
    public void EvaluateBoolean_Is_MismatchedType_ReturnsFalse()
    {
        Assert.False(Eval("principal is User", Context(principalEntityType: "Membership")));
    }

    [Fact]
    public void EvaluateBoolean_In_DirectMembership_ReturnsTrue()
    {
        var graph = new RelationshipGraph([
            new EntityParent { Child = new EntityUid("User", "alice"), Parent = new EntityUid("Group", "admins") },
        ]);
        Assert.True(Eval("principal in Group::\"admins\"", Context(relationshipGraph: graph)));
    }

    [Fact]
    public void EvaluateBoolean_In_TransitiveMembership_ReturnsTrue()
    {
        var graph = new RelationshipGraph([
            new EntityParent { Child = new EntityUid("User", "alice"), Parent = new EntityUid("Group", "senior-auditors") },
            new EntityParent { Child = new EntityUid("Group", "senior-auditors"), Parent = new EntityUid("Group", "audit-committee") },
        ]);
        Assert.True(Eval("principal in Group::\"audit-committee\"", Context(relationshipGraph: graph)));
    }

    [Fact]
    public void EvaluateBoolean_In_NoMembership_ReturnsFalse()
    {
        Assert.False(Eval("principal in Group::\"admins\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IsIn_MatchingTypeAndMembership_ReturnsTrue()
    {
        var graph = new RelationshipGraph([
            new EntityParent { Child = new EntityUid("User", "alice"), Parent = new EntityUid("Group", "admins") },
        ]);
        Assert.True(Eval("principal is User in Group::\"admins\"", Context(relationshipGraph: graph)));
    }

    // -- sets / records / contains family ----------------------------------

    [Fact]
    public void EvaluateBoolean_SetContains_ElementPresent_ReturnsTrue()
    {
        Assert.True(Eval("[1, 2, 3].contains(2)", Context()));
    }

    [Fact]
    public void EvaluateBoolean_SetContains_ElementAbsent_ReturnsFalse()
    {
        Assert.False(Eval("[1, 2, 3].contains(4)", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ContainsAll_AllElementsPresent_ReturnsTrue()
    {
        Assert.True(Eval("[1, 2, 3].containsAll([1, 2])", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ContainsAll_SomeElementMissing_ReturnsFalse()
    {
        Assert.False(Eval("[1, 2, 3].containsAll([1, 4])", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ContainsAny_AtLeastOneElementPresent_ReturnsTrue()
    {
        Assert.True(Eval("[1, 2, 3].containsAny([4, 2])", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ContainsAny_NoElementPresent_ReturnsFalse()
    {
        Assert.False(Eval("[1, 2, 3].containsAny([4, 5])", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ContextPermissionsContains_DrivingScenarioShape_ReturnsTrue()
    {
        var permissions = new[] { "approve_leave", "view_reports" };
        var context = Context(context: new Dictionary<string, object?> { ["permissions"] = permissions });
        Assert.True(Eval("context.permissions.contains(\"approve_leave\")", context));
    }

    [Fact]
    public void EvaluateBoolean_RecordFieldAccess_ReturnsFieldValue()
    {
        Assert.True(Eval("{ x: 1, y: 2 }.x == 1", Context()));
    }

    [Fact]
    public void EvaluateBoolean_RecordEquality_StructuralComparison()
    {
        Assert.True(Eval("{ x: 1, y: 2 } == { x: 1, y: 2 }", Context()));
    }

    // -- ip() / decimal() extension functions ------------------------------

    [Fact]
    public void EvaluateBoolean_IpIsIpv4_TrueForIpv4Address()
    {
        Assert.True(Eval("ip(\"10.0.0.1\").isIpv4()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpIsIpv4_FalseForIpv6Address()
    {
        Assert.False(Eval("ip(\"::1\").isIpv4()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpIsLoopback_TrueForLoopbackAddress()
    {
        Assert.True(Eval("ip(\"127.0.0.1\").isLoopback()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpIsInRange_AddressWithinCidr_ReturnsTrue()
    {
        Assert.True(Eval("ip(\"10.0.0.5\").isInRange(ip(\"10.0.0.0/24\"))", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpIsInRange_AddressOutsideCidr_ReturnsFalse()
    {
        Assert.False(Eval("ip(\"10.0.1.5\").isInRange(ip(\"10.0.0.0/24\"))", Context()));
    }

    [Fact]
    public void EvaluateBoolean_DecimalLessThan_TrueWhenLess()
    {
        Assert.True(Eval("decimal(\"1.5\").lessThan(decimal(\"2.0\"))", Context()));
    }

    [Fact]
    public void EvaluateBoolean_DecimalGreaterThanOrEqual_TrueWhenEqual()
    {
        Assert.True(Eval("decimal(\"1.5\").greaterThanOrEqual(decimal(\"1.5\"))", Context()));
    }

    // -- negative cases: disallowed extension functions/methods -----------

    [Fact]
    public void EvaluateBoolean_UnknownExtensionFunction_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("notARealFunction(\"x\") == \"x\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_UnknownSetMethod_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("[1, 2].notARealMethod(1)", Context()));
    }

    [Fact]
    public void EvaluateBoolean_UnknownIpMethod_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("ip(\"10.0.0.1\").notARealMethod()", Context()));
    }
}
