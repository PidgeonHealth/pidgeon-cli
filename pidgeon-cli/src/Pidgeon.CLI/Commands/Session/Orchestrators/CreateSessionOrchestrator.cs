// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Output;
using Pidgeon.CLI.Services;

namespace Pidgeon.CLI.Commands.Session.Orchestrators;

/// <summary>
/// Business logic for <c>pidgeon session create &lt;name&gt;</c>.
/// Creates a new named session and switches to it.
/// </summary>
public sealed class CreateSessionOrchestrator
{
    private readonly ILogger<CreateSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly SessionHelper _sessionHelper;

    public CreateSessionOrchestrator(
        ILogger<CreateSessionOrchestrator> logger,
        IConsoleOutput output,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(string name, string? description, CancellationToken cancellationToken)
    {
        try
        {
            var sessionName = await _sessionHelper.CreateNamedSessionAsync(name, description, cancellationToken);
            _output.WriteStatus($"Created and switched to session: {sessionName}");
            _output.WriteStatus("Set values with: pidgeon set <field> <value>");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            _output.WriteError(ex.Message);
            _output.WriteStatus("Use a different name or remove the existing session first");
            return 1;
        }
    }
}
