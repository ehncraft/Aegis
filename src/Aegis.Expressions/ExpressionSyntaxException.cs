namespace Aegis.Expressions;

public sealed class ExpressionSyntaxException : Exception
{
    public ExpressionSyntaxException(string message, int position)
        : base($"{message} (at position {position})")
    {
        Position = position;
    }

    public int Position { get; }
}
