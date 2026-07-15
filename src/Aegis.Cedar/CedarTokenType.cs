namespace Aegis.Cedar;

internal enum CedarTokenType
{
    Identifier,
    String,
    Long,
    True,
    False,

    // Keywords
    Permit,
    Forbid,
    When,
    Unless,
    Principal,
    Action,
    Resource,
    Context,
    If,
    Then,
    Else,
    In,
    Has,
    Like,
    Is,

    // Operators
    And,
    Or,
    Not,
    Equal,
    NotEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    Plus,
    Minus,
    Star,

    // Punctuation
    Dot,
    Colon,
    DoubleColon,
    Comma,
    Semicolon,
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,

    Eof,
}