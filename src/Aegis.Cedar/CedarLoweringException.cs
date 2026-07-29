namespace Aegis.Cedar;

/// <summary>
/// A parsed <c>.cedar</c> policy was structurally impossible to lower onto
/// Aegis's <c>ResourcePolicy</c>/<c>ActionRule</c> model -- distinct from
/// <see cref="CedarSyntaxException"/> (a parse failure) and from
/// <c>Aegis.Policies.PolicyLoadException</c> (which <c>CedarPolicyProvider</c>
/// wraps this into, matching every other <c>IPolicyProvider</c>'s
/// error-reporting convention). Raised for: an unconstrained resource scope
/// with no <see cref="CedarLoadOptions.DefaultResourceKind"/> configured, an
/// unconstrained action scope with no sibling policy establishing a concrete
/// action vocabulary for that resource, <c>principal is</c> a type other
/// than <see cref="CedarLoadOptions.PrincipalEntityType"/>, and any
/// extension function/method call outside the fixed <c>ip</c>/<c>decimal</c>
/// allow-list.
/// </summary>
public sealed class CedarLoweringException : Exception
{
    public CedarLoweringException(string message)
        : base(message)
    {
    }
}