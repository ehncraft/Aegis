namespace Aegis.Testing;

/// <summary>
/// Assertion-style extensions for writing policy regression tests directly
/// against an <see cref="AegisEngine"/> -- given this principal/resource/action,
/// expect allow or deny -- without spinning up a host, DI container, or
/// HTTP server. On a mismatch, throws <see cref="PolicyAssertionException"/>
/// with the full decision explanation, which any test runner reports as a
/// failed test.
/// </summary>
public static class AegisEngineAssertions
{
    /// <summary>Asserts <paramref name="principal"/> is allowed to perform <paramref name="action"/> on <paramref name="resource"/>.</summary>
    public static async Task ShouldAllowAsync(
        this AegisEngine engine,
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        var decision = await engine.AuthorizeAsync(principal, resource, action, cancellationToken);
        if (!decision.Allowed)
        {
            throw new PolicyAssertionException(expectedAllowed: true, principal, resource, action, decision);
        }
    }

    /// <summary>Asserts <paramref name="principal"/> is denied from performing <paramref name="action"/> on <paramref name="resource"/>.</summary>
    public static async Task ShouldDenyAsync(
        this AegisEngine engine,
        AegisPrincipal principal,
        AegisResource resource,
        string action,
        CancellationToken cancellationToken = default)
    {
        var decision = await engine.AuthorizeAsync(principal, resource, action, cancellationToken);
        if (decision.Allowed)
        {
            throw new PolicyAssertionException(expectedAllowed: false, principal, resource, action, decision);
        }
    }
}