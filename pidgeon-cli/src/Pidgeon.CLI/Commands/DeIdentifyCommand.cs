// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Services.DeIdentification;
using Pidgeon.Core.Domain.DeIdentification;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Command for de-identifying healthcare messages to create safe test data.
/// Removes PHI while preserving referential integrity and clinical relationships.
/// </summary>
public class DeIdentifyCommand : CommandBuilderBase
{
    private readonly IDeIdentificationEngine _deIdentificationEngine;

    public DeIdentifyCommand(
        ILogger<DeIdentifyCommand> logger,
        IConsoleOutput output,
        IDeIdentificationEngine deIdentificationEngine,
        FirstTimeUserService firstTimeUserService)
        : base(logger, output, firstTimeUserService)
    {
        _deIdentificationEngine = deIdentificationEngine;
    }

    public override Command CreateCommand()
    {
        var command = new Command("deident", "De-identify real messages/resources (on-device), preserving referential integrity");

        // Positional arguments for input and output
        var inputArg = new Argument<string>("input")
        {
            Description = "File or folder of source data"
        };
        var outputArg = new Argument<string>("output")
        {
            Description = "Output file/folder (created if missing)"
        };

        // Options
        var dateShiftOption = CreateNullableOption("--date-shift", "-d", "Shift dates by +/-N days (e.g., 30d, -14d)");
        var keepIdsOption = CreateNullableOption("--keep-ids", "-k", "Comma-list of identifiers to keep unhashed (e.g., visitId)");
        var saltOption = CreateNullableOption("--salt", "-s", "Salt for deterministic hashing");
        var previewOption = CreateBooleanOption("--preview", "-p", "Show sample before/after rows without writing files");

        // Redundant options for backward compatibility
        var inputOption = CreateNullableOption("--in", "-i", "File or folder of source data (redundant - use positional args)");
        var outputOption = CreateNullableOption("--out", "-o", "Output file/folder (redundant - use positional args)");

        command.Add(inputArg);
        command.Add(outputArg);
        command.Add(dateShiftOption);
        command.Add(keepIdsOption);
        command.Add(saltOption);
        command.Add(previewOption);
        command.Add(inputOption);
        command.Add(outputOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            try
            {
                // Get input/output from positional args or fallback to options
                var inputPath = parseResult.GetValue(inputArg) ?? parseResult.GetValue(inputOption);
                var outputPath = parseResult.GetValue(outputArg) ?? parseResult.GetValue(outputOption);

                // Validate required arguments
                if (string.IsNullOrEmpty(inputPath))
                {
                    Output.WriteError("Input path is required. Usage:");
                    Output.WriteStatus("  pidgeon deident <input> <output>");
                    Output.WriteStatus("  pidgeon deident --in <input> --out <output>");
                    return 1;
                }

                if (string.IsNullOrEmpty(outputPath))
                {
                    Output.WriteError("Output path is required. Usage:");
                    Output.WriteStatus("  pidgeon deident <input> <output>");
                    Output.WriteStatus("  pidgeon deident --in <input> --out <output>");
                    return 1;
                }
                var dateShiftStr = parseResult.GetValue(dateShiftOption);
                var keepIds = parseResult.GetValue(keepIdsOption);
                var salt = parseResult.GetValue(saltOption);
                var preview = parseResult.GetValue(previewOption);

                // Parse date shift option
                TimeSpan? dateShift = null;
                if (!string.IsNullOrEmpty(dateShiftStr))
                {
                    dateShift = ParseDateShift(dateShiftStr);
                    if (!dateShift.HasValue)
                    {
                        Logger.LogError("Invalid date-shift format. Use format like '30d' or '-14d'");
                        Output.WriteError("Invalid date-shift format. Use format like '30d' or '-14d'");
                        return 1;
                    }
                }

                // Parse keep-ids option
                var idsToKeep = new HashSet<string>();
                if (!string.IsNullOrEmpty(keepIds))
                {
                    idsToKeep = new HashSet<string>(keepIds.Split(',', StringSplitOptions.RemoveEmptyEntries));
                }

                // Create de-identification options
                var options = new DeIdentificationOptions
                {
                    Salt = salt,
                    DateShift = dateShift,
                    PreserveRelationships = true,
                    GenerateReport = !preview,
                    Method = DeIdentificationMethod.SafeHarborPlus,
                    PreviewMode = preview,
                    PreserveDateTimes = true,
                    CustomFieldMappings = new Dictionary<string, IdentifierType>()
                    // TODO: Support custom field mappings from config file
                };

                // Handle preview mode
                if (preview)
                {
                    Logger.LogInformation("Generating de-identification preview for {Input}", inputPath);
                    Output.WriteStatus($"Previewing de-identification changes for: {inputPath}");

                    var previewResult = await _deIdentificationEngine.PreviewChangesAsync(inputPath!, options);
                    if (previewResult.IsFailure)
                    {
                        Logger.LogError("Preview generation failed: {Error}", previewResult.Error.Message);
                        Output.WriteError($"Preview generation failed: {previewResult.Error.Message}");
                        return 1;
                    }

                    DisplayPreview(previewResult.Value);
                    return 0;
                }

                // Determine if input is file or directory
                bool isDirectory = Directory.Exists(inputPath);
                bool isFile = File.Exists(inputPath);

                if (!isDirectory && !isFile)
                {
                    Logger.LogError("Input path does not exist: {Path}", inputPath);
                    Output.WriteError($"Input path does not exist: {inputPath}");
                    return 1;
                }

                // Process based on input type
                if (isFile)
                {
                    Logger.LogInformation("De-identifying file: {Input} -> {Output}", inputPath, outputPath);
                    Output.WriteStatus($"De-identifying file: {inputPath}");

                    var result = await _deIdentificationEngine.ProcessFileAsync(inputPath!, outputPath!, options);
                    if (result.IsFailure)
                    {
                        Logger.LogError("De-identification failed: {Error}", result.Error.Message);
                        Output.WriteError($"De-identification failed: {result.Error.Message}");
                        return 1;
                    }

                    DisplaySummary(result.Value);
                    Output.WriteSuccess($"De-identified file saved to: {outputPath}");
                }
                else // isDirectory
                {
                    Logger.LogInformation("De-identifying directory: {Input} -> {Output}", inputPath, outputPath);
                    Output.WriteStatus($"De-identifying directory: {inputPath}");

                    var result = await _deIdentificationEngine.ProcessDirectoryAsync(inputPath!, outputPath!, options);
                    if (result.IsFailure)
                    {
                        Logger.LogError("De-identification failed: {Error}", result.Error.Message);
                        Output.WriteError($"De-identification failed: {result.Error.Message}");
                        return 1;
                    }

                    DisplayBatchSummary(result.Value);
                    Output.WriteSuccess($"De-identified files saved to: {outputPath}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during de-identification");
                Output.WriteError($"Unexpected error: {ex.Message}");
                return 1;
            }
        });

        return command;
    }

