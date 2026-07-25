// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core;
using Pidgeon.Core.Application.Interfaces.Generation;
using Pidgeon.Core.Generation;
using Pidgeon.Core.Generation.Types;
using System.CommandLine;
using System.Globalization;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// The <c>pidgeon run</c> verb group: work with shareable <c>.pidgeonrun</c> run artifacts
/// (SHAREABLE_RUN_STANDARD.md §4-§5). Every verb is offline — no registry lookup, no telemetry, no version
/// check — so a recipient reproduces and verifies a run on an air-gapped laptop with nothing but the binary
/// and the file. Verbs accept either a <c>.pidgeonrun</c> envelope or a bare run manifest; verify refuses a
/// bare manifest because it carries no proof block.
/// </summary>
public sealed class RunCommand : CommandBuilderBase
{
    // run verify extends the CLI's usual {0,1} convention with proof-grade outcomes a pipeline must
    // distinguish (SHAREABLE_RUN_STANDARD.md §4), the same reasoning behind conform --ci.
    private const int ExitVerified = 0;
    private const int ExitHashMismatch = 2;
    private const int ExitDeterminismMismatch = 3;
    private const int ExitInvalidArtifact = 4;

    private readonly IManifestReplayService _replayService;
    private readonly IShareProfileValidator _shareProfileValidator;

    public RunCommand(
        ILogger<RunCommand> logger,
        IConsoleOutput output,
        FirstTimeUserService firstTimeUserService,
        IManifestReplayService replayService,
        IShareProfileValidator shareProfileValidator)
        : base(logger, output, firstTimeUserService)
    {
        _replayService = replayService ?? throw new ArgumentNullException(nameof(replayService));
        _shareProfileValidator = shareProfileValidator ?? throw new ArgumentNullException(nameof(shareProfileValidator));
    }

    public override Command CreateCommand()
    {
        var command = new Command("run", "Work with shareable .pidgeonrun run artifacts: replay, verify, show, diff, fork, link");
        command.Add(CreateReplayCommand());
        command.Add(CreateVerifyCommand());
        command.Add(CreateShowCommand());
        command.Add(CreateDiffCommand());
        command.Add(CreateForkCommand());
        command.Add(CreateLinkCommand());
        return command;
    }

    private Command CreateReplayCommand()
    {
        var artifactArg = new Argument<string>("artifact") { Description = "A .pidgeonrun envelope, a bare run manifest, an r1. payload, or a share URL" };
        var outputOption = CreateNullableOption("--output", "-o", "Output file or folder for the reproduced messages (defaults to stdout)");
        var formatOption = CreateOptionalOption("--format", "-f", "Output format: auto|hl7|json|ndjson", "auto");
        var yesOption = CreateBooleanOption("--yes", "Confirm replay of a large run (count above the count-bomb threshold)");

        var command = new Command("replay", "Reproduce a run's bytes from its artifact") { artifactArg, outputOption, formatOption, yesOption };

        SetCommandAction(command, async (parseResult, _) =>
        {
            var load = await LoadArtifactAsync(parseResult.GetValue(artifactArg)!);
            if (load.IsFailure)
            {
                Output.WriteError(load.Error.Message);
                return ExitInvalidArtifact;
            }

            var manifest = load.Value.Manifest;
            if (RunGuards.RequiresConfirmation(manifest.Count) && !parseResult.GetValue(yesOption))
            {
                Output.WriteError($"This artifact generates {manifest.Count} messages (over {RunGuards.CountConfirmationThreshold}). Re-run with --yes to proceed.");
                return 1;
            }

            if (!manifest.IsCurrentDeterminismVersion)
                Output.WriteStatus(DeterminismSkewWarning(manifest.DeterminismVersion));

            Output.WriteStatus($"Replaying {manifest.Count} {manifest.MessageType} message(s) (seed {manifest.RootSeed}, engine {manifest.EngineVersion})...");

            var replay = await _replayService.ReplayAsync(manifest);
            if (replay.IsFailure)
                return HandleResult(replay);

            Output.WriteStatus($"Effective seed: {manifest.RootSeed}");

            var output = parseResult.GetValue(outputOption);
            if (!string.IsNullOrEmpty(output))
                await WriteMessagesToFileAsync(output, replay.Value.Messages);
            else
                WriteMessagesToStdout(replay.Value.Messages);

            return 0;
        });

        return command;
    }

