using Aegis.Cedar;

using Xunit;

namespace Aegis.Tests;

public class CedarParserTests
{
    [Fact]
    public void Parse_MinimalPermit_NoConditions()
    {
        var policies = CedarParser.Parse("permit(principal, action, resource);");

        var policy = Assert.Single(policies);
        Assert.Equal(CedarEffect.Permit, policy.Effect);
        Assert.IsType<CedarAnyScope>(policy.PrincipalScope);
        Assert.IsType<CedarAnyScope>(policy.ActionScope);
        Assert.IsType<CedarAnyScope>(policy.ResourceScope);
        Assert.Empty(policy.Conditions);
    }

    [Fact]
    public void Parse_Forbid_ParsesEffect()
    {
        var policies = CedarParser.Parse("forbid(principal, action, resource);");

        Assert.Equal(CedarEffect.Forbid, Assert.Single(policies).Effect);
    }

    [Fact]
    public void Parse_MultiplePolicies_ParsesAll()
    {
        var policies = CedarParser.Parse(
            "permit(principal, action, resource); forbid(principal, action, resource);");

        Assert.Equal(2, policies.Count);
        Assert.Equal(CedarEffect.Permit, policies[0].Effect);
        Assert.Equal(CedarEffect.Forbid, policies[1].Effect);
    }

    [Fact]
    public void Parse_PrincipalEqualsEntity_ParsesEqScope()
    {
        var policy = ParseSingle("permit(principal == User::\"alice\", action, resource);");

        var scope = Assert.IsType<CedarEqScope>(policy.PrincipalScope);
        Assert.Equal(["User"], scope.Entity.Type);
        Assert.Equal("alice", scope.Entity.Id);
    }

    [Fact]
    public void Parse_PrincipalInEntity_ParsesInScope()
    {
        var policy = ParseSingle("permit(principal in Group::\"admins\", action, resource);");

        var scope = Assert.IsType<CedarInScope>(policy.PrincipalScope);
        Assert.Equal(["Group"], scope.Entity.Type);
        Assert.Equal("admins", scope.Entity.Id);
    }

    [Fact]
    public void Parse_PrincipalIs_ParsesIsScope()
    {
        var policy = ParseSingle("permit(principal is User, action, resource);");

        var scope = Assert.IsType<CedarIsScope>(policy.PrincipalScope);
        Assert.Equal(["User"], scope.Type);
    }

    [Fact]
    public void Parse_ResourceIsIn_ParsesIsInScope()
    {
        var policy = ParseSingle("permit(principal, action, resource is Photo in Album::\"vacation\");");

        var scope = Assert.IsType<CedarIsInScope>(policy.ResourceScope);
        Assert.Equal(["Photo"], scope.Type);
        Assert.Equal("vacation", scope.Entity.Id);
    }

    [Fact]
    public void Parse_ActionEqualsEntity_ParsesEqScope()
    {
        var policy = ParseSingle("permit(principal, action == Action::\"view\", resource);");

        var scope = Assert.IsType<CedarEqScope>(policy.ActionScope);
        Assert.Equal("view", scope.Entity.Id);
    }

    [Fact]
    public void Parse_ActionInSet_ParsesInSetScope()
    {
        var policy = ParseSingle("permit(principal, action in [Action::\"view\", Action::\"edit\"], resource);");

        var scope = Assert.IsType<CedarInSetScope>(policy.ActionScope);
        Assert.Equal(2, scope.Entities.Count);
        Assert.Equal("view", scope.Entities[0].Id);
        Assert.Equal("edit", scope.Entities[1].Id);
    }

    [Fact]
    public void Parse_NamespacedEntityType_ParsesFullPath()
    {
        var policy = ParseSingle("permit(principal == MyApp::User::\"alice\", action, resource);");

        var scope = Assert.IsType<CedarEqScope>(policy.PrincipalScope);
        Assert.Equal(["MyApp", "User"], scope.Entity.Type);
        Assert.Equal("alice", scope.Entity.Id);
    }

    [Fact]
    public void Parse_When_ParsesConditionBody()
    {
        var policy = ParseSingle(
            "permit(principal, action, resource) when { principal.department == resource.department };");

        var condition = Assert.Single(policy.Conditions);
        Assert.Equal(CedarConditionKind.When, condition.Kind);
        Assert.IsType<CedarBinaryExpr>(condition.Body);
    }

