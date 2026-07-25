// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core;
using Pidgeon.Core.Application.Services.Generation;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Application.Interfaces.Standards.HL7;
using Pidgeon.Core.Generation;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Command for generating healthcare messages and synthetic test data.
/// </summary>
public partial class GenerateCommand : CommandBuilderBase
{
    private readonly IMessageGenerationService _messageGenerationService;
    private readonly SessionHelper _sessionHelper;
    private readonly IHL7DataProviderFactory _dataProviderFactory;
    private readonly IManifestReplayService _manifestReplayService;
    private readonly IShareProfileValidator _shareProfileValidator;
    private readonly ShareHintService _shareHintService;

    public GenerateCommand(
        ILogger<GenerateCommand> logger,
        IConsoleOutput output,
        IMessageGenerationService messageGenerationService,
        SessionHelper sessionHelper,
        FirstTimeUserService firstTimeUserService,
        IHL7DataProviderFactory dataProviderFactory,
        IManifestReplayService manifestReplayService,
        IShareProfileValidator shareProfileValidator,
        ShareHintService shareHintService)
        : base(logger, output, firstTimeUserService)
    {
        _messageGenerationService = messageGenerationService;
        _sessionHelper = sessionHelper;
        _dataProviderFactory = dataProviderFactory;
        _manifestReplayService = manifestReplayService;
        _shareProfileValidator = shareProfileValidator;
        _shareHintService = shareHintService;
    }

