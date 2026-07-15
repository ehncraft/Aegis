namespace Aegis.Cedar;

internal readonly record struct CedarToken(CedarTokenType Type, string Text, object? Value, int Position);