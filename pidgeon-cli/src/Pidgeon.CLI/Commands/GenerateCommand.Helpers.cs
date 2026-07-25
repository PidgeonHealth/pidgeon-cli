// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.Core.Generation;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// Helpers for <see cref="GenerateCommand"/>: file persistence, stdout emission
/// (raw messages in text mode, a structured envelope in --output-format json),
/// manifest replay, and share-envelope writing.
/// </summary>
public partial class GenerateCommand
{
    private async Task WriteToFileAsync(string filePath, IReadOnlyList<string> messages, string format)
    {
        // Single message: write to specified file directly
        if (messages.Count == 1)
        {
            await File.WriteAllTextAsync(filePath, messages[0]);
            Logger.LogInformation("Generated message written to {FilePath}", filePath);
            return;
        }

        // Multiple messages: write each to separate file with numeric suffix
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);

        // Ensure directory exists
        if (!string.IsNullOrEmpty(directory) && directory != ".")
        {
            Directory.CreateDirectory(directory);
        }

        var paddingWidth = messages.Count switch
        {
            <= 9 => 1,
            <= 99 => 2,
            <= 999 => 3,
            <= 9999 => 4,
            _ => 5
        };

        for (int i = 0; i < messages.Count; i++)
        {
            var suffix = (i + 1).ToString().PadLeft(paddingWidth, '0');
            var outputPath = Path.Combine(directory, $"{fileNameWithoutExt}_{suffix}{extension}");
            await File.WriteAllTextAsync(outputPath, messages[i]);

            Logger.LogInformation("Generated message {Index} written to {FilePath}", i + 1, outputPath);
        }
    }

    private void WriteToStdout(IReadOnlyList<string> messages, string format, string standard, string messageType, string? version, int? seed)
    {
        // Agent-legible JSON: emit ONE structured envelope on stdout (status stays on stderr) so an
        // agent asking for --output-format json gets a parseable result, not raw HL7 with status noise.
        if (Output.OutputFormat == "json")
        {
            Output.WriteData(new
            {
                standard,
                messageType,
                version,
                seed,
                count = messages.Count,
                format,
                messages
            });
            return;
        }

        foreach (var message in messages)
        {
            if (format != "auto" && messages.Count > 1)
            {
                Output.WriteData(new string('-', 50));
            }

            Output.WriteData(message);
        }
    }

    private async Task<int> ReplayFromManifestAsync(string manifestPath, string? output, string format)
    {
        if (!File.Exists(manifestPath))
        {
            Output.WriteError($"Manifest not found: {manifestPath}");
            return 1;
        }

        GenerationRunManifest manifest;
        try
        {
            manifest = GenerationRunManifest.FromJson(await File.ReadAllTextAsync(manifestPath));
        }
        catch (Exception ex)
        {
            Output.WriteError($"Invalid run manifest {manifestPath}: {ex.Message}");
            return 1;
        }

        if (!manifest.IsCurrentDeterminismVersion)
        {
            Output.WriteStatus(
                $"Warning: manifest determinism version {manifest.DeterminismVersion} differs from this engine's "
                + $"({GenerationDeterminism.DeterminismVersion}); replay may not be byte-identical.");
        }

        Output.WriteStatus(
            $"Replaying {manifest.Count} {manifest.MessageType} message(s) from manifest (seed {manifest.RootSeed}, engine {manifest.EngineVersion})...");

        var replay = await _manifestReplayService.ReplayAsync(manifest);
        if (replay.IsFailure)
            return HandleResult(replay);

        Output.WriteStatus($"Effective seed: {manifest.RootSeed}");

        if (!string.IsNullOrEmpty(output))
            await WriteToFileAsync(output, replay.Value.Messages, format);
        else
            WriteToStdout(replay.Value.Messages, format, manifest.Standard, manifest.MessageType, manifest.Hl7Version, manifest.RootSeed);

        return 0;
    }

    private static RunMeta BuildShareMeta(string? title, string? description, string? createdBy)
        => new()
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? null : createdBy
        };

    private async Task<int> WriteShareEnvelopeAsync(
        string standard,
        string messageType,
        int count,
        GenerationOptions resolved,
        IReadOnlyList<string> messages,
        RunMeta meta,
        string sharePath)
    {
        GenerationRunManifest manifest;
        try
        {
            manifest = GenerationRunManifest.Capture(standard, messageType, count, resolved);
        }
        catch (InvalidOperationException notReproducible)
        {
            Output.WriteError($"Run is not shareable: {notReproducible.Message}");
            return 1;
        }

        var collectPerMessage = count <= RunHashing.MessageHashInclusionThreshold;
        var hash = RunHashing.Compute(messages, collectPerMessage);
        var runId = RunIdentity.Compute(manifest, hash.OutputHash);

        var envelope = new RunEnvelope
        {
            RunId = runId,
            Manifest = manifest,
            Verification = new RunVerification
            {
                OutputHash = hash.OutputHash,
                MessageHashes = hash.MessageHashes
            },
            Meta = meta
        };

        var shareDir = Path.GetDirectoryName(sharePath);
        if (!string.IsNullOrEmpty(shareDir)) Directory.CreateDirectory(shareDir);
        await File.WriteAllTextAsync(sharePath, envelope.ToJson());
        Output.WriteStatus($"Shareable run written to {sharePath} (run id {runId}).");

        // The file is the primitive; the link is sugar (SHAREABLE_RUN_STANDARD.md §6.5). Offer the r1. share
        // URL inline when the run fits under the link ceiling, and note the file fallback when it does not.
        var shareUrl = RunLinkCodec.EncodeShareUrl(envelope);
        Output.WriteStatus(shareUrl.IsSuccess
            ? $"Share link: {shareUrl.Value}"
            : "(run too large to link; share the .pidgeonrun file — `pidgeon run link` for the payload)");
        return 0;
    }

    private async Task<string?> DetermineSessionAsync(string? sessionOverride, bool noSession, CancellationToken cancellationToken)
    {
        if (noSession)
            return null;

        if (!string.IsNullOrEmpty(sessionOverride))
            return sessionOverride;

        return await _sessionHelper.GetCurrentSessionAsync(cancellationToken);
    }

    private void WriteGenerateExamples()
    {
        Output.WriteStatus("Examples:");
        Output.WriteStatus("  pidgeon generate \"ADT^A01\"");
        Output.WriteStatus("  pidgeon generate Patient --count 10");
        Output.WriteStatus("  pidgeon generate hl7 \"ADT^A01\"");
    }
}