    [Fact]
    public void Parse_Unless_ParsesConditionKind()
    {
        var policy = ParseSingle("permit(principal, action, resource) unless { resource.locked };");

        Assert.Equal(CedarConditionKind.Unless, Assert.Single(policy.Conditions).Kind);
    }

    [Fact]
    public void Parse_WhenAndUnless_BothPresent_InOrder()
    {
        var policy = ParseSingle(
            "permit(principal, action, resource) when { true } unless { false } when { true };");

        Assert.Equal(3, policy.Conditions.Count);
        Assert.Equal(CedarConditionKind.When, policy.Conditions[0].Kind);
        Assert.Equal(CedarConditionKind.Unless, policy.Conditions[1].Kind);
        Assert.Equal(CedarConditionKind.When, policy.Conditions[2].Kind);
    }

    [Fact]
    public void Parse_Arithmetic_ParsesAddAndMultiply()
    {
        var body = ParseBody("1 + 2 * 3 == 7");

        var comparison = Assert.IsType<CedarBinaryExpr>(body);
        Assert.Equal(CedarBinaryOperator.Equal, comparison.Operator);
        var add = Assert.IsType<CedarBinaryExpr>(comparison.Left);
        Assert.Equal(CedarBinaryOperator.Add, add.Operator);
        var mult = Assert.IsType<CedarBinaryExpr>(add.Right);
        Assert.Equal(CedarBinaryOperator.Multiply, mult.Operator);
    }

    [Fact]
    public void Parse_Has_ParsesAttributeName()
    {
        var body = ParseBody("principal has admin");

        var has = Assert.IsType<CedarHasExpr>(body);
        Assert.Equal("admin", has.AttributeName);
        Assert.IsType<CedarVarExpr>(has.Target);
    }

    [Fact]
    public void Parse_HasWithStringAttribute_ParsesAttributeName()
    {
        var body = ParseBody("principal has \"weird attr\"");

        Assert.Equal("weird attr", Assert.IsType<CedarHasExpr>(body).AttributeName);
    }

    [Fact]
    public void Parse_Like_ParsesWildcardPattern()
    {
        var body = ParseBody("resource.name like \"draft-*\"");

        var like = Assert.IsType<CedarLikeExpr>(body);
        Assert.Equal("draft-*", like.Pattern);
    }

    [Fact]
    public void Parse_In_ParsesHierarchyExpr()
    {
        var body = ParseBody("principal in Group::\"admins\"");

        var inExpr = Assert.IsType<CedarInExpr>(body);
        Assert.IsType<CedarVarExpr>(inExpr.Left);
        Assert.IsType<CedarEntityRefExpr>(inExpr.Right);
    }

    [Fact]
    public void Parse_Is_ParsesTypeTest()
    {
        var body = ParseBody("resource is Photo");

        var isExpr = Assert.IsType<CedarIsExpr>(body);
        Assert.Equal(["Photo"], isExpr.Type);
        Assert.Null(isExpr.InExpr);
    }

    [Fact]
    public void Parse_IsIn_ParsesTypeTestWithHierarchy()
    {
        var body = ParseBody("resource is Photo in Album::\"vacation\"");

        var isExpr = Assert.IsType<CedarIsExpr>(body);
        Assert.NotNull(isExpr.InExpr);
    }

    [Fact]
    public void Parse_SetLiteral_ParsesElements()
    {
        var body = ParseBody("[1, 2, 3]");

        var set = Assert.IsType<CedarSetExpr>(body);
        Assert.Equal(3, set.Elements.Count);
    }

    [Fact]
    public void Parse_RecordLiteral_ParsesFieldsWithIdentifierAndStringKeys()
    {
        var body = ParseBody("{ name: \"alice\", \"some key\": 1 }");

        var record = Assert.IsType<CedarRecordExpr>(body);
        Assert.Equal(2, record.Fields.Count);
        Assert.Equal("name", record.Fields[0].Key);
        Assert.Equal("some key", record.Fields[1].Key);
    }

