namespace Aegis.Cedar;

/// <summary>
/// Static, load-time check that every extension function (<c>ip()</c>/
/// <c>decimal()</c>) and method call (<c>.contains()</c>, <c>.isInRange()</c>,
/// etc.) in a Cedar expression tree names something <see cref="CedarConditionEvaluator"/>
/// actually supports -- fail at policy-load time, not on a live request,
/// the same reason <c>PolicyValidator</c> exists at all. This is a name
/// check only (no receiver-type inference -- this milestone has no Cedar
/// schema/entity type-store to check against), so <see cref="CedarConditionEvaluator"/>
/// remains the source of truth for whether a given method actually applies
/// to a value's runtime kind; this just catches a typo'd or unsupported
/// name before it ever reaches a real request.
/// </summary>
internal static class CedarExtensionValidation
{
    private static readonly HashSet<string> AllowedFunctions = new(StringComparer.Ordinal) { "ip", "decimal" };

    private static readonly HashSet<string> AllowedMethods = new(StringComparer.Ordinal)
    {
        "contains", "containsAll", "containsAny",
        "isIpv4", "isIpv6", "isLoopback", "isMulticast", "isInRange",
        "lessThan", "lessThanOrEqual", "greaterThan", "greaterThanOrEqual",
    };

    public static IReadOnlyList<string> Validate(CedarExpr expr)
    {
        var errors = new List<string>();
        Walk(expr, errors);
        return errors;
    }

    private static void Walk(CedarExpr expr, List<string> errors)
    {
        switch (expr)
        {
            case CedarLiteralExpr or CedarEntityRefExpr or CedarVarExpr:
                break;
            case CedarAttrExpr e:
                Walk(e.Target, errors);
                break;
            case CedarHasExpr e:
                Walk(e.Target, errors);
                break;
            case CedarLikeExpr e:
                Walk(e.Target, errors);
                break;
            case CedarIsExpr e:
                Walk(e.Target, errors);
                if (e.InExpr is not null)
                {
                    Walk(e.InExpr, errors);
                }

                break;
            case CedarInExpr e:
                Walk(e.Left, errors);
                Walk(e.Right, errors);
                break;
            case CedarUnaryExpr e:
                Walk(e.Operand, errors);
                break;
            case CedarBinaryExpr e:
                Walk(e.Left, errors);
                Walk(e.Right, errors);
                break;
            case CedarIfExpr e:
                Walk(e.Condition, errors);
                Walk(e.Then, errors);
                Walk(e.Else, errors);
                break;
            case CedarSetExpr e:
                foreach (var element in e.Elements)
                {
                    Walk(element, errors);
                }

                break;
            case CedarRecordExpr e:
                foreach (var field in e.Fields)
                {
                    Walk(field.Value, errors);
                }

                break;
            case CedarMethodCallExpr e:
                Walk(e.Target, errors);
                foreach (var arg in e.Arguments)
                {
                    Walk(arg, errors);
                }

                if (!AllowedMethods.Contains(e.MethodName))
                {
                    errors.Add($"'{e.MethodName}' is not a supported Cedar method.");
                }

                break;
            case CedarExtensionCallExpr e:
                foreach (var arg in e.Arguments)
                {
                    Walk(arg, errors);
                }

                if (!AllowedFunctions.Contains(e.FunctionName))
                {
                    errors.Add($"'{e.FunctionName}' is not a supported Cedar extension function.");
                }

                break;
            default:
                errors.Add($"Unsupported Cedar expression node '{expr.GetType().Name}'.");
                break;
        }
    }
}
