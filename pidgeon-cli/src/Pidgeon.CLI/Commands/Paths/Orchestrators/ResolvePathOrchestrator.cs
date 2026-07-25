// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Paths.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon path resolve &lt;semantic-path&gt; &lt;message-type&gt;</c>.
/// Shows how a semantic path maps to standard-specific field
/// locations, optionally in detailed or script-friendly (path-only) form.
/// </summary>
public sealed class ResolvePathOrchestrator
{
    private readonly ILogger<ResolvePathOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly IFieldPathResolver _fieldPathResolver;
    private readonly IConfigurationService _configurationService;

    public ResolvePathOrchestrator(
        ILogger<ResolvePathOrchestrator> logger,
        IConsoleOutput output,
        IFieldPathResolver fieldPathResolver,
        IConfigurationService configurationService)
    {
        _logger = logger;
        _output = output;
        _fieldPathResolver = fieldPathResolver;
        _configurationService = configurationService;
    }

    public async Task<int> ExecuteAsync(
        string semanticPath,
        string messageType,
        string? standard,
        bool allStandards,
        string format,
        bool detailed,
        bool pathOnly,
        CancellationToken cancellationToken)
    {
        try
        {
            if (pathOnly)
            {
                var result = await _fieldPathResolver.ResolvePathAsync(semanticPath, messageType, standard);
                if (result.IsFailure)
                {
                    _output.WriteError($"Error: {result.Error.Message}");
                    return 1;
                }
                _output.WriteData(result.Value);
                return 0;
            }

            _output.WriteStatus($"Path Resolution: {semanticPath} -> {PathMetadataProvider.GetPathDescription(semanticPath)}");
            _output.WriteStatus("");
            _output.WriteStatus($"MESSAGE TYPE: {messageType}");
            _output.WriteStatus("");

            if (allStandards)
            {
                // TODO: Implement cross-standard resolution
                _output.WriteError("Cross-standard resolution not yet implemented");
                return 1;
            }

            var resolveResult = await _fieldPathResolver.ResolvePathAsync(semanticPath, messageType, standard);
            if (resolveResult.IsFailure)
            {
                _output.WriteError($"Resolution failed: {resolveResult.Error.Message}");
                return 1;
            }

            var effectiveStandard = standard;
            if (string.IsNullOrWhiteSpace(effectiveStandard))
            {
                var standardResult = await _configurationService.GetEffectiveStandardAsync(messageType, standard);
                effectiveStandard = standardResult.IsSuccess ? standardResult.Value : "Unknown";
            }

            _output.WriteStatus($"{effectiveStandard}:    {resolveResult.Value}");
            if (detailed)
            {
                _output.WriteStatus($"           | Field Type: {PathMetadataProvider.GetFieldType(semanticPath)}");
                _output.WriteStatus($"           | Description: {PathMetadataProvider.GetPathDescription(semanticPath)}");
                _output.WriteStatus($"           | Example: \"{PathMetadataProvider.GetExampleValue(semanticPath)}\"");
            }
            _output.WriteStatus("");

            _output.WriteStatus("Validation: Path is valid for this message type");
            _output.WriteStatus($"Use 'pidgeon set my-session {semanticPath} \"{PathMetadataProvider.GetExampleValue(semanticPath)}\"' to lock this value");

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving path");
            _output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }
}