    [Fact]
    public void Parse_EntityLiteral_InExpressionPosition()
    {
        var body = ParseBody("principal == User::\"alice\"");

        var eq = Assert.IsType<CedarBinaryExpr>(body);
        var entityRef = Assert.IsType<CedarEntityRefExpr>(eq.Right);
        Assert.Equal(["User"], entityRef.Type);
        Assert.Equal("alice", entityRef.Id);
    }

    [Fact]
    public void Parse_IfThenElse_ParsesAllThreeBranches()
    {
        var body = ParseBody("if principal.isAdmin then true else false");

        var ifExpr = Assert.IsType<CedarIfExpr>(body);
        Assert.IsType<CedarAttrExpr>(ifExpr.Condition);
        Assert.IsType<CedarLiteralExpr>(ifExpr.Then);
        Assert.IsType<CedarLiteralExpr>(ifExpr.Else);
    }

    [Fact]
    public void Parse_MethodCall_Contains_ParsesTargetAndArgs()
    {
        var body = ParseBody("resource.tags.contains(\"public\")");

        var call = Assert.IsType<CedarMethodCallExpr>(body);
        Assert.Equal("contains", call.MethodName);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void Parse_ExtensionCall_Ip_ParsesFunctionNameAndArgs()
    {
        var body = ParseBody("ip(\"10.0.0.1\").isInRange(ip(\"10.0.0.0/24\"))");

        var call = Assert.IsType<CedarMethodCallExpr>(body);
        Assert.Equal("isInRange", call.MethodName);
        var target = Assert.IsType<CedarExtensionCallExpr>(call.Target);
        Assert.Equal("ip", target.FunctionName);
    }

    [Fact]
    public void Parse_IndexAccess_EquivalentToAttrAccess()
    {
        var body = ParseBody("resource[\"owner\"] == principal.id");

        var eq = Assert.IsType<CedarBinaryExpr>(body);
        var attr = Assert.IsType<CedarAttrExpr>(eq.Left);
        Assert.Equal("owner", attr.Name);
    }

    [Fact]
    public void Parse_ParenthesesOverridePrecedence()
    {
        // Without parens this would be false || (false && true) == false.
        var body = ParseBody("(false || false) && true");

        var and = Assert.IsType<CedarBinaryExpr>(body);
        Assert.Equal(CedarBinaryOperator.And, and.Operator);
        Assert.IsType<CedarBinaryExpr>(and.Left);
    }

    [Fact]
    public void Parse_AndBindsTighterThanOr()
    {
        var body = ParseBody("false || true && true");

        var or = Assert.IsType<CedarBinaryExpr>(body);
        Assert.Equal(CedarBinaryOperator.Or, or.Operator);
        var and = Assert.IsType<CedarBinaryExpr>(or.Right);
        Assert.Equal(CedarBinaryOperator.And, and.Operator);
    }

    [Fact]
    public void Parse_UnaryNotAndNegate()
    {
        var body = ParseBody("!principal.isBanned && -1 < 0");

        var and = Assert.IsType<CedarBinaryExpr>(body);
        Assert.IsType<CedarUnaryExpr>(and.Left);
        var comparison = Assert.IsType<CedarBinaryExpr>(and.Right);
        Assert.IsType<CedarUnaryExpr>(comparison.Left);
    }

    [Fact]
    public void Parse_UnterminatedString_ThrowsSyntaxException()
    {
        Assert.Throws<CedarSyntaxException>(
            () => CedarParser.Parse("permit(principal, action, resource) when { \"unterminated };"));
    }

    [Fact]
    public void Parse_NonAssociatingRelationalOperators_Throws()
    {
        Assert.Throws<CedarSyntaxException>(
            () => CedarParser.Parse("permit(principal, action, resource) when { 1 == 1 == 1 };"));
    }

    [Fact]
    public void Parse_MalformedEntityLiteral_MissingId_Throws()
    {
        Assert.Throws<CedarSyntaxException>(
            () => CedarParser.Parse("permit(principal == User::, action, resource);"));
    }

    [Fact]
    public void Parse_UnknownToken_Throws()
    {
        Assert.Throws<CedarSyntaxException>(
            () => CedarParser.Parse("permit(principal, action, resource) when { 1 % 2 };"));
    }

    [Fact]
    public void Parse_MissingSemicolon_Throws()
    {
        Assert.Throws<CedarSyntaxException>(() => CedarParser.Parse("permit(principal, action, resource)"));
    }

    [Fact]
    public void Parse_InvalidEffectKeyword_Throws()
    {
        var ex = Assert.Throws<CedarSyntaxException>(
            () => CedarParser.Parse("allow(principal, action, resource);"));

        Assert.True(ex.Position >= 0);
    }

    [Fact]
    public void Parse_NotEqual_ParsesNotEqualOperator()
    {
        Assert.Equal(CedarBinaryOperator.NotEqual, Assert.IsType<CedarBinaryExpr>(ParseBody("1 != 2")).Operator);
    }

    [Fact]
    public void Parse_LessEqual_ParsesLessEqualOperator()
    {
        Assert.Equal(CedarBinaryOperator.LessEqual, Assert.IsType<CedarBinaryExpr>(ParseBody("1 <= 2")).Operator);
    }

    [Fact]
    public void Parse_Greater_ParsesGreaterOperator()
    {
        Assert.Equal(CedarBinaryOperator.Greater, Assert.IsType<CedarBinaryExpr>(ParseBody("2 > 1")).Operator);
    }

    [Fact]
    public void Parse_GreaterEqual_ParsesGreaterEqualOperator()
    {
        Assert.Equal(CedarBinaryOperator.GreaterEqual, Assert.IsType<CedarBinaryExpr>(ParseBody("2 >= 1")).Operator);
    }

    [Fact]
    public void Parse_BinarySubtraction_ParsesSubtractOperator()
    {
        var subtract = Assert.IsType<CedarBinaryExpr>(ParseBody("5 - 2"));

        Assert.Equal(CedarBinaryOperator.Subtract, subtract.Operator);
    }

    [Fact]
    public void Parse_ActionInSingularEntity_ParsesInScope_NotInSetScope()
    {
        var policy = ParseSingle("permit(principal, action in Action::\"view\", resource);");

        var scope = Assert.IsType<CedarInScope>(policy.ActionScope);
        Assert.Equal("view", scope.Entity.Id);
    }

    [Fact]
    public void Parse_ActionAndContextVars_ParseAsBarePrimaries()
    {
        var and = Assert.IsType<CedarBinaryExpr>(ParseBody("action.name == \"view\" && context.mfa"));

        var actionAttr = Assert.IsType<CedarAttrExpr>(Assert.IsType<CedarBinaryExpr>(and.Left).Left);
        Assert.Equal(CedarVar.Action, Assert.IsType<CedarVarExpr>(actionAttr.Target).Variable);

        var contextAttr = Assert.IsType<CedarAttrExpr>(and.Right);
        Assert.Equal(CedarVar.Context, Assert.IsType<CedarVarExpr>(contextAttr.Target).Variable);
    }

    [Fact]
    public void Parse_StringEscapes_DecodeToExpectedCharacters()
    {
        var literal = Assert.IsType<CedarLiteralExpr>(ParseBody("\"a\\nb\\tc\\rd\\\\e\\\"f\\u{0041}\""));

        Assert.Equal("a\nb\tc\rd\\e\"fA", literal.Value);
    }

    [Fact]
    public void Parse_LineComment_IsSkipped()
    {
        var policies = CedarParser.Parse(
            "// a comment about this policy\n" +
            "permit(principal, action, resource); // trailing comment\n");

        Assert.Single(policies);
    }

    [Fact]
    public void Parse_UnsupportedEscapeSequence_Throws()
    {
        Assert.Throws<CedarSyntaxException>(() => CedarParser.Parse("permit(principal, action, resource) when { \"\\q\" };"));
    }

    [Fact]
    public void Parse_UnterminatedUnicodeEscape_Throws()
    {
        Assert.Throws<CedarSyntaxException>(
            () => CedarParser.Parse("permit(principal, action, resource) when { \"\\u{41\" };"));
    }

    private static CedarPolicy ParseSingle(string source) => Assert.Single(CedarParser.Parse(source));

    private static CedarExpr ParseBody(string expression) =>
        Assert.Single(ParseSingle($"permit(principal, action, resource) when {{ {expression} }};").Conditions).Body;
}