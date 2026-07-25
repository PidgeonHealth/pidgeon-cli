// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Commands.Paths.Orchestrators;
using Pidgeon.CLI.Output;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// CLI command for semantic path discovery and exploration.
/// Enables users to discover, understand, and validate cross-standard field paths.
///
/// Thin parsing + routing shell over one orchestrator per subcommand.
/// Each path verb (list/resolve/validate/search) takes distinct arguments
/// and owns a unique output shape, so consolidating would only couple
/// unrelated logic. A pure <see cref="Paths.PathMetadataProvider"/> helper
/// is extracted because category grouping, descriptions, field types, and
/// example values are shared across the list/resolve/validate orchestrators;
/// extracting them keeps the three orchestrators from duplicating four
/// switch-expression tables. No renderer is extracted because per-subcommand
/// output is distinct prose, not a shared table.
/// </summary>
public class PathCommand : CommandBuilderBase
{
    private readonly ListPathOrchestrator _listOrchestrator;
    private readonly ResolvePathOrchestrator _resolveOrchestrator;
    private readonly ValidatePathOrchestrator _validateOrchestrator;
    private readonly SearchPathOrchestrator _searchOrchestrator;

    public PathCommand(
        ILogger<PathCommand> logger,
        IConsoleOutput output,
        ListPathOrchestrator listOrchestrator,
        ResolvePathOrchestrator resolveOrchestrator,
        ValidatePathOrchestrator validateOrchestrator,
        SearchPathOrchestrator searchOrchestrator)
        : base(logger, output)
    {
        _listOrchestrator = listOrchestrator;
        _resolveOrchestrator = resolveOrchestrator;
        _validateOrchestrator = validateOrchestrator;
        _searchOrchestrator = searchOrchestrator;
    }

    public override Command CreateCommand()
    {
        var command = new Command("path", "Discover and explore semantic field paths across healthcare standards");

        command.Add(BuildListCommand());
        command.Add(BuildResolveCommand());
        command.Add(BuildValidateCommand());
        command.Add(BuildSearchCommand());

        return command;
    }

    private Command BuildListCommand()
    {
        var command = new Command("list", "Discover available semantic paths for a message type");

        var messageTypeArg = new Argument<string?>("message-type")
        {
            Description = "HL7 message (ADT^A01), FHIR resource (Patient), or NCPDP type (NewRx). If omitted, shows universal paths.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var standardOption = CreateNullableOption("--standard", "Show paths for specific standard (hl7v23|fhirv4|ncpdp)");
        var categoryOption = CreateNullableOption("--category", "Filter by category: patient|encounter|provider|medication|all");
        var formatOption = CreateOptionalOption("--format", "Output format: table|json|csv", "table");
        var descriptionsOption = CreateBooleanOption("--descriptions", "Include field descriptions (verbose)");
        var examplesOption = CreateBooleanOption("--examples", "Show example values for each path");
        var outputOption = CreateNullableOption("--output", "Write results to file (default: stdout)");

        command.Add(messageTypeArg);
        command.Add(standardOption);
        command.Add(categoryOption);
        command.Add(formatOption);
        command.Add(descriptionsOption);
        command.Add(examplesOption);
        command.Add(outputOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _listOrchestrator.ExecuteAsync(
                parseResult.GetValue(messageTypeArg),
                parseResult.GetValue(standardOption),
                parseResult.GetValue(categoryOption),
                parseResult.GetValue(formatOption)!,
                parseResult.GetValue(descriptionsOption),
                parseResult.GetValue(examplesOption),
                parseResult.GetValue(outputOption),
                cancellationToken));

        return command;
    }

    private Command BuildResolveCommand()
    {
        var command = new Command("resolve", "Show how a semantic path maps to standard-specific field locations");

        var semanticPathArg = new Argument<string>("semantic-path")
        {
            Description = "Semantic path to resolve (e.g., patient.mrn, encounter.location)"
        };

        var messageTypeArg = new Argument<string>("message-type")
        {
            Description = "Target message type (ADT^A01, Patient, NewRx)"
        };

        var standardOption = CreateNullableOption("--standard", "Show mapping for specific standard only");
        var allStandardsOption = CreateBooleanOption("--all-standards", "Show mappings across all supported standards");
        var formatOption = CreateOptionalOption("--format", "Output format: table|json", "table");
        var detailedOption = CreateBooleanOption("--detailed", "Include field type, validation rules, and examples");
        var pathOnlyOption = CreateBooleanOption("--path-only", "Output only the resolved path (useful for scripting)");

        command.Add(semanticPathArg);
        command.Add(messageTypeArg);
        command.Add(standardOption);
        command.Add(allStandardsOption);
        command.Add(formatOption);
        command.Add(detailedOption);
        command.Add(pathOnlyOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _resolveOrchestrator.ExecuteAsync(
                parseResult.GetValue(semanticPathArg)!,
                parseResult.GetValue(messageTypeArg)!,
                parseResult.GetValue(standardOption),
                parseResult.GetValue(allStandardsOption),
                parseResult.GetValue(formatOption)!,
                parseResult.GetValue(detailedOption),
                parseResult.GetValue(pathOnlyOption),
                cancellationToken));

        return command;
    }

    private Command BuildValidateCommand()
    {
        var command = new Command("validate", "Check if a semantic path is valid for a given message type");

        var semanticPathArg = new Argument<string>("semantic-path")
        {
            Description = "Path to validate (e.g., medication.dosage)"
        };

        var messageTypeArg = new Argument<string>("message-type")
        {
            Description = "Target message type (ADT^A01, Patient, etc.)"
        };

        var standardOption = CreateNullableOption("--standard", "Validate against specific standard");
        var suggestionsOption = CreateBooleanOption("--suggestions", "Show related/alternative paths when invalid", true);
        var formatOption = CreateOptionalOption("--format", "Output format: text|json", "text");

        command.Add(semanticPathArg);
        command.Add(messageTypeArg);
        command.Add(standardOption);
        command.Add(suggestionsOption);
        command.Add(formatOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _validateOrchestrator.ExecuteAsync(
                parseResult.GetValue(semanticPathArg)!,
                parseResult.GetValue(messageTypeArg)!,
                parseResult.GetValue(standardOption),
                parseResult.GetValue(suggestionsOption),
                parseResult.GetValue(formatOption)!,
                cancellationToken));

        return command;
    }

    private Command BuildSearchCommand()
    {
        var command = new Command("search", "Find semantic paths by keyword or description");

        var queryArg = new Argument<string>("query")
        {
            Description = "Search term (e.g., \"phone\", \"date\", \"medical record\")"
        };

        var messageTypeOption = CreateNullableOption("--message-type", "Limit search to specific message type");
        var standardOption = CreateNullableOption("--standard", "Limit search to specific standard");
        var categoryOption = CreateNullableOption("--category", "Limit search to category (patient|encounter|provider|medication)");
        var exactOption = CreateBooleanOption("--exact", "Exact match only (no fuzzy search)");
        var formatOption = CreateOptionalOption("--format", "Output format: table|json", "table");

        command.Add(queryArg);
        command.Add(messageTypeOption);
        command.Add(standardOption);
        command.Add(categoryOption);
        command.Add(exactOption);
        command.Add(formatOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _searchOrchestrator.ExecuteAsync(
                parseResult.GetValue(queryArg)!,
                parseResult.GetValue(messageTypeOption),
                parseResult.GetValue(standardOption),
                parseResult.GetValue(categoryOption),
                parseResult.GetValue(exactOption),
                parseResult.GetValue(formatOption)!,
                cancellationToken));

        return command;
    }
}
