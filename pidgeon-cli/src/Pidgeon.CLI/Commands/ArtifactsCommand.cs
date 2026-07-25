// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces.Artifacts;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// <c>pidgeon artifacts</c> — local artifact discovery (ARTIFACT_ECOSYSTEM_PROGRAM.md §5.3): search
/// and list installable artifacts — starter recipes, installed recipes, and data packages — via the
/// one Core <see cref="IArtifactCatalogService"/> the Bridge and MCP projections also call. Distinct
/// from <c>lookup</c>/<c>find</c> (standards reference data), <c>catalog</c> (clinical scenarios),
/// and <c>path search</c> (field paths): this command answers "what can I install here".
/// </summary>
public class ArtifactsCommand : CommandBuilderBase
{
    private readonly IArtifactCatalogService _artifactCatalog;

    public ArtifactsCommand(
        ILogger<ArtifactsCommand> logger,
        IConsoleOutput output,
        IArtifactCatalogService artifactCatalog,
        FirstTimeUserService firstTimeUserService)
        : base(logger, output, firstTimeUserService)
    {
        _artifactCatalog = artifactCatalog;
    }

    public override Command CreateCommand()
    {
        var command = new Command(
            "artifacts",
            "Search installable artifacts — recipes and data packages; for standards reference data use lookup/find; for clinical scenarios use catalog");
        command.Add(CreateSearchCommand());
        command.Add(CreateListCommand());
        return command;
    }

    private Command CreateSearchCommand()
    {
        var queryArg = new Argument<string[]>("query")
        {
            Description = "Search terms matched against artifact titles, descriptions, names, vendors, and message types",
            Arity = ArgumentArity.OneOrMore
        };

        var (kindOption, vendorOption, standardOption, messageTypeOption, installedOption, limitOption) = CreateFilterOptions();

        var searchCommand = new Command("search", "Search local artifacts by term")
        {
            queryArg,
            kindOption,
            vendorOption,
            standardOption,
            messageTypeOption,
            installedOption,
            limitOption
        };

        SetInfoCommandAction(searchCommand, async (parseResult, cancellationToken) =>
        {
            var terms = parseResult.GetValue(queryArg) ?? Array.Empty<string>();
            var joined = string.Join(" ", terms);

            if (string.IsNullOrWhiteSpace(joined))
            {
                Output.WriteError("Search terms are required. Use 'pidgeon artifacts list' to list everything.");
                return 1;
            }

            return await ExecuteSearchAsync(new ArtifactSearchQuery
            {
                Query = joined,
                Kind = parseResult.GetValue(kindOption),
                Vendor = parseResult.GetValue(vendorOption),
                Standard = parseResult.GetValue(standardOption),
                MessageType = parseResult.GetValue(messageTypeOption),
                InstalledOnly = parseResult.GetValue(installedOption),
                Limit = parseResult.GetValue(limitOption)
            }, cancellationToken);
        });

        return searchCommand;
    }

    private Command CreateListCommand()
    {
        var (kindOption, vendorOption, standardOption, messageTypeOption, installedOption, limitOption) = CreateFilterOptions();

        var listCommand = new Command("list", "List local artifacts with optional filters")
        {
            kindOption,
            vendorOption,
            standardOption,
            messageTypeOption,
            installedOption,
            limitOption
        };

        SetInfoCommandAction(listCommand, async (parseResult, cancellationToken) =>
            await ExecuteSearchAsync(new ArtifactSearchQuery
            {
                Kind = parseResult.GetValue(kindOption),
                Vendor = parseResult.GetValue(vendorOption),
                Standard = parseResult.GetValue(standardOption),
                MessageType = parseResult.GetValue(messageTypeOption),
                InstalledOnly = parseResult.GetValue(installedOption),
                Limit = parseResult.GetValue(limitOption)
            }, cancellationToken));

        return listCommand;
    }

    private (Option<string?> Kind, Option<string?> Vendor, Option<string?> Standard, Option<string?> MessageType, Option<bool> Installed, Option<int> Limit) CreateFilterOptions()
    {
        var kindOption = CreateNullableOption(
            "--kind",
            "Filter by artifact kind: vendor-profile | generation-config | workflow | conform-endpoint | flock-population-config | engagement-pack | data-package");

        var vendorOption = CreateNullableOption(
            "--vendor",
            "Filter by vendor (e.g., epic, cerner)");

        var standardOption = CreateNullableOption(
            "--standard",
            "Filter by standard (e.g., hl7, fhir)");

        var messageTypeOption = new Option<string?>("--message-type", "--type")
        {
            Description = "Filter by message type (e.g., ADT^A01)"
        };

        var installedOption = CreateBooleanOption(
            "--installed",
            "Only artifacts already installed on this machine");

        var limitOption = CreateIntegerOption(
            "--limit",
            "Maximum results to return (default 20, max 100)",
            20);

        return (kindOption, vendorOption, standardOption, messageTypeOption, installedOption, limitOption);
    }

    private async Task<int> ExecuteSearchAsync(ArtifactSearchQuery query, CancellationToken cancellationToken)
    {
        var result = await _artifactCatalog.SearchArtifactsAsync(query, cancellationToken);
        if (result.IsFailure)
        {
            Output.WriteError(result.Error.Message);
            return 1;
        }

        var search = result.Value;

        if (Output.OutputFormat == "json")
        {
            Output.WriteData(new
            {
                query = query.Query,
                total = search.Total,
                truncated = search.Truncated,
                items = search.Items.Select(item => new
                {
                    id = item.Id,
                    name = item.Name,
                    kind = item.Kind,
                    title = item.Title,
                    description = item.Description,
                    vendor = item.Vendor,
                    standard = item.Standard,
                    messageType = item.MessageType,
                    source = item.Source,
                    installed = item.Installed,
                    installCommand = item.InstallCommand,
                    forkedFrom = item.ForkedFrom
                }).ToList()
            });
            return 0;
        }

        if (search.Items.Count == 0)
        {
            Output.WriteStatus("No artifacts found.");
            return 0;
        }

        Output.WriteStatus($"  {"Name",-28} {"Kind",-18} {"Vendor",-12} {"Type",-10} {"Installed",-10} Id");
        Output.WriteStatus($"  {new string('-', 28)} {new string('-', 18)} {new string('-', 12)} {new string('-', 10)} {new string('-', 10)} {new string('-', 16)}");
        foreach (var item in search.Items)
        {
            Output.WriteStatus(
                $"  {Truncate(item.Name, 26),-28} {item.Kind,-18} {Truncate(item.Vendor ?? "—", 10),-12} {Truncate(item.MessageType ?? "—", 8),-10} {(item.Installed ? "yes" : "no"),-10} {Truncate(item.Id, 20)}");
        }

        Output.WriteStatus("");
        Output.WriteStatus(search.Truncated
            ? $"Showing {search.Items.Count} of {search.Total} artifact(s) — refine the search or raise --limit."
            : $"{search.Total} artifact(s) found.");
        Output.WriteStatus("Install: run the artifact's install command (see --output-format json, field installCommand).");

        return 0;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..(maxLength - 2)] + "..";
    }
}
