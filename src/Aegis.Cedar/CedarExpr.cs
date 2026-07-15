namespace Aegis.Cedar;

/// <summary>
/// Cedar's expression AST -- richer than Aegis's own condition-expression
/// grammar (<c>Aegis.Expressions</c>) by design: entities, sets, records,
/// and Cedar-specific operators (<c>has</c>/<c>like</c>/<c>in</c>/<c>is</c>)
/// don't have an Aegis equivalent to lower onto, so Cedar expressions are
/// evaluated natively rather than translated. See #94's plan for why.
/// </summary>
internal abstract class CedarExpr(int position)
{
    public int Position { get; } = position;
}

internal sealed class CedarLiteralExpr(object? value, int position) : CedarExpr(position)
{
    public object? Value { get; } = value;
}

/// <summary>An entity literal such as <c>User::"alice"</c> or the namespaced <c>MyApp::Group::"admins"</c>.</summary>
internal sealed class CedarEntityRefExpr(IReadOnlyList<string> type, string id, int position) : CedarExpr(position)
{
    public IReadOnlyList<string> Type { get; } = type;

    public string Id { get; } = id;
}

internal enum CedarVar
{
    Principal,
    Action,
    Resource,
    Context,
}

internal sealed class CedarVarExpr(CedarVar variable, int position) : CedarExpr(position)
{
    public CedarVar Variable { get; } = variable;
}

/// <summary>Attribute access, either <c>expr.ident</c> or <c>expr["key"]</c> -- semantically identical.</summary>
internal sealed class CedarAttrExpr(CedarExpr target, string name, int position) : CedarExpr(position)
{
    public CedarExpr Target { get; } = target;

    public string Name { get; } = name;
}

internal sealed class CedarHasExpr(CedarExpr target, string attributeName, int position) : CedarExpr(position)
{
    public CedarExpr Target { get; } = target;

    public string AttributeName { get; } = attributeName;
}

/// <summary><paramref name="pattern"/> keeps Cedar's <c>*</c> wildcards uninterpreted -- matching is an evaluator concern.</summary>
internal sealed class CedarLikeExpr(CedarExpr target, string pattern, int position) : CedarExpr(position)
{
    public CedarExpr Target { get; } = target;

    public string Pattern { get; } = pattern;
}

/// <summary><c>expr is Type</c>, optionally <c>expr is Type in inExpr</c>.</summary>
internal sealed class CedarIsExpr(CedarExpr target, IReadOnlyList<string> type, CedarExpr? inExpr, int position)
    : CedarExpr(position)
{
    public CedarExpr Target { get; } = target;

    public IReadOnlyList<string> Type { get; } = type;

    public CedarExpr? InExpr { get; } = inExpr;
}

internal sealed class CedarInExpr(CedarExpr left, CedarExpr right, int position) : CedarExpr(position)
{
    public CedarExpr Left { get; } = left;

    public CedarExpr Right { get; } = right;
}

internal enum CedarUnaryOperator
{
    Not,
    Negate,
}

internal sealed class CedarUnaryExpr(CedarUnaryOperator op, CedarExpr operand, int position) : CedarExpr(position)
{
    public CedarUnaryOperator Operator { get; } = op;

    public CedarExpr Operand { get; } = operand;
}

internal enum CedarBinaryOperator
{
    And,
    Or,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Add,
    Subtract,
    Multiply,
}

internal sealed class CedarBinaryExpr(CedarBinaryOperator op, CedarExpr left, CedarExpr right, int position)
    : CedarExpr(position)
{
    public CedarBinaryOperator Operator { get; } = op;

    public CedarExpr Left { get; } = left;

    public CedarExpr Right { get; } = right;
}

internal sealed class CedarIfExpr(CedarExpr condition, CedarExpr thenBranch, CedarExpr elseBranch, int position)
    : CedarExpr(position)
{
    public CedarExpr Condition { get; } = condition;

    public CedarExpr Then { get; } = thenBranch;

    public CedarExpr Else { get; } = elseBranch;
}

internal sealed class CedarSetExpr(IReadOnlyList<CedarExpr> elements, int position) : CedarExpr(position)
{
    public IReadOnlyList<CedarExpr> Elements { get; } = elements;
}

internal sealed class CedarRecordField(string key, CedarExpr value)
{
    public string Key { get; } = key;

    public CedarExpr Value { get; } = value;
}

internal sealed class CedarRecordExpr(IReadOnlyList<CedarRecordField> fields, int position) : CedarExpr(position)
{
    public IReadOnlyList<CedarRecordField> Fields { get; } = fields;
}

/// <summary><c>target.methodName(args)</c> -- set methods (<c>contains</c>/<c>containsAll</c>/<c>containsAny</c>) and extension-type methods.</summary>
internal sealed class CedarMethodCallExpr(
    CedarExpr target, string methodName, IReadOnlyList<CedarExpr> arguments, int position) : CedarExpr(position)
{
    public CedarExpr Target { get; } = target;

    public string MethodName { get; } = methodName;

    public IReadOnlyList<CedarExpr> Arguments { get; } = arguments;
}

/// <summary>A free function call such as <c>ip("10.0.0.1")</c> or <c>decimal("1.23")</c>.</summary>
internal sealed class CedarExtensionCallExpr(string functionName, IReadOnlyList<CedarExpr> arguments, int position)
    : CedarExpr(position)
{
    public string FunctionName { get; } = functionName;

    public IReadOnlyList<CedarExpr> Arguments { get; } = arguments;
}