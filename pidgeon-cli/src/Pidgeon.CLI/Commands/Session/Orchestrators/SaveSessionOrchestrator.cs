// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session save &lt;name&gt;</c>.
/// Promotes the current temporary session to a permanent session
/// under a user-supplied name.
/// </summary>
public sealed class SaveSessionOrchestrator
{
    private readonly ILogger<SaveSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly SessionHelper _sessionHelper;

    public SaveSessionOrchestrator(
        ILogger<SaveSessionOrchestrator> logger,
        IConsoleOutput output,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(string name, CancellationToken cancellationToken)
    {
        var currentSession = await _sessionHelper.GetCurrentSessionAsync(cancellationToken);
        if (currentSession == null)
        {
            _output.WriteError("No active session to save");
            _output.WriteStatus("Create a session first:");
            _output.WriteStatus("   pidgeon set patient.mrn \"TEST123\"");
            return 1;
        }

        var isTemporary = await _sessionHelper.IsTemporarySessionAsync(currentSession, cancellationToken);
        if (!isTemporary)
        {
            _output.WriteError($"Session '{currentSession}' is already permanent");
            _output.WriteStatus("Use 'pidgeon session create <name>' to create a new session");
            return 1;
        }

        var result = await _sessionHelper.SaveTemporarySessionAsync(currentSession, name, cancellationToken);
        if (result.IsFailure)
        {
            _output.WriteError($"Failed to save session: {result.Error.Message}");
            return 1;
        }

        _output.WriteStatus($"Saved session: {currentSession} -> {name}");
        _output.WriteStatus("Permanent sessions persist until manually deleted");
        return 0;
    }
}
