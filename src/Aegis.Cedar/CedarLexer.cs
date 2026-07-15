using System.Globalization;
using System.Text;

namespace Aegis.Cedar;

/// <summary>Turns Cedar policy-file source into a flat token stream.</summary>
internal sealed class CedarLexer
{
    private static readonly Dictionary<string, CedarTokenType> Keywords = new(StringComparer.Ordinal)
    {
        ["permit"] = CedarTokenType.Permit,
        ["forbid"] = CedarTokenType.Forbid,
        ["when"] = CedarTokenType.When,
        ["unless"] = CedarTokenType.Unless,
        ["principal"] = CedarTokenType.Principal,
        ["action"] = CedarTokenType.Action,
        ["resource"] = CedarTokenType.Resource,
        ["context"] = CedarTokenType.Context,
        ["if"] = CedarTokenType.If,
        ["then"] = CedarTokenType.Then,
        ["else"] = CedarTokenType.Else,
        ["in"] = CedarTokenType.In,
        ["has"] = CedarTokenType.Has,
        ["like"] = CedarTokenType.Like,
        ["is"] = CedarTokenType.Is,
        ["true"] = CedarTokenType.True,
        ["false"] = CedarTokenType.False,
    };

    private readonly string _text;
    private int _pos;

    public CedarLexer(string text)
    {
        _text = text;
    }

    public List<CedarToken> Tokenize()
    {
        var tokens = new List<CedarToken>();
        CedarToken token;
        do
        {
            token = NextToken();
            tokens.Add(token);
        } while (token.Type != CedarTokenType.Eof);

        return tokens;
    }

    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    private char Peek(int offset = 1) =>
        _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

