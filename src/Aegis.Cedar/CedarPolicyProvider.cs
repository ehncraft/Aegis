using Aegis.Policies;

namespace Aegis.Cedar;

/// <summary>
/// Loads <see cref="ResourcePolicy"/> documents by parsing every
/// <c>*.cedar</c> file in a directory and lowering them together via
/// <see cref="CedarPolicySetLowerer"/> -- the public entry point for
/// Aegis issue #94 milestone 2 (<see cref="CedarPolicySetLowerer"/> itself
/// is internal, since its <c>CedarPolicy</c> input type is).
///
/// Mirrors <see cref="YamlPolicyLoader"/>'s directory-scan conventions
/// (top-level only, ordinal file-name order, same <see cref="PolicyLoadException"/>
/// error wrapping) but, unlike YAML's one-file-one-resource-policy
/// convention, every file's <c>permit</c>/<c>forbid</c> policies are
/// concatenated into one batch before lowering -- this is what makes
/// permits/forbids for the same resource/action defined in <em>different</em>
/// <c>.cedar</c> files merge correctly (see <see cref="CedarPolicySetLowerer"/>'s
/// merge step).
/// </summary>
public sealed class CedarPolicyProvider : IPolicyProvider
{
    private readonly string _directoryPath;
    private readonly CedarLoadOptions _options;

    public CedarPolicyProvider(string directoryPath, CedarLoadOptions? options = null)
    {
        _directoryPath = directoryPath;
        _options = options ?? new CedarLoadOptions();
    }

    public Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LoadDirectory(_directoryPath, _options));

    /// <summary>
    /// Synchronous convenience for callers that don't want the async
    /// <see cref="IPolicyProvider"/> ceremony for a pure-filesystem source --
    /// mirrors <see cref="YamlPolicyLoader.LoadDirectory"/>'s own
    /// non-interface API shape, for the same reason (see that type's doc
    /// comment: no real async I/O to speak of).
    /// </summary>
    public static IReadOnlyList<ResourcePolicy> LoadDirectory(string directoryPath, CedarLoadOptions? options = null)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Policy directory not found: '{directoryPath}'");
        }

        var files = Directory
            .EnumerateFiles(directoryPath, "*.cedar", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal);

        var allPolicies = new List<CedarPolicy>();
        foreach (var file in files)
        {
            allPolicies.AddRange(ParseFile(file));
        }

        try
        {
            return CedarPolicySetLowerer.Lower(allPolicies, options ?? new CedarLoadOptions());
        }
        catch (CedarLoweringException ex)
        {
            throw new PolicyLoadException(directoryPath, ex);
        }
    }

    private static IReadOnlyList<CedarPolicy> ParseFile(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            return CedarParser.Parse(text);
        }
        catch (Exception ex) when (ex is not PolicyLoadException)
        {
            throw new PolicyLoadException(filePath, ex);
        }
    }
}
