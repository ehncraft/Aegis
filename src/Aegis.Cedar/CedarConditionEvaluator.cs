using System.Net;
using System.Net.Sockets;

using Aegis.Relationships;

namespace Aegis.Cedar;

/// <summary>
/// Tree-walking evaluator for <see cref="CedarExpr"/> -- evaluated natively
/// rather than lowered onto <c>Aegis.Expressions</c>' own IR, since Cedar's
/// expression grammar (entities, sets, records, <c>has</c>/<c>like</c>/<c>in</c>,
/// method calls, extension functions) has no equivalent there. See
/// <c>CedarExpr.cs</c>'s own doc comment and Aegis issue #94.
/// </summary>
internal static class CedarConditionEvaluator
{
    private static readonly HashSet<string> AllowedSetMethods =
        new(StringComparer.Ordinal) { "contains", "containsAll", "containsAny" };

    private static readonly HashSet<string> AllowedIpMethods =
        new(StringComparer.Ordinal) { "isIpv4", "isIpv6", "isLoopback", "isMulticast", "isInRange" };

    private static readonly HashSet<string> AllowedDecimalMethods =
        new(StringComparer.Ordinal) { "lessThan", "lessThanOrEqual", "greaterThan", "greaterThanOrEqual" };

    public static bool EvaluateBoolean(CedarExpr expr, CedarEvaluationContext context) =>
        Eval(expr, context).AsBool();

    private static CedarValue Eval(CedarExpr expr, CedarEvaluationContext context) => expr switch
    {
        CedarLiteralExpr e => EvalLiteral(e),
        CedarEntityRefExpr e => CedarValue.Entity(new EntityUid(JoinType(e.Type), e.Id)),
        CedarVarExpr e => EvalVar(e, context),
        CedarAttrExpr e => EvalAttr(e, context),
        CedarHasExpr e => CedarValue.Bool(TryResolveAttr(e.Target, e.AttributeName, context, out _)),
        CedarLikeExpr e => EvalLike(e, context),
        CedarIsExpr e => EvalIs(e, context),
        CedarInExpr e => EvalIn(e, context),
        CedarUnaryExpr e => EvalUnary(e, context),
        CedarBinaryExpr e => EvalBinary(e, context),
        CedarIfExpr e => Eval(e.Condition, context).AsBool() ? Eval(e.Then, context) : Eval(e.Else, context),
        CedarSetExpr e => CedarValue.Set([.. e.Elements.Select(el => Eval(el, context))]),
        CedarRecordExpr e => CedarValue.Record(e.Fields.ToDictionary(f => f.Key, f => Eval(f.Value, context), StringComparer.Ordinal)),
        CedarMethodCallExpr e => EvalMethodCall(e, context),
        CedarExtensionCallExpr e => EvalExtensionCall(e, context),
        _ => throw new CedarConditionEvaluationException($"Unsupported Cedar expression node '{expr.GetType().Name}'"),
    };

    private static string JoinType(IReadOnlyList<string> type) => string.Join("::", type);

    private static CedarValue EvalLiteral(CedarLiteralExpr expr) => expr.Value switch
    {
        bool b => CedarValue.Bool(b),
        long l => CedarValue.Long(l),
        string s => CedarValue.String(s),
        null => throw new CedarConditionEvaluationException("Cedar literals cannot be null"),
        _ => throw new CedarConditionEvaluationException($"Unsupported Cedar literal type '{expr.Value.GetType().Name}'"),
    };

    /// <summary>
    /// A bare <c>principal</c>/<c>resource</c>/<c>action</c> evaluates to
    /// its entity reference (for <c>==</c>/<c>in</c>/<c>is</c> checks);
    /// <c>context</c> has no entity identity of its own -- only attribute
    /// access (<c>context.x</c>) is meaningful, matching every other
    /// evaluation-context shape in this codebase.
    /// </summary>
    private static CedarValue EvalVar(CedarVarExpr expr, CedarEvaluationContext context) => expr.Variable switch
    {
        CedarVar.Principal => CedarValue.Entity(new EntityUid(context.PrincipalEntityType, context.Principal.Id)),
        CedarVar.Resource => CedarValue.Entity(new EntityUid(context.Resource.Kind, context.Resource.Id ?? string.Empty)),
        CedarVar.Action => CedarValue.Entity(new EntityUid("Action", context.Action)),
        CedarVar.Context => throw new CedarConditionEvaluationException(
            "'context' has no value of its own -- only attribute access (context.x) is supported"),
        _ => throw new CedarConditionEvaluationException($"Unknown Cedar variable '{expr.Variable}'"),
    };

