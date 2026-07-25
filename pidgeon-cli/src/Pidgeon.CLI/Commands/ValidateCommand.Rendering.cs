// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Application.Services.Validation;
using Pidgeon.Core.Domain.Validation;
using System.Text.Json;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Rendering helpers for <see cref="ValidateCommand"/>: console issue/summary output,
/// the FHIR-lane profile heuristic, and the --report file writer.
/// </summary>
public partial class ValidateCommand
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private void RenderIssue(ValidationIssue issue)
    {
        var isError = issue.Severity == ValidationSeverity.Error;
        if (isError)
            Output.WriteError($"  {issue.RuleId}: {issue.Message}");
        else
            Output.WriteStatus($"  {issue.RuleId}: {issue.Message}");

        if (!string.IsNullOrEmpty(issue.Location))
        {
            // MessageIndex annotates WHICH message in a multi-message file the
            // issue belongs to; the lookup tip below keeps the raw Location.
            var location = issue.MessageIndex is int messageIndex
                ? $"{issue.Location} (message {messageIndex})"
                : issue.Location;
            Output.WriteStatus($"    Location: {location}");
        }
        if (!string.IsNullOrEmpty(issue.ExpectedValue))
            Output.WriteStatus($"    Expected: {issue.ExpectedValue}");
        if (!string.IsNullOrEmpty(issue.ActualValue))
            Output.WriteStatus($"    Actual:   {issue.ActualValue}");
        if (!string.IsNullOrEmpty(issue.Suggestion))
            Output.WriteStatus($"    Fix:      {issue.Suggestion}");
        if (!string.IsNullOrEmpty(issue.Location) && issue.Location != "Message")
            Output.WriteStatus($"    Tip:      pidgeon lookup {issue.Location}");
    }

    private void RenderSummary(ValidationStatistics stats)
    {
        if (stats.FieldsValidated <= 0) return;

        var timeMs = stats.ValidationTime.TotalMilliseconds;
        var timeSuffix = timeMs > 0 ? $" in {timeMs:F0}ms" : "";
        Output.WriteStatus($"\nSummary: {stats.FieldsValidated} fields checked, {stats.RulesPassed} of {stats.TotalRulesChecked} rules passed ({stats.ConformanceScore:F1}% conformance){timeSuffix}");
    }

    /// <summary>
    /// The FHIR-lane fallback heuristic. Only consulted for references no vendor lane resolved,
    /// so a vendor profile name can never be misrouted here on its filename suffix alone: a .json
    /// name is only treated as a FHIR profile when it is an existing file (a StructureDefinition
    /// on disk), never on the suffix by itself.
    /// </summary>
    private static bool IsFHIRProfile(string? profileName)
    {
        if (string.IsNullOrEmpty(profileName)) return false;

        // "us-core" alias
        if (string.Equals(profileName, "us-core", StringComparison.OrdinalIgnoreCase)) return true;

        // FHIR canonical URL
        if (profileName.StartsWith("http://hl7.org/fhir", StringComparison.OrdinalIgnoreCase)) return true;

        // Existing JSON file path (likely a StructureDefinition)
        if (profileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(profileName)) return true;

        // Directory path
        if (Directory.Exists(profileName)) return true;

        return false;
    }

    /// <summary>
    /// Writes the --report file: HTML with the shared attribution footer, or raw JSON
    /// results when the path ends in .json (machine output — no footer). Returns 1 when
    /// the report could not be written, 0 otherwise.
    /// </summary>
    private async Task<int> WriteValidationReportAsync(
        string reportPath,
        IReadOnlyList<ValidationReportEntry> entries,
        string[] files,
        ValidationMode mode,
        string? profileName)
    {
        try
        {
            var reportDir = Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrEmpty(reportDir)) Directory.CreateDirectory(reportDir);

            if (reportPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var payload = entries.Select(e => new { source = e.Source, result = e.Result });
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(payload, ReportJsonOptions));
            }
            else
            {
                var reproduce = BuildReproduceCommand(files, mode, profileName);
                await File.WriteAllTextAsync(reportPath, ValidationHtmlReportGenerator.GenerateReport(entries, reproduce));
            }

            Output.WriteStatus($"Validation report written to {reportPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Output.WriteError($"Validation report not written: {ex.Message}");
            return 1;
        }
    }

    private static string BuildReproduceCommand(string[] files, ValidationMode mode, string? profileName)
    {
        var quoted = files.Select(f => f.Contains(' ') ? $"\"{f}\"" : f);
        var profile = string.IsNullOrEmpty(profileName) ? "" : $" --profile {profileName}";
        return $"pidgeon validate {string.Join(' ', quoted)} --mode {mode}{profile}";
    }

    // FHIR profile diagnostics flow through RenderIssue via
    // FHIRResultTranslator, so the CLI uses one rendering path across all
    // standards.
}
