namespace Aegis;

/// <summary>The object being acted upon, e.g. an invoice, document, or repository.</summary>
public sealed class AegisResource
{
    /// <summary>Matches the <c>resource</c> key of a policy, e.g. "invoices".</summary>
    public required string Kind { get; init; }

    public string? Id { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; init; } =
        new Dictionary<string, object?>();

    public static AegisResource Create(string kind, string? id = null,
        IReadOnlyDictionary<string, object?>? attributes = null) => new()
        {
            Kind = kind,
            Id = id,
            Attributes = attributes ?? new Dictionary<string, object?>(),
        };
}