    private CedarToken NextToken()
    {
        SkipWhitespaceAndComments();

        var start = _pos;
        if (_pos >= _text.Length)
        {
            return new CedarToken(CedarTokenType.Eof, string.Empty, null, start);
        }

        var c = Current;

        if (c == ':' && Peek() == ':')
        {
            _pos += 2;
            return new CedarToken(CedarTokenType.DoubleColon, "::", null, start);
        }

        if (c == ':')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Colon, ":", null, start);
        }

        if (c == '.')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Dot, ".", null, start);
        }

        if (c == ',')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Comma, ",", null, start);
        }

        if (c == ';')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Semicolon, ";", null, start);
        }

        if (c == '(')
        {
            _pos++;
            return new CedarToken(CedarTokenType.LParen, "(", null, start);
        }

        if (c == ')')
        {
            _pos++;
            return new CedarToken(CedarTokenType.RParen, ")", null, start);
        }

        if (c == '{')
        {
            _pos++;
            return new CedarToken(CedarTokenType.LBrace, "{", null, start);
        }

        if (c == '}')
        {
            _pos++;
            return new CedarToken(CedarTokenType.RBrace, "}", null, start);
        }

        if (c == '[')
        {
            _pos++;
            return new CedarToken(CedarTokenType.LBracket, "[", null, start);
        }

        if (c == ']')
        {
            _pos++;
            return new CedarToken(CedarTokenType.RBracket, "]", null, start);
        }

        if (c == '=' && Peek() == '=')
        {
            _pos += 2;
            return new CedarToken(CedarTokenType.Equal, "==", null, start);
        }

        if (c == '!' && Peek() == '=')
        {
            _pos += 2;
            return new CedarToken(CedarTokenType.NotEqual, "!=", null, start);
        }

        if (c == '!')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Not, "!", null, start);
        }

        if (c == '&' && Peek() == '&')
        {
            _pos += 2;
            return new CedarToken(CedarTokenType.And, "&&", null, start);
        }

        if (c == '|' && Peek() == '|')
        {
            _pos += 2;
            return new CedarToken(CedarTokenType.Or, "||", null, start);
        }

        if (c == '<' && Peek() == '=')
        {
            _pos += 2;
            return new CedarToken(CedarTokenType.LessEqual, "<=", null, start);
        }

        if (c == '<')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Less, "<", null, start);
        }

        if (c == '>' && Peek() == '=')
        {
            _pos += 2;
            return new CedarToken(CedarTokenType.GreaterEqual, ">=", null, start);
        }

        if (c == '>')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Greater, ">", null, start);
        }

        if (c == '+')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Plus, "+", null, start);
        }

        if (c == '-')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Minus, "-", null, start);
        }

        if (c == '*')
        {
            _pos++;
            return new CedarToken(CedarTokenType.Star, "*", null, start);
        }

        if (c == '"')
        {
            return ReadString(start);
        }

        if (char.IsDigit(c))
        {
            return ReadLong(start);
        }

        if (char.IsLetter(c) || c == '_')
        {
            return ReadIdentifier(start);
        }

        throw new CedarSyntaxException($"Unexpected character '{c}'", start);
    }

    private void SkipWhitespaceAndComments()
    {
        while (true)
        {
            if (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            {
                _pos++;
                continue;
            }

            if (Current == '/' && Peek() == '/')
            {
                _pos += 2;
                while (_pos < _text.Length && _text[_pos] != '\n')
                {
                    _pos++;
                }

                continue;
            }

            break;
        }
    }

    private CedarToken ReadString(int start)
    {
        _pos++; // opening quote
        var sb = new StringBuilder();
        while (true)
        {
            if (_pos >= _text.Length)
            {
                throw new CedarSyntaxException("Unterminated string literal", start);
            }

            var c = _text[_pos];
            if (c == '"')
            {
                _pos++;
                break;
            }

            if (c == '\\')
            {
                _pos++;
                sb.Append(ReadEscape(start));
                continue;
            }

            sb.Append(c);
            _pos++;
        }

        return new CedarToken(CedarTokenType.String, sb.ToString(), sb.ToString(), start);
    }

    private char ReadEscape(int stringStart)
    {
        if (_pos >= _text.Length)
        {
            throw new CedarSyntaxException("Unterminated string literal", stringStart);
        }

        var c = _text[_pos];
        _pos++;
        switch (c)
        {
            case 'n':
                return '\n';
            case 'r':
                return '\r';
            case 't':
                return '\t';
            case '0':
                return '\0';
            case '\\':
            case '"':
            case '\'':
                return c;
            case 'u':
                return ReadUnicodeEscape(stringStart);
            default:
                throw new CedarSyntaxException($"Unsupported escape sequence '\\{c}'", stringStart);
        }
    }

    private char ReadUnicodeEscape(int stringStart)
    {
        if (Current != '{')
        {
            throw new CedarSyntaxException("Expected '{' after '\\u'", stringStart);
        }

        _pos++;
        var hexStart = _pos;
        while (Current != '}')
        {
            if (_pos >= _text.Length)
            {
                throw new CedarSyntaxException("Unterminated unicode escape, expected '}'", stringStart);
            }

            _pos++;
        }

        var hex = _text[hexStart.._pos];
        _pos++; // closing '}'
        return (char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private CedarToken ReadLong(int start)
    {
        while (char.IsDigit(Current))
        {
            _pos++;
        }

        var text = _text[start.._pos];
        var value = long.Parse(text, CultureInfo.InvariantCulture);
        return new CedarToken(CedarTokenType.Long, text, value, start);
    }

    private CedarToken ReadIdentifier(int start)
    {
        while (char.IsLetterOrDigit(Current) || Current == '_')
        {
            _pos++;
        }

        var text = _text[start.._pos];
        if (Keywords.TryGetValue(text, out var keywordType))
        {
            object? value = keywordType switch
            {
                CedarTokenType.True => true,
                CedarTokenType.False => false,
                _ => null,
            };
            return new CedarToken(keywordType, text, value, start);
        }

        return new CedarToken(CedarTokenType.Identifier, text, null, start);
    }
}