    private Command CreateVerifyCommand()
    {
        var artifactArg = new Argument<string>("artifact") { Description = "A .pidgeonrun envelope, an r1. payload, or a share URL (a bare manifest has no proof block)" };
        var bestEffortOption = CreateBooleanOption("--best-effort", "Verify anyway across a determinism-version mismatch, attributing the skew prominently");
        var yesOption = CreateBooleanOption("--yes", "Confirm verification of a large run (count above the count-bomb threshold)");

        var command = new Command("verify", "Prove an artifact reproduces byte-identically on this machine, offline") { artifactArg, bestEffortOption, yesOption };

        SetCommandAction(command, async (parseResult, _) =>
        {
            var load = await LoadArtifactAsync(parseResult.GetValue(artifactArg)!);
            if (load.IsFailure)
            {
                Output.WriteError(load.Error.Message);
                return ExitInvalidArtifact;
            }

            var artifact = load.Value;
            if (artifact.Envelope is null)
            {
                Output.WriteError("Artifact carries no verification block (a bare manifest cannot be verified). Share a .pidgeonrun envelope.");
                return ExitInvalidArtifact;
            }

            var manifest = artifact.Manifest;
            var verification = artifact.Envelope.Verification;

            if (RunGuards.RequiresConfirmation(manifest.Count) && !parseResult.GetValue(yesOption))
            {
                Output.WriteError($"This artifact regenerates {manifest.Count} messages (over {RunGuards.CountConfirmationThreshold}). Re-run with --yes to proceed.");
                return 1;
            }

            var bestEffort = parseResult.GetValue(bestEffortOption);
            var determinismMismatch = manifest.DeterminismVersion != GenerationDeterminism.DeterminismVersion;
            if (determinismMismatch && !bestEffort)
            {
                Output.WriteError(
                    $"Determinism-version mismatch: artifact {manifest.DeterminismVersion}, engine {GenerationDeterminism.DeterminismVersion}. "
                    + "A proof attempted under a changed contract is not a proof. Re-run with --best-effort to attempt anyway.");
                return ExitDeterminismMismatch;
            }

            Output.WriteStatus($"Regenerating {manifest.Count} {manifest.MessageType} message(s) in memory to verify (seed {manifest.RootSeed})...");

            var replay = await _replayService.ReplayAsync(manifest);
            if (replay.IsFailure)
            {
                Output.WriteError($"Verification could not regenerate the run: {replay.Error.Message}");
                return 1;
            }

            var collectPerMessage = manifest.Count <= RunHashing.MessageHashInclusionThreshold;
            var recomputed = RunHashing.Compute(replay.Value.Messages, collectPerMessage);

            if (string.Equals(recomputed.OutputHash, verification.OutputHash, StringComparison.OrdinalIgnoreCase))
            {
                Output.WriteSuccess($"Verified: {manifest.Count} {manifest.MessageType} message(s) reproduce byte-identically (output_hash {Short(recomputed.OutputHash)}).");
                if (determinismMismatch)
                    Output.WriteStatus("(verified across a determinism-version mismatch via --best-effort)");
                return ExitVerified;
            }

            Output.WriteError($"Hash mismatch: recomputed {Short(recomputed.OutputHash)} != recorded {Short(verification.OutputHash)}.");

            if (verification.MessageHashes is { Count: > 0 } stored && recomputed.MessageHashes is { } recomputedHashes)
            {
                var divergingIndex = RunHashing.FirstDivergingIndex(stored, recomputedHashes);
                if (divergingIndex >= 0)
                    Output.WriteError($"First diverging message index: {divergingIndex}.");
            }

            if (determinismMismatch)
                Output.WriteStatus("The artifact's determinism version differs from this engine's — the skew is the probable cause.");
            else
                Output.WriteStatus("Same determinism version: the divergence may be an engine-version skew or an installed data-package skew (see SHAREABLE_RUN_STANDARD.md §10).");

            return ExitHashMismatch;
        });

        return command;
    }

    private Command CreateShowCommand()
    {
        var artifactArg = new Argument<string>("artifact") { Description = "A .pidgeonrun envelope, a bare run manifest, an r1. payload, or a share URL" };
        var command = new Command("show", "Render an artifact's config, seed, clock, hashes, versions, and lineage (no generation)") { artifactArg };

        SetInfoCommandAction(command, async (parseResult, _) =>
        {
            var load = await LoadArtifactAsync(parseResult.GetValue(artifactArg)!);
            if (load.IsFailure)
            {
                Output.WriteError(load.Error.Message);
                return ExitInvalidArtifact;
            }

            RenderArtifact(load.Value);
            return 0;
        });

        return command;
    }

