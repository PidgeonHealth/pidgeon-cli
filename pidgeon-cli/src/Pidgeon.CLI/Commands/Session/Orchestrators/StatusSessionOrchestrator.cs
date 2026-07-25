// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session status</c>.
/// Shows current session state — active session name, type
/// (temporary vs permanent), field count, creation time, and the
/// next-step commands the user can run. Session file I/O stays
/// behind <see cref="SessionHelper"/>, so this orchestrator only
/// orchestrates reads + presentation.
/// </summary>
public sealed class StatusSessionOrchestrator
{
    private readonly ILogger<StatusSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly ILockSessionService _lockSessionService;
    private readonly SessionHelper _sessionHelper;

    public StatusSessionOrchestrator(
        ILogger<StatusSessionOrchestrator> logger,
        IConsoleOutput output,
        ILockSessionService lockSessionService,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _lockSessionService = lockSessionService;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var currentSession = await _sessionHelper.GetCurrentSessionAsync(cancellationToken);

        if (currentSession == null)
        {
            _output.WriteStatus("No active session");
            _output.WriteStatus("Create a session by setting a value:");
            _output.WriteStatus("   pidgeon set patient.mrn \"TEST123\"");
            _output.WriteStatus("Or create a named session:");
            _output.WriteStatus("   pidgeon session create my_scenario");
            return 0;
        }

        var sessionResult = await _lockSessionService.GetSessionAsync(currentSession, cancellationToken);
        if (sessionResult.IsFailure)
        {
            _output.WriteError($"Current session '{currentSession}' not found");
            await _sessionHelper.ClearCurrentSessionAsync(cancellationToken);
            return 1;
        }

        var session = sessionResult.Value;
        var isTemporary = await _sessionHelper.IsTemporarySessionAsync(currentSession, cancellationToken);

        _output.WriteStatus($"Current Session: {currentSession}");
        _output.WriteStatus($"   Type: {(isTemporary ? "Temporary (expires in 24h unless saved)" : "Permanent")}");
        _output.WriteStatus($"   Values: {session.LockedValues.Count} field(s) set");
        _output.WriteStatus($"   Created: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (session.LockedValues.Any())
        {
            _output.WriteStatus("");
            _output.WriteStatus("Commands:");
            _output.WriteStatus("   pidgeon set --list                 # Show all values");
            _output.WriteStatus("   pidgeon generate \"ADT^A01\"          # Use in generation");
            if (isTemporary)
            {
                _output.WriteStatus("   pidgeon session save <name>       # Save permanently");
            }
        }

        return 0;
    }
}
