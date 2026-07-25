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
/// Business logic for <c>pidgeon session import &lt;file&gt;</c>.
/// Loads a previously exported JSON session template, creates the
/// named session (deriving the name from <c>--name</c>, the template
/// payload, or the file name), and applies each field value through
/// <see cref="ILockSessionService"/>.
/// </summary>
public sealed class ImportSessionOrchestrator
{
    private readonly ILogger<ImportSessionOrchestrator> _logger;
    private readonly IConsoleOutput _output;
    private readonly ILockSessionService _lockSessionService;
    private readonly SessionHelper _sessionHelper;

    public ImportSessionOrchestrator(
        ILogger<ImportSessionOrchestrator> logger,
        IConsoleOutput output,
        ILockSessionService lockSessionService,
        SessionHelper sessionHelper)
    {
        _logger = logger;
        _output = output;
        _lockSessionService = lockSessionService;
        _sessionHelper = sessionHelper;
    }

    public async Task<int> ExecuteAsync(string file, string? name, CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            _output.WriteError($"File not found: {file}");
            return 1;
        }

        try
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            var template = JsonSerializer.Deserialize<JsonElement>(json);

            var sessionName = name;
            if (string.IsNullOrEmpty(sessionName))
            {
                if (template.TryGetProperty("name", out var nameProperty))
                {
                    sessionName = nameProperty.GetString();
                }
                else
                {
                    sessionName = Path.GetFileNameWithoutExtension(file);
                }
            }

            if (string.IsNullOrEmpty(sessionName))
            {
                _output.WriteError("Unable to determine session name");
                _output.WriteStatus("Specify name: --name <session_name>");
                return 1;
            }

            var description = template.TryGetProperty("description", out var descProperty) ?
                             descProperty.GetString() :
                             "Imported session template";

            var newSessionName = await _sessionHelper.CreateNamedSessionAsync(sessionName, description, cancellationToken);

            if (template.TryGetProperty("values", out var valuesProperty) && valuesProperty.ValueKind == JsonValueKind.Array)
            {
                var importedCount = 0;
                foreach (var valueElement in valuesProperty.EnumerateArray())
                {
                    if (valueElement.TryGetProperty("field", out var fieldProperty) &&
                        valueElement.TryGetProperty("value", out var valueProperty))
                    {
                        var fieldPath = fieldProperty.GetString();
                        var value = valueProperty.GetString();
                        var reason = valueElement.TryGetProperty("reason", out var reasonProperty) ?
                                   reasonProperty.GetString() :
                                   "Imported from template";

                        if (!string.IsNullOrEmpty(fieldPath) && !string.IsNullOrEmpty(value))
                        {
                            var setResult = await _lockSessionService.SetValueAsync(
                                newSessionName, fieldPath, value, reason, cancellationToken);

                            if (setResult.IsSuccess)
                            {
                                importedCount++;
                            }
                        }
                    }
                }

                _output.WriteStatus($"Imported session: {newSessionName}");
                _output.WriteStatus($"Imported {importedCount} field value(s) from {file}");
                _output.WriteStatus($"Current session: {newSessionName}");
            }
            else
            {
                _output.WriteStatus($"Created session: {newSessionName} (no values in template)");
            }
        }
        catch (Exception ex)
        {
            _output.WriteError($"Failed to import session: {ex.Message}");
            return 1;
        }

        return 0;
    }
}
