// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using System.CommandLine;
using System.CommandLine.Completions;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Command for generating shell completion setup scripts.
/// Users run this to manually configure tab completion in their shell environment.
/// </summary>
public class CompletionsCommand : CommandBuilderBase
{
    public CompletionsCommand(ILogger<CompletionsCommand> logger, IConsoleOutput output)
        : base(logger, output)
    {
    }

    public override Command CreateCommand()
    {
        var command = new Command("completions", "Generate shell completion setup scripts");

        // Shell argument - which shell to generate completion for
        var shellArgument = new Argument<string>("shell")
        {
            Description = "Shell to generate completion for: bash|zsh|powershell|fish"
        };

        // Add completions for the shell argument itself
        shellArgument.CompletionSources.Add("bash", "zsh", "powershell", "fish");

        command.Add(shellArgument);

        SetInfoCommandAction(command, async (parseResult, cancellationToken) =>
        {
            var shell = parseResult.GetValue(shellArgument);

            if (string.IsNullOrEmpty(shell))
            {
                ShowCompletionHelp();
                return 1;
            }

            // Get the root command to generate completion for
            var rootCommand = GetRootCommand(parseResult);
            if (rootCommand == null)
            {
                Logger.LogError("Unable to access root command for completion generation");
                return 1;
            }

            return await GenerateCompletionAsync(rootCommand, shell, cancellationToken);
        });

        return command;
    }

    private async Task<int> GenerateCompletionAsync(RootCommand rootCommand, string shell, CancellationToken cancellationToken)
    {
        await Task.Yield(); // Ensure async behavior

        try
        {
            // TODO: GetCompletionScript method not available in our System.CommandLine version
            // For now, provide instructions for manual setup using dotnet-suggest
            Output.WriteData($"# Completion setup for {shell}");
            Output.WriteData("# Install dotnet-suggest tool first:");
            Output.WriteData("# dotnet tool install --global dotnet-suggest");
            Output.WriteData("");

            switch (shell.ToLowerInvariant())
            {
                case "bash":
                    Output.WriteData("# Add to ~/.bashrc:");
                    Output.WriteData("# Register your app: dotnet-suggest register --command-path $(which pidgeon)");
                    Output.WriteData("# Add completion: eval \"$(dotnet-suggest script bash)\"");
                    break;
                case "zsh":
                    Output.WriteData("# Add to ~/.zshrc:");
                    Output.WriteData("# Register your app: dotnet-suggest register --command-path $(which pidgeon)");
                    Output.WriteData("# Add completion: eval \"$(dotnet-suggest script zsh)\"");
                    break;
                case "powershell":
                case "pwsh":
                    Output.WriteData("# Add to PowerShell profile:");
                    Output.WriteData("# Register your app: dotnet-suggest register --command-path (Get-Command pidgeon).Source");
                    Output.WriteData("# Add completion: Invoke-Expression (dotnet-suggest script pwsh | Out-String)");
                    break;
                case "fish":
                    Output.WriteData("# Add to ~/.config/fish/config.fish:");
                    Output.WriteData("# Register your app: dotnet-suggest register --command-path (which pidgeon)");
                    Output.WriteData("# Add completion: dotnet-suggest script fish | source");
                    break;
                default:
                    Output.WriteError($"Unsupported shell '{shell}'");
                    Output.WriteStatus("Supported shells: bash, zsh, powershell, fish");
                    return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to generate completion instructions for {Shell}", shell);
            return 1;
        }
    }

    private static RootCommand? GetRootCommand(ParseResult parseResult)
    {
        // Try to get root command directly from ParseResult
        return parseResult.RootCommandResult?.Command as RootCommand;
    }

    private void ShowCompletionHelp()
    {
        Output.WriteStatus("Pidgeon CLI Shell Completion Setup");
        Output.WriteStatus("===================================================");
        Output.WriteStatus("");
        Output.WriteStatus("USAGE");
        Output.WriteStatus("  pidgeon completions <shell>");
        Output.WriteStatus("");
        Output.WriteStatus("SUPPORTED SHELLS");
        Output.WriteStatus("  bash       Generate bash completion script");
        Output.WriteStatus("  zsh        Generate zsh completion script");
        Output.WriteStatus("  powershell Generate PowerShell completion script");
        Output.WriteStatus("  fish       Generate fish completion script");
        Output.WriteStatus("");
        Output.WriteStatus("QUICK SETUP");
        Output.WriteStatus("");
        Output.WriteStatus("Bash:");
        Output.WriteStatus("  echo 'source <(pidgeon completions bash)' >> ~/.bashrc");
        Output.WriteStatus("  source ~/.bashrc");
        Output.WriteStatus("");
        Output.WriteStatus("Zsh:");
        Output.WriteStatus("  pidgeon completions zsh > ~/.zsh/completions/_pidgeon");
        Output.WriteStatus("");
        Output.WriteStatus("PowerShell:");
        Output.WriteStatus("  Add-Content $PROFILE \"`npidgeon completions powershell | Out-String | Invoke-Expression\"");
        Output.WriteStatus("");
        Output.WriteStatus("Fish:");
        Output.WriteStatus("  pidgeon completions fish > ~/.config/fish/completions/pidgeon.fish");
    }
}