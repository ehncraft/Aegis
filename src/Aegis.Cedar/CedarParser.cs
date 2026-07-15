using System.Diagnostics;

namespace Aegis.Cedar;

/// <summary>
/// Recursive-descent parser for Cedar policy files.
///
/// Policy grammar (from the <see href="https://docs.cedarpolicy.com/">Cedar
/// language reference</see>):
///   policySet  := policy*
///   policy     := effect '(' scope ')' condition* ';'
///   effect     := 'permit' | 'forbid'
///   scope      := principalScope ',' actionScope ',' resourceScope
///   condition  := ('when' | 'unless') '{' expr '}'
///
/// Expression grammar (lowest to highest precedence):
///   expr       := 'if' expr 'then' expr 'else' expr | or
///   or         := and ('||' and)*
///   and        := relation ('&&' relation)*
///   relation   := add (relop add | 'has' attr | 'like' PATTERN | 'is' NAME ('in' add)?)?
///   relop      := '==' | '!=' | '&lt;' | '&lt;=' | '&gt;' | '&gt;=' | 'in'
///   add        := mult (('+' | '-') mult)*
///   mult       := unary ('*' unary)*
///   unary      := ('!' | '-')* member
///   member     := primary ('.' IDENT ('(' args ')')? | '[' STRING ']')*
///   primary    := LONG | STRING | 'true' | 'false' | 'principal' | 'action'
///               | 'resource' | 'context' | NAME '(' args ')' | entityRef
///               | '(' expr ')' | '[' args ']' | '{' recordFields '}'
///
/// Cedar's relational operators don't chain -- <c>a == b == c</c> is a
/// syntax error, same as Aegis's own comparison operators (see
/// <c>Aegis.Expressions/Parser.cs</c>), so <c>relation</c> allows at most
/// one relational suffix rather than looping.
/// </summary>
internal sealed class CedarParser
{
    private readonly List<CedarToken> _tokens;
    private int _pos;

    private CedarParser(List<CedarToken> tokens)
    {
        _tokens = tokens;
    }

    public static IReadOnlyList<CedarPolicy> Parse(string text)
    {
        var tokens = new CedarLexer(text).Tokenize();
        var parser = new CedarParser(tokens);
        var policies = new List<CedarPolicy>();
        while (parser.Current.Type != CedarTokenType.Eof)
        {
            policies.Add(parser.ParsePolicy());
        }

        return policies;
    }

    private CedarToken Current => _tokens[_pos];

    private CedarPolicy ParsePolicy()
    {
        var effect = Current.Type switch
        {
            CedarTokenType.Permit => CedarEffect.Permit,
            CedarTokenType.Forbid => CedarEffect.Forbid,
            _ => throw new CedarSyntaxException(
                $"Expected 'permit' or 'forbid' but found '{Current.Text}'", Current.Position),
        };
        Advance();

        Expect(CedarTokenType.LParen, "'('");
        var principalScope = ParsePrincipalOrResourceScope(CedarTokenType.Principal);
        Expect(CedarTokenType.Comma, "','");
        var actionScope = ParseActionScope();
        Expect(CedarTokenType.Comma, "','");
        var resourceScope = ParsePrincipalOrResourceScope(CedarTokenType.Resource);
        Expect(CedarTokenType.RParen, "')'");

        var conditions = new List<CedarCondition>();
        while (Current.Type is CedarTokenType.When or CedarTokenType.Unless)
        {
            var kind = Current.Type == CedarTokenType.When ? CedarConditionKind.When : CedarConditionKind.Unless;
            Advance();
            Expect(CedarTokenType.LBrace, "'{'");
            var body = ParseExpr();
            Expect(CedarTokenType.RBrace, "'}'");
            conditions.Add(new CedarCondition(kind, body));
        }

        Expect(CedarTokenType.Semicolon, "';'");
        return new CedarPolicy(effect, principalScope, actionScope, resourceScope, conditions);
    }

