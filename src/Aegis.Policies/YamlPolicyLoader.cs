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

    /// <summary>Loads every <c>*.yaml</c>/<c>*.yml</c> file directly inside <paramref name="directoryPath"/>.</summary>
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

        return files.Select(LoadFile).ToList();
    }
}
