using Aegis.Cedar;
using Aegis.Policies;
using Aegis.Relationships;

using Xunit;

namespace Aegis.Tests;

public class CedarPolicySetLowererTests
{
    private static IReadOnlyList<ResourcePolicy> Lower(string source, CedarLoadOptions? options = null) =>
        CedarPolicySetLowerer.Lower(CedarParser.Parse(source), options ?? new CedarLoadOptions());

    private static AegisEngine Engine(IReadOnlyList<ResourcePolicy> policies, string principalEntityType = "User") =>
        AegisEngine.FromPolicies(policies, principalEntityType: principalEntityType);

    private static async Task<AegisEngine> EngineWithGraphAsync(
        IReadOnlyList<ResourcePolicy> policies, IReadOnlyList<EntityParent> entityParents, string principalEntityType = "User") =>
        await AegisEngine.FromPolicies(policies, principalEntityType: principalEntityType)
            .WithRelationshipsAsync(new InMemoryRelationshipProvider(entityParents));

    // -- resource scope -----------------------------------------------------

    [Fact]
    public void Lower_ResourceAnyScope_WithDefaultResourceKind_Succeeds()
    {
        var policies = Lower(
            "permit(principal, action == Action::\"view\", resource);",
            new CedarLoadOptions { DefaultResourceKind = "documents" });

        var policy = Assert.Single(policies);
        Assert.Equal("documents", policy.Resource, ignoreCase: true);
    }

    [Fact]
    public void Lower_ResourceAnyScope_WithoutDefaultResourceKind_Throws()
    {
        Assert.Throws<CedarLoweringException>(() => Lower("permit(principal, action == Action::\"view\", resource);"));
    }

    [Fact]
    public void Lower_ResourceEqScope_DispatchesByEntityType()
    {
        var policies = Lower("permit(principal, action == Action::\"view\", resource == documents::\"doc-1\");");

        Assert.Equal("documents", Assert.Single(policies).Resource, ignoreCase: true);
    }

    [Fact]
    public async Task Lower_ResourceEqScope_MatchingId_AllowsAsync()
    {
        var policies = Lower("permit(principal, action == Action::\"view\", resource == documents::\"doc-1\");");
        var engine = Engine(policies);

        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Lower_ResourceEqScope_MismatchedId_DeniesAsync()
    {
        var policies = Lower("permit(principal, action == Action::\"view\", resource == documents::\"doc-1\");");
        var engine = Engine(policies);

        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-2"), "view");

        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Lower_ResourceIsScope_DispatchesByLastTypeSegment()
    {
        var policies = Lower("permit(principal, action == Action::\"view\", resource is documents);");

        Assert.Equal("documents", Assert.Single(policies).Resource, ignoreCase: true);
    }

    [Fact]
    public async Task Lower_ResourceIsInScope_DispatchesByTypeAndAppliesRelationshipCheckAsync()
    {
        var policies = Lower(
            "permit(principal, action == Action::\"view\", resource is documents in Folder::\"shared\");");
        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("documents", "doc-1"), Parent = new EntityUid("Folder", "shared") },
        };
        var engine = await EngineWithGraphAsync(policies, entityParents);

        var allowed = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");
        var denied = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-2"), "view");

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    [Fact]
    public async Task Lower_ResourceBareInScope_WithDefaultResourceKind_AppliesRelationshipCheckAsync()
    {
        var policies = Lower(
            "permit(principal, action == Action::\"view\", resource in Folder::\"shared\");",
            new CedarLoadOptions { DefaultResourceKind = "documents" });
        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("documents", "doc-1"), Parent = new EntityUid("Folder", "shared") },
        };
        var engine = await EngineWithGraphAsync(policies, entityParents);

        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Lower_ResourceBareInScope_WithoutDefaultResourceKind_Throws()
    {
        Assert.Throws<CedarLoweringException>(
            () => Lower("permit(principal, action == Action::\"view\", resource in Folder::\"shared\");"));
    }

    // -- action scope -------------------------------------------------------

