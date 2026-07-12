namespace Aegis;

/// <summary>
/// Every problem <see cref="PolicyValidator"/> found, reported together --
/// so a policy set with three typos surfaces all three in one pass instead
/// of one failed request at a time.
/// </summary>
public sealed class PolicyValidationException : Exception
{
    public PolicyValidationException(IReadOnlyList<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IReadOnlyList<string> errors) =>
        $"Policy validation failed with {errors.Count} error(s):{Environment.NewLine}" +
        string.Join(Environment.NewLine, errors.Select(error => $"  - {error}"));
}