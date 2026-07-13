namespace Aegis.Expressions;

/// <summary>
/// Walks the <em>unoptimized</em> IR (before <see cref="IrOptimizer"/> can
/// fold away a branch) to list every <c>${name}</c> reference an expression
/// makes, in first-appearance order and with duplicates -- <c>PolicyValidator</c>
/// (Aegis.Evaluator) needs every reference, including ones a constant-folded
/// dead branch would otherwise never evaluate, to catch a typo'd variable
/// name regardless of whether that branch is reachable at runtime.
/// </summary>
internal static class IrVariableCollector
{
    public static IReadOnlyList<string> Collect(IrNode node)
    {
        var names = new List<string>();
        Walk(node, names);
        return names;
    }

    private static void Walk(IrNode node, List<string> names)
    {
        switch (node)
        {
            case IrVariable variable:
                names.Add(variable.Name);
                break;
            case IrNot not:
                Walk(not.Operand, names);
                break;
            case IrLogical logical:
                Walk(logical.Left, names);
                Walk(logical.Right, names);
                break;
            case IrComparison comparison:
                Walk(comparison.Left, names);
                Walk(comparison.Right, names);
                break;
        }
    }
}