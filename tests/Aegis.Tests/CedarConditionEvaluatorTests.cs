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

    [Fact]
    public void EvaluateBoolean_UnknownDecimalMethod_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(
            () => Eval("decimal(\"1.0\").notARealMethod(decimal(\"2.0\"))", Context()));
    }

    [Fact]
    public void EvaluateBoolean_MethodCallOnUnsupportedTargetKind_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("\"hello\".notAMethod()", Context()));
    }

    // -- bare principal/resource/action/context var evaluation ------------

    [Fact]
    public void EvaluateBoolean_BareActionVar_EqualsActionEntity()
    {
        Assert.True(Eval("action == Action::\"view\"", Context(action: "view")));
    }

    [Fact]
    public void EvaluateBoolean_BareContextVar_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("context == principal", Context()));
    }

    // -- attribute resolution: roles, resource.kind, action/context properties, unsupported targets --

    [Fact]
    public void EvaluateBoolean_PrincipalRoles_ResolvesFromPrincipalRoles()
    {
        var principal = AegisPrincipal.Create("alice", roles: ["Admin", "Finance"]);
        Assert.True(Eval("principal.roles.contains(\"Admin\")", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_ResourceKind_ResolvesToResourceKind()
    {
        Assert.True(Eval("resource.kind == \"document\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ResourceUnknownAttribute_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("resource.doesNotExist == \"x\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ActionProperty_ResolvesFromActionProperties()
    {
        var context = Context(actionProperties: new Dictionary<string, object?> { ["urgency"] = "high" });
        Assert.True(Eval("action.urgency == \"high\"", context));
    }

    [Fact]
    public void EvaluateBoolean_ActionUnknownProperty_HasReturnsFalse()
    {
        Assert.False(Eval("action has doesNotExist", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ContextMissingDictionary_HasReturnsFalse()
    {
        Assert.False(Eval("context has anything", Context(context: null)));
    }

    [Fact]
    public void EvaluateBoolean_AttributeAccessOnNonRecordNonScopeTarget_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("(1).foo == 1", Context()));
    }

    // -- FromClr boxing: int, decimal, EntityUid, nested record/set, unsupported type --

    [Fact]
    public void EvaluateBoolean_IntAttribute_BoxesAsLong()
    {
        var principal = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["count"] = 5 });
        Assert.True(Eval("principal.count == 5", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_DecimalAttribute_BoxesAsDecimal()
    {
        var principal = AegisPrincipal.Create(
            "alice", attributes: new Dictionary<string, object?> { ["balance"] = 100.5m });
        Assert.True(Eval("principal.balance.greaterThan(decimal(\"50\"))", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_EntityUidAttribute_BoxesAsEntity()
    {
        var principal = AegisPrincipal.Create(
            "alice", attributes: new Dictionary<string, object?> { ["manager"] = new EntityUid("User", "bob") });
        Assert.True(Eval("principal.manager == User::\"bob\"", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_NestedRecordAttribute_ResolvesNestedField()
    {
        var address = new Dictionary<string, object?> { ["city"] = "Nairobi" };
        var principal = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["address"] = address });
        Assert.True(Eval("principal.address.city == \"Nairobi\"", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_NestedListAttribute_ResolvesAsSet()
    {
        var scores = new List<object?> { 1L, 2L, 3L };
        var principal = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["scores"] = scores });
        Assert.True(Eval("principal.scores.contains(2)", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_NullAttribute_Throws()
    {
        var principal = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["foo"] = null });
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("principal.foo == 1", Context(principal: principal)));
    }

    [Fact]
    public void EvaluateBoolean_UnsupportedAttributeType_Throws()
    {
        var principal = AegisPrincipal.Create(
            "alice", attributes: new Dictionary<string, object?> { ["occurredAt"] = DateTime.UtcNow });
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("principal.occurredAt == 1", Context(principal: principal)));
    }

    // -- like: non-string target, trailing wildcards -----------------------

    [Fact]
    public void EvaluateBoolean_Like_NonStringTarget_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("1 like \"a*\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_Like_TrailingUnconsumedWildcards_StillMatches()
    {
        Assert.True(Eval("\"ab\" like \"ab**\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_Like_EmptyTextAgainstWildcard_Matches()
    {
        Assert.True(Eval("\"\" like \"*\"", Context()));
    }

    // -- is/in: type mismatches on non-entity operands ---------------------

    [Fact]
    public void EvaluateBoolean_Is_NonEntityTarget_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("1 is User", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IsIn_NonEntityAncestor_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("principal is User in 1", Context()));
    }

    [Fact]
    public void EvaluateBoolean_In_NonEntityOperand_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("1 in Group::\"admins\"", Context()));
    }

    // -- ip: isIpv6, isMulticast (IPv4 and IPv6), invalid formats -----------

    [Fact]
    public void EvaluateBoolean_IpIsIpv6_TrueForIpv6Address()
    {
        Assert.True(Eval("ip(\"::1\").isIpv6()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpIsMulticast_TrueForIpv4MulticastAddress()
    {
        Assert.True(Eval("ip(\"224.0.0.1\").isMulticast()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpIsMulticast_TrueForIpv6MulticastAddress()
    {
        Assert.True(Eval("ip(\"ff02::1\").isMulticast()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpIsMulticast_FalseForUnicastAddress()
    {
        Assert.False(Eval("ip(\"10.0.0.1\").isMulticast()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpInvalidAddress_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("ip(\"not-an-ip\").isIpv4()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpInvalidNetwork_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("ip(\"not-an-ip/24\").isIpv4()", Context()));
    }

    [Fact]
    public void EvaluateBoolean_ExtensionCallNonStringArgument_Throws()
    {
        Assert.Throws<CedarConditionEvaluationException>(() => Eval("ip(1).isIpv4()", Context()));
    }

    // -- decimal: lessThanOrEqual, greaterThan ------------------------------

    [Fact]
    public void EvaluateBoolean_DecimalLessThanOrEqual_TrueWhenEqual()
    {
        Assert.True(Eval("decimal(\"1.5\").lessThanOrEqual(decimal(\"1.5\"))", Context()));
    }

    [Fact]
    public void EvaluateBoolean_DecimalGreaterThan_TrueWhenGreater()
    {
        Assert.True(Eval("decimal(\"2.0\").greaterThan(decimal(\"1.5\"))", Context()));
    }

    // -- CedarValue.ValueEquals: every kind, plus cross-kind and mismatches --

    [Fact]
    public void EvaluateBoolean_BoolEquality_MatchesExpected()
    {
        Assert.True(Eval("true == true", Context()));
        Assert.False(Eval("true == false", Context()));
    }

    [Fact]
    public void EvaluateBoolean_EntityEquality_MatchesExpected()
    {
        Assert.True(Eval("User::\"alice\" == User::\"alice\"", Context()));
        Assert.False(Eval("User::\"alice\" == User::\"bob\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_DecimalEquality_MatchesExpected()
    {
        Assert.True(Eval("decimal(\"1.5\") == decimal(\"1.5\")", Context()));
        Assert.False(Eval("decimal(\"1.5\") == decimal(\"2.0\")", Context()));
    }

    [Fact]
    public void EvaluateBoolean_IpEquality_MatchesExpected()
    {
        Assert.True(Eval("ip(\"10.0.0.1\") == ip(\"10.0.0.1\")", Context()));
        Assert.False(Eval("ip(\"10.0.0.1\") == ip(\"10.0.0.2\")", Context()));
    }

    [Fact]
    public void EvaluateBoolean_CrossKindEquality_ReturnsFalseNotError()
    {
        Assert.False(Eval("1 == \"1\"", Context()));
    }

    [Fact]
    public void EvaluateBoolean_SetEquality_OrderIndependent()
    {
        Assert.True(Eval("[1, 2] == [2, 1]", Context()));
    }

    [Fact]
    public void EvaluateBoolean_SetEquality_DifferentCount_False()
    {
        Assert.False(Eval("[1, 2] == [1, 2, 3]", Context()));
    }

    [Fact]
    public void EvaluateBoolean_SetEquality_SameCountDifferentElements_False()
    {
        Assert.False(Eval("[1, 2] == [1, 3]", Context()));
    }

    [Fact]
    public void EvaluateBoolean_RecordEquality_DifferentFieldCount_False()
    {
        Assert.False(Eval("{ x: 1 } == { x: 1, y: 2 }", Context()));
    }

    [Fact]
    public void EvaluateBoolean_RecordEquality_SameKeysDifferentValues_False()
    {
        Assert.False(Eval("{ x: 1, y: 2 } == { x: 1, y: 3 }", Context()));
    }
}