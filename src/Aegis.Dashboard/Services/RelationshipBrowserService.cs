using Aegis.Relationships;

using Microsoft.Extensions.Options;

namespace Aegis.Dashboard.Services;

/// <summary>
/// Loads entity-parent facts from <see cref="DashboardOptions.RelationshipsDirectory"/>
/// lazily, on first request, and caches the result -- unlike <see cref="PolicyBrowserService"/>,
/// loading is inherently async (<see cref="IRelationshipProvider.LoadEntityParentsAsync"/>),
/// so it can't happen eagerly in a DI constructor.
/// </summary>
public sealed class RelationshipBrowserService
{
    private readonly string? _directory;
    private Task<IReadOnlyList<EntityParent>>? _loadTask;

    public RelationshipBrowserService(IOptions<DashboardOptions> options)
    {
        _directory = string.IsNullOrWhiteSpace(options.Value.RelationshipsDirectory)
            ? null
            : DashboardPaths.Resolve(options.Value.RelationshipsDirectory);
    }

    public bool IsConfigured => _directory is not null;

    public string? LoadError { get; private set; }

    public async Task<IReadOnlyList<EntityParent>> GetEntityParentsAsync(CancellationToken cancellationToken = default)
    {
        if (_directory is null)
        {
            return [];
        }

        _loadTask ??= LoadAsync(_directory, cancellationToken);

        try
        {
            return await _loadTask;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            _loadTask = null; // let a later call retry, e.g. after the directory is fixed
            return [];
        }
    }

    /// <summary>
    /// Genuinely <c>async</c>, not a passthrough -- <see cref="YamlRelationshipProvider.LoadEntityParentsAsync"/>
    /// throws synchronously (e.g. a missing directory) rather than
    /// returning a faulted task, so a passthrough would escape the
    /// try/catch in <see cref="GetEntityParentsAsync"/> around
    /// <c>_loadTask ??= LoadAsync(...)</c> instead of being caught on await.
    /// </summary>
    private static async Task<IReadOnlyList<EntityParent>> LoadAsync(string directory, CancellationToken cancellationToken) =>
        await new YamlRelationshipProvider(directory).LoadEntityParentsAsync(cancellationToken);
}