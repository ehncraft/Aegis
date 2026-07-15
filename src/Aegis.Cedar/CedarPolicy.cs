namespace Aegis.Cedar;

internal enum CedarEffect
{
    Permit,
    Forbid,
}

/// <summary>An entity reference such as <c>User::"alice"</c> or the namespaced <c>MyApp::Group::"admins"</c>.</summary>
internal sealed class EntityRef(IReadOnlyList<string> type, string id)
{
    public IReadOnlyList<string> Type { get; } = type;

    public string Id { get; } = id;
}

/// <summary>
/// One of <c>principal</c>/<c>action</c>/<c>resource</c>'s constraints in a
/// <c>permit</c>/<c>forbid</c> head. <see cref="CedarInSetScope"/> only
/// arises for <c>action</c> (<c>action in [Action::"a", Action::"b"]</c>);
/// the others are shared by all three positions.
/// </summary>
internal abstract class CedarScopeConstraint;

/// <summary>Unconstrained -- the bare variable name with no <c>==</c>/<c>in</c>/<c>is</c>.</summary>
internal sealed class CedarAnyScope : CedarScopeConstraint;

internal sealed class CedarEqScope(EntityRef entity) : CedarScopeConstraint
{
    public EntityRef Entity { get; } = entity;
}

internal sealed class CedarInScope(EntityRef entity) : CedarScopeConstraint
{
    public EntityRef Entity { get; } = entity;
}

internal sealed class CedarIsScope(IReadOnlyList<string> type) : CedarScopeConstraint
{
    public IReadOnlyList<string> Type { get; } = type;
}

/// <summary><c>resource is Photo in Album::"vacation"</c>.</summary>
internal sealed class CedarIsInScope(IReadOnlyList<string> type, EntityRef entity) : CedarScopeConstraint
{
    public IReadOnlyList<string> Type { get; } = type;

    public EntityRef Entity { get; } = entity;
}

/// <summary><c>action in [Action::"a", Action::"b"]</c>.</summary>
internal sealed class CedarInSetScope(IReadOnlyList<EntityRef> entities) : CedarScopeConstraint
{
    public IReadOnlyList<EntityRef> Entities { get; } = entities;
}

internal enum CedarConditionKind
{
    When,
    Unless,
}

internal sealed class CedarCondition(CedarConditionKind kind, CedarExpr body)
{
    public CedarConditionKind Kind { get; } = kind;

    public CedarExpr Body { get; } = body;
}

internal sealed class CedarPolicy(
    CedarEffect effect,
    CedarScopeConstraint principalScope,
    CedarScopeConstraint actionScope,
    CedarScopeConstraint resourceScope,
    IReadOnlyList<CedarCondition> conditions)
{
    public CedarEffect Effect { get; } = effect;

    public CedarScopeConstraint PrincipalScope { get; } = principalScope;

    public CedarScopeConstraint ActionScope { get; } = actionScope;

    public CedarScopeConstraint ResourceScope { get; } = resourceScope;

    public IReadOnlyList<CedarCondition> Conditions { get; } = conditions;
}