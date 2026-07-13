using Aegis.Policies;

using Microsoft.Extensions.Options;

namespace Aegis.Dashboard.Services;

/// <summary>
/// Loads policies once at startup from <see cref="DashboardOptions.PoliciesDirectory"/>
/// -- eager, like <c>AegisEngine.Create</c>, so a bad policy file fails
/// loudly (surfaced via <see cref="LoadError"/>) rather than on first page view.
/// </summary>
public sealed class PolicyBrowserService
{
    public PolicyBrowserService(IOptions<DashboardOptions> options)
    {
        var directory = options.Value.PoliciesDirectory;
        IsConfigured = !string.IsNullOrWhiteSpace(directory);

        if (!IsConfigured)
        {
            Policies = [];
            return;
        }

        try
        {
            Policies = YamlPolicyLoader.LoadDirectory(DashboardPaths.Resolve(directory!));
        }
        catch (Exception ex)
        {
            Policies = [];
            LoadError = ex.Message;
        }
    }

    public bool IsConfigured { get; }

    public IReadOnlyList<ResourcePolicy> Policies { get; }

    public string? LoadError { get; }
}