    private Command CreateDiffCommand()
    {
        var leftArg = new Argument<string>("a") { Description = "First artifact (file, r1. payload, or share URL)" };
        var rightArg = new Argument<string>("b") { Description = "Second artifact (file, r1. payload, or share URL)" };
        var command = new Command("diff", "Compare two artifacts at the config level plus their output hashes (free)") { leftArg, rightArg };

        SetInfoCommandAction(command, async (parseResult, _) =>
        {
            var loadA = await LoadArtifactAsync(parseResult.GetValue(leftArg)!);
            if (loadA.IsFailure)
            {
                Output.WriteError(loadA.Error.Message);
                return ExitInvalidArtifact;
            }

            var loadB = await LoadArtifactAsync(parseResult.GetValue(rightArg)!);
            if (loadB.IsFailure)
            {
                Output.WriteError(loadB.Error.Message);
                return ExitInvalidArtifact;
            }

            return RenderDiff(loadA.Value, loadB.Value) ? 0 : 1;
        });

        return command;
    }

    private Command CreateForkCommand()
    {
        var artifactArg = new Argument<string>("artifact") { Description = "Parent .pidgeonrun envelope, bare manifest, r1. payload, or share URL to fork from" };
        var setOption = new Option<string[]>("--set")
        {
            Description = "Override a procedural knob as key=value (repeatable). Allowed: " + string.Join(", ", AllowedForkKeys),
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var outputOption = CreateNullableOption("--output", "-o", "Path to write the forked child .pidgeonrun");
        var titleOption = CreateNullableOption("--title", "Optional title recorded in the child's metadata");
        var descriptionOption = CreateNullableOption("--description", "Optional description recorded in the child's metadata");

        var command = new Command("fork", "Derive a child run with a memory of its parent (regenerates and rehashes)")
        {
            artifactArg, setOption, outputOption, titleOption, descriptionOption
        };

        SetCommandAction(command, async (parseResult, _) =>
        {
            var outputPath = parseResult.GetValue(outputOption);
            if (string.IsNullOrEmpty(outputPath))
            {
                Output.WriteError("Fork requires an output path: -o <child.pidgeonrun>");
                return 1;
            }

            var load = await LoadArtifactAsync(parseResult.GetValue(artifactArg)!);
            if (load.IsFailure)
            {
                Output.WriteError(load.Error.Message);
                return ExitInvalidArtifact;
            }

            var parent = load.Value;

            var applied = ApplyForkOverrides(parent.Manifest, parseResult.GetValue(setOption) ?? Array.Empty<string>());
            if (applied.IsFailure)
            {
                Output.WriteError(applied.Error.Message);
                return 1;
            }

            var childManifest = applied.Value;
            var meta = new RunMeta
            {
                Title = NullIfBlank(parseResult.GetValue(titleOption)),
                Description = NullIfBlank(parseResult.GetValue(descriptionOption)),
                CreatedAt = DateTime.UtcNow
            };

            var shareCheck = _shareProfileValidator.Validate(childManifest, meta);
            if (!shareCheck.IsShareable)
            {
                Output.WriteError("Forked run is not shareable:");
                foreach (var violation in shareCheck.Violations)
                    Output.WriteError($"  - {violation.Message}");
                return 1;
            }

            // The child always regenerates and rehashes — an envelope whose hash was not computed from its
            // own regeneration is nonconforming (SHAREABLE_RUN_STANDARD.md §5.3).
            var parentRunId = await ResolveParentRunIdAsync(parent);
            if (parentRunId.IsFailure)
            {
                Output.WriteError(parentRunId.Error.Message);
                return 1;
            }

            var replay = await _replayService.ReplayAsync(childManifest);
            if (replay.IsFailure)
                return HandleResult(replay);

            var collectPerMessage = childManifest.Count <= RunHashing.MessageHashInclusionThreshold;
            var hash = RunHashing.Compute(replay.Value.Messages, collectPerMessage);
            var childRunId = RunIdentity.Compute(childManifest, hash.OutputHash);

            var envelope = new RunEnvelope
            {
                RunId = childRunId,
                Manifest = childManifest,
                Verification = new RunVerification { OutputHash = hash.OutputHash, MessageHashes = hash.MessageHashes },
                Meta = meta,
                Provenance = new RunProvenance
                {
                    ForkedFrom = new RunForkedFrom
                    {
                        RunId = parentRunId.Value,
                        DeterminismVersion = parent.Manifest.DeterminismVersion
                    }
                }
            };

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(outputPath, envelope.ToJson());
            Output.WriteSuccess($"Forked run written to {outputPath} (run id {childRunId}, forked from {parentRunId.Value}).");
            return 0;
        });

        return command;
    }

    private Command CreateLinkCommand()
    {
        var artifactArg = new Argument<string>("artifact") { Description = "A .pidgeonrun envelope path, a bare r1. payload, or a share URL to re-encode as a link" };
        var command = new Command("link", "Encode an artifact as a shareable r1. link + account-portal share URL (offline, nothing stored)") { artifactArg };

        SetInfoCommandAction(command, async (parseResult, _) =>
        {
            var load = await LoadArtifactAsync(parseResult.GetValue(artifactArg)!);
            if (load.IsFailure)
            {
                Output.WriteError(load.Error.Message);
                return ExitInvalidArtifact;
            }

            if (load.Value.Envelope is null)
            {
                Output.WriteError("A bare manifest carries no verification/identity to link. Share a .pidgeonrun envelope (generate --share).");
                return ExitInvalidArtifact;
            }

            var link = RunLinkCodec.Encode(load.Value.Envelope);
            if (link.IsFailure)
            {
                Output.WriteError(link.Error.Message);
                return 1;
            }

            Output.WriteData(link.Value);
            Output.WriteStatus($"Share URL: {RunLinkCodec.RunPageUrl}#{link.Value}");
            return 0;
        });

        return command;
    }

    // ---- shared helpers ----------------------------------------------------

    private static readonly string[] AllowedForkKeys =
    {
        "seed", "count", "as_of", "message_type", "standard", "patient_type",
        "clinical_scenario_id", "vendor_profile", "hl7_version", "ncpdp_version", "is_cohort_sequence"
    };

    // The single artifact resolver every verb goes through (SHAREABLE_RUN_STANDARD.md §6.3): the argument may
    // be a file path, a bare r1. payload, or a full share URL. Link decoding is pure, offline string work
    // (the air-gap path, §6.5); only the file branch touches I/O.
    private async Task<Result<RunArtifact>> LoadArtifactAsync(string argument)
    {
        if (RunLinkCodec.IsLink(argument))
            return RunArtifactReader.ReadLink(argument);

        if (!File.Exists(argument))
            return Result<RunArtifact>.Failure(Error.Create("ARTIFACT_NOT_FOUND", $"Artifact not found: {argument}"));

        string json;
        try
        {
            json = await File.ReadAllTextAsync(argument);
        }
        catch (Exception ex)
        {
            return Result<RunArtifact>.Failure(Error.Create("ARTIFACT_READ_FAILED", $"Could not read {argument}: {ex.Message}"));
        }

        return RunArtifactReader.Read(json);
    }

    private async Task<Result<string>> ResolveParentRunIdAsync(RunArtifact parent)
    {
        if (parent.Envelope is not null)
            return Result<string>.Success(parent.Envelope.RunId);

        // A bare-manifest parent has no stored run id — regenerate it to compute one so forked_from is exact.
        var replay = await _replayService.ReplayAsync(parent.Manifest);
        if (replay.IsFailure)
            return replay.Error;

        var hash = RunHashing.Compute(replay.Value.Messages, collectPerMessageHashes: false);
        return Result<string>.Success(RunIdentity.Compute(parent.Manifest, hash.OutputHash));
    }

    private static Result<GenerationRunManifest> ApplyForkOverrides(GenerationRunManifest parent, IReadOnlyList<string> sets)
    {
        var manifest = parent;
        foreach (var entry in sets)
        {
            var separator = entry.IndexOf('=');
            if (separator <= 0)
                return Result<GenerationRunManifest>.Failure(Error.Create("FORK_SET_INVALID", $"--set expects key=value, got '{entry}'."));

            var key = entry[..separator].Trim().ToLowerInvariant();
            var value = entry[(separator + 1)..];

            var applied = ApplyForkOverride(manifest, key, value);
            if (applied.IsFailure)
                return applied;
            manifest = applied.Value;
        }

        return Result<GenerationRunManifest>.Success(manifest);
    }

    private static Result<GenerationRunManifest> ApplyForkOverride(GenerationRunManifest manifest, string key, string value)
    {
        switch (key)
        {
            case "seed":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                    return ForkParseError(key, value);
                return Result<GenerationRunManifest>.Success(manifest with { RootSeed = seed });

            case "count":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 1)
                    return ForkParseError(key, value);
                return Result<GenerationRunManifest>.Success(manifest with { Count = count });

            case "as_of":
                if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var asOf))
                    return ForkParseError(key, value);
                return Result<GenerationRunManifest>.Success(manifest with { AsOf = asOf });

