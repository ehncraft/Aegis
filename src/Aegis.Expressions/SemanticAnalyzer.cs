namespace Aegis.Expressions;

/// <summary>
/// Rejects expressions that are provably wrong from their shape alone --
/// no <see cref="EvaluationContext"/> needed -- before they're optimized or
/// compiled. Attribute paths and <c>${name}</c> variables are dynamically
/// typed (resolved against caller-supplied dictionaries), so this can only
/// judge literal operands; it deliberately doesn't attempt type inference
/// across member/variable references the way Cedar's validator does against
/// a schema.
///
/// Runs on the <em>unoptimized</em> IR, same as <see cref="IrVariableCollector"/>,
/// so an error is reported even inside a branch <see cref="IrOptimizer"/>
/// would otherwise fold away as dead.
/// </summary>
internal static class SemanticAnalyzer
{
    public static void Validate(IrNode node)
    {
        switch (node)
        {
            case IrNot not:
                RequireBooleanOperand(not.Operand, "'!'");
                Validate(not.Operand);
                break;

            case IrLogical logical:
                var operatorText = logical.Operator == IrLogicalOperator.And ? "'&&'" : "'||'";
                RequireBooleanOperand(logical.Left, operatorText);
                RequireBooleanOperand(logical.Right, operatorText);
                Validate(logical.Left);
                Validate(logical.Right);
                break;

            case IrComparison { Operator: IrComparisonOperator.Equal or IrComparisonOperator.NotEqual } comparison:
                Validate(comparison.Left);
                Validate(comparison.Right);
                break;

            case IrComparison comparison:
                RequireComparableConstants(comparison);
                Validate(comparison.Left);
                Validate(comparison.Right);
                break;
        }
    }

    private static void RequireBooleanOperand(IrNode operand, string operatorText)
    {
        if (operand is IrConstant { Value: not bool } constant)
        {
            throw new ExpressionSyntaxException(
                $"Operator {operatorText} requires a boolean operand but found constant '{constant.Value}'",
                constant.Position);
        }
    }

    /// <summary>
    /// Only literal-vs-literal is checked here -- if either side is a
    /// member path or variable, its type is only known at request time, and
    /// <see cref="ExpressionRuntime.Compare"/> already throws
    /// <see cref="ExpressionEvaluationException"/> then.
    /// </summary>
    private static void RequireComparableConstants(IrComparison comparison)
    {
        if (comparison.Left is not IrConstant { Value: { } left } ||
            comparison.Right is not IrConstant { Value: { } right })
        {
            return;
        }

        var comparable = (ExpressionRuntime.TryToDouble(left, out _) && ExpressionRuntime.TryToDouble(right, out _))
            || (left is string && right is string);

        if (!comparable)
        {
            throw new ExpressionSyntaxException(
                $"Cannot compare constant '{left}' and '{right}'", comparison.Position);
        }
    }
}