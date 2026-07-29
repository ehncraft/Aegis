using System.Globalization;
using System.Text;

namespace Aegis.Cedar;

/// <summary>
/// Renders a <see cref="CedarExpr"/> back into Cedar source text -- the
/// inverse of <see cref="CedarParser"/>'s expression parsing. Used by
/// <see cref="CedarPolicySetLowerer"/> to turn the synthesized condition it
/// builds (combining scope-derived fragments with the original policy's own
/// <c>when</c>/<c>unless</c> bodies) into the <c>string</c>
/// <c>AllowRule.When</c>/<c>ForbidRule.When</c> actually holds -- see
/// <c>Aegis.Policies</c>' <c>Language</c> field doc comment for why that's a
/// rendered string rather than a typed AST. <see cref="PolicyEvaluator"/>
/// re-parses this text via <see cref="CedarParser.ParseCondition"/> on first
/// use, so round-trip correctness (render then re-parse then evaluate
/// behaves identically to evaluating the original tree) is the whole point
/// of this type -- every sub-expression is parenthesized, even where
/// operator precedence wouldn't strictly require it, to make that
/// correctness immediate to verify rather than relying on a precedence
/// table staying in sync with <see cref="CedarParser"/>'s own grammar.
/// </summary>
internal static class CedarExprRenderer
{
    public static string Render(CedarExpr expr) => expr switch
    {
        CedarLiteralExpr e => RenderLiteral(e),
        CedarEntityRefExpr e => RenderEntityRef(e.Type, e.Id),
        CedarVarExpr e => RenderVar(e.Variable),
        CedarAttrExpr e => $"{Parenthesized(e.Target)}.{e.Name}",
        CedarHasExpr e => $"({Parenthesized(e.Target)} has {e.AttributeName})",
        CedarLikeExpr e => $"({Parenthesized(e.Target)} like {RenderLikePattern(e.Pattern)})",
        CedarIsExpr e => RenderIs(e),
        CedarInExpr e => $"({Parenthesized(e.Left)} in {Parenthesized(e.Right)})",
        CedarUnaryExpr e => RenderUnary(e),
        CedarBinaryExpr e => RenderBinary(e),
        CedarIfExpr e => $"(if {Render(e.Condition)} then {Render(e.Then)} else {Render(e.Else)})",
        CedarSetExpr e => $"[{string.Join(", ", e.Elements.Select(Render))}]",
        CedarRecordExpr e => RenderRecord(e),
        CedarMethodCallExpr e => $"{Parenthesized(e.Target)}.{e.MethodName}({string.Join(", ", e.Arguments.Select(Render))})",
        CedarExtensionCallExpr e => $"{e.FunctionName}({string.Join(", ", e.Arguments.Select(Render))})",
        _ => throw new CedarLoweringException($"Cannot render unsupported Cedar expression node '{expr.GetType().Name}'"),
    };

    /// <summary>
    /// Every non-atomic sub-expression is wrapped in <c>(...)</c> -- see
    /// this type's own doc comment for why "always parenthesize" was chosen
    /// over a precedence-aware minimal-parens renderer.
    /// </summary>
    private static string Parenthesized(CedarExpr expr) => expr switch
    {
        CedarLiteralExpr or CedarEntityRefExpr or CedarVarExpr => Render(expr),
        _ => $"({Render(expr)})",
    };

    private static string RenderLiteral(CedarLiteralExpr expr) => expr.Value switch
    {
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        string s => RenderStringLiteral(s),
        null => throw new CedarLoweringException("Cannot render a null Cedar literal"),
        _ => throw new CedarLoweringException($"Cannot render a Cedar literal of type '{expr.Value.GetType().Name}'"),
    };

    private static string RenderEntityRef(IReadOnlyList<string> type, string id) =>
        $"{string.Join("::", type)}::{RenderStringLiteral(id)}";

    private static string RenderVar(CedarVar variable) => variable switch
    {
        CedarVar.Principal => "principal",
        CedarVar.Action => "action",
        CedarVar.Resource => "resource",
        CedarVar.Context => "context",
        _ => throw new CedarLoweringException($"Cannot render unknown Cedar variable '{variable}'"),
    };

    private static string RenderIs(CedarIsExpr expr)
    {
        var typeName = string.Join("::", expr.Type);
        return expr.InExpr is null
            ? $"({Parenthesized(expr.Target)} is {typeName})"
            : $"({Parenthesized(expr.Target)} is {typeName} in {Parenthesized(expr.InExpr)})";
    }

    private static string RenderUnary(CedarUnaryExpr expr)
    {
        var op = expr.Operator == CedarUnaryOperator.Not ? "!" : "-";
        return $"({op}{Parenthesized(expr.Operand)})";
    }

    private static string RenderBinary(CedarBinaryExpr expr)
    {
        var op = expr.Operator switch
        {
            CedarBinaryOperator.And => "&&",
            CedarBinaryOperator.Or => "||",
            CedarBinaryOperator.Equal => "==",
            CedarBinaryOperator.NotEqual => "!=",
            CedarBinaryOperator.Less => "<",
            CedarBinaryOperator.LessEqual => "<=",
            CedarBinaryOperator.Greater => ">",
            CedarBinaryOperator.GreaterEqual => ">=",
            CedarBinaryOperator.Add => "+",
            CedarBinaryOperator.Subtract => "-",
            CedarBinaryOperator.Multiply => "*",
            _ => throw new CedarLoweringException($"Cannot render unknown Cedar binary operator '{expr.Operator}'"),
        };

        return $"({Parenthesized(expr.Left)} {op} {Parenthesized(expr.Right)})";
    }

    private static string RenderRecord(CedarRecordExpr expr)
    {
        var fields = expr.Fields.Select(f => $"{RenderStringLiteral(f.Key)}: {Render(f.Value)}");
        return $"{{{string.Join(", ", fields)}}}";
    }

    private static string RenderStringLiteral(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// <see cref="CedarLikeExpr.Pattern"/> already has ordinary string
    /// escapes decoded (it's lexed via the same string-literal path as any
    /// other string) but keeps <see cref="CedarLexer.LiteralStarMarker"/>
    /// wherever the original source had <c>\*</c> -- this renders each
    /// marker back to <c>\*</c> and every bare <c>*</c> stays a bare
    /// wildcard, then applies the same escaping <see cref="RenderStringLiteral"/>
    /// does for everything else.
    /// </summary>
    private static string RenderLikePattern(string pattern)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in pattern)
        {
            switch (c)
            {
                case CedarLexer.LiteralStarMarker:
                    sb.Append("\\*");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}