namespace Aegis.Expressions;

internal static class Evaluator
{
    public static object? Evaluate(Expr expr, EvaluationContext context) => expr switch
    {
        LiteralExpr literal => literal.Value,
        MemberExpr member => ResolveMember(member, context),
        VariableExpr variable => context.ResolveVariable(variable.Name),
        UnaryExpr unary => EvaluateUnary(unary, context),
        BinaryExpr binary => EvaluateBinary(binary, context),
        _ => throw new ExpressionEvaluationException($"Unsupported expression node '{expr.GetType().Name}'"),
    };

    private static object? ResolveMember(MemberExpr member, EvaluationContext context) =>
        context.TryResolve(member.Path, out var value)
            ? value
            : null;

    private static bool EvaluateUnary(UnaryExpr unary, EvaluationContext context)
    {
        var operand = Evaluate(unary.Operand, context);
        return unary.Operator switch
        {
            TokenType.Not => !ToBool(operand),
            _ => throw new ExpressionEvaluationException($"Unsupported unary operator '{unary.Operator}'"),
        };
    }

    private static bool EvaluateBinary(BinaryExpr binary, EvaluationContext context)
    {
        if (binary.Operator == TokenType.And)
        {
            return ToBool(Evaluate(binary.Left, context)) && ToBool(Evaluate(binary.Right, context));
        }

        if (binary.Operator == TokenType.Or)
        {
            return ToBool(Evaluate(binary.Left, context)) || ToBool(Evaluate(binary.Right, context));
        }

        var left = Evaluate(binary.Left, context);
        var right = Evaluate(binary.Right, context);

        return binary.Operator switch
        {
            TokenType.Equal => AreEqual(left, right),
            TokenType.NotEqual => !AreEqual(left, right),
            TokenType.Less => Compare(left, right) < 0,
            TokenType.LessEqual => Compare(left, right) <= 0,
            TokenType.Greater => Compare(left, right) > 0,
            TokenType.GreaterEqual => Compare(left, right) >= 0,
            _ => throw new ExpressionEvaluationException($"Unsupported binary operator '{binary.Operator}'"),
        };
    }

    private static bool ToBool(object? value) => value switch
    {
        null => false,
        bool b => b,
        _ => throw new ExpressionEvaluationException(
            $"Expected a boolean expression but found '{value}' ({value.GetType().Name})"),
    };

    private static bool AreEqual(object? left, object? right)
    {
        // An unresolved member (missing attribute) is never equal to
        // anything, including another unresolved member — two principals
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

    private static int Compare(object? left, object? right)
    {
        if (TryToDouble(left, out var l) && TryToDouble(right, out var r))
        {
            return l.CompareTo(r);
        }

        if (left is string ls && right is string rs)
        {
            return string.CompareOrdinal(ls, rs);
        }

        throw new ExpressionEvaluationException(
            $"Cannot compare '{left}' and '{right}'");
    }

    private static bool TryToDouble(object? value, out double result)
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