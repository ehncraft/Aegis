namespace Aegis.Expressions;

/// <summary>
/// Recursive-descent parser for policy condition expressions.
///
/// Grammar (lowest to highest precedence):
///   or         := and ( '||' and )*
///   and        := equality ( '&&' equality )*
///   equality   := comparison ( ('==' | '!=') comparison )*
///   comparison := unary ( ('&lt;' | '&lt;=' | '&gt;' | '&gt;=') unary )*
///   unary      := '!' unary | primary
///   primary    := literal | member | variable | '(' or ')'
///   member     := IDENTIFIER ('.' IDENTIFIER)*
///   variable   := '${' IDENTIFIER '}'
/// </summary>
internal sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    private Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public static Expr Parse(string text)
    {
        var tokens = new Lexer(text).Tokenize();
        var parser = new Parser(tokens);
        var expr = parser.ParseOr();
        parser.Expect(TokenType.Eof, "end of expression");
        return expr;
    }

    private Token Current => _tokens[_pos];

    private Expr ParseOr()
    {
        var left = ParseAnd();
        while (Current.Type == TokenType.Or)
        {
            var op = Advance().Type;
            left = new BinaryExpr(op, left, ParseAnd());
        }

        return left;
    }

    private Expr ParseAnd()
    {
        var left = ParseEquality();
        while (Current.Type == TokenType.And)
        {
            var op = Advance().Type;
            left = new BinaryExpr(op, left, ParseEquality());
        }

        return left;
    }

    private Expr ParseEquality()
    {
        var left = ParseComparison();
        while (Current.Type is TokenType.Equal or TokenType.NotEqual)
        {
            var op = Advance().Type;
            left = new BinaryExpr(op, left, ParseComparison());
        }

        return left;
    }

    private Expr ParseComparison()
    {
        var left = ParseUnary();
        while (Current.Type is TokenType.Less or TokenType.LessEqual
            or TokenType.Greater or TokenType.GreaterEqual)
        {
            var op = Advance().Type;
            left = new BinaryExpr(op, left, ParseUnary());
        }

        return left;
    }

    private Expr ParseUnary()
    {
        if (Current.Type == TokenType.Not)
        {
            Advance();
            return new UnaryExpr(TokenType.Not, ParseUnary());
        }

        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        var token = Current;
        switch (token.Type)
        {
            case TokenType.String:
            case TokenType.Number:
            case TokenType.True:
            case TokenType.False:
                Advance();
                return new LiteralExpr(token.Value);

            case TokenType.Identifier:
                return ParseMember();

            case TokenType.Variable:
                Advance();
                return new VariableExpr(token.Text);

            case TokenType.LParen:
                Advance();
                var inner = ParseOr();
                Expect(TokenType.RParen, "')'");
                return inner;

            default:
                throw new ExpressionSyntaxException(
                    $"Unexpected token '{token.Text}'", token.Position);
        }
    }

    private MemberExpr ParseMember()
    {
        var path = new List<string> { Expect(TokenType.Identifier, "identifier").Text };
        while (Current.Type == TokenType.Dot)
        {
            Advance();
            path.Add(Expect(TokenType.Identifier, "identifier").Text);
        }

        return new MemberExpr(path);
    }

    private Token Advance() => _tokens[_pos++];

    private Token Expect(TokenType type, string expected)
    {
        if (Current.Type != type)
        {
            throw new ExpressionSyntaxException(
                $"Expected {expected} but found '{Current.Text}'", Current.Position);
        }

        return Advance();
    }
}