// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Paths.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon path list [message-type]</c>.
/// Lists semantic paths for a message type, or a universal set when no
/// message type is supplied. Delegates category grouping, descriptions,
/// and example values to <see cref="PathMetadataProvider"/>.
/// </summary>
public sealed class ListPathOrchestrator
{
    private readonly ILogger<ListPathOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly IFieldPathResolver _fieldPathResolver;
    private readonly IConfigurationService _configurationService;

    public ListPathOrchestrator(
        ILogger<ListPathOrchestrator> logger,
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
        string? messageType,
        string? standard,
        string? category,
        string format,
        bool descriptions,
        bool examples,
        string? output,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(messageType))
            {
                return ShowUniversalPaths(category, descriptions);
            }

            return await ShowMessageTypePathsAsync(messageType, standard, category, descriptions, examples, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing paths");
            _output.WriteError($"Error: {ex.Message}");
            return 1;
        }
    }

    private int ShowUniversalPaths(string? category, bool descriptions)
    {
        _output.WriteStatus("Universal Semantic Paths (All Standards)");
        _output.WriteStatus("");
        _output.WriteStatus("Common semantic paths that work across HL7, FHIR, and NCPDP:");
        _output.WriteStatus("");

        // TODO: Implement universal path listing
        // For now, show a sample of common paths
        var universalPaths = new[]
        {
            ("patient.mrn", "Medical Record Number"),
            ("patient.lastName", "Patient Last Name"),
            ("patient.firstName", "Patient First Name"),
            ("patient.dateOfBirth", "Date of Birth"),
            ("patient.sex", "Administrative Sex"),
            ("patient.phoneNumber", "Primary Phone Number"),
            ("message.sendingApplication", "Sending Application"),
            ("message.sendingFacility", "Sending Facility"),
            ("message.timestamp", "Message Date/Time")
        };

        _output.WriteStatus("PATIENT DEMOGRAPHICS");
        foreach (var (path, desc) in universalPaths.Where(p => p.Item1.StartsWith("patient.")))
        {
            if (descriptions)
                _output.WriteStatus($"  {path,-25} {desc}");
            else
                _output.WriteStatus($"  {path}");
        }

        _output.WriteStatus("");
        _output.WriteStatus("MESSAGE CONTROL");
        foreach (var (path, desc) in universalPaths.Where(p => p.Item1.StartsWith("message.")))
        {
            if (descriptions)
                _output.WriteStatus($"  {path,-25} {desc}");
            else
                _output.WriteStatus($"  {path}");
        }

        _output.WriteStatus("");
        _output.WriteStatus("Use 'pidgeon path list <message-type>' to see message-specific paths");
        _output.WriteStatus("Use 'pidgeon path resolve <path> <message-type>' to see standard mappings");

        return 0;
    }

    private async Task<int> ShowMessageTypePathsAsync(
        string messageType,
        string? standard,
        string? category,
        bool descriptions,
        bool examples,
        CancellationToken cancellationToken)
    {
        var result = await _fieldPathResolver.GetAvailablePathsAsync(messageType, standard);
        if (result.IsFailure)
        {
            _output.WriteError($"Failed to get paths for {messageType}: {result.Error.Message}");
            return 1;
        }

        var paths = result.Value;
        if (!paths.Any())
        {
            _output.WriteStatus($"No semantic paths found for message type: {messageType}");
            _output.WriteStatus("");
            _output.WriteStatus("Try:");
            _output.WriteStatus("   pidgeon path list                    # Show universal paths");
            _output.WriteStatus("   pidgeon path search <keyword>        # Search for specific paths");
            return 0;
        }

        var effectiveStandard = standard;
        if (string.IsNullOrWhiteSpace(effectiveStandard))
        {
            var standardResult = await _configurationService.GetEffectiveStandardAsync(messageType, standard);
            effectiveStandard = standardResult.IsSuccess ? standardResult.Value : "Unknown";
        }

        _output.WriteStatus($"Available Semantic Paths: {messageType} ({effectiveStandard})");
        _output.WriteStatus("");

        var groupedPaths = PathMetadataProvider.GroupPathsByCategory(paths, category);

        foreach (var group in groupedPaths.OrderBy(g => g.Key))
        {
            _output.WriteStatus(group.Key.ToUpperInvariant());
            foreach (var (path, description) in group.Value.OrderBy(p => p.Key))
            {
                if (descriptions)
                {
                    _output.WriteStatus($"  {path,-25} {description}");
                    if (examples)
                    {
                        _output.WriteStatus($"  {"",-25} Example: {PathMetadataProvider.GetExampleValue(path)}");
                    }
                }
                else
                {
                    _output.WriteStatus($"  {path}");
                }
            }
            _output.WriteStatus("");
        }

        _output.WriteStatus("Use 'pidgeon path resolve <path> <message-type>' to see standard-specific mappings");
        _output.WriteStatus("Use 'pidgeon path search <keyword>' to find paths by description");

        return 0;
    }
}
