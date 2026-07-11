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
}
