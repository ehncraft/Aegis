namespace Aegis.Cedar;

/// <summary>
/// A Cedar <c>when</c>/<c>unless</c> body couldn't be evaluated against a
/// given <see cref="CedarEvaluationContext"/> -- a type mismatch (e.g.
/// <c>like</c> against a non-string), a missing attribute accessed without
/// <c>has</c>, or an unrecognized extension function/method that slipped
/// past load-time validation. Distinct from <see cref="CedarSyntaxException"/>
/// (a parse failure) and <see cref="CedarLoweringException"/> (a policy
/// structurally impossible to lower onto Aegis's model) -- this one is
/// purely an evaluation-time failure.
/// </summary>
public sealed class CedarConditionEvaluationException : Exception
{
    public CedarConditionEvaluationException(string message)
        : base(message)
    {
    }
}