    [Fact]
    public void Lower_ActionAnyScope_WithSiblingVocabulary_ExpandsToEveryKnownAction()
    {
        var policies = Lower(
            """
            permit(principal, action == Action::"view", resource is documents);
            permit(principal, action == Action::"edit", resource is documents);
            permit(principal in Group::"admins", action, resource is documents);
            """);

        var policy = Assert.Single(policies);
        Assert.Equal(["edit", "view"], policy.Actions.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Lower_ActionAnyScope_WithNoSiblingVocabulary_Throws()
    {
        Assert.Throws<CedarLoweringException>(
            () => Lower("permit(principal in Group::\"admins\", action, resource is documents);"));
    }

    [Fact]
    public void Lower_ActionInSetScope_DispatchesToEachNamedAction()
    {
        var policies = Lower(
            "permit(principal, action in [Action::\"view\", Action::\"edit\"], resource is documents);");

        var policy = Assert.Single(policies);
        Assert.Equal(["edit", "view"], policy.Actions.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Lower_ActionInScope_TreatedAsSingleAction()
    {
        var policies = Lower("permit(principal, action in Action::\"view\", resource is documents);");

        var policy = Assert.Single(policies);
        Assert.Equal(["view"], policy.Actions.Keys);
    }

    // -- principal scope ------------------------------------------------

    [Fact]
    public async Task Lower_PrincipalAnyScope_Unconditional_AllowsAsync()
    {
        var policies = Lower("permit(principal, action == Action::\"view\", resource is documents);");
        var engine = Engine(policies);

        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Lower_PrincipalEqScope_MatchingId_AllowsAsync()
    {
        var policies = Lower(
            "permit(principal == User::\"alice\", action == Action::\"view\", resource is documents);");
        var engine = Engine(policies);

        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task Lower_PrincipalEqScope_MismatchedId_DeniesAsync()
    {
        var policies = Lower(
            "permit(principal == User::\"alice\", action == Action::\"view\", resource is documents);");
        var engine = Engine(policies);

        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("bob"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task Lower_PrincipalIsScope_MatchingType_AllowsAsync()
    {
        var policies = Lower("permit(principal is User, action == Action::\"view\", resource is documents);");
        var engine = Engine(policies, principalEntityType: "User");

        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public void Lower_PrincipalIsScope_MismatchedType_Throws()
    {
        Assert.Throws<CedarLoweringException>(() => Lower(
            "permit(principal is User, action == Action::\"view\", resource is documents);",
            new CedarLoadOptions { PrincipalEntityType = "Membership" }));
    }

    [Fact]
    public async Task Lower_PrincipalInScope_AppliesRelationshipCheckAsync()
    {
        var policies = Lower(
            "permit(principal in Group::\"admins\", action == Action::\"view\", resource is documents);");
        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("User", "alice"), Parent = new EntityUid("Group", "admins") },
        };
        var engine = await EngineWithGraphAsync(policies, entityParents);

        var allowed = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");
        var denied = await engine.AuthorizeAsync(AegisPrincipal.Create("bob"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(allowed.Allowed);
        Assert.False(denied.Allowed);
    }

    // -- N permit / M forbid merge ----------------------------------------

    [Fact]
    public async Task Lower_MultiplePermitsSameAction_AllowsIfAnyMatchesAsync()
    {
        var policies = Lower(
            """
            permit(principal == User::"alice", action == Action::"view", resource is documents);
            permit(principal == User::"bob", action == Action::"view", resource is documents);
            """);
        var engine = Engine(policies);

        var alice = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");
        var bob = await engine.AuthorizeAsync(AegisPrincipal.Create("bob"), AegisResource.Create("documents", "doc-1"), "view");
        var carol = await engine.AuthorizeAsync(AegisPrincipal.Create("carol"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(alice.Allowed);
        Assert.True(bob.Allowed);
        Assert.False(carol.Allowed);
    }

    [Fact]
    public async Task Lower_ForbidMatchesOneOfManyForbids_OverridesMatchingPermitAsync()
    {
        var policies = Lower(
            """
            permit(principal, action == Action::"view", resource is documents);
            forbid(principal == User::"alice", action == Action::"view", resource is documents) when { true };
            forbid(principal == User::"bob", action == Action::"view", resource is documents) when { true };
            """);
        var engine = Engine(policies);

        var alice = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");
        var carol = await engine.AuthorizeAsync(AegisPrincipal.Create("carol"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.False(alice.Allowed);
        Assert.True(carol.Allowed);
    }

    // -- driving scenario: department-scoped Staff roles -------------------
    //
    // Organization -> Department -> Membership hierarchy; a global,
    // non-department-scoped system-Admin role; forbid always overriding a
    // matching permit. Validates the design end-to-end, not OneId's actual
    // policy set (that's separate, later work in backend-ronford-one-id).

    private const string DrivingScenarioCedar =
        """
        permit(principal, action == Action::"approveLeaveRequest", resource)
        when {
            principal.departmentId == resource.departmentId &&
            context.permissions.contains("approve_leave")
        };

        permit(principal in Role::"system-admin", action == Action::"reviewPlatformChangeRequest", resource);

        forbid(principal, action == Action::"approveLeaveRequest", resource)
        when { principal.status == "suspended" };
        """;

    private static IReadOnlyList<ResourcePolicy> DrivingScenarioPolicies() =>
        Lower(DrivingScenarioCedar, new CedarLoadOptions { DefaultResourceKind = "LeaveRequest", PrincipalEntityType = "Membership" });

    [Fact]
    public async Task DrivingScenario_DepartmentMatchAndPermission_AllowsAsync()
    {
        var engine = Engine(DrivingScenarioPolicies(), principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1", ["status"] = "active" });
        var resource = AegisResource.Create("LeaveRequest", "lr-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        var context = new Dictionary<string, object?> { ["permissions"] = new[] { "approve_leave" } };

        var decision = await engine.AuthorizeAsync(principal, resource, "approveLeaveRequest", actionProperties: null, context: context);

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task DrivingScenario_DepartmentMismatch_DeniesAsync()
    {
        var engine = Engine(DrivingScenarioPolicies(), principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-2", ["status"] = "active" });
        var resource = AegisResource.Create("LeaveRequest", "lr-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        var context = new Dictionary<string, object?> { ["permissions"] = new[] { "approve_leave" } };

        var decision = await engine.AuthorizeAsync(principal, resource, "approveLeaveRequest", actionProperties: null, context: context);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task DrivingScenario_MissingPermission_DeniesAsync()
    {
        var engine = Engine(DrivingScenarioPolicies(), principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1", ["status"] = "active" });
        var resource = AegisResource.Create("LeaveRequest", "lr-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        var context = new Dictionary<string, object?> { ["permissions"] = new[] { "view_reports" } };

        var decision = await engine.AuthorizeAsync(principal, resource, "approveLeaveRequest", actionProperties: null, context: context);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task DrivingScenario_SuspendedMembership_DeniedDespiteMatchingPermitAsync()
    {
        var engine = Engine(DrivingScenarioPolicies(), principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1", ["status"] = "suspended" });
        var resource = AegisResource.Create("LeaveRequest", "lr-1",
            attributes: new Dictionary<string, object?> { ["departmentId"] = "dept-1" });
        var context = new Dictionary<string, object?> { ["permissions"] = new[] { "approve_leave" } };

        var decision = await engine.AuthorizeAsync(principal, resource, "approveLeaveRequest", actionProperties: null, context: context);

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task DrivingScenario_SystemAdminInRole_AllowsAcrossOrganizationsAsync()
    {
        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("Membership", "mem-2"), Parent = new EntityUid("Role", "system-admin") },
        };
        var engine = await EngineWithGraphAsync(DrivingScenarioPolicies(), entityParents, principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-2");
        var resource = AegisResource.Create("LeaveRequest", "change-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "reviewPlatformChangeRequest");

        Assert.True(decision.Allowed);
    }

    [Fact]
    public async Task DrivingScenario_SystemAdminInRole_MismatchedPrincipalEntityType_DeniesAsync()
    {
        // Graph tuple is keyed "Membership", but the engine defaults to "User" --
        // proves the two must agree, exercising the PolicyEvaluator fix end-to-end.
        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("Membership", "mem-2"), Parent = new EntityUid("Role", "system-admin") },
        };
        var engine = await EngineWithGraphAsync(DrivingScenarioPolicies(), entityParents);
        var principal = AegisPrincipal.Create("mem-2");
        var resource = AegisResource.Create("LeaveRequest", "change-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "reviewPlatformChangeRequest");

        Assert.False(decision.Allowed);
    }
}