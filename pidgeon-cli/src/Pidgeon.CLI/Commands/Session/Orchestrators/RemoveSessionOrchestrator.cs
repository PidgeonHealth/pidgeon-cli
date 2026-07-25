// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session remove &lt;name&gt;</c>.
/// Deletes a named session; if it was the current session, the
/// current-session pointer is cleared as well.
/// </summary>
public sealed class RemoveSessionOrchestrator
{
    private readonly ILogger<RemoveSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly ILockSessionService _lockSessionService;
    private readonly SessionHelper _sessionHelper;

    public RemoveSessionOrchestrator(
        ILogger<RemoveSessionOrchestrator> logger,
        IConsoleOutput output,
        ILockSessionService lockSessionService,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _lockSessionService = lockSessionService;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var sessionResult = await _lockSessionService.GetSessionAsync(name, cancellationToken);
        if (sessionResult.IsFailure)
        {
            _output.WriteError($"Session '{name}' not found");
            return 1;
        }

        var currentSession = await _sessionHelper.GetCurrentSessionAsync(cancellationToken);
        if (currentSession == name)
        {
            await _sessionHelper.ClearCurrentSessionAsync(cancellationToken);
        }

        var removeResult = await _lockSessionService.RemoveSessionAsync(name, cancellationToken);
        if (removeResult.IsFailure)
        {
            _output.WriteError($"Failed to remove session: {removeResult.Error.Message}");
            return 1;
        }

        _output.WriteStatus($"Removed session: {name}");
        if (currentSession == name)
        {
            _output.WriteStatus("Session was current - cleared session context");
        }

        return 0;
    }
}
