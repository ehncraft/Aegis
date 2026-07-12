using System.Globalization;

namespace Aegis.Cli;

/// <summary>
/// Parses <c>--*-attr key=value</c> CLI options into an attribute
/// dictionary, inferring a type from each value (bool, integer, decimal,
/// else string) rather than leaving everything a string -- policies that
/// compare an attribute with <![CDATA[<=]]>/<![CDATA[>=]]> need a numeric
/// value, not text, to evaluate correctly.
/// </summary>
internal static class AttributeParsing
{
    public static Dictionary<string, object?> Parse(IEnumerable<string> keyValuePairs)
    {
        var result = new Dictionary<string, object?>();
        foreach (var pair in keyValuePairs)
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw new ArgumentException(
                    $"'{pair}' is not in key=value format (expected e.g. department=finance).");
            }

            var key = pair[..separatorIndex];
            var rawValue = pair[(separatorIndex + 1)..];
            result[key] = InferValue(rawValue);
        }

        return result;
    }

    private static object InferValue(string raw)
    {
        if (raw is "true" or "false")
        {
            return bool.Parse(raw);
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return raw;
    }
}