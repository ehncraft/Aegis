namespace Aegis.Policies;

public sealed class AllowRule
{
    /// <summary>Grants access if the principal holds any of these roles.</summary>
    public List<string>? Roles { get; set; }

    /// <summary>Grants access if this condition expression evaluates to true.</summary>
    public string? When { get; set; }

    /// <summary>
    /// The grammar <see cref="When"/> is written in -- <c>null</c> (default)
    /// for Aegis's own <c>${name}</c>-style expression grammar, parsed by
    /// <c>Aegis.Expressions</c>; <c>"cedar"</c> when <see cref="When"/> is
    /// rendered Cedar source text produced by lowering a <c>.cedar</c> file
    /// (see <c>Aegis.Cedar</c>'s <c>CedarPolicySetLowerer</c>), evaluated by
    /// <c>Aegis.Cedar</c>'s <c>CedarConditionEvaluator</c> instead.
    /// Deliberately a string discriminator here rather than a typed Cedar
    /// AST field, so this shared model type stays free of any dependency on
    /// the Cedar frontend -- <c>PolicyEvaluator</c> re-parses the text on
    /// first use.
    /// </summary>
    public string? Language { get; set; }
}