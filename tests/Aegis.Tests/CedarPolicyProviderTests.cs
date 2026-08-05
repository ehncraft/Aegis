using Aegis.Cedar;
using Aegis.Policies;
using Aegis.Relationships;

using Xunit;

namespace Aegis.Tests;

public class CedarPolicyProviderTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string CedarDirectory => Path.Combine(FixturesPath, "Cedar");

    [Fact]
    public void LoadDirectory_MergesPermitAndForbidAcrossFiles()
    {
        var policies = CedarPolicyProvider.LoadDirectory(CedarDirectory);

        var policy = Assert.Single(policies);
        var rule = policy.Actions["view"];
        Assert.NotNull(rule.Allow);
        Assert.NotNull(rule.Forbid);
        Assert.Equal("cedar", rule.Allow!.Language);
        Assert.Equal("cedar", rule.Forbid!.Language);
    }

    [Fact]
    public async Task LoadPoliciesAsync_MergesPermitAndForbidAcrossFilesAsync()
    {
        var provider = CedarPolicyProvider.Create(CedarDirectory);

        var policies = await provider.LoadPoliciesAsync();

        var policy = Assert.Single(policies);
        Assert.True(policy.Actions["view"].Allow is not null);
        Assert.True(policy.Actions["view"].Forbid is not null);
    }

    [Fact]
    public async Task LoadPoliciesAsync_EndToEnd_ForbidOverridesMatchingPermitAsync()
    {
        var provider = CedarPolicyProvider.Create(CedarDirectory);
        var policies = await provider.LoadPoliciesAsync();
        var engine = AegisEngine.FromPolicies(policies);

        var suspended = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["suspended"] = true });
        var active = AegisPrincipal.Create("bob", attributes: new Dictionary<string, object?> { ["suspended"] = false });
        var resource = AegisResource.Create("documents", "doc-1");

        var suspendedDecision = await engine.AuthorizeAsync(suspended, resource, "view");
        var activeDecision = await engine.AuthorizeAsync(active, resource, "view");

        Assert.False(suspendedDecision.Allowed);
        Assert.True(activeDecision.Allowed);
    }

    [Fact]
    public void LoadDirectory_SyntaxError_ThrowsPolicyLoadExceptionWithFilePath()
    {
        var directory = Path.Combine(FixturesPath, "CedarSyntaxError");

        var ex = Assert.Throws<PolicyLoadException>(() => CedarPolicyProvider.LoadDirectory(directory));

        Assert.Contains("broken.cedar", ex.PolicySource);
        Assert.IsType<CedarSyntaxException>(ex.InnerException);
    }

    [Fact]
    public void LoadDirectory_LoweringError_ThrowsPolicyLoadExceptionWithDirectoryPath()
    {
        var directory = Path.Combine(FixturesPath, "CedarLoweringError");

        var ex = Assert.Throws<PolicyLoadException>(() => CedarPolicyProvider.LoadDirectory(directory));

        Assert.Equal(directory, ex.PolicySource);
        Assert.IsType<CedarLoweringException>(ex.InnerException);
    }

    [Fact]
    public void LoadDirectory_ThrowsWhenDirectoryMissing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => CedarPolicyProvider.LoadDirectory(Path.Combine(FixturesPath, "does-not-exist")));
    }

    [Fact]
    public void LoadFromText_MergesPermitAndForbidAcrossTexts()
    {
        var policies = CedarPolicyProvider.LoadFromText(
            ["permit(principal, action == Action::\"view\", resource is documents);",
             "forbid(principal, action == Action::\"view\", resource is documents) when { principal.suspended };"],
            ["row-1", "row-2"]);

        var policy = Assert.Single(policies);
        var rule = policy.Actions["view"];
        Assert.NotNull(rule.Allow);
        Assert.NotNull(rule.Forbid);
    }

    [Fact]
    public async Task LoadFromText_EndToEnd_ForbidOverridesMatchingPermitAsync()
    {
        var policies = CedarPolicyProvider.LoadFromText(
            ["permit(principal, action == Action::\"view\", resource is documents);",
             "forbid(principal, action == Action::\"view\", resource is documents) when { principal.suspended };"],
            ["row-1", "row-2"]);
        var engine = AegisEngine.FromPolicies(policies);

        var suspended = AegisPrincipal.Create("alice", attributes: new Dictionary<string, object?> { ["suspended"] = true });
        var active = AegisPrincipal.Create("bob", attributes: new Dictionary<string, object?> { ["suspended"] = false });
        var resource = AegisResource.Create("documents", "doc-1");

        var suspendedDecision = await engine.AuthorizeAsync(suspended, resource, "view");
        var activeDecision = await engine.AuthorizeAsync(active, resource, "view");

        Assert.False(suspendedDecision.Allowed);
        Assert.True(activeDecision.Allowed);
    }

    [Fact]
    public void LoadFromText_SyntaxError_ThrowsPolicyLoadExceptionWithRowSource()
    {
        var ex = Assert.Throws<PolicyLoadException>(() =>
            CedarPolicyProvider.LoadFromText(["not valid cedar at all"], ["row-broken"]));

        Assert.Equal("row-broken", ex.PolicySource);
        Assert.IsType<CedarSyntaxException>(ex.InnerException);
    }

    [Fact]
    public void LoadFromText_MismatchedListLengths_ThrowsArgumentException() =>
        Assert.Throws<ArgumentException>(() =>
            CedarPolicyProvider.LoadFromText(["permit(principal, action, resource);"], []));

    // -- acceptance test: the whole milestone, wired through the real
    // AegisEngine.CreateAsync(IPolicyProvider) entry point rather than
    // FromPolicies -- if this doesn't pass cleanly, the gap is somewhere
    // in the lowering/evaluation pipeline, not a test-writing gap.

    [Fact]
    public async Task CreateAsync_DrivingScenario_ApprovesWithinDepartmentAndPermissionAsync()
    {
        var provider = CedarPolicyProvider.Create(
            Path.Combine(FixturesPath, "CedarDrivingScenario"),
            new CedarLoadOptions { DefaultResourceKind = "LeaveRequest", PrincipalEntityType = "Membership" });
        var engine = await AegisEngine.CreateAsync(provider, principalEntityType: "Membership");

        var principal = AegisPrincipal.Create("mem-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1", ["status"] = "active" });
        var resource = AegisResource.Create("LeaveRequest", "lr-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        var context = new Dictionary<string, object?> { ["permissions"] = new[] { "approve_leave" } };

        var decision = await engine.AuthorizeAsync(principal, resource, "approveLeaveRequest", actionProperties: null, context: context);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task CreateAsync_DrivingScenario_SuspendedMembershipDeniedDespiteMatchingPermitAsync()
    {
        var provider = CedarPolicyProvider.Create(
            Path.Combine(FixturesPath, "CedarDrivingScenario"),
            new CedarLoadOptions { DefaultResourceKind = "LeaveRequest", PrincipalEntityType = "Membership" });
        var engine = await AegisEngine.CreateAsync(provider, principalEntityType: "Membership");

        var principal = AegisPrincipal.Create("mem-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1", ["status"] = "suspended" });
        var resource = AegisResource.Create("LeaveRequest", "lr-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        var context = new Dictionary<string, object?> { ["permissions"] = new[] { "approve_leave" } };

        var decision = await engine.AuthorizeAsync(principal, resource, "approveLeaveRequest", actionProperties: null, context: context);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task CreateAsync_DrivingScenario_SystemAdminReviewsAcrossOrganizationsAsync()
    {
        var provider = CedarPolicyProvider.Create(
            Path.Combine(FixturesPath, "CedarDrivingScenario"),
            new CedarLoadOptions { DefaultResourceKind = "LeaveRequest", PrincipalEntityType = "Membership" });
        var engine = await AegisEngine.CreateAsync(provider, principalEntityType: "Membership");
        var withGraph = await engine.WithRelationshipsAsync(new InMemoryRelationshipProvider(
        [
            new EntityParent
            {
                Child = new EntityUid("Membership", "mem-2"),
                Parent = new EntityUid("Role", "system-admin"),
            },
        ]));

        var principal = AegisPrincipal.Create("mem-2");
        var resource = AegisResource.Create("LeaveRequest", "change-1");

        var decision = await withGraph.AuthorizeAsync(principal, resource, "reviewPlatformChangeRequest");

        Assert.True(decision.Allowed);
    }
}