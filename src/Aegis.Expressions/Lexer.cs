using System.Globalization;
using System.Text;

namespace Aegis.Expressions;

/// <summary>Turns a condition string into a flat token stream.</summary>
internal sealed class Lexer
{
    private readonly string _text;
    private int _pos;

    public Lexer(string text)
    {
        _text = text;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        Token token;
        do
        {
            token = NextToken();
            tokens.Add(token);
        } while (token.Type != TokenType.Eof);

        return tokens;
    }

    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    private char Peek(int offset = 1) =>
        _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private Token NextToken()
    {
        SkipWhitespace();

        var start = _pos;
        if (_pos >= _text.Length)
        {
            return new Token(TokenType.Eof, string.Empty, null, start);
        }

        var c = Current;

        if (c == '.')
        {
            _pos++;
            return new Token(TokenType.Dot, ".", null, start);
        }

        if (c == '(')
        {
            _pos++;
            return new Token(TokenType.LParen, "(", null, start);
        }

        if (c == ')')
        {
            _pos++;
            return new Token(TokenType.RParen, ")", null, start);
        }

        if (c == '=' && Peek() == '=')
        {
            _pos += 2;
            return new Token(TokenType.Equal, "==", null, start);
        }

        if (c == '!' && Peek() == '=')
        {
            _pos += 2;
            return new Token(TokenType.NotEqual, "!=", null, start);
        }

        if (c == '!')
        {
            _pos++;
            return new Token(TokenType.Not, "!", null, start);
        }

        if (c == '&' && Peek() == '&')
        {
            _pos += 2;
            return new Token(TokenType.And, "&&", null, start);
        }

        if (c == '|' && Peek() == '|')
        {
            _pos += 2;
            return new Token(TokenType.Or, "||", null, start);
        }

        if (c == '<' && Peek() == '=')
        {
            _pos += 2;
            return new Token(TokenType.LessEqual, "<=", null, start);
        }

        if (c == '<')
        {
            _pos++;
            return new Token(TokenType.Less, "<", null, start);
        }

        if (c == '>' && Peek() == '=')
        {
            _pos += 2;
            return new Token(TokenType.GreaterEqual, ">=", null, start);
        }

        if (c == '>')
        {
            _pos++;
            return new Token(TokenType.Greater, ">", null, start);
        }

        if (c == '\'' || c == '"')
        {
            return ReadString(c, start);
        }

        if (char.IsDigit(c))
        {
            return ReadNumber(start);
        }

        if (char.IsLetter(c) || c == '_')
        {
            return ReadIdentifier(start);
        }

        throw new ExpressionSyntaxException($"Unexpected character '{c}'", start);
    }

    private void SkipWhitespace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
        {
            _pos++;
        }
    }

    private Token ReadString(char quote, int start)
    {
        _pos++; // opening quote
        var sb = new StringBuilder();
        while (true)
        {
            if (_pos >= _text.Length)
            {
                throw new ExpressionSyntaxException("Unterminated string literal", start);
            }

            var c = _text[_pos];
            if (c == quote)
            {
                _pos++;
                break;
            }

            if (c == '\\' && Peek() == quote)
            {
                sb.Append(quote);
                _pos += 2;
                continue;
            }

            sb.Append(c);
            _pos++;
        }

        return new Token(TokenType.String, sb.ToString(), sb.ToString(), start);
    }

    private Token ReadNumber(int start)
    {
        while (char.IsDigit(Current))
        {
            _pos++;
        }

        if (Current == '.' && char.IsDigit(Peek()))
        {
            _pos++;
            while (char.IsDigit(Current))
            {
                _pos++;
            }
        }

        var text = _text[start.._pos];
        var value = double.Parse(text, CultureInfo.InvariantCulture);
        return new Token(TokenType.Number, text, value, start);
    }

    private Token ReadIdentifier(int start)
    {
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            _pos++;
        }

        var text = _text[start.._pos];
        return text switch
        {
            "true" => new Token(TokenType.True, text, true, start),
            "false" => new Token(TokenType.False, text, false, start),
            _ => new Token(TokenType.Identifier, text, null, start),
        };
    }
}
