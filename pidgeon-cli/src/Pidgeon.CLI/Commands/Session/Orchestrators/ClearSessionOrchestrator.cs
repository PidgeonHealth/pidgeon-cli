// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session clear</c>.
/// Clears the current-session pointer without removing any stored
/// sessions — the next <c>pidgeon set</c> will create a new
/// temporary session.
/// </summary>
public sealed class ClearSessionOrchestrator
{
    private readonly ILogger<ClearSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly SessionHelper _sessionHelper;

    public ClearSessionOrchestrator(
        ILogger<ClearSessionOrchestrator> logger,
        IConsoleOutput output,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var currentSession = await _sessionHelper.GetCurrentSessionAsync(cancellationToken);
        if (currentSession == null)
        {
            _output.WriteError("No active session to clear");
            return 0;
        }

        await _sessionHelper.ClearCurrentSessionAsync(cancellationToken);
        _output.WriteStatus("Cleared session context");
        _output.WriteStatus("Next 'set' command will create a new session");

        return 0;
    }
}