    /// <summary>
    /// Parses date shift string format (e.g., "30d", "-14d").
    /// </summary>
    private static TimeSpan? ParseDateShift(string dateShiftStr)
    {
        if (string.IsNullOrWhiteSpace(dateShiftStr))
            return null;

        dateShiftStr = dateShiftStr.Trim();

        // Check for 'd' suffix for days
        if (dateShiftStr.EndsWith("d", StringComparison.OrdinalIgnoreCase))
        {
            var daysStr = dateShiftStr[..^1];
            if (int.TryParse(daysStr, out var days))
            {
                return TimeSpan.FromDays(days);
            }
        }

        // Try parsing as plain number of days
        if (int.TryParse(dateShiftStr, out var plainDays))
        {
            return TimeSpan.FromDays(plainDays);
        }

        return null;
    }

    /// <summary>
    /// Displays a preview of de-identification changes.
    /// </summary>
    private void DisplayPreview(DeIdentificationPreview preview)
    {
        if (Output.OutputFormat == "json") { Output.WriteData(preview); return; }
        Output.WriteStatus("\nDe-identification Preview:");
        Output.WriteStatus($"   Files to process: {preview.FilesToProcess.Count}");

        if (preview.EstimatedStatistics != null)
        {
            Output.WriteStatus($"   Estimated fields to modify: {preview.EstimatedStatistics.FieldsModified}");
            Output.WriteStatus($"   Estimated dates to shift: {preview.EstimatedStatistics.DatesShifted}");
        }

        if (preview.SampleChanges.Any())
        {
            Output.WriteStatus("\n   Sample changes:");
            foreach (var change in preview.SampleChanges.Take(5))
            {
                Output.WriteData($"     {change.Location}: \"{change.OriginalValue}\" -> \"{change.ReplacementValue}\"");
            }
        }

        if (preview.ComplianceAssessment != null)
        {
            var compliant = preview.ComplianceAssessment.MeetsSafeHarbor ? "Yes" : "No";
            Output.WriteStatus($"\n   HIPAA Safe Harbor compliance: {compliant}");
        }

        Output.WriteStatus($"\n   Estimated processing time: {preview.ResourceEstimate.EstimatedTime.TotalSeconds:F1}s");
    }