            case "message_type":
                return Result<GenerationRunManifest>.Success(manifest with { MessageType = value });

            case "standard":
                return Result<GenerationRunManifest>.Success(manifest with { Standard = value });

            case "patient_type":
                if (!Enum.TryParse<PatientType>(value, ignoreCase: true, out var patientType))
                    return ForkParseError(key, value);
                return Result<GenerationRunManifest>.Success(manifest with { PatientType = patientType });

            case "clinical_scenario_id":
                return Result<GenerationRunManifest>.Success(manifest with { ClinicalScenarioId = NullIfBlank(value) });

            case "vendor_profile":
                var vendor = VendorProfileKeyMap.ParseVendor(value);
                if (vendor is null)
                    return ForkParseError(key, value);
                return Result<GenerationRunManifest>.Success(manifest with { VendorProfile = vendor });

            case "hl7_version":
                return Result<GenerationRunManifest>.Success(manifest with { Hl7Version = value, Hl7VersionExplicit = true });

            case "ncpdp_version":
                return Result<GenerationRunManifest>.Success(manifest with { NcpdpVersion = NullIfBlank(value) });

            case "is_cohort_sequence":
                if (!bool.TryParse(value, out var isCohort))
                    return ForkParseError(key, value);
                return Result<GenerationRunManifest>.Success(manifest with { IsCohortSequence = isCohort });

