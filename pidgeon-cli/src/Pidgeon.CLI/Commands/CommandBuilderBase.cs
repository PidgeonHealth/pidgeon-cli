// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Base class for CLI command builders providing common option creation and command execution patterns.
/// </summary>
public abstract class CommandBuilderBase
{
    protected readonly ILogger Logger;
    protected readonly FirstTimeUserService? FirstTimeUserService;
    protected IConsoleOutput Output { get; }

    protected CommandBuilderBase(ILogger logger, IConsoleOutput output)
    {
        Logger = logger;
        Output = output;
    }

    protected CommandBuilderBase(ILogger logger, IConsoleOutput output, FirstTimeUserService firstTimeUserService)
    {
        Logger = logger;
        Output = output;
        FirstTimeUserService = firstTimeUserService;
    }

    // Backward-compatible constructors for commands that don't inject IConsoleOutput yet
    protected CommandBuilderBase(ILogger logger)
    {
        Logger = logger;
        Output = ConsoleOutput.Create();
    }

    protected CommandBuilderBase(ILogger logger, FirstTimeUserService firstTimeUserService)
    {
        Logger = logger;
        Output = ConsoleOutput.Create();
        FirstTimeUserService = firstTimeUserService;
    }

    /// <summary>
    /// Creates the command with all options and actions configured.
    /// Must be implemented by derived classes.
    /// </summary>
    public abstract Command CreateCommand();

    /// <summary>
    /// Creates a required string option with standard validation.
    /// </summary>
    protected static Option<string> CreateRequiredOption(string name, string description)
    {
        return new Option<string>(name)
        {
            Description = description,
            Required = true
        };
    }

    /// <summary>
    /// Creates an optional string option with default value.
    /// </summary>
    protected static Option<string> CreateOptionalOption(string name, string description, string defaultValue)
    {
        return new Option<string>(name)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// Creates an optional string option with short alias and default value.
    /// </summary>
    protected static Option<string> CreateOptionalOption(string name, string shortAlias, string description, string defaultValue)
    {
        return new Option<string>(name, shortAlias)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// Creates an optional nullable string option.
    /// </summary>
    protected static Option<string?> CreateNullableOption(string name, string description)
    {
        return new Option<string?>(name)
        {
            Description = description
        };
    }

    /// <summary>
    /// Creates an optional nullable string option with short alias.
    /// </summary>
    protected static Option<string?> CreateNullableOption(string name, string shortAlias, string description)
    {
        return new Option<string?>(name, shortAlias)
        {
            Description = description
        };
    }

    /// <summary>
    /// Creates a boolean option with default value.
    /// </summary>
    protected static Option<bool> CreateBooleanOption(string name, string description, bool defaultValue = false)
    {
        return new Option<bool>(name)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// Creates a boolean option with short alias.
    /// </summary>
    protected static Option<bool> CreateBooleanOption(string name, string shortAlias, string description, bool defaultValue = false)
    {
        return new Option<bool>(name, shortAlias)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// Creates an integer option with default value.
    /// </summary>
    protected static Option<int> CreateIntegerOption(string name, string description, int defaultValue)
    {
        return new Option<int>(name)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// Creates an integer option with short alias.
    /// </summary>
    protected static Option<int> CreateIntegerOption(string name, string shortAlias, string description, int defaultValue)
    {
        return new Option<int>(name, shortAlias)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue
        };
    }

    /// <summary>
    /// Validates that a file exists and returns appropriate error code.
    /// </summary>
    protected int ValidateFileExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Output.WriteError($"File not found: {filePath}");
            Output.WriteStatus("  Check the path and try again. Use an absolute path if needed.");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Validates that a directory exists and returns appropriate error code.
    /// </summary>
    protected int ValidateDirectoryExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Output.WriteError($"Directory not found: {directoryPath}");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Reads all files with specified extension from directory.
    /// </summary>
    protected static async Task<Result<List<string>>> ReadMessagesFromDirectoryAsync(
        string directory,
        string extension,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var files = Directory.GetFiles(directory, $"*.{extension}");
            if (files.Length == 0)
            {
                return Result<List<string>>.Failure($"No .{extension} files found in: {directory}");
            }

            var messages = new List<string>();
            foreach (var file in files)
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                messages.Add(content);
            }

            return Result<List<string>>.Success(messages);
        }
        catch (Exception ex)
        {
            return Result<List<string>>.Failure($"Error reading files: {ex.Message}");
        }
    }

    /// <summary>
    /// Standard error handling pattern for command execution.
    /// </summary>
    protected int HandleResult<T>(Result<T> result, string? successMessage = null)
    {
        if (result.IsSuccess)
        {
            if (!string.IsNullOrEmpty(successMessage))
                Output.WriteStatus(successMessage);
            return 0;
        }

        Output.WriteError($"{result.Error.Code}: {result.Error.Message}");
        if (!string.IsNullOrEmpty(result.Error.Context))
            Output.WriteStatus($"Context: {result.Error.Context}");

        Logger.LogError("Command failed: {ErrorCode} - {ErrorMessage}",
            result.Error.Code, result.Error.Message);

        return 1;
    }

    /// <summary>
    /// Standard exception handling pattern for command execution.
    /// </summary>
    protected int HandleException(Exception ex, string context = "")
    {
        Output.WriteError($"An error occurred{(string.IsNullOrEmpty(context) ? "" : $" during {context}")}: {ex.Message}");
        Logger.LogError(ex, "Command execution failed: {Context}", context);
        return 1;
    }

    /// <summary>
    /// Auto-graduates first-time users by creating minimal config on productive commands.
    /// </summary>
    protected async Task<bool> TryAutoGraduateUserAsync()
    {
        if (FirstTimeUserService == null) return true;

        try
        {
            if (await FirstTimeUserService.IsFirstTimeUserAsync())
            {
                var result = await FirstTimeUserService.CreateMinimalConfigAsync();
                if (result.IsFailure)
                {
                    Logger.LogWarning("Failed to create minimal config for auto-graduation: {Error}", result.Error);
                    return false;
                }

                Logger.LogDebug("Auto-graduated first-time user to experienced status");
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error during auto-graduation check");
            return true;
        }
    }

    /// <summary>
    /// Creates a command with auto-graduation and standard exception handling.
    /// </summary>
    protected void SetCommandAction(Command command, Func<ParseResult, CancellationToken, Task<int>> action)
    {
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                await TryAutoGraduateUserAsync();
                return await action(parseResult, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Output.WriteError("Operation was cancelled.");
                return 1;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unhandled exception in command {Command}", command.Name);
                Output.WriteError($"Command '{command.Name}' failed: {ex.Message}");
                Output.WriteStatus("  Run with --verbose for more details.");
                return 1;
            }
        });
    }

    /// <summary>
    /// Creates a command without auto-graduation for info/help commands.
    /// </summary>
    protected void SetInfoCommandAction(Command command, Func<ParseResult, CancellationToken, Task<int>> action)
    {
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            try
            {
                return await action(parseResult, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Output.WriteError("Operation was cancelled.");
                return 1;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unhandled exception in command {Command}", command.Name);
                Output.WriteError($"Command '{command.Name}' failed: {ex.Message}");
                Output.WriteStatus("  Run with --verbose for more details.");
                return 1;
            }
        });
    }
}
