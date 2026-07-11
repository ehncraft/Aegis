namespace Aegis.Expressions;

internal abstract class Expr;

internal sealed class LiteralExpr(object? value) : Expr
{
    public object? Value { get; } = value;
}

/// <summary>A dotted path such as <c>principal.department</c>.</summary>
internal sealed class MemberExpr(IReadOnlyList<string> path) : Expr
{
    public IReadOnlyList<string> Path { get; } = path;
}

internal sealed class UnaryExpr(TokenType op, Expr operand) : Expr
{
    public TokenType Operator { get; } = op;

    public Expr Operand { get; } = operand;
}

internal sealed class BinaryExpr(TokenType op, Expr left, Expr right) : Expr
{
    public TokenType Operator { get; } = op;

    public Expr Left { get; } = left;

    public Expr Right { get; } = right;
}
