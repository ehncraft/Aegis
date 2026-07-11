namespace Aegis.Expressions;

internal readonly record struct Token(TokenType Type, string Text, object? Value, int Position);