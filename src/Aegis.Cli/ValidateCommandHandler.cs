using Aegis.Policies;

namespace Aegis.Cli;

internal static class ValidateCommandHandler
{
    public static int Execute(string policiesDirectory, TextWriter output)
    {
        try
        {
            var policies = YamlPolicyLoader.LoadDirectory(policiesDirectory);
            PolicyValidator.Validate(policies);
            output.WriteLine($"{policies.Count} polic{(policies.Count == 1 ? "y" : "ies")} valid.");
            return 0;
        }
        catch (PolicyValidationException ex)
        {
            foreach (var error in ex.Errors)
            {
                output.WriteLine($"error: {error}");
            }

            return 1;
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or PolicyLoadException)
        {
            output.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}