using System.Text.Encodings.Web;
using System.Text.Json;

using Aegis.Policies;

namespace Aegis.Cli;

internal static class AuthorizeCommandHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> ExecuteAsync(
        string policiesDirectory,
        string principalId,
        IReadOnlyList<string> roles,
        IReadOnlyDictionary<string, object?> principalAttributes,
        string resourceKind,
        string? resourceId,
        IReadOnlyDictionary<string, object?> resourceAttributes,
        string action,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        AegisEngine engine;
        try
        {
            engine = AegisEngine.Create(policiesDirectory);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or PolicyLoadException or PolicyValidationException)
        {
            output.WriteLine($"error: {ex.Message}");
            return 2;
        }

        var principal = AegisPrincipal.Create(principalId, roles, principalAttributes);
        var resource = AegisResource.Create(resourceKind, resourceId, resourceAttributes);

        var decision = await engine.AuthorizeAsync(principal, resource, action, cancellationToken);

        output.WriteLine(JsonSerializer.Serialize(decision.Explanation, JsonOptions));

        return decision.Allowed ? 0 : 1;
    }
}