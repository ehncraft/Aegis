using System.Net;

using Aegis.Relationships;

namespace Aegis.Cedar;

internal enum CedarValueKind
{
    Bool,
    Long,
    String,
    Entity,
    Set,
    Record,
    Ip,
    Decimal,
}

/// <summary>
/// A runtime value produced by <see cref="CedarConditionEvaluator"/> --
/// typed, unlike Aegis's own loose <c>object?</c>-based expression context,
/// because Cedar's <c>has</c>/<c>like</c>/<c>contains</c>/<c>in</c> operators
/// need to distinguish sets/records/entities from primitives to behave
/// correctly and to produce Cedar-accurate error messages.
/// </summary>
internal readonly struct CedarValue
{
    private readonly object? _payload;

    private CedarValue(CedarValueKind kind, object? payload)
    {
        Kind = kind;
        _payload = payload;
    }

    public CedarValueKind Kind { get; }

    public static CedarValue Bool(bool value) => new(CedarValueKind.Bool, value);

    public static CedarValue Long(long value) => new(CedarValueKind.Long, value);

    public static CedarValue String(string value) => new(CedarValueKind.String, value);

    public static CedarValue Entity(EntityUid value) => new(CedarValueKind.Entity, value);

    public static CedarValue Set(IReadOnlyList<CedarValue> value) => new(CedarValueKind.Set, value);

    public static CedarValue Record(IReadOnlyDictionary<string, CedarValue> value) => new(CedarValueKind.Record, value);

    /// <summary>
    /// Cedar's <c>ip</c> extension type -- represents both a single address
    /// and a CIDR range via <see cref="IPNetwork"/> (a /32 or /128 prefix
    /// length for a bare address), whose own <see cref="IPNetwork.Contains(IPAddress)"/>
    /// is exactly Cedar's <c>isInRange</c>.
    /// </summary>
    public static CedarValue Ip(IPNetwork value) => new(CedarValueKind.Ip, value);

    public static CedarValue Decimal(decimal value) => new(CedarValueKind.Decimal, value);

    public bool AsBool() => Kind == CedarValueKind.Bool
        ? (bool)_payload!
        : throw new CedarConditionEvaluationException($"Expected a bool value but found {Kind}");

    public long AsLong() => Kind == CedarValueKind.Long
        ? (long)_payload!
        : throw new CedarConditionEvaluationException($"Expected a long value but found {Kind}");

    public string AsString() => Kind == CedarValueKind.String
        ? (string)_payload!
        : throw new CedarConditionEvaluationException($"Expected a string value but found {Kind}");

    public EntityUid AsEntity() => Kind == CedarValueKind.Entity
        ? (EntityUid)_payload!
        : throw new CedarConditionEvaluationException($"Expected an entity value but found {Kind}");

    public IReadOnlyList<CedarValue> AsSet() => Kind == CedarValueKind.Set
        ? (IReadOnlyList<CedarValue>)_payload!
        : throw new CedarConditionEvaluationException($"Expected a set value but found {Kind}");

    public IReadOnlyDictionary<string, CedarValue> AsRecord() => Kind == CedarValueKind.Record
        ? (IReadOnlyDictionary<string, CedarValue>)_payload!
        : throw new CedarConditionEvaluationException($"Expected a record value but found {Kind}");

    public IPNetwork AsIp() => Kind == CedarValueKind.Ip
        ? (IPNetwork)_payload!
        : throw new CedarConditionEvaluationException($"Expected an ip value but found {Kind}");

    public decimal AsDecimal() => Kind == CedarValueKind.Decimal
        ? (decimal)_payload!
        : throw new CedarConditionEvaluationException($"Expected a decimal value but found {Kind}");

    /// <summary>
    /// Structural equality across every <see cref="CedarValueKind"/> --
    /// Cedar's <c>==</c> is defined for any two values of the same runtime
    /// type (false, not an error, for values of different types), matching
    /// <see cref="CedarConditionEvaluator"/>'s binary-equality handling.
    /// </summary>
    public bool ValueEquals(CedarValue other)
    {
        if (Kind != other.Kind)
        {
            return false;
        }

        return Kind switch
        {
            CedarValueKind.Bool => AsBool() == other.AsBool(),
            CedarValueKind.Long => AsLong() == other.AsLong(),
            CedarValueKind.String => string.Equals(AsString(), other.AsString(), StringComparison.Ordinal),
            CedarValueKind.Entity => AsEntity() == other.AsEntity(),
            CedarValueKind.Decimal => AsDecimal() == other.AsDecimal(),
            CedarValueKind.Ip => AsIp().Equals(other.AsIp()),
            CedarValueKind.Set => SetEquals(AsSet(), other.AsSet()),
            CedarValueKind.Record => RecordEquals(AsRecord(), other.AsRecord()),
            _ => throw new CedarConditionEvaluationException($"Unhandled Cedar value kind {Kind}"),
        };
    }

    private static bool SetEquals(IReadOnlyList<CedarValue> left, IReadOnlyList<CedarValue> right)
    {
        // Cedar set equality is order-independent, duplicate-insensitive --
        // every element of one must have a match in the other.
        if (left.Count != right.Count)
        {
            return false;
        }

        var remaining = new List<CedarValue>(right);
        foreach (var item in left)
        {
            var index = remaining.FindIndex(item.ValueEquals);
            if (index < 0)
            {
                return false;
            }

            remaining.RemoveAt(index);
        }

        return remaining.Count == 0;
    }

    private static bool RecordEquals(
        IReadOnlyDictionary<string, CedarValue> left, IReadOnlyDictionary<string, CedarValue> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !value.ValueEquals(otherValue))
            {
                return false;
            }
        }

        return true;
    }
}