    private CedarScopeConstraint ParsePrincipalOrResourceScope(CedarTokenType varToken)
    {
        Expect(varToken, varToken.ToString());

        if (Current.Type == CedarTokenType.Equal)
        {
            Advance();
            return new CedarEqScope(ParseEntityRef());
        }

        if (Current.Type == CedarTokenType.In)
        {
            Advance();
            return new CedarInScope(ParseEntityRef());
        }

        if (Current.Type == CedarTokenType.Is)
        {
            Advance();
            var type = ParseNamePath();
            if (Current.Type == CedarTokenType.In)
            {
                Advance();
                return new CedarIsInScope(type, ParseEntityRef());
            }

            return new CedarIsScope(type);
        }

        return new CedarAnyScope();
    }

    private CedarScopeConstraint ParseActionScope()
    {
        Expect(CedarTokenType.Action, "'action'");

        if (Current.Type == CedarTokenType.Equal)
        {
            Advance();
            return new CedarEqScope(ParseEntityRef());
        }

        if (Current.Type == CedarTokenType.In)
        {
            Advance();
            if (Current.Type == CedarTokenType.LBracket)
            {
                Advance();
                var entities = new List<EntityRef>();
                if (Current.Type != CedarTokenType.RBracket)
                {
                    entities.Add(ParseEntityRef());
                    while (Current.Type == CedarTokenType.Comma)
                    {
                        Advance();
                        entities.Add(ParseEntityRef());
                    }
                }

                Expect(CedarTokenType.RBracket, "']'");
                return new CedarInSetScope(entities);
            }

            return new CedarInScope(ParseEntityRef());
        }

        return new CedarAnyScope();
    }

    /// <summary><c>IDENT ('::' IDENT)* '::' STRING</c>.</summary>
    private EntityRef ParseEntityRef()
    {
        var type = new List<string> { Expect(CedarTokenType.Identifier, "entity type").Text };
        while (true)
        {
            Expect(CedarTokenType.DoubleColon, "'::'");
            if (Current.Type == CedarTokenType.String)
            {
                var id = Advance().Text;
                return new EntityRef(type, id);
            }

            type.Add(Expect(CedarTokenType.Identifier, "entity type segment or id").Text);
        }
    }

    /// <summary><c>IDENT ('::' IDENT)*</c> -- a type name, no trailing id.</summary>
    private List<string> ParseNamePath()
    {
        var segments = new List<string> { Expect(CedarTokenType.Identifier, "type name").Text };
        while (Current.Type == CedarTokenType.DoubleColon)
        {
            Advance();
            segments.Add(Expect(CedarTokenType.Identifier, "type name segment").Text);
        }

        return segments;
    }

    private CedarExpr ParseExpr()
    {
        if (Current.Type == CedarTokenType.If)
        {
            var start = Advance();
            var condition = ParseExpr();
            Expect(CedarTokenType.Then, "'then'");
            var thenBranch = ParseExpr();
            Expect(CedarTokenType.Else, "'else'");
            var elseBranch = ParseExpr();
            return new CedarIfExpr(condition, thenBranch, elseBranch, start.Position);
        }

        return ParseOr();
    }

    private CedarExpr ParseOr()
    {
        var left = ParseAnd();
        while (Current.Type == CedarTokenType.Or)
        {
            var op = Advance();
            left = new CedarBinaryExpr(CedarBinaryOperator.Or, left, ParseAnd(), op.Position);
        }

        return left;
    }

    private CedarExpr ParseAnd()
    {
        var left = ParseRelation();
        while (Current.Type == CedarTokenType.And)
        {
            var op = Advance();
            left = new CedarBinaryExpr(CedarBinaryOperator.And, left, ParseRelation(), op.Position);
        }

        return left;
    }

