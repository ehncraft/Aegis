namespace Aegis.Expressions;

/// <summary>
/// Expression semantics shared by two callers: <see cref="IrOptimizer"/>
/// (folding a literal-only subtree at compile time) and the delegate
/// <see cref="ExpressionCompiler"/> generates (evaluating a non-constant
/// subtree at request time). Keeping this logic in one place means both
/// produce identical results and identical exceptions for the same inputs --
/// constant folding a comparison can never behave differently than
/// evaluating that same comparison would have at runtime.
/// </summary>
internal static class ExpressionRuntime
{
    public static object? ResolveMember(EvaluationContext context, IReadOnlyList<string> path) =>
        context.TryResolve(path, out var value) ? value : null;

    public static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        _ => throw new ExpressionEvaluationException(
            $"Expected a boolean expression but found '{value}' ({value.GetType().Name})"),
    };

    public static bool AreEqual(object? left, object? right)
    {
        // An unresolved member (missing attribute) is never equal to
        // anything, including another unresolved member -- two principals
        // that both lack `department` must not match each other by
        // accident. There's no null literal in the grammar, so this only
        // affects unresolved paths, never an intentional comparison.
        if (left is null || right is null)
        {
            return false;
        }

        if (TryToDouble(left, out var l) && TryToDouble(right, out var r))
        {
            return l.Equals(r);
        }

        return left.Equals(right);
    }

    public static int Compare(object? left, object? right)
    {
        if (TryToDouble(left, out var l) && TryToDouble(right, out var r))
        {
            return l.CompareTo(r);
        }

        if (left is string ls && right is string rs)
        {
            return string.CompareOrdinal(ls, rs);
        }

        throw new ExpressionEvaluationException($"Cannot compare '{left}' and '{right}'");
    }

    public static bool TryToDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case float f:
                result = f;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}