namespace Aegis.Expressions;

/// <summary>
/// <paramref name="position"/> is the source offset of this node's leading
/// token, carried through to the IR so <c>SemanticAnalyzer</c> can point at
/// where a statically-detectable error came from.
/// </summary>
internal abstract class Expr(int position)
{
    public int Position { get; } = position;
}

internal sealed class LiteralExpr(object? value, int position) : Expr(position)
{
    public object? Value { get; } = value;
}

/// <summary>A dotted path such as <c>principal.department</c>.</summary>
internal sealed class MemberExpr(IReadOnlyList<string> path, int position) : Expr(position)
{
    public IReadOnlyList<string> Path { get; } = path;
}

/// <summary>A <c>${name}</c> reference to a policy variable.</summary>
internal sealed class VariableExpr(string name, int position) : Expr(position)
{
    public string Name { get; } = name;
}

internal sealed class UnaryExpr(TokenType op, Expr operand, int position) : Expr(position)
{
    public TokenType Operator { get; } = op;

    public Expr Operand { get; } = operand;
}

internal sealed class BinaryExpr(TokenType op, Expr left, Expr right, int position) : Expr(position)
{
    public TokenType Operator { get; } = op;

    public Expr Left { get; } = left;

    public Expr Right { get; } = right;
}