// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session list</c>.
/// Renders every known session (name, field count, temporary vs
/// permanent flag, current marker, description).
/// </summary>
public sealed class ListSessionsOrchestrator
{
    private readonly ILogger<ListSessionsOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly ILockSessionService _lockSessionService;
    private readonly SessionHelper _sessionHelper;

    public ListSessionsOrchestrator(
        ILogger<ListSessionsOrchestrator> logger,
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
        var sessions = await _lockSessionService.ListSessionsAsync(cancellationToken);
        if (sessions.IsFailure)
        {
            _output.WriteError($"Failed to list sessions: {sessions.Error.Message}");
            return 1;
        }

        var currentSession = await _sessionHelper.GetCurrentSessionAsync(cancellationToken);

        if (!sessions.Value.Any())
        {
            _output.WriteStatus("No sessions found.");
            _output.WriteStatus("Create a session:");
            _output.WriteStatus("   pidgeon set patient.mrn \"TEST123\"     # Auto-creates temporary session");
            _output.WriteStatus("   pidgeon session create my_scenario     # Creates named session");
            return 0;
        }

        _output.WriteStatus("Available Sessions:");
        _output.WriteStatus("");

        foreach (var session in sessions.Value.OrderBy(s => s.Name))
        {
            var isTemporary = await _sessionHelper.IsTemporarySessionAsync(session.Name, cancellationToken);
            var isCurrent = session.Name == currentSession;
            var prefix = isCurrent ? "-> " : "   ";
            var suffix = isCurrent ? " [current]" : "";
            var type = isTemporary ? "temporary" : "permanent";

            _output.WriteStatus($"{prefix}{session.Name} ({session.LockedValues.Count} fields, {type}){suffix}");
            if (!string.IsNullOrEmpty(session.Description))
            {
                _output.WriteStatus($"    {session.Description}");
            }
        }

        _output.WriteStatus("");
        _output.WriteStatus("Commands:");
        _output.WriteStatus("   pidgeon session use <name>        # Switch to session");
        _output.WriteStatus("   pidgeon session show <name>       # Show session details");

        return 0;
    }
}
