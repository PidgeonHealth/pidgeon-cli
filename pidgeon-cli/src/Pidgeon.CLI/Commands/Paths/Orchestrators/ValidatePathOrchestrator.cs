// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Paths.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon path validate &lt;semantic-path&gt; &lt;message-type&gt;</c>.
/// Checks if a semantic path is valid for the given message
/// type and optionally suggests related paths when invalid.
/// </summary>
public sealed class ValidatePathOrchestrator
{
    private readonly ILogger<ValidatePathOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly IFieldPathResolver _fieldPathResolver;

    public ValidatePathOrchestrator(
        ILogger<ValidatePathOrchestrator> logger,
        IConsoleOutput output,
        IFieldPathResolver fieldPathResolver)
    {
        _logger = logger;
        _output = output;
        _fieldPathResolver = fieldPathResolver;
    }

    public async Task<int> ExecuteAsync(
        string semanticPath,
        string messageType,
        string? standard,
        bool suggestions,
        string format,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _fieldPathResolver.ValidatePathAsync(semanticPath, messageType, standard);
            if (result.IsFailure)
            {
                _output.WriteError($"Validation failed: {result.Error.Message}");
                return 1;
            }

            if (result.Value)
            {
                _output.WriteSuccess($"Valid Path: {semanticPath} for {messageType}");
                _output.WriteStatus("");
                _output.WriteStatus($"Use 'pidgeon path resolve {semanticPath} \"{messageType}\"' to see standard mappings");
                _output.WriteStatus($"Use 'pidgeon set my-session {semanticPath} \"<value>\"' to lock this field");
                return 0;
            }

            _output.WriteError($"Invalid Path: {semanticPath} for {messageType}");
            _output.WriteStatus("");

            if (suggestions)
            {
                _output.WriteStatus("REASON: Path is not applicable to this message type");
                _output.WriteStatus("");
                _output.WriteStatus("SUGGESTIONS:");

                // TODO: Implement intelligent suggestions based on path analysis
                var availableResult = await _fieldPathResolver.GetAvailablePathsAsync(messageType, standard);
                if (availableResult.IsSuccess && availableResult.Value.Any())
                {
                    _output.WriteStatus($"  For {messageType} messages, consider:");
                    var suggestionsList = availableResult.Value.Take(5);
                    foreach (var suggestion in suggestionsList)
                    {
                        _output.WriteStatus($"  - {suggestion.Key,-25} ({suggestion.Value})");
                    }
                }
                _output.WriteStatus("");
                _output.WriteStatus($"Use 'pidgeon path list \"{messageType}\"' to see all available paths");
            }

            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating path");
            _output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }
}
