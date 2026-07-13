using System.Text.Json;

namespace Aegis.AuthZen;

/// <summary>
/// Request body for <c>POST /access/v1/evaluations</c> (batch access
/// evaluations) -- https://openid.github.io/authzen/. <see cref="Subject"/>/
/// <see cref="Resource"/>/<see cref="Action"/>/<see cref="Context"/> are
/// defaults each entry in <see cref="Evaluations"/> can individually
/// override.
/// </summary>
public sealed class AuthZenEvaluationsRequest
{
    public AuthZenEntity? Subject { get; init; }

    public AuthZenEntity? Resource { get; init; }

    public AuthZenAction? Action { get; init; }

    public Dictionary<string, JsonElement>? Context { get; init; }

    public required List<AuthZenEvaluationOverride> Evaluations { get; init; }

    public AuthZenEvaluationsOptions? Options { get; init; }
}

/// <summary>One batch entry -- any field left null falls back to the request-level default.</summary>
public sealed class AuthZenEvaluationOverride
{
    public AuthZenEntity? Subject { get; init; }

    public AuthZenEntity? Resource { get; init; }

    public AuthZenAction? Action { get; init; }

    public Dictionary<string, JsonElement>? Context { get; init; }
}

public sealed class AuthZenEvaluationsOptions
{
    /// <summary>
    /// <c>execute_all</c> (default, run every evaluation), <c>deny_on_first_deny</c>,
    /// or <c>permit_on_first_permit</c> -- the latter two short-circuit, and
    /// per the spec return only the evaluations actually executed (a
    /// partial array), not the full batch with unexecuted entries omitted
    /// or nulled.
    /// </summary>
    public string? EvaluationsSemantic { get; init; }
}