namespace Aegis.Expressions;

internal enum TokenType
{
    Identifier,
    String,
    Number,
    True,
    False,
    Dot,
    Equal,
    NotEqual,
    And,
    Or,
    Not,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    LParen,
    RParen,
    Variable,
    Eof,
}