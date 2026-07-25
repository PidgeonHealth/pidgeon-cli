// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Logging;
using Pidgeon.CLI.Commands.Session.Orchestrators;
using Pidgeon.CLI.Output;
using System.CommandLine;

namespace Pidgeon.CLI.Commands;

/// <summary>
/// CLI command for comprehensive session management. Provides
/// explicit control over session lifecycle, import/export, and
/// status.
///
/// Thin parsing + routing shell over one orchestrator per subcommand.
/// Each session verb (status/save/create/use/list/show/clear/remove/export/import)
/// takes distinct arguments and owns a unique output shape, so
/// consolidating into fewer orchestrators would only couple unrelated
/// logic. No <c>SessionStore</c> helper is extracted because session
/// file I/O lives behind <see cref="CLI.Services.SessionHelper"/>.
/// No shared renderer is extracted because the session <c>list</c>
/// and <c>show</c> outputs are distinct prose, not a shared table.
/// </summary>
public class SessionCommand : CommandBuilderBase
{
    private readonly StatusSessionOrchestrator _statusOrchestrator;
    private readonly SaveSessionOrchestrator _saveOrchestrator;
    private readonly CreateSessionOrchestrator _createOrchestrator;
    private readonly UseSessionOrchestrator _useOrchestrator;
    private readonly ListSessionsOrchestrator _listOrchestrator;
    private readonly ShowSessionOrchestrator _showOrchestrator;
    private readonly ClearSessionOrchestrator _clearOrchestrator;
    private readonly RemoveSessionOrchestrator _removeOrchestrator;
    private readonly ExportSessionOrchestrator _exportOrchestrator;
    private readonly ImportSessionOrchestrator _importOrchestrator;

    public SessionCommand(
        ILogger<SessionCommand> logger,
        IConsoleOutput output,
        StatusSessionOrchestrator statusOrchestrator,
        SaveSessionOrchestrator saveOrchestrator,
        CreateSessionOrchestrator createOrchestrator,
        UseSessionOrchestrator useOrchestrator,
        ListSessionsOrchestrator listOrchestrator,
        ShowSessionOrchestrator showOrchestrator,
        ClearSessionOrchestrator clearOrchestrator,
        RemoveSessionOrchestrator removeOrchestrator,
        ExportSessionOrchestrator exportOrchestrator,
        ImportSessionOrchestrator importOrchestrator)
        : base(logger, output)
    {
        _statusOrchestrator = statusOrchestrator;
        _saveOrchestrator = saveOrchestrator;
        _createOrchestrator = createOrchestrator;
        _useOrchestrator = useOrchestrator;
        _listOrchestrator = listOrchestrator;
        _showOrchestrator = showOrchestrator;
        _clearOrchestrator = clearOrchestrator;
        _removeOrchestrator = removeOrchestrator;
        _exportOrchestrator = exportOrchestrator;
        _importOrchestrator = importOrchestrator;
    }

    public override Command CreateCommand()
    {
        var command = new Command("session", "Manage sessions for maintaining field values across commands");

        command.Add(BuildStatusCommand());
        command.Add(BuildSaveCommand());
        command.Add(BuildCreateCommand());
        command.Add(BuildUseCommand());
        command.Add(BuildListCommand());
        command.Add(BuildShowCommand());
        command.Add(BuildClearCommand());
        command.Add(BuildRemoveCommand());
        command.Add(BuildExportCommand());
        command.Add(BuildImportCommand());

        SetCommandAction(command, async (parseResult, cancellationToken) =>
        {
            return await _statusOrchestrator.ExecuteAsync(cancellationToken);
        });

        return command;
    }

    private Command BuildStatusCommand()
    {
        var command = new Command("status", "Show current session status");
        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _statusOrchestrator.ExecuteAsync(cancellationToken));
        return command;
    }

    private Command BuildSaveCommand()
    {
        var command = new Command("save", "Save temporary session with a permanent name");
        var nameArg = new Argument<string>("name") { Description = "Name for the permanent session" };
        command.Add(nameArg);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _saveOrchestrator.ExecuteAsync(parseResult.GetValue(nameArg)!, cancellationToken));
        return command;
    }

    private Command BuildCreateCommand()
    {
        var command = new Command("create", "Create new named session and switch to it");
        var nameArg = new Argument<string>("name") { Description = "Name for the new session" };
        var descriptionOption = CreateNullableOption("--description", "-d", "Description for the session");
        command.Add(nameArg);
        command.Add(descriptionOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _createOrchestrator.ExecuteAsync(
                parseResult.GetValue(nameArg)!,
                parseResult.GetValue(descriptionOption),
                cancellationToken));
        return command;
    }

    private Command BuildUseCommand()
    {
        var command = new Command("use", "Switch to existing session");
        var nameArg = new Argument<string>("name") { Description = "Name of the session to switch to" };
        command.Add(nameArg);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _useOrchestrator.ExecuteAsync(parseResult.GetValue(nameArg)!, cancellationToken));
        return command;
    }

    private Command BuildListCommand()
    {
        var command = new Command("list", "List all sessions with status");
        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _listOrchestrator.ExecuteAsync(cancellationToken));
        return command;
    }

    private Command BuildShowCommand()
    {
        var command = new Command("show", "Show session details");
        var nameArg = new Argument<string?>("name")
        {
            Description = "Name of session to show (current session if not specified)",
            Arity = ArgumentArity.ZeroOrOne
        };
        command.Add(nameArg);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _showOrchestrator.ExecuteAsync(parseResult.GetValue(nameArg), cancellationToken));
        return command;
    }

    private Command BuildClearCommand()
    {
        var command = new Command("clear", "Clear current session");
        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _clearOrchestrator.ExecuteAsync(cancellationToken));
        return command;
    }

    private Command BuildRemoveCommand()
    {
        var command = new Command("remove", "Remove specific session");
        var nameArg = new Argument<string>("name") { Description = "Name of the session to remove" };
        command.Add(nameArg);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _removeOrchestrator.ExecuteAsync(parseResult.GetValue(nameArg)!, cancellationToken));
        return command;
    }

    private Command BuildExportCommand()
    {
        var command = new Command("export", "Export session as template");
        var fileArg = new Argument<string>("file") { Description = "Output file path (JSON format)" };
        var sessionOption = CreateNullableOption("--session", "-s", "Session to export (current if not specified)");
        command.Add(fileArg);
        command.Add(sessionOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _exportOrchestrator.ExecuteAsync(
                parseResult.GetValue(fileArg)!,
                parseResult.GetValue(sessionOption),
                cancellationToken));
        return command;
    }

    private Command BuildImportCommand()
    {
        var command = new Command("import", "Import session template");
        var fileArg = new Argument<string>("file") { Description = "Template file path (JSON format)" };
        var nameOption = CreateNullableOption("--name", "-n", "Name for imported session (derived from file if not specified)");
        command.Add(fileArg);
        command.Add(nameOption);

        SetCommandAction(command, async (parseResult, cancellationToken) =>
            await _importOrchestrator.ExecuteAsync(
                parseResult.GetValue(fileArg)!,
                parseResult.GetValue(nameOption),
                cancellationToken));
        return command;
    }
}
