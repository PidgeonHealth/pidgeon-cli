// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;
using Pidgeon.Core.Application.Interfaces.Configuration;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session export &lt;file&gt;</c>.
/// Serializes the named session (current session when name is
/// omitted) to a JSON template that can later be re-imported via
/// <c>pidgeon session import</c>.
/// </summary>
public sealed class ExportSessionOrchestrator
{
    private readonly ILogger<ExportSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly ILockSessionService _lockSessionService;
    private readonly SessionHelper _sessionHelper;

    public ExportSessionOrchestrator(
        ILogger<ExportSessionOrchestrator> logger,
        IConsoleOutput output,
        ILockSessionService lockSessionService,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _lockSessionService = lockSessionService;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(string file, string? sessionName, CancellationToken cancellationToken)
    {
        string targetSession;
        if (string.IsNullOrEmpty(sessionName))
        {
            var current = await _sessionHelper.GetCurrentSessionAsync(cancellationToken);
            if (current == null)
            {
                _output.WriteError("No current session to export");
                _output.WriteStatus("Specify session name: --session <name>");
                return 1;
            }
            targetSession = current;
        }
        else
        {
            targetSession = sessionName;
        }

        var sessionResult = await _lockSessionService.GetSessionAsync(targetSession, cancellationToken);
        if (sessionResult.IsFailure)
        {
            _output.WriteError($"Session '{targetSession}' not found");
            return 1;
        }

        var session = sessionResult.Value;

        var template = new
        {
            name = targetSession,
            description = session.Description ?? $"Exported session from {targetSession}",
            created = session.CreatedAt,
            exported = DateTime.UtcNow,
            values = session.LockedValues.Select(v => new
            {
                field = v.FieldPath,
                value = v.Value,
                reason = v.Reason
            }).ToArray()
        };

        try
        {
            var json = JsonSerializer.Serialize(template, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(file, json, cancellationToken);
            _output.WriteStatus($"Exported session: {targetSession} -> {file}");
            _output.WriteStatus($"Exported {session.LockedValues.Count} field value(s)");
        }
        catch (Exception ex)
        {
            _output.WriteError($"Failed to export session: {ex.Message}");
            return 1;
        }

        return 0;
    }
}
