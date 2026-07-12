using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class PolicyEvaluatorTests
{
    private static ResourcePolicy InvoicePolicy() => new()
    {
        Resource = "invoices",
        Name = "invoice-policy",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            ["approve"] = new()
            {
                Allow = new AllowRule { When = "principal.department == resource.department" },
            },
        },
    };

    [Fact]
    public void Allows_WhenPrincipalHasRequiredRole()
    {
        var evaluator = new PolicyEvaluator([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = evaluator.Authorize(principal, resource, "view");

        Assert.True(decision.Allowed);
        Assert.Equal("invoice-policy", decision.Explanation.MatchedPolicy);
        Assert.Equal("view", decision.Explanation.MatchedRule);
    }

    [Fact]
    public void Denies_WhenPrincipalLacksRequiredRole()
    {
        var evaluator = new PolicyEvaluator([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("bob", roles: ["Sales"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = evaluator.Authorize(principal, resource, "view");

        Assert.False(decision.Allowed);
        Assert.Single(decision.Explanation.Conditions);
        Assert.False(decision.Explanation.Conditions[0].Result);
    }

    [Fact]
    public void Allows_WhenAttributeConditionMatches()
    {
        var evaluator = new PolicyEvaluator([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var resource = AegisResource.Create("invoices", "INV-1",
            attributes: new Dictionary<string, object?> { ["department"] = "finance" });

        var decision = evaluator.Authorize(principal, resource, "approve");

        Assert.True(decision.Allowed);
        var condition = Assert.Single(decision.Explanation.Conditions);
        Assert.Equal("principal.department == resource.department", condition.Expression);
        Assert.True(condition.Result);
    }

    [Fact]
    public void Denies_WhenAttributeConditionDoesNotMatch()
    {
        var evaluator = new PolicyEvaluator([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("alice",
            attributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var resource = AegisResource.Create("invoices", "INV-1",
            attributes: new Dictionary<string, object?> { ["department"] = "engineering" });

        var decision = evaluator.Authorize(principal, resource, "approve");

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Denies_WhenNoPolicyMatchesResource()
    {
        var evaluator = new PolicyEvaluator([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create("documents", "DOC-1");

        var decision = evaluator.Authorize(principal, resource, "view");

        Assert.False(decision.Allowed);
        Assert.Contains("documents", decision.Explanation.Reason);
    }

    [Fact]
    public void Denies_WhenNoRuleMatchesAction()
    {
        var evaluator = new PolicyEvaluator([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = evaluator.Authorize(principal, resource, "delete");

        Assert.False(decision.Allowed);
        Assert.Contains("delete", decision.Explanation.Reason);
    }

    [Fact]
    public void StaticRolesOnly_ExplanationIsUnchangedIntersectsString()
    {
        var evaluator = new PolicyEvaluator([InvoicePolicy()]);
        var principal = AegisPrincipal.Create("alice", roles: ["Finance"]);
        var resource = AegisResource.Create("invoices", "INV-1");

        var decision = evaluator.Authorize(principal, resource, "view");

        var condition = Assert.Single(decision.Explanation.Conditions);
        Assert.Equal("principal.roles intersects [Finance]", condition.Expression);
    }

    private static ResourcePolicy PolicyWithOwnerDerivedRole() => new()
    {
        Resource = "loans",
        Name = "loan-policy",
        DerivedRoles = new Dictionary<string, DerivedRoleDefinition>
        {
            ["owner"] = new() { When = "principal.id == resource.ownerId" },
        },
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["owner"] } },
            ["manage"] = new() { Allow = new AllowRule { Roles = ["Admin", "owner"] } },
        },
    };

    [Fact]
    public void DerivedRole_Allows_WhenConditionMatches()
    {
        var evaluator = new PolicyEvaluator([PolicyWithOwnerDerivedRole()]);
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create(
            "loans", "LOAN-1", attributes: new Dictionary<string, object?> { ["ownerId"] = "alice" });

        var decision = evaluator.Authorize(principal, resource, "view");

        Assert.True(decision.Allowed);
        var condition = Assert.Single(decision.Explanation.Conditions);
        Assert.Equal("derived role 'owner': principal.id == resource.ownerId", condition.Expression);
        Assert.True(condition.Result);
    }

    [Fact]
    public void DerivedRole_Denies_WhenConditionDoesNotMatch()
    {
        var evaluator = new PolicyEvaluator([PolicyWithOwnerDerivedRole()]);
        var principal = AegisPrincipal.Create("bob");
        var resource = AegisResource.Create(
            "loans", "LOAN-1", attributes: new Dictionary<string, object?> { ["ownerId"] = "alice" });

        var decision = evaluator.Authorize(principal, resource, "view");

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void MixedStaticAndDerivedRoles_EachGetsOwnConditionExplanation()
    {
        var evaluator = new PolicyEvaluator([PolicyWithOwnerDerivedRole()]);
        var principal = AegisPrincipal.Create("alice", roles: ["Admin"]);
        var resource = AegisResource.Create(
            "loans", "LOAN-1", attributes: new Dictionary<string, object?> { ["ownerId"] = "someone-else" });

        var decision = evaluator.Authorize(principal, resource, "manage");

        Assert.True(decision.Allowed);
        Assert.Equal(2, decision.Explanation.Conditions.Count);
        Assert.Equal("principal.roles contains 'Admin'", decision.Explanation.Conditions[0].Expression);
        Assert.True(decision.Explanation.Conditions[0].Result);
        Assert.Equal(
            "derived role 'owner': principal.id == resource.ownerId", decision.Explanation.Conditions[1].Expression);
        Assert.False(decision.Explanation.Conditions[1].Result);
    }

    [Fact]
    public void Variable_ResolvesInsideWhenCondition()
    {
        var policy = new ResourcePolicy
        {
            Resource = "accounts",
            Variables = new Dictionary<string, string> { ["isFinance"] = "principal.department == 'finance'" },
            Actions = new Dictionary<string, ActionRule>
            {
                ["view"] = new() { Allow = new AllowRule { When = "${isFinance}" } },
            },
        };
        var evaluator = new PolicyEvaluator([policy]);
        var principal = AegisPrincipal.Create(
            "alice", attributes: new Dictionary<string, object?> { ["department"] = "finance" });
        var resource = AegisResource.Create("accounts", "ACC-1");

        var decision = evaluator.Authorize(principal, resource, "view");

        Assert.True(decision.Allowed);
        var condition = Assert.Single(decision.Explanation.Conditions);
        Assert.Equal("${isFinance}", condition.Expression);
    }

    [Fact]
    public void Variable_ResolvesInsideDerivedRoleCondition()
    {
        var policy = new ResourcePolicy
        {
            Resource = "accounts",
            Variables = new Dictionary<string, string> { ["sameId"] = "principal.id == resource.ownerId" },
            DerivedRoles = new Dictionary<string, DerivedRoleDefinition> { ["owner"] = new() { When = "${sameId}" } },
            Actions = new Dictionary<string, ActionRule>
            {
                ["view"] = new() { Allow = new AllowRule { Roles = ["owner"] } },
            },
        };
        var evaluator = new PolicyEvaluator([policy]);
        var principal = AegisPrincipal.Create("alice");
        var resource = AegisResource.Create(
            "accounts", "ACC-1", attributes: new Dictionary<string, object?> { ["ownerId"] = "alice" });

        var decision = evaluator.Authorize(principal, resource, "view");

        Assert.True(decision.Allowed);
    }
}