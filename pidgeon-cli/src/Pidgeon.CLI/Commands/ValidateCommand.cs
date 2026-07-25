// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Application.Interfaces.Standards;
using Pidgeon.Core.Application.Services.Validation;
using Pidgeon.Core.Domain.Configuration.Entities;
using Pidgeon.Core.Domain.Validation;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Command for validating healthcare messages.
/// </summary>
public partial class ValidateCommand : CommandBuilderBase
{
    private readonly IMessageValidationService _validationService;
    private readonly IProfileValidationService _profileValidationService;
    private readonly IValidationProfileResolver _profileResolver;
    private readonly ISemanticAdvisoryService? _advisory;
    private readonly ConfigurationStorage? _configStorage;

    public ValidateCommand(
        ILogger<ValidateCommand> logger,
        IConsoleOutput output,
        IMessageValidationService validationService,
        IProfileValidationService profileValidationService,
        IValidationProfileResolver profileResolver,
        FirstTimeUserService firstTimeUserService,
        ISemanticAdvisoryService? advisory = null,
        ConfigurationStorage? configStorage = null)
        : base(logger, output, firstTimeUserService)
    {
        _validationService = validationService;
        _profileValidationService = profileValidationService;
        _profileResolver = profileResolver;
        _advisory = advisory;
        _configStorage = configStorage;
    }