    private CedarExpr ParseRelation()
    {
        var left = ParseAdd();

        if (Current.Type is CedarTokenType.Equal or CedarTokenType.NotEqual or CedarTokenType.Less
            or CedarTokenType.LessEqual or CedarTokenType.Greater or CedarTokenType.GreaterEqual)
        {
            var op = Advance();
            return new CedarBinaryExpr(MapRelationalOperator(op.Type), left, ParseAdd(), op.Position);
        }

        if (Current.Type == CedarTokenType.In)
        {
            var op = Advance();
            return new CedarInExpr(left, ParseAdd(), op.Position);
        }

        if (Current.Type == CedarTokenType.Has)
        {
            var op = Advance();
            var name = Current.Type == CedarTokenType.String
                ? Advance().Text
                : Expect(CedarTokenType.Identifier, "attribute name").Text;
            return new CedarHasExpr(left, name, op.Position);
        }

        if (Current.Type == CedarTokenType.Like)
        {
            var op = Advance();
            var pattern = Expect(CedarTokenType.String, "'like' pattern").Text;
            return new CedarLikeExpr(left, pattern, op.Position);
        }

        if (Current.Type == CedarTokenType.Is)
        {
            var op = Advance();
            var type = ParseNamePath();
            var inExpr = Current.Type == CedarTokenType.In ? ParseIsInSuffix() : null;
            return new CedarIsExpr(left, type, inExpr, op.Position);
        }

        return left;
    }

    private CedarExpr ParseIsInSuffix()
    {
        Advance(); // 'in'
        return ParseAdd();
    }

    private static CedarBinaryOperator MapRelationalOperator(CedarTokenType type) => type switch
    {
        CedarTokenType.Equal => CedarBinaryOperator.Equal,
        CedarTokenType.NotEqual => CedarBinaryOperator.NotEqual,
        CedarTokenType.Less => CedarBinaryOperator.Less,
        CedarTokenType.LessEqual => CedarBinaryOperator.LessEqual,
        CedarTokenType.Greater => CedarBinaryOperator.Greater,
        CedarTokenType.GreaterEqual => CedarBinaryOperator.GreaterEqual,
        _ => throw new UnreachableException($"Unsupported relational operator '{type}'"),
    };

    private CedarExpr ParseAdd()
    {
        var left = ParseMult();
        while (Current.Type is CedarTokenType.Plus or CedarTokenType.Minus)
        {
            var op = Advance();
            var kind = op.Type == CedarTokenType.Plus ? CedarBinaryOperator.Add : CedarBinaryOperator.Subtract;
            left = new CedarBinaryExpr(kind, left, ParseMult(), op.Position);
        }

        return left;
    }

    private CedarExpr ParseMult()
    {
        var left = ParseUnary();
        while (Current.Type == CedarTokenType.Star)
        {
            var op = Advance();
            left = new CedarBinaryExpr(CedarBinaryOperator.Multiply, left, ParseUnary(), op.Position);
        }

        return left;
    }

    private CedarExpr ParseUnary()
    {
        if (Current.Type == CedarTokenType.Not)
        {
            var op = Advance();
            return new CedarUnaryExpr(CedarUnaryOperator.Not, ParseUnary(), op.Position);
        }

        if (Current.Type == CedarTokenType.Minus)
        {
            var op = Advance();
            return new CedarUnaryExpr(CedarUnaryOperator.Negate, ParseUnary(), op.Position);
        }

        return ParseMember();
    }

    private CedarExpr ParseMember()
    {
        var expr = ParsePrimary();
        while (true)
        {
            if (Current.Type == CedarTokenType.Dot)
            {
                Advance();
                var name = Expect(CedarTokenType.Identifier, "member name").Text;
                if (Current.Type == CedarTokenType.LParen)
                {
                    Advance();
                    var args = ParseArgs();
                    Expect(CedarTokenType.RParen, "')'");
                    expr = new CedarMethodCallExpr(expr, name, args, expr.Position);
                }
                else
                {
                    expr = new CedarAttrExpr(expr, name, expr.Position);
                }

                continue;
            }

            if (Current.Type == CedarTokenType.LBracket)
            {
                Advance();
                var key = Expect(CedarTokenType.String, "attribute key").Text;
                Expect(CedarTokenType.RBracket, "']'");
                expr = new CedarAttrExpr(expr, key, expr.Position);
                continue;
            }

            return expr;
        }
    }

