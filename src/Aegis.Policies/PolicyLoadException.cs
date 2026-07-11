namespace Aegis.Policies;

public sealed class PolicyLoadException : Exception
{
    public PolicyLoadException(string filePath, Exception inner)
        : base($"Failed to load policy from '{filePath}': {inner.Message}", inner)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
}
