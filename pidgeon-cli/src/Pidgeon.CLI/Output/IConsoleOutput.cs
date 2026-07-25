// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.CLI.Output;

/// <summary>
/// Interface for console output with status/data separation.
/// Status output goes to stderr (suppressed by --quiet).
/// Data output goes to stdout (always emitted, JSON-serialized with --output json).
/// </summary>
public interface IConsoleOutput
{
    /// <summary>
    /// Writes a status line to stderr. Suppressed by --quiet.
    /// </summary>
    void WriteStatus(string message);

    /// <summary>
    /// Writes a status line to stderr with color. Suppressed by --quiet.
    /// </summary>
    void WriteStatus(string message, ConsoleColor color);

    /// <summary>
    /// Writes raw data to stdout. Never suppressed.
    /// </summary>
    void WriteData(string data);

    /// <summary>
    /// Writes an object to stdout. JSON-serialized when --output json.
    /// </summary>
    void WriteData(object data);

    /// <summary>
    /// Writes a line to stderr. Suppressed by --quiet. Alias for WriteStatus.
    /// </summary>
    void WriteLine(string message);

    /// <summary>
    /// Writes a colored line to stderr. Suppressed by --quiet.
    /// </summary>
    void WriteLine(string message, ConsoleColor color);

    /// <summary>
    /// Writes an error to stderr. Never suppressed, even with --quiet.
    /// </summary>
    void WriteError(string message);

    /// <summary>
    /// Writes a warning to stderr. Suppressed by --quiet.
    /// </summary>
    void WriteWarning(string message);

    /// <summary>
    /// Writes a success message to stderr. Suppressed by --quiet.
    /// </summary>
    void WriteSuccess(string message);

    /// <summary>
    /// Whether --quiet mode is active.
    /// </summary>
    bool IsQuiet { get; }

    /// <summary>
    /// Output format: "text" or "json".
    /// </summary>
    string OutputFormat { get; }
}