            default:
                return Result<GenerationRunManifest>.Failure(Error.Create(
                    "FORK_SET_UNKNOWN",
                    $"unknown --set key '{key}'; allowed: {string.Join(", ", AllowedForkKeys)}."));
        }
    }

    private static Result<GenerationRunManifest> ForkParseError(string key, string value)
        => Result<GenerationRunManifest>.Failure(Error.Create("FORK_SET_INVALID", $"invalid value for --set {key}: '{value}'."));

    private bool RenderDiff(RunArtifact a, RunArtifact b)
    {
        var manifestA = a.Manifest;
        var manifestB = b.Manifest;
        var identical = true;

        Output.WriteStatus("Config delta:");
        identical &= DiffField("standard", manifestA.Standard, manifestB.Standard);
        identical &= DiffField("message_type", manifestA.MessageType, manifestB.MessageType);
        identical &= DiffField("seed", manifestA.RootSeed, manifestB.RootSeed);
        identical &= DiffField("count", manifestA.Count, manifestB.Count);
        identical &= DiffField("as_of", FormatClock(manifestA.AsOf), FormatClock(manifestB.AsOf));
        identical &= DiffField("patient_type", manifestA.PatientType, manifestB.PatientType);
        identical &= DiffField("hl7_version", manifestA.Hl7Version, manifestB.Hl7Version);
        identical &= DiffField("ncpdp_version", manifestA.NcpdpVersion, manifestB.NcpdpVersion);
        identical &= DiffField("vendor_profile", manifestA.VendorProfile, manifestB.VendorProfile);
        identical &= DiffField("is_cohort_sequence", manifestA.IsCohortSequence, manifestB.IsCohortSequence);
        identical &= DiffField("clinical_scenario_id", manifestA.ClinicalScenarioId, manifestB.ClinicalScenarioId);
        identical &= DiffField("determinism_version", manifestA.DeterminismVersion, manifestB.DeterminismVersion);
        identical &= DiffField("engine_version", manifestA.EngineVersion, manifestB.EngineVersion);

        var hashA = a.Envelope?.Verification.OutputHash;
        var hashB = b.Envelope?.Verification.OutputHash;
        if (hashA is not null && hashB is not null)
        {
            var hashesEqual = string.Equals(hashA, hashB, StringComparison.OrdinalIgnoreCase);
            Output.WriteData($"  output_hash  {Short(hashA)} {(hashesEqual ? "=" : "≠")} {Short(hashB)}   ({(hashesEqual ? "outputs identical" : "outputs differ")})");
            identical &= hashesEqual;
        }
        else
        {
            Output.WriteData("  output_hash  (n/a — one or both artifacts are bare manifests)");
        }

        Output.WriteStatus(identical ? "Artifacts are identical." : "Artifacts differ.");
        return identical;
    }

    private bool DiffField<T>(string name, T left, T right)
    {
        var equal = EqualityComparer<T>.Default.Equals(left, right);
        var leftText = FormatValue(left);
        var rightText = FormatValue(right);
        Output.WriteData(equal
            ? $"  {name,-20} {leftText}   (same)"
            : $"  {name,-20} {leftText} → {rightText}   (changed)");
        return equal;
    }

    private void RenderArtifact(RunArtifact artifact)
    {
        var manifest = artifact.Manifest;

        if (artifact.Envelope is { } envelope)
        {
            Output.WriteStatus($"run_id           {envelope.RunId}");
            Output.WriteStatus($"format_version   {envelope.FormatVersion}");
        }
        else
        {
            Output.WriteStatus("run_id           (bare manifest — no envelope)");
        }

        Output.WriteStatus($"standard         {manifest.Standard}");
        Output.WriteStatus($"message_type     {manifest.MessageType}");
        Output.WriteStatus($"count            {manifest.Count}");
        Output.WriteStatus($"seed             {manifest.RootSeed}");
        Output.WriteStatus($"as_of            {FormatClock(manifest.AsOf)}");
        Output.WriteStatus($"patient_type     {manifest.PatientType}");
        Output.WriteStatus($"hl7_version      {manifest.Hl7Version}");
        if (manifest.NcpdpVersion is not null)
            Output.WriteStatus($"ncpdp_version    {manifest.NcpdpVersion}");
        if (manifest.VendorProfile is not null)
            Output.WriteStatus($"vendor_profile   {manifest.VendorProfile}");
        if (manifest.IsCohortSequence)
            Output.WriteStatus("is_cohort_seq    true");
        if (manifest.ClinicalScenarioId is not null)
            Output.WriteStatus($"scenario         {manifest.ClinicalScenarioId}");
        Output.WriteStatus($"determinism_ver  {manifest.DeterminismVersion}{(manifest.IsCurrentDeterminismVersion ? "" : $" (engine is {GenerationDeterminism.DeterminismVersion})")}");
        Output.WriteStatus($"engine_version   {manifest.EngineVersion}");

        if (artifact.Envelope is { } env)
        {
            Output.WriteStatus($"output_hash      {env.Verification.OutputHash}");
            Output.WriteStatus($"hash_algorithm   {env.Verification.HashAlgorithm} / {env.Verification.Canonicalization}");
            if (env.Verification.MessageHashes is { Count: > 0 } hashes)
                Output.WriteStatus($"message_hashes   {hashes.Count} recorded");
            if (env.Meta.Title is { } title)
                Output.WriteStatus($"title            {title}");
            if (env.Meta.Description is { } description)
                Output.WriteStatus($"description       {description}");
            Output.WriteStatus($"created_at       {FormatClock(env.Meta.CreatedAt)}");
            if (env.Meta.CreatedBy is { } createdBy)
                Output.WriteStatus($"created_by       {createdBy}");
            if (env.Provenance is { } provenance)
                Output.WriteStatus($"forked_from      {provenance.ForkedFrom.RunId} (determinism version {provenance.ForkedFrom.DeterminismVersion})");
        }
    }

    private async Task WriteMessagesToFileAsync(string path, IReadOnlyList<string> messages)
    {
        if (messages.Count == 1)
        {
            var singleDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(singleDir)) Directory.CreateDirectory(singleDir);
            await File.WriteAllTextAsync(path, messages[0]);
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) directory = ".";
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var width = messages.Count.ToString(CultureInfo.InvariantCulture).Length;

        for (var i = 0; i < messages.Count; i++)
        {
            var suffix = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
            await File.WriteAllTextAsync(Path.Combine(directory, $"{stem}_{suffix}{extension}"), messages[i]);
        }

        Output.WriteStatus($"Wrote {messages.Count} message(s) to {directory}");
    }

    private void WriteMessagesToStdout(IReadOnlyList<string> messages)
    {
        if (Output.OutputFormat == "json")
        {
            Output.WriteData(new { count = messages.Count, messages });
            return;
        }

        foreach (var message in messages)
            Output.WriteData(message);
    }

    private static string DeterminismSkewWarning(int manifestVersion)
        => $"Warning: artifact determinism version {manifestVersion} differs from this engine's ({GenerationDeterminism.DeterminismVersion}); replay may not be byte-identical.";

    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12] + "…";

    private static string FormatClock(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static string FormatValue<T>(T value) => value is null ? "(none)" : value.ToString() ?? "(none)";

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
