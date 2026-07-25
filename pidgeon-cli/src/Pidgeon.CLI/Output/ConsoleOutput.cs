// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Pidgeon.CLI.Output;

/// <summary>
/// Console output with status/data separation, quiet mode, and JSON output.
/// Status → stderr (suppressed by --quiet). Data → stdout (always emitted).
/// </summary>
public class ConsoleOutput : IConsoleOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly TextWriter _statusWriter;
    private readonly TextWriter _dataWriter;

    public bool IsQuiet { get; }
    public string OutputFormat { get; }

    public ConsoleOutput(TextWriter statusWriter, TextWriter dataWriter, bool quiet, string outputFormat)
    {
        _statusWriter = statusWriter ?? throw new ArgumentNullException(nameof(statusWriter));
        _dataWriter = dataWriter ?? throw new ArgumentNullException(nameof(dataWriter));
        IsQuiet = quiet;
        OutputFormat = outputFormat ?? "text";
    }

    /// <summary>
    /// Creates a ConsoleOutput with default Console.Error/Console.Out writers.
    /// </summary>
    public static ConsoleOutput Create(bool quiet = false, string outputFormat = "text")
        => new(Console.Error, Console.Out, quiet, outputFormat);

    public void WriteStatus(string message)
    {
        if (!IsQuiet)
            _statusWriter.WriteLine(message);
    }

    public void WriteStatus(string message, ConsoleColor color)
    {
        if (IsQuiet) return;

        var original = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = color;
            _statusWriter.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = original;
        }
    }

    public void WriteData(string data)
    {
        _dataWriter.WriteLine(data);
    }

    public void WriteData(object data)
    {
        if (OutputFormat == "json")
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            _dataWriter.WriteLine(json);
        }
        else
        {
            _dataWriter.WriteLine(data.ToString());
        }
    }

    public void WriteLine(string message) => WriteStatus(message);

    public void WriteLine(string message, ConsoleColor color) => WriteStatus(message, color);

    public void WriteError(string message)
    {
        // Errors are never suppressed
        var original = Console.ForegroundColor;
        try
        {
            Console.ForegroundColor = ConsoleColor.Red;
            _statusWriter.WriteLine($"ERROR: {message}");
        }
        finally
        {
            Console.ForegroundColor = original;
        }
    }

    public void WriteWarning(string message)
    {
        WriteStatus($"WARNING: {message}", ConsoleColor.Yellow);
    }

    public void WriteSuccess(string message)
    {
        WriteStatus($"SUCCESS: {message}", ConsoleColor.Green);
    }
}
