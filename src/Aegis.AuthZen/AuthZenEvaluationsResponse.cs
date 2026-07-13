namespace Aegis.AuthZen;

/// <summary>Response body for <c>POST /access/v1/evaluations</c> -- https://openid.github.io/authzen/.</summary>
public sealed class AuthZenEvaluationsResponse
{
    public required List<AuthZenEvaluationResponse> Evaluations { get; init; }
}