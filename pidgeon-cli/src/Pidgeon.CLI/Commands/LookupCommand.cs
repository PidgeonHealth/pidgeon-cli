// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.Core.Application.Interfaces.Reference;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// CLI command for healthcare standards reference lookup functionality.
/// Provides smart inference, search, and browsing capabilities across HL7, FHIR, and NCPDP standards.
/// </summary>
public partial class LookupCommand : CommandBuilderBase
{
    private readonly IStandardReferenceService _referenceService;

    public LookupCommand(
        ILogger<LookupCommand> logger,
        IConsoleOutput output,
        IStandardReferenceService referenceService)
        : base(logger, output)
    {
        _referenceService = referenceService;
    }

    public override Command CreateCommand()
    {
        var command = new Command("lookup", "Look up healthcare standards definitions with smart inference");

        // Positional argument for element path
        var pathArg = new Argument<string?>("path")
        {
            Description = "Element path (e.g., PID.3.5, Patient.identifier, NewRx.Patient) - leave empty to browse",
            Arity = ArgumentArity.ZeroOrOne
        };

        // Options for different lookup modes
        var searchOption = CreateNullableOption("--search", "-s", "Search across all standards for elements matching query");
        var standardOption = CreateNullableOption("--standard", "-t", "Explicit standard (hl7v23, fhir-r4, ncpdp)");
        var vendorOption = CreateNullableOption("--vendor", "-v", "Show vendor-specific variations (epic, cerner, allscripts)");
        var formatOption = CreateOptionalOption("--format", "-f", "Output format: text|json|table", "text");
        var segmentsOption = CreateBooleanOption("--segments", "List all segments/resources in specified standard");
        var childrenOption = CreateBooleanOption("--children", "List child elements of specified path");
        var examplesOption = CreateBooleanOption("--examples", "Show detailed examples and usage patterns");
        var crossRefOption = CreateBooleanOption("--cross-ref", "Show cross-references to other standards");

        // Beta features
        var interactiveOption = CreateBooleanOption("--inter", "-i", "Beta: Interactive TUI browsing mode (in development)");

        command.Add(pathArg);
        command.Add(searchOption);
        command.Add(standardOption);
        command.Add(vendorOption);
        command.Add(formatOption);
        command.Add(segmentsOption);
        command.Add(childrenOption);
        command.Add(examplesOption);
        command.Add(crossRefOption);
        command.Add(interactiveOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            try
            {
                var path = parseResult.GetValue(pathArg);
                var search = parseResult.GetValue(searchOption);
                var standard = parseResult.GetValue(standardOption);
                var vendor = parseResult.GetValue(vendorOption);
                var format = parseResult.GetValue(formatOption);
                var showSegments = parseResult.GetValue(segmentsOption);
                var showChildren = parseResult.GetValue(childrenOption);
                var showExamples = parseResult.GetValue(examplesOption);
                var showCrossRef = parseResult.GetValue(crossRefOption);
                var interactive = parseResult.GetValue(interactiveOption);

                // Check for interactive TUI mode availability
                if (interactive)
                {
                    Output.WriteWarning("Interactive TUI browsing mode is currently in development.");
                    Output.WriteStatus("This feature will provide a terminal-based interface for exploring healthcare standards.");
                    Output.WriteStatus("    For now, use standard lookup commands. Check release notes for updates.");
                    Output.WriteStatus("");
                }

                // Handle different operation modes
                if (!string.IsNullOrEmpty(search))
                {
                    return await HandleSearchAsync(search, standard, format, cancellationToken);
                }

                if (showSegments)
                {
                    if (string.IsNullOrEmpty(standard))
                    {
                        Output.WriteError("--segments requires --standard option");
                        Output.WriteStatus("Usage: pidgeon lookup --segments --standard hl7v23");
                        return 1;
                    }
                    return await HandleListSegmentsAsync(standard, format, cancellationToken);
                }

                if (!string.IsNullOrEmpty(path))
                {
                    if (showChildren)
                    {
                        return await HandleListChildrenAsync(path, format, cancellationToken);
                    }
                    else
                    {
                        return await HandleElementLookupAsync(path, standard, vendor, format, showExamples, showCrossRef, cancellationToken);
                    }
                }

                // No specific operation - show help and suggestions
                return await HandleBrowseModeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during lookup");
                Output.WriteError($"Error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    private async Task<int> HandleElementLookupAsync(
        string path,
        string? explicitStandard,
        string? vendor,
        string? format,
        bool showExamples,
        bool showCrossRef,
        CancellationToken cancellationToken)
    {
        var result = !string.IsNullOrEmpty(explicitStandard)
            ? await _referenceService.LookupAsync(path, explicitStandard, cancellationToken)
            : await _referenceService.LookupAsync(path, cancellationToken);

        if (result.IsFailure)
        {
            Output.WriteError(result.Error.Message);
            Output.WriteStatus("");

            // Try to provide helpful suggestions
            var inferResult = _referenceService.InferStandard(path);
            if (inferResult.IsSuccess)
            {
                Output.WriteStatus($"Suggestions for {inferResult.Value}:");
                // TODO: Get suggestions from the service
            }

            return 1;
        }

        var element = result.Value;

        // Handle vendor-specific lookup
        if (!string.IsNullOrEmpty(vendor))
        {
            // TODO: Implement vendor variation lookup
            Output.WriteStatus($"Vendor-specific information for {vendor} not yet implemented");
            Output.WriteStatus("");
        }

        // Display the element information
        DisplayElement(element, format, showExamples, showCrossRef);

        return 0;
    }

    private async Task<int> HandleSearchAsync(string query, string? standard, string? format, CancellationToken cancellationToken)
    {
        Output.WriteStatus($"Searching for '{query}'");
        if (!string.IsNullOrEmpty(standard))
        {
            Output.WriteStatus($"   Standard: {standard}");
        }
        Output.WriteStatus("");

        var result = !string.IsNullOrEmpty(standard)
            ? await _referenceService.SearchAsync(query, standard, cancellationToken)
            : await _referenceService.SearchAsync(query, cancellationToken);

        if (result.IsFailure)
        {
            Output.WriteError(result.Error.Message);
            return 1;
        }

        var elements = result.Value;
        if (!elements.Any())
        {
            Output.WriteStatus($"No results found for '{query}'");
            Output.WriteStatus("");
            Output.WriteStatus("Try:");
            Output.WriteStatus("  - Broader search terms (e.g., 'patient' instead of 'patient name')");
            Output.WriteStatus("  - Different spelling or abbreviations");
            Output.WriteStatus("  - Browse available elements: pidgeon lookup --segments --standard hl7v23");
            return 0;
        }

        DisplaySearchResults(elements, format);
        return 0;
    }

    private async Task<int> HandleListSegmentsAsync(string standard, string? format, CancellationToken cancellationToken)
    {
        Output.WriteStatus($"Segments/Resources in {standard.ToUpperInvariant()}");
        Output.WriteStatus("");

        var result = await _referenceService.ListTopLevelElementsAsync(standard, cancellationToken);

        if (result.IsFailure)
        {
            Output.WriteError(result.Error.Message);
            return 1;
        }

        var elements = result.Value;
        if (!elements.Any())
        {
            Output.WriteStatus($"No segments found for standard '{standard}'");
            return 0;
        }

        DisplayElementList(elements, format);
        return 0;
    }

    private async Task<int> HandleListChildrenAsync(string parentPath, string? format, CancellationToken cancellationToken)
    {
        Output.WriteStatus($"Child elements of {parentPath}");
        Output.WriteStatus("");

        var result = await _referenceService.ListChildrenAsync(parentPath, cancellationToken);

        if (result.IsFailure)
        {
            Output.WriteError(result.Error.Message);
            return 1;
        }

        var children = result.Value;
        if (!children.Any())
        {
            Output.WriteStatus($"No child elements found for '{parentPath}'");
            return 0;
        }

        DisplayElementList(children, format);
        return 0;
    }

    private async Task<int> HandleBrowseModeAsync(CancellationToken cancellationToken)
    {
        Output.WriteStatus("Pidgeon Standards Reference");
        Output.WriteStatus("");

        // Show available standards
        var standardsResult = await _referenceService.GetSupportedStandardsAsync(cancellationToken);
        if (standardsResult.IsSuccess)
        {
            Output.WriteStatus("Supported Standards:");
            foreach (var kvp in standardsResult.Value)
            {
                Output.WriteStatus($"  {kvp.Key.PadRight(10)} - {kvp.Value}");
            }
            Output.WriteStatus("");
        }

        Output.WriteStatus("Usage Examples:");
        Output.WriteStatus("  pidgeon lookup PID.3.5                    # Look up specific field");
        Output.WriteStatus("  pidgeon lookup Patient.identifier          # FHIR resource element");
        Output.WriteStatus("  pidgeon lookup --search \"medical record\"   # Search across all standards");
        Output.WriteStatus("  pidgeon lookup PID --children              # List all PID fields");
        Output.WriteStatus("  pidgeon lookup --segments --standard hl7v23 # Browse HL7 segments");
        Output.WriteStatus("");

        Output.WriteStatus("Vendor Intelligence:");
        Output.WriteStatus("  pidgeon lookup PID.3.5 --vendor epic      # Epic-specific patterns");
        Output.WriteStatus("  pidgeon lookup MSH.3 --vendor cerner      # Cerner implementation notes");
        Output.WriteStatus("");

        Output.WriteStatus("Beta Features:");
        Output.WriteStatus("  pidgeon lookup --inter                     # Visual terminal browser (in development)");
        Output.WriteStatus("  pidgeon lookup PID.3 --cross-ref          # Cross-standard mappings");

        return 0;
    }
}
