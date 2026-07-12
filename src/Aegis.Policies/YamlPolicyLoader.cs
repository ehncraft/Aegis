using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aegis.Policies;

/// <summary>
/// Loads <see cref="ResourcePolicy"/> documents from YAML files on disk.
///
/// This is a filesystem-only reference implementation. Other sources (Git,
/// blob storage, a database) are future pluggable providers, not this type's
/// concern.
/// </summary>
public static class YamlPolicyLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static ResourcePolicy LoadFile(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var policy = Deserializer.Deserialize<ResourcePolicy>(reader)
                ?? throw new InvalidOperationException("Policy file is empty");
            policy.Source = filePath;
            return policy;
        }
        catch (Exception ex) when (ex is not PolicyLoadException)
        {
            throw new PolicyLoadException(filePath, ex);
        }
    }

    /// <summary>
    /// Loads every <c>*.yaml</c>/<c>*.yml</c> file directly inside <paramref name="directoryPath"/>.
    ///
    /// A file is a resource policy if it has a <c>resource:</c> key, or a
    /// shared library (variables/derived roles reusable via <c>imports:</c>)
    /// if it has a <c>name:</c> key instead. Every policy's imports are
    /// resolved against the libraries found in the same directory before
    /// returning -- see <see cref="PolicyImportResolver"/>.
    /// </summary>
    public static IReadOnlyList<ResourcePolicy> LoadDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Policy directory not found: '{directoryPath}'");
        }

        var files = Directory
            .EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

        var policies = new List<ResourcePolicy>();
        var libraries = new List<PolicyLibrary>();

        foreach (var file in files)
        {
            if (IsLibraryFile(file))
            {
                libraries.Add(LoadLibrary(file));
            }
            else
            {
                policies.Add(LoadFile(file));
            }
        }

        PolicyImportResolver.ResolveImports(policies, libraries);
        return policies;
    }

    private static bool IsLibraryFile(string filePath)
    {
        using var reader = new StreamReader(filePath);
        var probe = Deserializer.Deserialize<PolicyKindProbe>(reader);
        return string.IsNullOrEmpty(probe?.Resource) && !string.IsNullOrEmpty(probe?.Name);
    }

    private static PolicyLibrary LoadLibrary(string filePath)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var library = Deserializer.Deserialize<PolicyLibrary>(reader)
                ?? throw new InvalidOperationException("Policy library file is empty");
            library.Source = filePath;
            return library;
        }
        catch (Exception ex) when (ex is not PolicyLoadException)
        {
            throw new PolicyLoadException(filePath, ex);
        }
    }

    private sealed class PolicyKindProbe
    {
        public string? Resource { get; set; }

        public string? Name { get; set; }
    }
}