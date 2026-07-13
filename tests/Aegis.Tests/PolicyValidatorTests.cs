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

    [Fact]
    public void Validate_ValidVariablesAndDerivedRoles_DoesNotThrow()
    {
        var policy = ValidPolicy();
        policy.Variables["isFinance"] = "principal.department == 'finance'";
        policy.DerivedRoles["owner"] = new DerivedRoleDefinition { When = "principal.id == resource.ownerId" };
        policy.Actions["view"].Allow!.When = "${isFinance}";

        var exception = Record.Exception(() => PolicyValidator.Validate([policy]));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_UndefinedVariableInActionWhen_Throws()
    {
        var policy = ValidPolicy();
        policy.Actions["approve"].Allow!.When = "${missing}";

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("missing", ex.Errors[0]);
        Assert.Contains("approve", ex.Errors[0]);
    }

    [Fact]
    public void Validate_UndefinedVariableInAnotherVariable_Throws()
    {
        var policy = ValidPolicy();
        policy.Variables["isFinance"] = "${missing}";

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("isFinance", ex.Errors[0]);
        Assert.Contains("missing", ex.Errors[0]);
    }

    [Fact]
    public void Validate_UndefinedVariableInDerivedRole_Throws()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["owner"] = new DerivedRoleDefinition { When = "${missing}" };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("owner", ex.Errors[0]);
        Assert.Contains("missing", ex.Errors[0]);
    }

    [Fact]
    public void Validate_DerivedRoleWithoutWhen_ThrowsMentioningMissingCondition()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["owner"] = new DerivedRoleDefinition { When = "" };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("owner", ex.Errors[0]);
    }

    [Fact]
    public void Validate_InvalidVariableExpression_ThrowsWithVariableNameInMessage()
    {
        var policy = ValidPolicy();
        policy.Variables["isFinance"] = "principal.department ==";

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("isFinance", ex.Errors[0]);
    }

    [Fact]
    public void Validate_InvalidDerivedRoleExpression_ThrowsWithRoleNameInMessage()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["owner"] = new DerivedRoleDefinition { When = "principal.id ==" };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("owner", ex.Errors[0]);
    }

    [Fact]
    public void Validate_DirectSelfReferencingVariable_ThrowsCircularReferenceError()
    {
        var policy = ValidPolicy();
        policy.Variables["isFinance"] = "${isFinance}";

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("circular", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isFinance", ex.Errors[0]);
    }

    [Fact]
    public void Validate_TransitiveCircularVariableReference_ThrowsCircularReferenceError()
    {
        var policy = ValidPolicy();
        policy.Variables["a"] = "${b}";
        policy.Variables["b"] = "${a}";

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("circular", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ValidReBacDerivedRole_DoesNotThrow()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["committeeMember"] = new DerivedRoleDefinition
        {
            In = new DerivedRoleHierarchyCheck { Type = "Group", Id = "resource.committeeId" },
        };

        var exception = Record.Exception(() => PolicyValidator.Validate([policy]));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_DerivedRoleWithBothWhenAndIn_Throws()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["owner"] = new DerivedRoleDefinition
        {
            When = "principal.id == resource.ownerId",
            In = new DerivedRoleHierarchyCheck { Type = "Group", Id = "'audit-committee'" },
        };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("both", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DerivedRoleWithNeitherWhenNorIn_Throws()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["owner"] = new DerivedRoleDefinition();

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("owner", ex.Errors[0]);
    }

    [Fact]
    public void Validate_InMissingType_Throws()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["committeeMember"] = new DerivedRoleDefinition
        {
            In = new DerivedRoleHierarchyCheck { Id = "'audit-committee'" },
        };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("type", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_InMissingId_Throws()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["committeeMember"] = new DerivedRoleDefinition
        {
            In = new DerivedRoleHierarchyCheck { Type = "Group" },
        };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("id", ex.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_InIdInvalidExpression_Throws()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["committeeMember"] = new DerivedRoleDefinition
        {
            In = new DerivedRoleHierarchyCheck { Type = "Group", Id = "resource.committeeId ==" },
        };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("committeeMember", ex.Errors[0]);
    }

    [Fact]
    public void Validate_InIdUndefinedVariable_Throws()
    {
        var policy = ValidPolicy();
        policy.DerivedRoles["committeeMember"] = new DerivedRoleDefinition
        {
            In = new DerivedRoleHierarchyCheck { Type = "Group", Id = "${missing}" },
        };

        var ex = Assert.Throws<PolicyValidationException>(() => PolicyValidator.Validate([policy]));

        Assert.Single(ex.Errors);
        Assert.Contains("missing", ex.Errors[0]);
    }
}