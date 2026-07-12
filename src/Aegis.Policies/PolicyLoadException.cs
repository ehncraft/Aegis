namespace Aegis.Policies;

/// <summary>A policy failed to load -- <see cref="PolicySource"/> identifies where from (a file path, a SQL table, etc).</summary>
public sealed class PolicyLoadException : Exception
{
    public PolicyLoadException(string policySource, Exception inner)
        : base($"Failed to load policy from '{policySource}': {inner.Message}", inner)
    {
        PolicySource = policySource;
    }

    public string PolicySource { get; }
}