    private CedarExpr ParsePrimary()
    {
        var token = Current;
        switch (token.Type)
        {
            case CedarTokenType.Long:
            case CedarTokenType.String:
            case CedarTokenType.True:
            case CedarTokenType.False:
                Advance();
                return new CedarLiteralExpr(token.Value, token.Position);

            case CedarTokenType.Principal:
                Advance();
                return new CedarVarExpr(CedarVar.Principal, token.Position);

            case CedarTokenType.Action:
                Advance();
                return new CedarVarExpr(CedarVar.Action, token.Position);

            case CedarTokenType.Resource:
                Advance();
                return new CedarVarExpr(CedarVar.Resource, token.Position);

            case CedarTokenType.Context:
                Advance();
                return new CedarVarExpr(CedarVar.Context, token.Position);

            case CedarTokenType.Identifier:
                return ParseIdentifierPrimary();

            case CedarTokenType.LParen:
                Advance();
                var inner = ParseExpr();
                Expect(CedarTokenType.RParen, "')'");
                return inner;

            case CedarTokenType.LBracket:
                return ParseSetLiteral();

            case CedarTokenType.LBrace:
                return ParseRecordLiteral();

            default:
                throw new CedarSyntaxException($"Unexpected token '{token.Text}'", token.Position);
        }
    }

    /// <summary>
    /// An identifier starting a primary is either an extension call
    /// (<c>ip(...)</c>) or the start of an entity literal (<c>User::"id"</c>)
    /// -- Cedar has no other bare-identifier primary (no policy-level
    /// variables the way Aegis's own grammar has <c>${name}</c>).
    /// </summary>
    private CedarExpr ParseIdentifierPrimary()
    {
        var first = Expect(CedarTokenType.Identifier, "identifier");

        if (Current.Type == CedarTokenType.LParen)
        {
            Advance();
            var args = ParseArgs();
            Expect(CedarTokenType.RParen, "')'");
            return new CedarExtensionCallExpr(first.Text, args, first.Position);
        }

        if (Current.Type == CedarTokenType.DoubleColon)
        {
            var type = new List<string> { first.Text };
            while (true)
            {
                Expect(CedarTokenType.DoubleColon, "'::'");
                if (Current.Type == CedarTokenType.String)
                {
                    var id = Advance().Text;
                    return new CedarEntityRefExpr(type, id, first.Position);
                }

                type.Add(Expect(CedarTokenType.Identifier, "entity type segment or id").Text);
            }
        }

        throw new CedarSyntaxException($"Unexpected identifier '{first.Text}'", first.Position);
    }

    private List<CedarExpr> ParseArgs()
    {
        var args = new List<CedarExpr>();
        if (Current.Type != CedarTokenType.RParen)
        {
            args.Add(ParseExpr());
            while (Current.Type == CedarTokenType.Comma)
            {
                Advance();
                args.Add(ParseExpr());
            }
        }

        return args;
    }

    private CedarSetExpr ParseSetLiteral()
    {
        var start = Expect(CedarTokenType.LBracket, "'['");
        var elements = new List<CedarExpr>();
        if (Current.Type != CedarTokenType.RBracket)
        {
            elements.Add(ParseExpr());
            while (Current.Type == CedarTokenType.Comma)
            {
                Advance();
                elements.Add(ParseExpr());
            }
        }

        Expect(CedarTokenType.RBracket, "']'");
        return new CedarSetExpr(elements, start.Position);
    }

    private CedarRecordExpr ParseRecordLiteral()
    {
        var start = Expect(CedarTokenType.LBrace, "'{'");
        var fields = new List<CedarRecordField>();
        if (Current.Type != CedarTokenType.RBrace)
        {
            fields.Add(ParseRecordField());
            while (Current.Type == CedarTokenType.Comma)
            {
                Advance();
                fields.Add(ParseRecordField());
            }
        }

        Expect(CedarTokenType.RBrace, "'}'");
        return new CedarRecordExpr(fields, start.Position);
    }

    private CedarRecordField ParseRecordField()
    {
        var key = Current.Type == CedarTokenType.String
            ? Advance().Text
            : Expect(CedarTokenType.Identifier, "record key").Text;
        Expect(CedarTokenType.Colon, "':'");
        var value = ParseExpr();
        return new CedarRecordField(key, value);
    }

    private CedarToken Advance() => _tokens[_pos++];

    private CedarToken Expect(CedarTokenType type, string expected)
    {
        if (Current.Type != type)
        {
            throw new CedarSyntaxException($"Expected {expected} but found '{Current.Text}'", Current.Position);
        }

        return Advance();
    }
}