    private static CedarValue EvalAttr(CedarAttrExpr expr, CedarEvaluationContext context) =>
        TryResolveAttr(expr.Target, expr.Name, context, out var value)
            ? value
            : throw new CedarConditionEvaluationException($"No attribute '{expr.Name}' on the given target");

    /// <summary>
    /// <c>principal</c>/<c>resource</c>/<c>action</c>/<c>context</c> are
    /// resolved directly against their backing <see cref="AegisPrincipal"/>/
    /// <see cref="AegisResource"/>/dictionaries, since they aren't
    /// themselves <see cref="CedarValue"/> records internally. Anything else
    /// (a nested attribute chain, a record/set-producing expression) falls
    /// through to evaluating the target and looking the field up on the
    /// resulting record -- this milestone has no general entity-attribute
    /// store, so attribute access on an arbitrary entity literal (e.g.
    /// <c>Photo::"p1".owner</c>) isn't supported.
    /// </summary>
    private static bool TryResolveAttr(
        CedarExpr target, string name, CedarEvaluationContext context, out CedarValue value)
    {
        if (target is CedarVarExpr varExpr)
        {
            return varExpr.Variable switch
            {
                CedarVar.Principal => TryResolvePrincipalAttribute(name, context, out value),
                CedarVar.Resource => TryResolveResourceAttribute(name, context, out value),
                CedarVar.Action => TryResolveActionAttribute(name, context, out value),
                CedarVar.Context => TryResolveContextAttribute(name, context, out value),
                _ => throw new CedarConditionEvaluationException($"Unknown Cedar variable '{varExpr.Variable}'"),
            };
        }

        var targetValue = Eval(target, context);
        if (targetValue.Kind != CedarValueKind.Record)
        {
            value = default;
            return false;
        }

        return targetValue.AsRecord().TryGetValue(name, out value);
    }

    private static bool TryResolvePrincipalAttribute(string name, CedarEvaluationContext context, out CedarValue value)
    {
        switch (name)
        {
            case "id":
                value = CedarValue.String(context.Principal.Id);
                return true;
            case "roles":
                value = CedarValue.Set([.. context.Principal.Roles.Select(CedarValue.String)]);
                return true;
            default:
                if (context.Principal.Attributes.TryGetValue(name, out var raw))
                {
                    value = FromClr(raw);
                    return true;
                }

                value = default;
                return false;
        }
    }

    private static bool TryResolveResourceAttribute(string name, CedarEvaluationContext context, out CedarValue value)
    {
        switch (name)
        {
            case "id":
                value = CedarValue.String(context.Resource.Id ?? string.Empty);
                return true;
            case "kind":
                value = CedarValue.String(context.Resource.Kind);
                return true;
            default:
                if (context.Resource.Attributes.TryGetValue(name, out var raw))
                {
                    value = FromClr(raw);
                    return true;
                }

                value = default;
                return false;
        }
    }

