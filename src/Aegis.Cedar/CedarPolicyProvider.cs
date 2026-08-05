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
///
/// The <see cref="IPolicyProvider"/>-conforming wrapper this class also constructs via
/// <see cref="Create"/> is internal (<see cref="CedarPolicyProviderImpl"/>) -- callers see it
/// only through the interface, same as every other Aegis provider.
/// </summary>
public static class CedarPolicyProvider
{
    public static IPolicyProvider Create(string directoryPath, CedarLoadOptions? options = null) =>
        new CedarPolicyProviderImpl(directoryPath, options);

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
            allPolicies.AddRange(ParseSource(file, () => File.ReadAllText(file)));
        }

        return Lower(allPolicies, directoryPath, options);
    }

    /// <summary>
    /// Same parse-and-lower pipeline as <see cref="LoadDirectory"/>, for policy text that
    /// didn't come from the filesystem -- a SQL row, an admin API submission, etc. (see
    /// <c>Aegis.Sql</c>'s <c>CedarSqlPolicyProvider</c> for the SQL-backed <see cref="IPolicyProvider"/>
    /// built on top of this). <paramref name="policyTexts"/> are concatenated into one batch
    /// before lowering, same as every file in a directory is -- this is what makes
    /// permit/forbid rules for the same resource/action defined in separate rows merge
    /// correctly (see <see cref="CedarPolicySetLowerer"/>'s merge step). Each text's own
    /// <paramref name="sourceNames"/> entry (same order, same count) is used only for
    /// <see cref="PolicyLoadException.PolicySource"/> if that one text fails to parse --
    /// callers that don't have a meaningful per-row identifier can pass an index or a
    /// constant placeholder.
    /// </summary>
    public static IReadOnlyList<ResourcePolicy> LoadFromText(
        IReadOnlyList<string> policyTexts, IReadOnlyList<string> sourceNames, CedarLoadOptions? options = null)
    {
        if (policyTexts.Count != sourceNames.Count)
        {
            throw new ArgumentException(
                $"{nameof(policyTexts)} and {nameof(sourceNames)} must be the same length ({policyTexts.Count} vs {sourceNames.Count}).",
                nameof(sourceNames));
        }

        var allPolicies = new List<CedarPolicy>();
        for (var i = 0; i < policyTexts.Count; i++)
        {
            var text = policyTexts[i];
            allPolicies.AddRange(ParseSource(sourceNames[i], () => text));
        }

        return Lower(allPolicies, "<in-memory>", options);
    }

    private static IReadOnlyList<ResourcePolicy> Lower(
        IReadOnlyList<CedarPolicy> policies, string batchSource, CedarLoadOptions? options)
    {
        try
        {
            return CedarPolicySetLowerer.Lower(policies, options ?? new CedarLoadOptions());
        }
        catch (CedarLoweringException ex)
        {
            throw new PolicyLoadException(batchSource, ex);
        }
    }

    private static IReadOnlyList<CedarPolicy> ParseSource(string sourceName, Func<string> readText)
    {
        try
        {
            return CedarParser.Parse(readText());
        }
        catch (Exception ex) when (ex is not PolicyLoadException)
        {
            throw new PolicyLoadException(sourceName, ex);
        }
    }
}

internal sealed class CedarPolicyProviderImpl(string directoryPath, CedarLoadOptions? options) : IPolicyProvider
{
    public Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CedarPolicyProvider.LoadDirectory(directoryPath, options));
}