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

    [Fact]
    public void Lower_ResourceAnyScope_NoDefault_InfersFromBatch()
    {
        var policies = Lower(
            """
            permit(principal, action == Action::"view", resource is documents);
            permit(principal, action == Action::"archive", resource);
            """);

        var policy = Assert.Single(policies);
        Assert.Equal("documents", policy.Resource, ignoreCase: true);
        Assert.Equal(["archive", "view"], policy.Actions.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Lower_ResourceAnyScope_NoDefault_InfersAcrossMultipleKinds()
    {
        var policies = Lower(
            """
            permit(principal, action == Action::"view", resource is documents);
            permit(principal, action == Action::"view", resource is folders);
            permit(principal, action == Action::"manage", resource);
            """);

        Assert.Equal(["documents", "folders"], policies.Select(p => p.Resource).OrderBy(r => r, StringComparer.OrdinalIgnoreCase));
        Assert.All(policies, p => Assert.Contains("manage", p.Actions.Keys));
    }

    [Fact]
    public void Lower_ResourceAnyScope_DefaultConfigured_OverridesInference()
    {
        var policies = Lower(
            """
            permit(principal, action == Action::"view", resource is documents);
            permit(principal, action == Action::"view", resource is folders);
            permit(principal, action == Action::"manage", resource);
            """,
            new CedarLoadOptions { DefaultResourceKind = "documents" });

        var manageResourceKinds = policies.Where(p => p.Actions.ContainsKey("manage")).Select(p => p.Resource);
        Assert.Equal(["documents"], manageResourceKinds);
    }

    [Fact]
    public async Task Lower_ResourceBareInScope_NoDefault_InfersFromBatchAsync()
    {
        var policies = Lower(
            """
            permit(principal, action == Action::"edit", resource is documents);
            permit(principal, action == Action::"view", resource in Folder::"shared");
            """);
        var policy = Assert.Single(policies);
        Assert.Equal("documents", policy.Resource, ignoreCase: true);
        Assert.Equal(["edit", "view"], policy.Actions.Keys.OrderBy(k => k));

        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("documents", "doc-1"), Parent = new EntityUid("Folder", "shared") },
        };
        var engine = await EngineWithGraphAsync(policies, entityParents);
        var decision = await engine.AuthorizeAsync(AegisPrincipal.Create("alice"), AegisResource.Create("documents", "doc-1"), "view");

        Assert.True(decision.Allowed);
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
    public void Lower_ActionInScope_NoActionGroupsConfigured_TreatedAsSingleAction()
    {
        var policies = Lower("permit(principal, action in Action::\"view\", resource is documents);");

        var policy = Assert.Single(policies);
        Assert.Equal(["view"], policy.Actions.Keys);
    }

    [Fact]
    public void Lower_ActionInScope_WithActionGroups_ExpandsToAllGroupMembers()
    {
        var actionGroups = new RelationshipGraph(
        [
            new EntityParent { Child = new EntityUid("Action", "approveChangeRequest"), Parent = new EntityUid("Action", "departmentManagement") },
            new EntityParent { Child = new EntityUid("Action", "manageStaff"), Parent = new EntityUid("Action", "departmentManagement") },
        ]);

        var policies = Lower(
            "permit(principal, action in Action::\"departmentManagement\", resource is departments);",
            new CedarLoadOptions { ActionGroups = actionGroups });

        var policy = Assert.Single(policies);
        Assert.Equal(
            ["approveChangeRequest", "departmentManagement", "manageStaff"],
            policy.Actions.Keys.OrderBy(k => k));
    }

    [Fact]
    public void Lower_ActionInScope_GroupWithNoMembers_StillMatchesGroupNameItself()
    {
        var actionGroups = new RelationshipGraph(
        [
            new EntityParent { Child = new EntityUid("Action", "departmentManagement"), Parent = new EntityUid("Action", "platformManagement") },
        ]);

        var policies = Lower(
            "permit(principal, action in Action::\"departmentManagement\", resource is departments);",
            new CedarLoadOptions { ActionGroups = actionGroups });

        var policy = Assert.Single(policies);
        Assert.Equal(["departmentManagement"], policy.Actions.Keys);
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

    // -- gap-closing scenario: action groups + resource-kind batch inference --
    //
    // A department-manager Role granted a bundle of permissions via a Cedar
    // action group (the "fixed permission catalog, extensible roles" shape),
    // plus a global platform-owner Role whose policy names no resource
    // kind at all and must be inferred from every other kind this same
    // batch establishes (Department, LeaveRequest).

    private const string GapClosingScenarioCedar =
        """
        permit(principal in Role::"dept-manager", action in Action::"departmentManagement", resource is Department);

        permit(principal, action == Action::"approveLeaveRequest", resource is LeaveRequest);

        permit(principal in Role::"platform-owner", action == Action::"auditAnything", resource);
        """;

    private static IReadOnlyList<ResourcePolicy> GapClosingScenarioPolicies()
    {
        var actionGroups = new RelationshipGraph(
        [
            new EntityParent { Child = new EntityUid("Action", "approveChangeRequest"), Parent = new EntityUid("Action", "departmentManagement") },
            new EntityParent { Child = new EntityUid("Action", "manageStaff"), Parent = new EntityUid("Action", "departmentManagement") },
        ]);

        return Lower(GapClosingScenarioCedar, new CedarLoadOptions { PrincipalEntityType = "Membership", ActionGroups = actionGroups });
    }

    [Fact]
    public async Task GapClosingScenario_DeptManagerRole_GrantsEveryActionGroupMemberAsync()
    {
        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("Membership", "mem-1"), Parent = new EntityUid("Role", "dept-manager") },
        };
        var engine = await EngineWithGraphAsync(GapClosingScenarioPolicies(), entityParents, principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-1");
        var resource = AegisResource.Create("Department", "dept-1");

        var approveChangeRequest = await engine.AuthorizeAsync(principal, resource, "approveChangeRequest");
        var manageStaff = await engine.AuthorizeAsync(principal, resource, "manageStaff");
        var departmentManagementItself = await engine.AuthorizeAsync(principal, resource, "departmentManagement");

        Assert.True(approveChangeRequest.Allowed);
        Assert.True(manageStaff.Allowed);
        Assert.True(departmentManagementItself.Allowed);
    }

    [Fact]
    public async Task GapClosingScenario_NonManagerMembership_DeniedDepartmentActionsAsync()
    {
        var engine = await EngineWithGraphAsync(GapClosingScenarioPolicies(), [], principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-1");
        var resource = AegisResource.Create("Department", "dept-1");

        var decision = await engine.AuthorizeAsync(principal, resource, "approveChangeRequest");

        Assert.False(decision.Allowed);
    }

    [Fact]
    public async Task GapClosingScenario_PlatformOwnerRole_GrantsAuditAcrossEveryInferredResourceKindAsync()
    {
        var entityParents = new[]
        {
            new EntityParent { Child = new EntityUid("Membership", "mem-3"), Parent = new EntityUid("Role", "platform-owner") },
        };
        var engine = await EngineWithGraphAsync(GapClosingScenarioPolicies(), entityParents, principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-3");

        var onDepartment = await engine.AuthorizeAsync(principal, AegisResource.Create("Department", "dept-1"), "auditAnything");
        var onLeaveRequest = await engine.AuthorizeAsync(principal, AegisResource.Create("LeaveRequest", "lr-1"), "auditAnything");

        Assert.True(onDepartment.Allowed);
        Assert.True(onLeaveRequest.Allowed);
    }

    [Fact]
    public async Task GapClosingScenario_NonOwnerMembership_DeniedAuditAsync()
    {
        var engine = await EngineWithGraphAsync(GapClosingScenarioPolicies(), [], principalEntityType: "Membership");
        var principal = AegisPrincipal.Create("mem-3");

        var decision = await engine.AuthorizeAsync(principal, AegisResource.Create("Department", "dept-1"), "auditAnything");

        Assert.False(decision.Allowed);
    }
}