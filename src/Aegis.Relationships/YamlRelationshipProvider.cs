using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Aegis.Relationships;

/// <summary>
/// Loads entity-hierarchy facts from every <c>*.yaml</c>/<c>*.yml</c> file
/// directly inside a directory, each shaped like Cedar's own entity data
/// format (https://docs.cedarpolicy.com/auth/entities-syntax.html) translated
/// to YAML:
///
/// <code>
/// entities:
///   - uid:
///       type: User
///       id: alice
///     parents:
///       - type: Group
///         id: senior-auditors
/// </code>
/// </summary>
public sealed class YamlRelationshipProvider(string directoryPath) : IRelationshipProvider
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public Task<IReadOnlyList<EntityParent>> LoadEntityParentsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Relationship entity directory not found: '{directoryPath}'");
        }

        var files = Directory
            .EnumerateFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);

        var entityParents = new List<EntityParent>();

        foreach (var file in files)
        {
            EntityFileDocument? document;
            try
            {
                using var reader = new StreamReader(file);
                document = Deserializer.Deserialize<EntityFileDocument>(reader);
            }
            catch (YamlDotNet.Core.YamlException ex)
            {
                throw new InvalidOperationException($"Failed to load relationship entities from '{file}': {ex.Message}", ex);
            }

            foreach (var entity in document?.Entities ?? [])
            {
                var child = new EntityUid(entity.Uid.Type, entity.Uid.Id);
                foreach (var parent in entity.Parents ?? [])
                {
                    entityParents.Add(new EntityParent { Child = child, Parent = new EntityUid(parent.Type, parent.Id) });
                }
            }
        }

        return Task.FromResult<IReadOnlyList<EntityParent>>(entityParents);
    }

    private sealed class EntityFileDocument
    {
        public List<EntityEntry>? Entities { get; set; }
    }

    private sealed class EntityEntry
    {
        public EntityUidEntry Uid { get; set; } = new();

        public List<EntityUidEntry>? Parents { get; set; }
    }

    private sealed class EntityUidEntry
    {
        public string Type { get; set; } = string.Empty;

        public string Id { get; set; } = string.Empty;
    }
}