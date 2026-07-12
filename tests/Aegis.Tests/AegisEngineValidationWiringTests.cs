using Aegis.Policies;

using Xunit;

namespace Aegis.Tests;

/// <summary>
/// PolicyValidatorTests covers the validation rules themselves; this
/// covers that every AegisEngine construction path actually runs them.
/// </summary>
public class AegisEngineValidationWiringTests
{
    private sealed class InvalidPolicyProvider : IPolicyProvider
    {
        public Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourcePolicy>>(
            [
                new ResourcePolicy
                {
                    Resource = "invoices",
                    Actions = new Dictionary<string, ActionRule>
                    {
                        ["approve"] = new() { Allow = new AllowRule { When = "principal.department ==" } },
                    },
                },
            ]);
    }

    [Fact]
    public void Create_InvalidPolicyDirectory_ThrowsPolicyValidationException()
    {
        var fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "InvalidPolicies");

        Assert.Throws<PolicyValidationException>(() => AegisEngine.Create(fixturesPath));
    }

    [Fact]
    public void FromPolicies_InvalidPolicy_ThrowsPolicyValidationException()
    {
        var policy = new ResourcePolicy
        {
            Resource = "invoices",
            Actions = new Dictionary<string, ActionRule> { ["view"] = new() { Allow = null } },
        };

        Assert.Throws<PolicyValidationException>(() => AegisEngine.FromPolicies([policy]));
    }

    [Fact]
    public async Task CreateAsync_InvalidPolicyProvider_ThrowsPolicyValidationExceptionAsync()
    {
        await Assert.ThrowsAsync<PolicyValidationException>(
            () => AegisEngine.CreateAsync(new InvalidPolicyProvider()));
    }
}