    public override Command CreateCommand()
    {
        var command = new Command("generate", "Generate synthetic messages/resources with smart standard inference");

        // Positional arguments for smart inference: pidgeon generate <message-type> or pidgeon generate <standard> <message-type>
        var messageTypeArg = new Argument<string[]>("message-types")
        {
            Description = "Message type (e.g., ADT^A01, Patient, NewRx) - supports 239+ HL7/FHIR/NCPDP types",
            Arity = ArgumentArity.ZeroOrMore
        };

        // Add smart completions that work during tab completion but don't affect help display
        HealthcareCompletions.AddSmartCompletionsToArgument(messageTypeArg);

        // Options
        var countOption = CreateIntegerOption("--count", "-c", "Number of messages to generate", 1);
        var outputOption = CreateNullableOption("--output", "-o", "Output file path (optional, defaults to console)");
        var formatOption = CreateOptionalOption("--format", "-f", "Output format: auto|hl7|json|ndjson", "auto");
        HealthcareCompletions.AddFormatCompletions(formatOption);

        var modeOption = CreateOptionalOption("--mode", "-m", "Generation mode: procedural|model|api", "procedural");
        HealthcareCompletions.AddGenerationModeCompletions(modeOption);
        var seedOption = CreateNullableOption("--seed", "-s", "Deterministic seed for reproducible data");
        var vendorOption = CreateNullableOption("--vendor", "-v", "Apply vendor pattern (e.g., epic, cerner, meditech)");
        HealthcareCompletions.AddVendorCompletions(vendorOption);
        var hl7VersionOption = CreateOptionalOption("--hl7-version", "HL7 version for generation; use --hl7-version list to see supported versions", "2.3");
        var sessionOption = CreateNullableOption("--session", "Use specific session (overrides current session)");
        var noSessionOption = CreateBooleanOption("--no-session", "Force pure random generation (ignore current session)");
        var asOfOption = CreateNullableOption("--as-of", "Wall-clock instant to stamp messages with (ISO 8601, e.g. 2025-03-01T08:00). Defaults to the real current time; recorded in the manifest for replay.");
        var manifestOption = CreateNullableOption("--manifest", "Write a run-manifest (effective seed + clock + options) to this path so the run can be replayed with --from-manifest.");
        var fromManifestOption = CreateNullableOption("--from-manifest", "Replay a run from a manifest written by --manifest. The message-type argument is taken from the manifest.");
        var shareOption = CreateNullableOption("--share", "Write a shareable .pidgeonrun envelope (manifest + verification hash + run id) to this path. Runs the SHARE-PROFILE gate; a non-shareable run exits 1 with the reasons listed.");
        var titleOption = CreateNullableOption("--title", "Optional title recorded in a shared run's metadata (used with --share).");
        var descriptionOption = CreateNullableOption("--description", "Optional description recorded in a shared run's metadata (used with --share).");
        var byOption = CreateNullableOption("--by", "Optional author string recorded in a shared run's metadata (used with --share). Never auto-filled.");

        command.Add(messageTypeArg);
        command.Add(countOption);
        command.Add(outputOption);
        command.Add(formatOption);
        command.Add(modeOption);
        command.Add(seedOption);
        command.Add(vendorOption);
        command.Add(hl7VersionOption);
        command.Add(sessionOption);
        command.Add(noSessionOption);
        command.Add(asOfOption);
        command.Add(manifestOption);
        command.Add(fromManifestOption);
        command.Add(shareOption);
        command.Add(titleOption);
        command.Add(descriptionOption);
        command.Add(byOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            try
            {
                // Parse smart inference arguments
                var args = parseResult.GetValue(messageTypeArg) ?? Array.Empty<string>();

                var fromManifestPath = parseResult.GetValue(fromManifestOption);
                if (!string.IsNullOrEmpty(fromManifestPath))
                {
                    return await ReplayFromManifestAsync(
                        fromManifestPath,
                        parseResult.GetValue(outputOption),
                        parseResult.GetValue(formatOption) ?? "auto");
                }

                if (args.Length == 0)
                {
                    Logger.LogError("Message type is required. Usage: pidgeon generate <message-type> or pidgeon generate <standard> <message-type>");
                    WriteGenerateExamples();
                    return 1;
                }

                var parsingResult = SmartCommandParser.Parse(args);
                if (parsingResult.IsFailure)
                {
                    Logger.LogError(parsingResult.Error.Message);
                    Output.WriteError(parsingResult.Error.Message);
                    WriteGenerateExamples();
                    return 1;
                }

                var request = parsingResult.Value;

                var count = parseResult.GetValue(countOption);
                var output = parseResult.GetValue(outputOption);
                var format = parseResult.GetValue(formatOption);
                var mode = parseResult.GetValue(modeOption);
                var seedStr = parseResult.GetValue(seedOption);
                int? seed = null;
                if (!string.IsNullOrEmpty(seedStr) && int.TryParse(seedStr, out var parsedSeed))
                {
                    seed = parsedSeed;
                }
                var asOfStr = parseResult.GetValue(asOfOption);
                DateTime? asOf = null;
                if (!string.IsNullOrEmpty(asOfStr))
                {
                    if (!DateTime.TryParse(asOfStr, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var parsedAsOf))
                    {
                        Output.WriteError($"Invalid --as-of timestamp: {asOfStr}. Use ISO 8601, e.g. 2025-03-01T08:00.");
                        return 1;
                    }
                    asOf = parsedAsOf;
                }
                var vendor = parseResult.GetValue(vendorOption);
                var hl7Version = parseResult.GetValue(hl7VersionOption) ?? "2.3";
                var hl7VersionExplicit = parseResult.Tokens.Any(t =>
                    t.Value == "--hl7-version");

                // Handle --hl7-version list
                if (hl7Version.Equals("list", StringComparison.OrdinalIgnoreCase))
                {
                    var versions = _dataProviderFactory.GetSupportedVersions();
                    Output.WriteStatus("Available HL7 versions:");
                    foreach (var v in versions)
                    {
                        var marker = v == "2.3" ? " (default)" : "";
                        Output.WriteData($"  {v}{marker}");
                    }
                    return 0;
                }

                // Validate version
                if (!_dataProviderFactory.IsVersionSupported(hl7Version))
                {
                    Output.WriteError($"Unsupported HL7 version: {hl7Version}");
                    Output.WriteStatus("Use --hl7-version list to see available versions.");
                    return 1;
                }

                var sessionOverride = parseResult.GetValue(sessionOption);
                var noSession = parseResult.GetValue(noSessionOption);

                // Determine which session to use (smart session management)
                string? sessionToUse = await DetermineSessionAsync(sessionOverride, noSession, cancellationToken);

                // Check AI mode availability
                if (mode == "model" || mode == "api")
                {
                    var modeDesc = mode == "model" ? "Local AI models" : "Cloud AI APIs";
                    Output.WriteStatus($"Warning: {modeDesc} are in beta development. Using procedural mode for now.");
                    mode = "procedural";
                }

                Output.WriteStatus($"Generating {count} {request.Standard.ToUpperInvariant()} {request.MessageType} message(s)...");
                if (request.ExplicitStandardProvided)
                {
                    Output.WriteStatus($"Standard: {request.Standard.ToUpperInvariant()} (explicit)");
                }
                else
                {
                    Output.WriteStatus($"Standard: {request.Standard.ToUpperInvariant()}");
                }

                if (!string.IsNullOrEmpty(sessionToUse))
                {
                    Output.WriteStatus($"Using session: {sessionToUse}");
                }

                var options = new GenerationOptions
                {
                    UseAI = mode != "procedural",
                    Seed = seed,
                    AsOf = asOf,
                    LockSessionName = sessionToUse,
                    VendorProfile = VendorProfileKeyMap.ParseVendor(vendor),
                    Hl7Version = hl7Version,
                    Hl7VersionExplicit = hl7VersionExplicit
                };

                // Resolve the seed and clock once so an unseeded run is reproducible and its effective
                // values can be surfaced and recorded.
                var resolved = GenerationDeterminism.ResolveRun(options);

                var sharePath = parseResult.GetValue(shareOption);
                RunMeta? shareMeta = null;
                if (!string.IsNullOrEmpty(sharePath))
                {
                    shareMeta = BuildShareMeta(
                        parseResult.GetValue(titleOption),
                        parseResult.GetValue(descriptionOption),
                        parseResult.GetValue(byOption));

                    // SHARE-PROFILE runs before generation so a non-shareable run fails fast without wasted work.
                    var shareCheck = _shareProfileValidator.ValidateOptions(resolved, shareMeta);
                    if (!shareCheck.IsShareable)
                    {
                        Output.WriteError("Run is not shareable:");
                        foreach (var violation in shareCheck.Violations)
                            Output.WriteError($"  - {violation.Message}");
                        return 1;
                    }
                }

                var result = await _messageGenerationService.GenerateSyntheticDataAsync(request.Standard, request.MessageType, count, resolved);

                if (result.IsSuccess)
                {
                    Output.WriteStatus($"Effective seed: {resolved.Seed}");

                    var manifestPath = parseResult.GetValue(manifestOption);
                    if (!string.IsNullOrEmpty(manifestPath))
                    {
                        try
                        {
                            var manifest = GenerationRunManifest.Capture(request.Standard, request.MessageType, count, resolved);
                            var manifestDir = Path.GetDirectoryName(manifestPath);
                            if (!string.IsNullOrEmpty(manifestDir)) Directory.CreateDirectory(manifestDir);
                            await File.WriteAllTextAsync(manifestPath, manifest.ToJson());
                            Output.WriteStatus($"Run manifest written to {manifestPath}");
                        }
                        catch (InvalidOperationException notReproducible)
                        {
                            // A run that cannot be faithfully reproduced (AI / vendor / adhoc-lock / Context)
                            // was asked to emit a manifest — surface it rather than silently skipping.
                            Output.WriteError($"Run manifest not written: {notReproducible.Message}");
                        }
                        catch (Exception manifestEx)
                        {
                            Output.WriteStatus($"Run manifest not written: {manifestEx.Message}");
                        }
                    }

                    if (!string.IsNullOrEmpty(sharePath))
                    {
                        var shareExit = await WriteShareEnvelopeAsync(
                            request.Standard, request.MessageType, count, resolved, result.Value, shareMeta!, sharePath);
                        if (shareExit != 0)
                            return shareExit;
                    }

                    if (!string.IsNullOrEmpty(output))
                    {
                        // Write to file(s)
                        await WriteToFileAsync(output, result.Value, format!);

                        if (count == 1)
                        {
                            Output.WriteStatus($"Generated {count} {request.MessageType} message and saved to {output}");
                        }
                        else
                        {
                            var directory = Path.GetDirectoryName(output) ?? ".";
                            var filePattern = $"{Path.GetFileNameWithoutExtension(output)}_*{Path.GetExtension(output)}";
                            Output.WriteStatus($"Generated {count} {request.MessageType} messages and saved to {Path.Combine(directory, filePattern)}");
                        }
                    }
                    else
                    {
                        // Write to stdout as data
                        WriteToStdout(result.Value, format!, request.Standard, request.MessageType, hl7Version, resolved.Seed);
                        Output.WriteStatus($"Generated {count} {request.MessageType} message(s)");
                    }

                    // One-time growth nudge (b175): a successful run without --share earns the
                    // single share hint; the service owns every suppression rule.
                    if (_shareHintService.TryClaimFirstShareHint(
                            sharePassed: !string.IsNullOrEmpty(sharePath),
                            quiet: Output.IsQuiet,
                            outputFormat: Output.OutputFormat))
                    {
                        Output.WriteStatus(ShareHintService.HintText);
                    }

                    return 0;
                }

                return HandleResult(result);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Unexpected error during message generation");
                Output.WriteError($"Generation failed: {ex.Message}");
                Output.WriteStatus("  Check your message type and options. Run with --verbose for details.");
                return 1;
            }
        });

        return command;
    }
}
