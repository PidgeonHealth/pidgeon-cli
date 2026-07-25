// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session show [name]</c>.
/// Prints the full field-value contents of a session (current session
/// when name is omitted), including field path, locked value, reason,
/// and lock timestamp.
/// </summary>
public sealed class ShowSessionOrchestrator
{
    private readonly ILogger<ShowSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly ILockSessionService _lockSessionService;
    private readonly SessionHelper _sessionHelper;

    public ShowSessionOrchestrator(
        ILogger<ShowSessionOrchestrator> logger,
        IConsoleOutput output,
        ILockSessionService lockSessionService,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _lockSessionService = lockSessionService;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(string? name, CancellationToken cancellationToken)
    {
        string sessionName;
        if (string.IsNullOrEmpty(name))
        {
            var current = await _sessionHelper.GetCurrentSessionAsync(cancellationToken);
            if (current == null)
            {
                _output.WriteError("No current session to show");
                _output.WriteStatus("Specify a session name or create one first");
                return 1;
            }
            sessionName = current;
        }
        else
        {
            sessionName = name;
        }

        var sessionResult = await _lockSessionService.GetSessionAsync(sessionName, cancellationToken);
        if (sessionResult.IsFailure)
        {
            _output.WriteError($"Session '{sessionName}' not found");
            return 1;
        }

        var session = sessionResult.Value;
        var isTemporary = await _sessionHelper.IsTemporarySessionAsync(sessionName, cancellationToken);

        _output.WriteStatus($"Session: {sessionName}");
        _output.WriteStatus($"   Type: {(isTemporary ? "Temporary (expires in 24h unless saved)" : "Permanent")}");
        _output.WriteStatus($"   Created: {session.CreatedAt:yyyy-MM-dd HH:mm:ss}");
        if (!string.IsNullOrEmpty(session.Description))
        {
            _output.WriteStatus($"   Description: {session.Description}");
        }
        _output.WriteStatus("");

        if (!session.LockedValues.Any())
        {
            _output.WriteStatus("No values set yet.");
            _output.WriteStatus("Set values: pidgeon set <field> <value>");
            return 0;
        }

        foreach (var value in session.LockedValues.OrderBy(v => v.FieldPath))
        {
            _output.WriteStatus($"  {value.FieldPath} = \"{value.Value}\"");
            if (!string.IsNullOrEmpty(value.Reason))
            {
                _output.WriteStatus($"   Reason: {value.Reason}");
            }
            _output.WriteStatus($"   Set: {value.LockedAt:yyyy-MM-dd HH:mm:ss}");
            _output.WriteStatus("");
        }

        _output.WriteStatus($"Total: {session.LockedValues.Count} value(s) set");

        return 0;
    }
}
