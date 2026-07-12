using System.Text.RegularExpressions;

namespace Aegis.Sql;

/// <summary>
/// Validates and quotes SQL Server table/column identifiers. Values are
/// always bound as <c>SqlParameter</c>s, but SQL has no equivalent for
/// parameterizing identifiers -- table/column names have to be interpolated
/// into the command text itself. Per the
/// <see href="https://cheatsheetseries.owasp.org/cheatsheets/SQL_Injection_Prevention_Cheat_Sheet.html">
/// OWASP SQL Injection Prevention Cheat Sheet</see>'s guidance for exactly
/// this situation ("map parameter values to legal/expected... names"),
/// whitelisting is the strongest available defense here: every identifier
/// is checked against a strict letters/digits/underscore pattern and
/// rejected outright if it doesn't match, before it ever reaches a query.
/// Bracket-quoting on top of that is defense in depth, not the primary
/// control.
/// </summary>
public static partial class SqlIdentifier
{
    public static string Quote(string identifier)
    {
        if (!SafeIdentifierPattern().IsMatch(identifier))
        {
            throw new ArgumentException(
                $"'{identifier}' is not a safe SQL identifier -- only letters, digits, and underscores are " +
                "allowed, and it can't start with a digit. Table/column names are configuration, but they're " +
                "interpolated directly into SQL text (identifiers can't be parameterized), so this is checked " +
                "rather than trusted.",
                nameof(identifier));
        }

        return $"[{identifier}]";
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SafeIdentifierPattern();
}