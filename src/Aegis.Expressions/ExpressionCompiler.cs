using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Aegis.Expressions;

/// <summary>
/// Compiles optimized IR to a <see cref="Func{T,TResult}"/> via
/// <see cref="System.Linq.Expressions"/> -- built once per <see cref="CompiledExpression"/>
/// and then invoked directly on every <c>Evaluate</c>/<c>EvaluateBoolean</c>
/// call, instead of re-walking the tree and re-dispatching on node type each
/// time. Every generated node calls back into <see cref="ExpressionRuntime"/>
/// (or <see cref="EvaluationContext"/>) rather than re-implementing
/// comparison/coercion logic in the tree itself, so a compiled expression's
/// runtime behavior -- including exception messages -- is identical to
/// evaluating the same IR directly would have been.
/// </summary>
internal static class ExpressionCompiler
{
    private static readonly ParameterExpression ContextParameter =
        Expression.Parameter(typeof(EvaluationContext), "context");

    private static readonly MethodInfo ResolveMemberMethod =
        typeof(ExpressionRuntime).GetMethod(nameof(ExpressionRuntime.ResolveMember))!;

    private static readonly MethodInfo ResolveVariableMethod =
        typeof(EvaluationContext).GetMethod(
            nameof(EvaluationContext.ResolveVariable), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ToBoolMethod =
        typeof(ExpressionRuntime).GetMethod(nameof(ExpressionRuntime.ToBool))!;

    private static readonly MethodInfo AreEqualMethod =
        typeof(ExpressionRuntime).GetMethod(nameof(ExpressionRuntime.AreEqual))!;

    private static readonly MethodInfo CompareMethod =
        typeof(ExpressionRuntime).GetMethod(nameof(ExpressionRuntime.Compare))!;

    public static Func<EvaluationContext, object?> Compile(IrNode root)
    {
        var body = Build(root);
        return Expression.Lambda<Func<EvaluationContext, object?>>(body, ContextParameter).Compile();
    }

    private static Expression Build(IrNode node) => node switch
    {
        IrConstant constant => Expression.Constant(constant.Value, typeof(object)),
        IrMember member => Expression.Call(
            ResolveMemberMethod, ContextParameter, Expression.Constant(member.Path, typeof(IReadOnlyList<string>))),
        IrVariable variable => Expression.Call(
            ContextParameter, ResolveVariableMethod, Expression.Constant(variable.Name)),
        IrNot not => AsObject(Expression.Not(BuildBool(not.Operand))),
        IrLogical logical => AsObject(BuildLogical(logical)),
        IrComparison comparison => AsObject(BuildComparison(comparison)),
        _ => throw new UnreachableException($"Unsupported IR node '{node.GetType().Name}'"),
    };

    private static MethodCallExpression BuildBool(IrNode node) => Expression.Call(ToBoolMethod, Build(node));

    private static BinaryExpression BuildLogical(IrLogical logical)
    {
        var left = BuildBool(logical.Left);
        var right = BuildBool(logical.Right);
        return logical.Operator == IrLogicalOperator.And
            ? Expression.AndAlso(left, right)
            : Expression.OrElse(left, right);
    }

    private static Expression BuildComparison(IrComparison comparison)
    {
        var left = Build(comparison.Left);
        var right = Build(comparison.Right);

        if (comparison.Operator is IrComparisonOperator.Equal or IrComparisonOperator.NotEqual)
        {
            var equal = Expression.Call(AreEqualMethod, left, right);
            return comparison.Operator == IrComparisonOperator.Equal ? equal : Expression.Not(equal);
        }

        var compared = Expression.Call(CompareMethod, left, right);
        var zero = Expression.Constant(0);
        return comparison.Operator switch
        {
            IrComparisonOperator.Less => Expression.LessThan(compared, zero),
            IrComparisonOperator.LessEqual => Expression.LessThanOrEqual(compared, zero),
            IrComparisonOperator.Greater => Expression.GreaterThan(compared, zero),
            IrComparisonOperator.GreaterEqual => Expression.GreaterThanOrEqual(compared, zero),
            _ => throw new UnreachableException(
                $"Unsupported comparison operator '{comparison.Operator}'"),
        };
    }

    private static UnaryExpression AsObject(Expression expr) => Expression.Convert(expr, typeof(object));
}
