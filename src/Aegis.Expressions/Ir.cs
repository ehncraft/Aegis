namespace Aegis.Expressions;

/// <summary>
/// The intermediate representation a <see cref="CompiledExpression"/> is
/// lowered to before optimization and codegen. Unlike <see cref="Expr"/>
/// (the parse tree, one node shape per grammar production), IR nodes are
/// keyed by evaluation semantics rather than lexical token -- <c>==</c> and
/// <c>&lt;</c> both parse as <see cref="BinaryExpr"/> but lower to distinct
/// <see cref="IrComparison"/> operators, decoupling <see cref="IrLowering"/>,
/// <see cref="SemanticAnalyzer"/>, <see cref="IrOptimizer"/>, and
/// <see cref="ExpressionCompiler"/> from the grammar's token set.
/// </summary>
internal abstract class IrNode(int position)
{
    public int Position { get; } = position;
}

internal sealed class IrConstant(object? value, int position) : IrNode(position)
{
    public object? Value { get; } = value;
}

internal sealed class IrMember(IReadOnlyList<string> path, int position) : IrNode(position)
{
    public IReadOnlyList<string> Path { get; } = path;
}

internal sealed class IrVariable(string name, int position) : IrNode(position)
{
    public string Name { get; } = name;
}

internal sealed class IrNot(IrNode operand, int position) : IrNode(position)
{
    public IrNode Operand { get; } = operand;
}

internal enum IrLogicalOperator
{
    And,
    Or,
}

internal sealed class IrLogical(IrLogicalOperator op, IrNode left, IrNode right, int position) : IrNode(position)
{
    public IrLogicalOperator Operator { get; } = op;

    public IrNode Left { get; } = left;

    public IrNode Right { get; } = right;
}

internal enum IrComparisonOperator
{
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
}

internal sealed class IrComparison(IrComparisonOperator op, IrNode left, IrNode right, int position) : IrNode(position)
{
    public IrComparisonOperator Operator { get; } = op;

    public IrNode Left { get; } = left;

    public IrNode Right { get; } = right;
}