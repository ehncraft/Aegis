using System.Diagnostics;

namespace Aegis.Expressions;

/// <summary>Lowers the parse tree (<see cref="Expr"/>) to <see cref="IrNode"/>.</summary>
internal static class IrLowering
{
    public static IrNode Lower(Expr expr) => expr switch
    {
        LiteralExpr literal => new IrConstant(literal.Value, literal.Position),
        MemberExpr member => new IrMember(member.Path, member.Position),
        VariableExpr variable => new IrVariable(variable.Name, variable.Position),
        UnaryExpr unary => LowerUnary(unary),
        BinaryExpr binary => LowerBinary(binary),
        _ => throw new UnreachableException($"Unsupported expression node '{expr.GetType().Name}'"),
    };

    private static IrNot LowerUnary(UnaryExpr unary) => unary.Operator switch
    {
        TokenType.Not => new IrNot(Lower(unary.Operand), unary.Position),
        _ => throw new UnreachableException($"Unsupported unary operator '{unary.Operator}'"),
    };

    private static IrNode LowerBinary(BinaryExpr binary)
    {
        var left = Lower(binary.Left);
        var right = Lower(binary.Right);

        return binary.Operator switch
        {
            TokenType.And => new IrLogical(IrLogicalOperator.And, left, right, binary.Position),
            TokenType.Or => new IrLogical(IrLogicalOperator.Or, left, right, binary.Position),
            TokenType.Equal => new IrComparison(IrComparisonOperator.Equal, left, right, binary.Position),
            TokenType.NotEqual => new IrComparison(IrComparisonOperator.NotEqual, left, right, binary.Position),
            TokenType.Less => new IrComparison(IrComparisonOperator.Less, left, right, binary.Position),
            TokenType.LessEqual => new IrComparison(IrComparisonOperator.LessEqual, left, right, binary.Position),
            TokenType.Greater => new IrComparison(IrComparisonOperator.Greater, left, right, binary.Position),
            TokenType.GreaterEqual =>
                new IrComparison(IrComparisonOperator.GreaterEqual, left, right, binary.Position),
            _ => throw new UnreachableException($"Unsupported binary operator '{binary.Operator}'"),
        };
    }
}
