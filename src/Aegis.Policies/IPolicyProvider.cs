namespace Aegis.Policies;

/// <summary>
/// Loads <see cref="ResourcePolicy"/> documents from a pluggable backend --
/// a database, Git, blob storage, etc. <see cref="YamlPolicyLoader"/>'s
/// filesystem loading predates this interface and doesn't implement it (it
/// has no async I/O to speak of), but every other backend should.
/// </summary>
public interface IPolicyProvider
{
    Task<IReadOnlyList<ResourcePolicy>> LoadPoliciesAsync(CancellationToken cancellationToken = default);
}