using System.Text.Json;

namespace Aegis.AuthZen;

/// <summary>
/// Request body for <c>POST /access/v1/evaluation</c> (single access
/// evaluation) -- https://openid.github.io/authzen/.
/// </summary>
public sealed class AuthZenEvaluationRequest
{
    public required AuthZenEntity Subject { get; init; }

    public required AuthZenEntity Resource { get; init; }

    public required AuthZenAction Action { get; init; }

    public Dictionary<string, JsonElement>? Context { get; init; }
}