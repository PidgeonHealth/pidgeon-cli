// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.Core.Application.Interfaces.Capability;
using Pidgeon.Core.Domain.Capability;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Reports the honest per-(type, version) capability matrix: for each generatable message type
/// and version, the level we can actually claim — derived from a live generate→validate→oracle
/// sweep, not a hand-maintained claims table. Lets a user see exactly which cells are
/// independently validated, spec-validated, merely generated, or unsupported.
/// </summary>
public class CapabilitiesCommand : CommandBuilderBase
{
    private readonly ICapabilitySignalingService _capabilities;

    public CapabilitiesCommand(
        ILogger<CapabilitiesCommand> logger,
        IConsoleOutput output,
        ICapabilitySignalingService capabilities)
        : base(logger, output)
    {
        _capabilities = capabilities;
    }

    public override Command CreateCommand()
    {
        var command = new Command(
            "capabilities",
            "Show the per-(type, version) capability matrix, derived from real validation");

        var standardOption = CreateNullableOption("--standard", "-s", "Limit to one standard (e.g. hl7, fhir, ncpdp)");
        // Not "--version": that collides with the global version flag and prints the CLI version.
        var versionOption = CreateNullableOption("--std-version", "Limit to one standard version (e.g. 2.5.1)");

        command.Add(standardOption);
        command.Add(versionOption);

        SetInfoCommandAction(command, async (parseResult, cancellationToken) =>
        {
            var standard = parseResult.GetValue(standardOption);
            var version = parseResult.GetValue(versionOption);
            var query = new CapabilityQuery(standard, version);

            Output.WriteStatus("Probing capabilities (generating and validating a sample per cell)…");
            var report = await _capabilities.DescribeCapabilitiesAsync(query, cancellationToken);

            if (report.Cells.Count == 0 && report.ValidateCells.Count == 0)
            {
                Output.WriteStatus("No matching capability cells.");
                return 0;
            }

            if (Output.OutputFormat == "json")
            {
                Output.WriteData(new
                {
                    generatedAt = report.GeneratedAt,
                    cells = report.Cells.Select(c => new
                    {
                        standard = c.Standard,
                        messageType = c.MessageType,
                        version = c.Version,
                        level = c.Level.ToString(),
                        generationTier = c.GenerationTier,
                        basis = c.Basis
                    }).ToList(),
                    validateCells = report.ValidateCells.Select(c => new
                    {
                        standard = c.Standard,
                        artifactType = c.ArtifactType,
                        level = c.Level.ToString(),
                        basis = c.Basis
                    }).ToList()
                });
                return 0;
            }

            RenderText(report);
            return 0;
        });

        return command;
    }

    private void RenderText(CapabilityReport report)
    {
        if (report.Cells.Count > 0)
        {
            Output.WriteStatus("Generate capability (derived from live generate→validate→oracle sweep)");
            Output.WriteStatus("Levels: IndependentlyValidated > SpecValidated > Generated > Unsupported");
            Output.WriteStatus("");

            foreach (var standard in report.Standards)
            {
                var cells = report.ForStandard(standard).ToList();
                Output.WriteStatus($"{standard.ToUpperInvariant()}  ({cells.Count} cell(s))");

                foreach (var levelGroup in cells
                    .GroupBy(c => c.Level)
                    .OrderByDescending(g => g.Key))
                {
                    Output.WriteStatus($"  {levelGroup.Key} — {levelGroup.Count()}");
                }

                // The tier column renders only for standards that carry a tier axis
                // (FHIR: clinical = curated builder, structural = schema-derived
                // minimal instance) so other standards' tables are unchanged.
                var hasTiers = cells.Any(c => c.GenerationTier is not null);
                if (hasTiers)
                {
                    Output.WriteStatus("  Tiers: clinical = curated scenario-coherent builder; structural = schema-derived minimal valid instance");
                    Output.WriteStatus($"  {"Type",-14} {"Version",-10} {"Level",-24} {"Tier",-11} Basis");
                    Output.WriteStatus($"  {new string('-', 14)} {new string('-', 10)} {new string('-', 24)} {new string('-', 11)} {new string('-', 30)}");
                }
                else
                {
                    Output.WriteStatus($"  {"Type",-14} {"Version",-10} {"Level",-24} Basis");
                    Output.WriteStatus($"  {new string('-', 14)} {new string('-', 10)} {new string('-', 24)} {new string('-', 30)}");
                }

                foreach (var c in cells)
                {
                    Output.WriteStatus(hasTiers
                        ? $"  {c.MessageType,-14} {c.Version ?? "-",-10} {c.Level,-24} {c.GenerationTier ?? "-",-11} {Truncate(c.Basis, 70)}"
                        : $"  {c.MessageType,-14} {c.Version ?? "-",-10} {c.Level,-24} {Truncate(c.Basis, 70)}");
                }

                Output.WriteStatus("");
            }

            Output.WriteStatus($"{report.Cells.Count} generate cell(s) across {report.Standards.Count} standard(s).");
        }

        RenderValidate(report);
    }

    /// <summary>
    /// Renders the separate validate-capability axis: standards we validate but do not generate
    /// (today, C-CDA). Kept visually distinct from the generate matrix so a reader never conflates
    /// "we can validate this artifact" with "we can generate it conformantly".
    /// </summary>
    private void RenderValidate(CapabilityReport report)
    {
        if (report.ValidateCells.Count == 0)
            return;

        Output.WriteStatus("");
        Output.WriteStatus("Validate capability (derived from which validation oracles are loaded)");
        Output.WriteStatus("Levels: Conformance > Structural > Unrecognized");
        Output.WriteStatus("");

        foreach (var standard in report.ValidateStandards)
        {
            var cells = report.ForValidateStandard(standard).ToList();
            Output.WriteStatus($"{standard.ToUpperInvariant()}  ({cells.Count} artifact(s))");

            foreach (var levelGroup in cells
                .GroupBy(c => c.Level)
                .OrderByDescending(g => g.Key))
            {
                Output.WriteStatus($"  {levelGroup.Key} — {levelGroup.Count()}");
            }

            Output.WriteStatus($"  {"Artifact",-40} {"Level",-14} Basis");
            Output.WriteStatus($"  {new string('-', 40)} {new string('-', 14)} {new string('-', 30)}");
            foreach (var c in cells)
            {
                Output.WriteStatus(
                    $"  {Truncate(c.ArtifactType, 40),-40} {c.Level,-14} {Truncate(c.Basis, 60)}");
            }

            Output.WriteStatus("");
        }

        Output.WriteStatus($"{report.ValidateCells.Count} validate artifact(s) across {report.ValidateStandards.Count} standard(s).");
    }

    private static string Truncate(string value, int max)
        => string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..(max - 1)] + "…";
}