    public override Command CreateCommand()
    {
        var command = new Command("validate", "Validate healthcare messages against standards");

        // Positional arguments for files (supports multiple files)
        var filesArg = new Argument<string[]>("files")
        {
            Description = "Path(s) to message file(s) to validate (supports wildcards)",
            Arity = ArgumentArity.ZeroOrMore
        };

        // Options
        var modeOption = new Option<ValidationMode>("--mode", "-m")
        {
            Description = "Validation mode (Strict, Compatibility, Lenient)",
            DefaultValueFactory = _ => ValidationMode.Strict
        };
        var standardOption = CreateNullableOption("--standard", "-s", "Specific standard to validate against (auto-detect if not specified)");
        var profileOption = CreateNullableOption("--profile", "-p", "Vendor profile name to validate against");

        // Redundant option for backward compatibility and script usage
        var fileOption = CreateNullableOption("--file", "-f", "Path to file (redundant - use positional args instead)");

        var reportOption = CreateNullableOption("--report", "Write a validation report to this path (HTML with failures and guidance, or raw JSON when the path ends in .json)");

        // Advisory clinical-sense tiers beside spec conformance (Semantic Validation L5).
        // Both are additive and never change validity or the exit code.
        var clinicalOption = new Option<bool>("--clinical")
        {
            Description = "Run deterministic clinical checks (advisory, free, offline) beside spec validation"
        };
        var semanticOption = new Option<bool>("--semantic")
        {
            Description = "Run the advisory on-device semantic review (clinical-sense LLM judge) beside spec validation"
        };

        command.Add(filesArg);
        command.Add(modeOption);
        command.Add(standardOption);
        command.Add(profileOption);
        command.Add(fileOption);
        command.Add(reportOption);
        command.Add(clinicalOption);
        command.Add(semanticOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            // Get files from positional args or fallback to --file option
            var files = parseResult.GetValue(filesArg);
            var fallbackFile = parseResult.GetValue(fileOption);

            // Support both patterns: positional args OR --file option
            if (files == null || files.Length == 0)
            {
                if (!string.IsNullOrEmpty(fallbackFile))
                {
                    files = new[] { fallbackFile };
                }
                else
                {
                    Output.WriteError("No files specified. Usage:");
                    Output.WriteStatus("  pidgeon validate <file1> [file2] [...]");
                    Output.WriteStatus("  pidgeon validate --file <file>");
                    return 1;
                }
            }

            var mode = parseResult.GetValue(modeOption);
            var standard = parseResult.GetValue(standardOption);
            var profileName = parseResult.GetValue(profileOption);
            var reportPath = parseResult.GetValue(reportOption);
            var runClinical = parseResult.GetValue(clinicalOption);
            var runSemantic = parseResult.GetValue(semanticOption);

            // Fall back to the active vendor profile set via `pidgeon config use`
            // when --profile is not supplied. Explicit --profile always wins.
            bool activeProfileUsed = false;
            if (string.IsNullOrEmpty(profileName) && _configStorage is not null)
            {
                var active = _configStorage.TryGetActiveProfile();
                if (!string.IsNullOrEmpty(active))
                {
                    profileName = active;
                    activeProfileUsed = true;
                }
            }

            // Vendor-first resolution order: if the name resolves through any vendor lane
            // (built-in defaults, the spec repository, the installed shared store, the local config
            // store, a saved address) it is a vendor profile — the FHIR lane is the fallback for
            // references that resolve nowhere and look like a FHIR profile (path/URL/alias). A
            // .json-suffixed name resolves identically to its bare stem, so the filename form a
            // recipe install prints cannot misroute to the FHIR lane.
            ValidationProfileResolution? resolution = null;
            if (!string.IsNullOrEmpty(profileName))
                resolution = await _profileResolver.ResolveAsync(profileName);

            bool isFhirProfile = resolution is null && IsFHIRProfile(profileName);

            VendorSpecification? profile = null;
            if (!string.IsNullOrEmpty(profileName) && !isFhirProfile)
            {
                if (resolution is null)
                {
                    Output.WriteError($"Vendor profile not found: {profileName}");
                    Output.WriteStatus("List available profiles: pidgeon config profile list");
                    Output.WriteStatus("List installed shared profiles: pidgeon recipe list");
                    return 1;
                }

                profileName = resolution.CanonicalName;
                profile = resolution.Specification;

                if (resolution.Source == ValidationProfileSource.SharedStore)
                    Output.WriteStatus($"Using installed shared profile '{profileName}' (from the recipe store; rules derived from its field patterns)");
                else if (resolution.Source == ValidationProfileSource.BuiltInDefaults)
                    Output.WriteStatus($"Using built-in vendor defaults '{profileName}' (generation-oriented; no spec-level profile rules — standard validation still applies)");
                else if (activeProfileUsed)
                    Output.WriteStatus($"Using active vendor profile '{profileName}' (set via pidgeon config use; override with --profile)");
                else
                    Output.WriteStatus($"Using vendor profile: {profileName}");
            }
            else if (isFhirProfile)
            {
                Output.WriteStatus($"Using FHIR profile: {profileName}");
            }

            // Validate all files
            int overallResult = 0;
            var jsonResults = new List<ValidationResult>();
            var reportEntries = new List<ValidationReportEntry>();

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];

                if (file != "-")
                {
                    var fileCheck = ValidateFileExists(file);
                    if (fileCheck != 0)
                    {
                        overallResult = fileCheck;
                        continue;
                    }
                }

                if (files.Length > 1)
                {
                    Output.WriteStatus($"\n[{i + 1}/{files.Length}] Validating: {file}");
                    Output.WriteStatus(new string('-', 50));
                }

                Output.WriteStatus($"Validating {file} with {mode} mode...");

                string content;
                if (file == "-")
                {
                    content = await Console.In.ReadToEndAsync(cancellationToken);
                }
                else
                {
                    content = await File.ReadAllTextAsync(file, cancellationToken);
                }
                var result = await _validationService.ValidateAsync(content, standard, profile: profileName, mode: mode);

                if (result.IsSuccess)
                {
                    var validation = result.Value;
                    reportEntries.Add(new ValidationReportEntry(file, validation));

                    // JSON output mode: collect results for later serialization
                    if (Output.OutputFormat == "json")
                    {
                        jsonResults.Add(validation);
                        if (!validation.IsValid) overallResult = 1;
                    }
                    else
                    {
                        // Rich text output
                        if (validation.IsValid)
                        {
                            Output.WriteSuccess("Validation passed!");
                            var warnings = validation.Issues.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
                            if (warnings.Any())
                            {
                                Output.WriteWarning($"Warnings: {warnings.Count}");
                                foreach (var warning in warnings)
                                    RenderIssue(warning);
                            }
                        }
                        else
                        {
                            var errors = validation.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
                            Output.WriteError($"Validation failed with {errors.Count} error(s):");
                            foreach (var error in errors)
                                RenderIssue(error);

                            var warnings = validation.Issues.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
                            if (warnings.Any())
                            {
                                Output.WriteWarning($"Warnings: {warnings.Count}");
                                foreach (var warning in warnings)
                                    RenderIssue(warning);
                            }

                            overallResult = 1;
                        }

                        // Summary statistics
                        RenderSummary(validation.Statistics);
                    }
                }
                else
                {
                    Output.WriteError($"Error validating {file}: {result.Error.Message}");
                    overallResult = 1;
                }

                // HL7 vendor profile validation (runs in addition to standard validation)
                if (profile != null)
                {
                    var profileResult = await _profileValidationService.ValidateAsync(content, profile);
                    if (profileResult.IsSuccess)
                    {
                        var pv = profileResult.Value;
                        if (pv.IsValid)
                        {
                            Output.WriteSuccess($"Profile validation passed ({pv.RulesChecked} rules checked)");
                        }
                        else
                        {
                            var profileErrors = pv.Issues.Where(i => i.Severity == ValidationSeverity.Error).ToList();
                            Output.WriteError($"Profile validation failed with {profileErrors.Count} error(s):");
                            foreach (var error in profileErrors)
                                RenderIssue(error);
                            overallResult = 1;
                        }

                        var profileWarnings = pv.Issues.Where(i => i.Severity == ValidationSeverity.Warning).ToList();
                        if (profileWarnings.Any())
                        {
                            Output.WriteWarning($"Profile warnings: {profileWarnings.Count}");
                            foreach (var warning in profileWarnings)
                                RenderIssue(warning);
                        }
                    }
                    else
                    {
                        Output.WriteError($"Profile validation error: {profileResult.Error.Message}");
                        overallResult = 1;
                    }
                }

                // FHIR profile validation flows through the standard
                // IStandardValidationPlugin dispatch above (profileName is passed
                // as `profile` to _validationService.ValidateAsync).

                // Advisory clinical-sense tiers. Additive beside
                // spec conformance: findings ride a structurally-valid result and can never
                // change validity or the exit code. JSON mode attaches them to the result's
                // SemanticFindings channel; text mode renders them as muted advisory rows.
                if ((runClinical || runSemantic) && result.IsSuccess)
                {
                    if (_advisory is null)
                    {
                        Output.WriteStatus("Clinical/semantic advisory is not available in this build.");
                    }
                    else
                    {
                        var isJson = Output.OutputFormat == "json";

                        if (runClinical)
                        {
                            var clinicalOutcome = _advisory.RunClinical(content);
                            if (isJson) result.Value.SemanticFindings.AddRange(clinicalOutcome.Findings);
                            else _advisory.Render(clinicalOutcome, "Clinical checks");
                        }

                        if (runSemantic)
                        {
                            var semanticOutcome = await _advisory.RunSemanticAsync(content, cancellationToken);
                            if (isJson) result.Value.SemanticFindings.AddRange(semanticOutcome.Findings);
                            else _advisory.Render(semanticOutcome, "Semantic review");
                        }
                    }
                }
            }

            // JSON output: emit collected results
            if (Output.OutputFormat == "json" && jsonResults.Count > 0)
            {
                if (jsonResults.Count == 1)
                    Output.WriteData(jsonResults[0]);
                else
                    Output.WriteData(jsonResults);
            }

            // Shareable report file (HTML with the attribution footer, or raw JSON)
            if (!string.IsNullOrEmpty(reportPath) && reportEntries.Count > 0)
            {
                var reportResult = await WriteValidationReportAsync(reportPath, reportEntries, files, mode, profileName);
                if (reportResult != 0) overallResult = 1;
            }

            // Summary for multiple files
            if (files.Length > 1)
            {
                Output.WriteStatus($"\nValidation complete: {files.Length} file(s) processed");
                if (overallResult == 0)
                    Output.WriteSuccess("All files passed validation");
                else
                    Output.WriteError("Some files failed validation");
            }

            return overallResult;
        });

        return command;
    }
}