    private static bool TryResolveActionAttribute(string name, CedarEvaluationContext context, out CedarValue value)
    {
        if (name == "name")
        {
            value = CedarValue.String(context.Action);
            return true;
        }

        if (context.ActionProperties is not null && context.ActionProperties.TryGetValue(name, out var raw))
        {
            value = FromClr(raw);
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryResolveContextAttribute(string name, CedarEvaluationContext context, out CedarValue value)
    {
        if (context.Context is not null && context.Context.TryGetValue(name, out var raw))
        {
            value = FromClr(raw);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Boxes an attribute value from an <see cref="AegisPrincipal"/>/
    /// <see cref="AegisResource"/>'s loosely-typed <c>Attributes</c>
    /// dictionary (or a caller-supplied <c>context</c>/<c>actionProperties</c>
    /// dictionary) into a <see cref="CedarValue"/>, recursing into nested
    /// dictionaries/enumerables. Cedar has no null value -- an attribute
    /// present with a null value is treated as an evaluation error, not a
    /// missing attribute (that distinction belongs to <c>has</c>, which
    /// never reaches here for an actually-missing key).
    /// </summary>
    private static CedarValue FromClr(object? value) => value switch
    {
        null => throw new CedarConditionEvaluationException("Cedar has no null value, but an attribute was present with a null value"),
        bool b => CedarValue.Bool(b),
        long l => CedarValue.Long(l),
        int i => CedarValue.Long(i),
        string s => CedarValue.String(s),
        decimal d => CedarValue.Decimal(d),
        EntityUid u => CedarValue.Entity(u),
        IReadOnlyDictionary<string, object?> record =>
            CedarValue.Record(record.ToDictionary(kv => kv.Key, kv => FromClr(kv.Value), StringComparer.Ordinal)),
        IEnumerable<object?> items => CedarValue.Set([.. items.Select(FromClr)]),
        _ => throw new CedarConditionEvaluationException($"Cannot use a value of type '{value.GetType().Name}' in a Cedar expression"),
    };

    private static CedarValue EvalLike(CedarLikeExpr expr, CedarEvaluationContext context)
    {
        var target = Eval(expr.Target, context);
        if (target.Kind != CedarValueKind.String)
        {
            throw new CedarConditionEvaluationException($"'like' requires a string value but found {target.Kind}");
        }

        return CedarValue.Bool(LikeMatches(target.AsString(), expr.Pattern));
    }

    /// <summary>
    /// Cedar's <c>like</c> wildcard matching -- <c>*</c> matches any run of
    /// characters (including empty); <see cref="CedarLexer.LiteralStarMarker"/>
    /// (what a <c>\*</c> escape decodes to) matches one literal <c>*</c>
    /// character in the text. Classic two-pointer glob match (a single
    /// wildcard kind, no backtracking stack needed).
    /// </summary>
    private static bool LikeMatches(string text, string pattern)
    {
        var t = 0;
        var p = 0;
        var starIndex = -1;
        var matchIndex = 0;

        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                starIndex = p;
                matchIndex = t;
                p++;
            }
            else if (p < pattern.Length && LiteralChar(pattern[p]) == text[t])
            {
                t++;
                p++;
            }
            else if (starIndex != -1)
            {
                p = starIndex + 1;
                matchIndex++;
                t = matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static char LiteralChar(char patternChar) =>
        patternChar == CedarLexer.LiteralStarMarker ? '*' : patternChar;

    private static CedarValue EvalIs(CedarIsExpr expr, CedarEvaluationContext context)
    {
        var target = Eval(expr.Target, context);
        if (target.Kind != CedarValueKind.Entity)
        {
            throw new CedarConditionEvaluationException($"'is' requires an entity value but found {target.Kind}");
        }

        var isType = string.Equals(target.AsEntity().Type, JoinType(expr.Type), StringComparison.Ordinal);
        if (!isType || expr.InExpr is null)
        {
            return CedarValue.Bool(isType);
        }

        var ancestor = Eval(expr.InExpr, context);
        if (ancestor.Kind != CedarValueKind.Entity)
        {
            throw new CedarConditionEvaluationException($"'in' target of 'is ... in' requires an entity value but found {ancestor.Kind}");
        }

        return CedarValue.Bool(context.RelationshipGraph.IsIn(target.AsEntity(), ancestor.AsEntity()));
    }

    private static CedarValue EvalIn(CedarInExpr expr, CedarEvaluationContext context)
    {
        var left = Eval(expr.Left, context);
        var right = Eval(expr.Right, context);
        if (left.Kind != CedarValueKind.Entity || right.Kind != CedarValueKind.Entity)
        {
            throw new CedarConditionEvaluationException("'in' requires both sides to be entity values");
        }

        return CedarValue.Bool(context.RelationshipGraph.IsIn(left.AsEntity(), right.AsEntity()));
    }

    private static CedarValue EvalUnary(CedarUnaryExpr expr, CedarEvaluationContext context)
    {
        var operand = Eval(expr.Operand, context);
        return expr.Operator switch
        {
            CedarUnaryOperator.Not => CedarValue.Bool(!operand.AsBool()),
            CedarUnaryOperator.Negate => CedarValue.Long(-operand.AsLong()),
            _ => throw new CedarConditionEvaluationException($"Unknown Cedar unary operator '{expr.Operator}'"),
        };
    }

    private static CedarValue EvalBinary(CedarBinaryExpr expr, CedarEvaluationContext context)
    {
        // && and || short-circuit -- the right operand is never evaluated
        // once the result is already determined, matching Cedar's spec and
        // avoiding an unnecessary (possibly attribute-missing) evaluation.
        switch (expr.Operator)
        {
            case CedarBinaryOperator.And:
                return Eval(expr.Left, context).AsBool() ? CedarValue.Bool(Eval(expr.Right, context).AsBool()) : CedarValue.Bool(false);
            case CedarBinaryOperator.Or:
                return Eval(expr.Left, context).AsBool() ? CedarValue.Bool(true) : CedarValue.Bool(Eval(expr.Right, context).AsBool());
        }

        var left = Eval(expr.Left, context);
        var right = Eval(expr.Right, context);

        return expr.Operator switch
        {
            CedarBinaryOperator.Equal => CedarValue.Bool(left.ValueEquals(right)),
            CedarBinaryOperator.NotEqual => CedarValue.Bool(!left.ValueEquals(right)),
            CedarBinaryOperator.Less => CedarValue.Bool(left.AsLong() < right.AsLong()),
            CedarBinaryOperator.LessEqual => CedarValue.Bool(left.AsLong() <= right.AsLong()),
            CedarBinaryOperator.Greater => CedarValue.Bool(left.AsLong() > right.AsLong()),
            CedarBinaryOperator.GreaterEqual => CedarValue.Bool(left.AsLong() >= right.AsLong()),
            CedarBinaryOperator.Add => CedarValue.Long(left.AsLong() + right.AsLong()),
            CedarBinaryOperator.Subtract => CedarValue.Long(left.AsLong() - right.AsLong()),
            CedarBinaryOperator.Multiply => CedarValue.Long(left.AsLong() * right.AsLong()),
            _ => throw new CedarConditionEvaluationException($"Unknown Cedar binary operator '{expr.Operator}'"),
        };
    }

    private static CedarValue EvalMethodCall(CedarMethodCallExpr expr, CedarEvaluationContext context)
    {
        var target = Eval(expr.Target, context);

        return target.Kind switch
        {
            CedarValueKind.Set => EvalSetMethod(expr, target, context),
            CedarValueKind.Ip => EvalIpMethod(expr, target, context),
            CedarValueKind.Decimal => EvalDecimalMethod(expr, target, context),
            _ => throw new CedarConditionEvaluationException(
                $"'{expr.MethodName}' is not a supported method on a {target.Kind} value"),
        };
    }

    private static CedarValue EvalSetMethod(CedarMethodCallExpr expr, CedarValue target, CedarEvaluationContext context)
    {
        if (!AllowedSetMethods.Contains(expr.MethodName))
        {
            throw new CedarConditionEvaluationException($"'{expr.MethodName}' is not a supported set method");
        }

        var set = target.AsSet();
        return expr.MethodName switch
        {
            "contains" => CedarValue.Bool(set.Any(item => item.ValueEquals(Eval(RequireSingleArg(expr), context)))),
            "containsAll" => CedarValue.Bool(Eval(RequireSingleArg(expr), context).AsSet()
                .All(needle => set.Any(item => item.ValueEquals(needle)))),
            "containsAny" => CedarValue.Bool(Eval(RequireSingleArg(expr), context).AsSet()
                .Any(needle => set.Any(item => item.ValueEquals(needle)))),
            _ => throw new CedarConditionEvaluationException($"'{expr.MethodName}' is not a supported set method"),
        };
    }

    private static CedarValue EvalIpMethod(CedarMethodCallExpr expr, CedarValue target, CedarEvaluationContext context)
    {
        if (!AllowedIpMethods.Contains(expr.MethodName))
        {
            throw new CedarConditionEvaluationException($"'{expr.MethodName}' is not a supported ip method");
        }

        var ip = target.AsIp();
        return expr.MethodName switch
        {
            "isIpv4" => CedarValue.Bool(ip.BaseAddress.AddressFamily == AddressFamily.InterNetwork),
            "isIpv6" => CedarValue.Bool(ip.BaseAddress.AddressFamily == AddressFamily.InterNetworkV6),
            "isLoopback" => CedarValue.Bool(IPAddress.IsLoopback(ip.BaseAddress)),
            "isMulticast" => CedarValue.Bool(IsMulticast(ip.BaseAddress)),
            "isInRange" => CedarValue.Bool(IsInRange(ip, Eval(RequireSingleArg(expr), context).AsIp())),
            _ => throw new CedarConditionEvaluationException($"'{expr.MethodName}' is not a supported ip method"),
        };
    }

    private static bool IsMulticast(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => bytes[0] is >= 224 and <= 239,
            AddressFamily.InterNetworkV6 => bytes[0] == 0xFF,
            _ => false,
        };
    }

    /// <summary>
    /// <c>self.isInRange(other)</c> -- true when <paramref name="self"/>'s
    /// address range is a subset of <paramref name="other"/>'s: <paramref name="other"/>
    /// contains <paramref name="self"/>'s base address, and <paramref name="self"/>
    /// is at least as specific (a longer or equal prefix).
    /// </summary>
    private static bool IsInRange(IPNetwork self, IPNetwork other) =>
        other.Contains(self.BaseAddress) && self.PrefixLength >= other.PrefixLength;

    private static CedarValue EvalDecimalMethod(CedarMethodCallExpr expr, CedarValue target, CedarEvaluationContext context)
    {
        if (!AllowedDecimalMethods.Contains(expr.MethodName))
        {
            throw new CedarConditionEvaluationException($"'{expr.MethodName}' is not a supported decimal method");
        }

        var value = target.AsDecimal();
        var other = Eval(RequireSingleArg(expr), context).AsDecimal();
        return expr.MethodName switch
        {
            "lessThan" => CedarValue.Bool(value < other),
            "lessThanOrEqual" => CedarValue.Bool(value <= other),
            "greaterThan" => CedarValue.Bool(value > other),
            "greaterThanOrEqual" => CedarValue.Bool(value >= other),
            _ => throw new CedarConditionEvaluationException($"'{expr.MethodName}' is not a supported decimal method"),
        };
    }

    private static CedarExpr RequireSingleArg(CedarMethodCallExpr expr) => expr.Arguments.Count == 1
        ? expr.Arguments[0]
        : throw new CedarConditionEvaluationException($"'{expr.MethodName}' requires exactly one argument");

    private static CedarValue EvalExtensionCall(CedarExtensionCallExpr expr, CedarEvaluationContext context)
    {
        var arg = RequireSingleExtensionArg(expr);
        var argValue = Eval(arg, context);
        if (argValue.Kind != CedarValueKind.String)
        {
            throw new CedarConditionEvaluationException($"'{expr.FunctionName}' requires a string argument");
        }

        return expr.FunctionName switch
        {
            "ip" => ParseIp(argValue.AsString()),
            "decimal" => ParseDecimal(argValue.AsString()),
            _ => throw new CedarConditionEvaluationException($"'{expr.FunctionName}' is not a supported extension function"),
        };
    }

    private static CedarExpr RequireSingleExtensionArg(CedarExtensionCallExpr expr) => expr.Arguments.Count == 1
        ? expr.Arguments[0]
        : throw new CedarConditionEvaluationException($"'{expr.FunctionName}' requires exactly one argument");

    private static CedarValue ParseIp(string text)
    {
        // A bare address (no "/prefix") is a single host -- represented as
        // the narrowest possible network (a full-length prefix), matching
        // Cedar's own "single address is its own /32 or /128 range" semantics.
        if (!text.Contains('/', StringComparison.Ordinal))
        {
            if (!IPAddress.TryParse(text, out var address))
            {
                throw new CedarConditionEvaluationException($"'{text}' is not a valid ip address");
            }

            var prefixLength = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
            return CedarValue.Ip(new IPNetwork(address, prefixLength));
        }

        if (!IPNetwork.TryParse(text, out var network))
        {
            throw new CedarConditionEvaluationException($"'{text}' is not a valid ip network");
        }

        return CedarValue.Ip(network);
    }

    private static CedarValue ParseDecimal(string text) =>
        decimal.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? CedarValue.Decimal(value)
            : throw new CedarConditionEvaluationException($"'{text}' is not a valid decimal value");
}
