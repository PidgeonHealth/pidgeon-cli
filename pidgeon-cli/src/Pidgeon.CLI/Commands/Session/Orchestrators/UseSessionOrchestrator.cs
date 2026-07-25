// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session use &lt;name&gt;</c>.
/// Switches the current context to an existing permanent session.
/// </summary>
public sealed class UseSessionOrchestrator
{
    private readonly ILogger<UseSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly ILockSessionService _lockSessionService;
    private readonly SessionHelper _sessionHelper;

    public UseSessionOrchestrator(
        ILogger<UseSessionOrchestrator> logger,
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
            _output.WriteStatus("List available sessions: pidgeon session list");
            return 1;
        }

        await _sessionHelper.SetCurrentSessionAsync(name, isTemporary: false, cancellationToken);

        var session = sessionResult.Value;
        _output.WriteStatus($"Current session: {name} ({session.LockedValues.Count} fields set)");
        _output.WriteStatus("Use 'pidgeon set --list' to see all field values");

        return 0;
    }
}
