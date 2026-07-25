// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Pidgeon.Core.Domain.Validation;

namespace Pidgeon.CLI.Services;

/// <summary>
/// Backs the <c>pidgeon validate --clinical</c> and <c>--semantic</c> advisory tiers.
/// Split out as an interface so <c>ValidateCommand</c> depends on the contract, not the
/// on-device-judge-backed implementation (<see cref="SemanticAdvisoryService"/>, which needs
/// the AI provider stack and is commercial-only); the community CLI build simply has no
/// registration for it, and <c>ValidateCommand</c> treats an absent advisory as "unavailable
/// in this build" rather than failing to compile.
/// </summary>
public interface ISemanticAdvisoryService
{
    /// <summary>
    /// Runs the deterministic clinical checks over the raw message. Always available:
    /// the runner self-degrades to an empty result when no checks/rules are loaded or
    /// the snapshot cannot be rendered, so the free tier never fails.
    /// </summary>
    SemanticAdvisoryOutcome RunClinical(string content);

    /// <summary>
    /// Renders a clinical snapshot and runs the advisory judge over the selected
    /// families. Returns an unavailable outcome (never throws) when the message can't
    /// be rendered, no provider is configured, or the PHI guard refuses the provider.
    /// </summary>
    Task<SemanticAdvisoryOutcome> RunSemanticAsync(string content, CancellationToken cancellationToken);

    /// <summary>
    /// Renders an advisory outcome to the console as muted status (never an error,
    /// never red) under its tier header.
    /// </summary>
    void Render(SemanticAdvisoryOutcome outcome, string header);
}

/// <summary>
/// The result of one advisory tier run: the findings, whether the tier could run,
/// and a degrade reason when it could not. Advisory findings never carry a
/// severity above <see cref="ValidationSeverity.Advisory"/> (type-enforced on
/// <see cref="SemanticFinding"/>).
/// </summary>
public sealed record SemanticAdvisoryOutcome
{
    public IReadOnlyList<SemanticFinding> Findings { get; init; } = Array.Empty<SemanticFinding>();

    /// <summary>False when the tier could not run (no model, PHI refusal, render failure).</summary>
    public bool Available { get; init; } = true;

    /// <summary>Why the tier could not run, for the muted degrade note. Null when available.</summary>
    public string? UnavailableReason { get; init; }

    public static SemanticAdvisoryOutcome Unavailable(string reason)
        => new() { Available = false, UnavailableReason = reason };
}
