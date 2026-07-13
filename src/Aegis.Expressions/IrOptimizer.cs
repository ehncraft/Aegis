using System.Diagnostics;

namespace Aegis.Expressions;

/// <summary>
/// Bottom-up constant folding over the IR, run after <see cref="SemanticAnalyzer"/>
/// so every constant it touches is already known-valid (a boolean operand
/// for <see cref="IrNot"/>/<see cref="IrLogical"/>, a comparable pair for an
/// ordering <see cref="IrComparison"/>).
///
/// <see cref="IrLogical"/> only folds on its <em>left</em> operand being
/// constant, matching the original left-to-right short-circuit exactly: the
/// tree-walking evaluator this replaced always evaluated (and could throw
/// evaluating) the left operand, then conditionally the right one. Folding
/// on a constant right operand instead (e.g. rewriting `x && false` to the
/// constant `false`) would silently skip evaluating `x` and any exception it
/// could raise -- a real behavior change, not just an optimization.
/// </summary>
internal static class IrOptimizer
{
    public static IrNode Optimize(IrNode node) => node switch
    {
        IrNot not => OptimizeNot(not),
        IrLogical logical => OptimizeLogical(logical),
        IrComparison comparison => OptimizeComparison(comparison),
        _ => node,
    };

    private static IrNode OptimizeNot(IrNot not)
    {
        var operand = Optimize(not.Operand);
        return operand is IrConstant { Value: bool b }
            ? new IrConstant(!b, not.Position)
            : new IrNot(operand, not.Position);
    }

    private static IrNode OptimizeLogical(IrLogical logical)
    {
        var left = Optimize(logical.Left);
        var right = Optimize(logical.Right);

        if (left is IrConstant { Value: bool leftValue })
        {
            return logical.Operator switch
            {
                IrLogicalOperator.And => leftValue ? right : new IrConstant(false, logical.Position),
                IrLogicalOperator.Or => leftValue ? new IrConstant(true, logical.Position) : right,
                _ => new IrLogical(logical.Operator, left, right, logical.Position),
            };
        }

        return new IrLogical(logical.Operator, left, right, logical.Position);
    }

    private static IrNode OptimizeComparison(IrComparison comparison)
    {
        var left = Optimize(comparison.Left);
        var right = Optimize(comparison.Right);

        if (left is not IrConstant leftConstant || right is not IrConstant rightConstant)
        {
            return new IrComparison(comparison.Operator, left, right, comparison.Position);
        }

        var result = comparison.Operator switch
        {
            IrComparisonOperator.Equal => ExpressionRuntime.AreEqual(leftConstant.Value, rightConstant.Value),
            IrComparisonOperator.NotEqual => !ExpressionRuntime.AreEqual(leftConstant.Value, rightConstant.Value),
            IrComparisonOperator.Less => ExpressionRuntime.Compare(leftConstant.Value, rightConstant.Value) < 0,
            IrComparisonOperator.LessEqual =>
                ExpressionRuntime.Compare(leftConstant.Value, rightConstant.Value) <= 0,
            IrComparisonOperator.Greater => ExpressionRuntime.Compare(leftConstant.Value, rightConstant.Value) > 0,
            IrComparisonOperator.GreaterEqual =>
                ExpressionRuntime.Compare(leftConstant.Value, rightConstant.Value) >= 0,
            _ => throw new UnreachableException($"Unsupported comparison operator '{comparison.Operator}'"),
        };

        return new IrConstant(result, comparison.Position);
    }
}