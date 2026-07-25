// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Pidgeon.CLI.Services;

/// <summary>
/// Owns the one-time "add --share" hint after a successful generate run. The hint is
/// shown at most once per machine (a marker file beside the other per-user state under
/// ~/.pidgeon), and only in an interactive text session: never under --quiet, never in
/// JSON output mode, never when the console is redirected, and never when the user
/// already passed --share. A run that fails never reaches this service.
/// </summary>
public class ShareHintService
{
    public const string HintText = "tip: add --share to get a link teammates can replay and verify offline";

    private const string MarkerFileName = ".share-hint-shown";

    private readonly string _markerPath;
    private readonly Func<bool> _isInteractive;

    public ShareHintService()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pidgeon"),
            static () => !Console.IsOutputRedirected && !Console.IsErrorRedirected)
    {
    }

    public ShareHintService(string stateDirectory, Func<bool> isInteractive)
    {
        _markerPath = Path.Combine(stateDirectory, MarkerFileName);
        _isInteractive = isInteractive;
    }

    /// <summary>
    /// Returns true exactly once per machine when the hint should be printed, and
    /// records the claim. Suppressed runs (quiet, JSON, non-interactive, --share
    /// already passed) never burn the claim, so the hint stays available for the
    /// first run that could actually display it.
    /// </summary>
    public bool TryClaimFirstShareHint(bool sharePassed, bool quiet, string outputFormat)
    {
        if (sharePassed || quiet || outputFormat == "json" || !_isInteractive())
            return false;

        try
        {
            if (File.Exists(_markerPath))
                return false;

            var directory = Path.GetDirectoryName(_markerPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("O"));
            return true;
        }
        catch (Exception)
        {
            // A growth nudge must never fail or noise up a successful generate run;
            // an unwritable state directory just means no hint this time.
            return false;
        }
    }
}
