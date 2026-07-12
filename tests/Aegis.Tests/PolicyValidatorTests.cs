using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

public class PolicyValidatorTests
{
    private static ResourcePolicy ValidPolicy() => new()
    {
        Resource = "invoices",
        Source = "invoices.yaml",
        Actions = new Dictionary<string, ActionRule>
        {
            ["view"] = new() { Allow = new AllowRule { Roles = ["Finance"] } },
            ["approve"] = new() { Allow = new AllowRule { When = "principal.department == resource.department" } },
        },
    };

    [Fact]
    public void Validate_ValidPolicies_DoesNotThrow()
    {
        var exception = Record.Exception(() => PolicyValidator.Validate([ValidPolicy()]));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_InvalidWhenExpression_ThrowsWithResourceAndActionInMessage()
    {
        var policy = ValidPolicy();
        policy.Actions["approve"].Allow!.When = "principal.department ==";

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("invoices", ex.Errors[0]);
        Assert.Contains("approve", ex.Errors[0]);
    }

    [Fact]
    public void Validate_ActionRuleWithoutAllow_ThrowsMentioningTypoLikelihood()
    {
        var policy = ValidPolicy();
        policy.Actions["delete"] = new ActionRule { Allow = null }; // e.g. a typo'd "alow:" key

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("delete", ex.Errors[0]);
        Assert.Contains("typo", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DuplicateResourceAcrossPolicies_ThrowsNamingBothSources()
    {
        var first = new ResourcePolicy { Resource = "invoices", Source = "a.yaml" };
        var second = new ResourcePolicy { Resource = "invoices", Source = "b.yaml" };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([first, second]));

        Assert.Single(ex.Errors);
        Assert.Contains("a.yaml", ex.Errors[0]);
        Assert.Contains("b.yaml", ex.Errors[0]);
    }

    [Fact]
    public void Validate_DuplicateResourceIsCaseInsensitive()
    {
        var first = new ResourcePolicy { Resource = "Invoices", Source = "a.yaml" };
        var second = new ResourcePolicy { Resource = "invoices", Source = "b.yaml" };

        Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([first, second]));
    }

    [Fact]
    public void Validate_MultipleProblems_AggregatesAllOfThemNotJustTheFirst()
    {
        var badExpression = ValidPolicy();
        badExpression.Actions["approve"].Allow!.When = "principal.department ==";

        var missingAllow = new ResourcePolicy
        {
            Resource = "documents",
            Source = "documents.yaml",
            Actions = new Dictionary<string, ActionRule> { ["view"] = new() { Allow = null } },
        };

        var duplicateA = new ResourcePolicy { Resource = "loans", Source = "a.yaml" };
        var duplicateB = new ResourcePolicy { Resource = "loans", Source = "b.yaml" };

        var ex = Assert.Throws<PolicyValidationException>(
            () => PolicyValidator.Validate([badExpression, missingAllow, duplicateA, duplicateB]));

        Assert.Equal(3, ex.Errors.Count);
    }
}