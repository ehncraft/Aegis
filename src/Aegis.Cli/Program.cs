using System.CommandLine;

using Aegis.Cli;

var policiesDirectoryArgument = new Argument<string>("policies-directory")
{
    Description = "Directory containing *.yaml policy files.",
};

var validateCommand = new Command("validate", "Load and validate every policy in a directory.")
{
    policiesDirectoryArgument,
};
validateCommand.SetAction((parseResult, _) => Task.FromResult(
    ValidateCommandHandler.Execute(parseResult.GetValue(policiesDirectoryArgument)!, Console.Out)));

var principalIdOption = new Option<string>("--principal-id") { Required = true, Description = "Principal id." };
var roleOption = new Option<string[]>("--role")
{
    Description = "Principal role (repeatable).",
    AllowMultipleArgumentsPerToken = true,
    DefaultValueFactory = _ => [],
};
var principalAttrOption = new Option<string[]>("--principal-attr")
{
    Description = "Principal attribute as key=value (repeatable).",
    AllowMultipleArgumentsPerToken = true,
    DefaultValueFactory = _ => [],
};
var resourceKindOption = new Option<string>("--resource-kind")
{
    Required = true,
    Description = "Resource kind, matching a policy's 'resource' key.",
};
var resourceIdOption = new Option<string?>("--resource-id") { Description = "Resource id." };
var resourceAttrOption = new Option<string[]>("--resource-attr")
{
    Description = "Resource attribute as key=value (repeatable).",
    AllowMultipleArgumentsPerToken = true,
    DefaultValueFactory = _ => [],
};
var actionOption = new Option<string>("--action") { Required = true, Description = "Action being authorized." };

var authorizeCommand = new Command("authorize", "Evaluate an authorization decision and print the full explanation.")
{
    policiesDirectoryArgument,
    principalIdOption,
    roleOption,
    principalAttrOption,
    resourceKindOption,
    resourceIdOption,
    resourceAttrOption,
    actionOption,
};
authorizeCommand.SetAction(async (parseResult, cancellationToken) =>
{
    try
    {
        var principalAttributes = AttributeParsing.Parse(parseResult.GetValue(principalAttrOption) ?? []);
        var resourceAttributes = AttributeParsing.Parse(parseResult.GetValue(resourceAttrOption) ?? []);

        return await AuthorizeCommandHandler.ExecuteAsync(
            parseResult.GetValue(policiesDirectoryArgument)!,
            parseResult.GetValue(principalIdOption)!,
            parseResult.GetValue(roleOption) ?? [],
            principalAttributes,
            parseResult.GetValue(resourceKindOption)!,
            parseResult.GetValue(resourceIdOption),
            resourceAttributes,
            parseResult.GetValue(actionOption)!,
            Console.Out,
            cancellationToken);
    }
    catch (ArgumentException ex)
    {
        await Console.Error.WriteLineAsync($"error: {ex.Message}");
        return 2;
    }
});

var rootCommand = new RootCommand("Aegis policy CLI -- validate policies and evaluate authorization decisions.")
{
    validateCommand,
    authorizeCommand,
};

return await rootCommand.Parse(args).InvokeAsync();