    /// <summary>
    /// Displays a summary of the de-identification results.
    /// </summary>
    private void DisplaySummary(DeIdentificationResult result)
    {
        if (Output.OutputFormat == "json") { Output.WriteData(result); return; }
        Output.WriteStatus("\nDe-identification Summary:");
        Output.WriteStatus($"   Messages processed: {result.Statistics.TotalMessages}");
        Output.WriteStatus($"   Fields modified: {result.Statistics.FieldsModified}");
        Output.WriteStatus($"   Dates shifted: {result.Statistics.DatesShifted}");
        Output.WriteStatus($"   Processing time: {result.Statistics.TotalProcessingTime.TotalMilliseconds:F0}ms");

        if (result.Compliance != null)
        {
            Output.WriteStatus($"   HIPAA Safe Harbor: {result.Compliance.Status}");
            if (!string.IsNullOrWhiteSpace(result.Compliance.Notes))
                Output.WriteStatus($"   {result.Compliance.Notes}");
        }
    }

    /// <summary>
    /// Displays a summary of batch de-identification results.
    /// </summary>
    private void DisplayBatchSummary(BatchDeIdentificationResult result)
    {
        if (Output.OutputFormat == "json") { Output.WriteData(result); return; }
        Output.WriteStatus("\nBatch De-identification Summary:");
        Output.WriteStatus($"   Total files: {result.Metadata.TotalFiles}");
        Output.WriteStatus($"   Successful: {result.Metadata.SuccessfulFiles}");
        Output.WriteStatus($"   Failed: {result.Metadata.FailedFiles}");

        if (result.CombinedStatistics != null)
        {
            Output.WriteStatus($"   Total messages: {result.CombinedStatistics.TotalMessages}");
            Output.WriteStatus($"   Fields modified: {result.CombinedStatistics.FieldsModified}");
            Output.WriteStatus($"   Dates shifted: {result.CombinedStatistics.DatesShifted}");
            Output.WriteStatus($"   Unique subjects: {result.CombinedStatistics.UniqueSubjects}");
        }

        Output.WriteStatus($"   Total processing time: {result.Metadata.TotalProcessingTime.TotalSeconds:F1}s");

        if (result.BatchCompliance != null)
        {
            Output.WriteStatus($"   HIPAA Safe Harbor: {result.BatchCompliance.Status}");
            if (!string.IsNullOrWhiteSpace(result.BatchCompliance.Notes))
                Output.WriteStatus($"   {result.BatchCompliance.Notes}");
        }

        if (result.FileResults.Any(r => !r.Success))
        {
            Output.WriteWarning("\n   Failed files:");
            foreach (var failure in result.FileResults.Where(r => !r.Success).Take(5))
            {
                Output.WriteError($"     - {failure.InputPath}: {failure.ErrorMessage}");
            }
        }
    }
}
