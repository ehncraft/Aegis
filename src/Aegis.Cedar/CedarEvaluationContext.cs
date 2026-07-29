using Aegis.Relationships;

namespace Aegis.Cedar;

/// <summary>
/// Everything <see cref="CedarConditionEvaluator"/> needs to evaluate one
/// Cedar <c>when</c>/<c>unless</c> body -- mirrors the four scopes
/// <c>PolicyEvaluator.BuildContext</c> assembles for Aegis's own expression
/// grammar (principal/resource/action/context), but Cedar-typed rather than
/// a reuse of <see cref="Aegis.Expressions.EvaluationContext"/>, which has
/// no entity/set/record concept of its own.
/// </summary>
internal sealed class CedarEvaluationContext
{
    public required AegisPrincipal Principal { get; init; }

    public required AegisResource Resource { get; init; }

    public required string Action { get; init; }

    public IReadOnlyDictionary<string, object?>? ActionProperties { get; init; }

    public IReadOnlyDictionary<string, object?>? Context { get; init; }

    public RelationshipGraph RelationshipGraph { get; init; } = RelationshipGraph.Empty;

    /// <summary>
    /// The entity type <see cref="Principal"/> is treated as for <c>is</c>/
    /// <c>in</c> checks -- see <c>PolicyEvaluator</c>'s constructor parameter
    /// of the same name, which this must agree with when both are driven by
    /// the same caller.
    /// </summary>
    public required string PrincipalEntityType { get; init; }
}
