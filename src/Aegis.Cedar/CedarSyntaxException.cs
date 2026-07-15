namespace Aegis.Cedar;

public sealed class CedarSyntaxException : Exception
{
    public CedarSyntaxException(string message, int position)
        : base($"{message} (at position {position})")
    {
        Position = position;
    }

    public int Position { get; }
}