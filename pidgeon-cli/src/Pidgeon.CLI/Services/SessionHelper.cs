// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pidgeon.Core;
using Pidgeon.Core.Application.Interfaces.Configuration;
using Pidgeon.Core.Domain.Configuration.Entities;
using System.Text.Json;

namespace Pidgeon.CLI.Services;

/// <summary>
/// Helper service for managing session state and context for the CLI.
/// Implements smart session management with auto-creation and persistence tracking.
/// </summary>
public class SessionHelper
{
    private readonly ILockSessionService _lockSessionService;
    private readonly ILockStorageProvider _lockStorageProvider;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<SessionHelper> _logger;

    private const string CurrentSessionFileName = "current_session.json";
    private const int TemporarySessionTtlHours = 24;

    // Friendly name generation components
    private static readonly string[] Adjectives =
    {
        "caring", "happy", "brave", "calm", "bright", "swift", "wise", "kind",
        "eager", "noble", "gentle", "quick", "smart", "warm", "cool", "bold"
    };

    private static readonly string[] Animals =
    {
        "crane", "horse", "eagle", "dolphin", "panda", "tiger", "falcon", "owl",
        "wolf", "bear", "fox", "lion", "hawk", "deer", "otter", "lynx"
    };

    public SessionHelper(
        ILockSessionService lockSessionService,
        ILockStorageProvider lockStorageProvider,
        IHostEnvironment environment,
        ILogger<SessionHelper> logger)
    {
        _lockSessionService = lockSessionService;
        _lockStorageProvider = lockStorageProvider;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current session name from persistent storage.
    /// </summary>
    public async Task<string?> GetCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionStatePath = GetSessionStatePath();
            if (!File.Exists(sessionStatePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(sessionStatePath, cancellationToken);
            var sessionState = JsonSerializer.Deserialize<SessionState>(json);

            if (sessionState?.CurrentSession == null)
            {
                return null;
            }

            // Check if session still exists
            var sessionResult = await _lockSessionService.GetSessionAsync(sessionState.CurrentSession, cancellationToken);
            if (sessionResult.IsFailure)
            {
                // Session no longer exists, clear state
                await ClearCurrentSessionAsync(cancellationToken);
                return null;
            }

            // Check if temporary session has expired
            if (sessionState.IsTemporary && sessionState.ExpiresAt.HasValue && sessionState.ExpiresAt.Value <= DateTime.UtcNow)
            {
                _logger.LogInformation("Temporary session {SessionName} has expired", sessionState.CurrentSession);
                await _lockSessionService.RemoveSessionAsync(sessionState.CurrentSession, cancellationToken);
                await ClearCurrentSessionAsync(cancellationToken);
                return null;
            }

            return sessionState.CurrentSession;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get current session");
            return null;
        }
    }

    /// <summary>
    /// Sets the current session name in persistent storage.
    /// </summary>
    public async Task SetCurrentSessionAsync(string sessionName, bool isTemporary = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionState = new SessionState
            {
                CurrentSession = sessionName,
                LastActivity = DateTime.UtcNow,
                IsTemporary = isTemporary,
                ExpiresAt = isTemporary ? DateTime.UtcNow.AddHours(TemporarySessionTtlHours) : null
            };

            var sessionStatePath = GetSessionStatePath();
            var json = JsonSerializer.Serialize(sessionState, new JsonSerializerOptions { WriteIndented = true });

            var directory = Path.GetDirectoryName(sessionStatePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(sessionStatePath, json, cancellationToken);
            _logger.LogInformation("Set current session to {SessionName}", sessionName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set current session");
        }
    }

    /// <summary>
    /// Clears the current session context.
    /// </summary>
    public async Task ClearCurrentSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionStatePath = GetSessionStatePath();
            if (File.Exists(sessionStatePath))
            {
                File.Delete(sessionStatePath);
                _logger.LogInformation("Cleared current session");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear current session");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Gets the current session or creates a new temporary session if none exists.
    /// </summary>
    public async Task<string> GetOrCreateSessionAsync(CancellationToken cancellationToken = default)
    {
        var currentSession = await GetCurrentSessionAsync(cancellationToken);
        if (currentSession != null)
        {
            return currentSession;
        }

        return await CreateTemporarySessionAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a new temporary session with a friendly name.
    /// </summary>
    public async Task<string> CreateTemporarySessionAsync(CancellationToken cancellationToken = default)
    {
        var sessionName = GenerateFriendlyName();

        var result = await _lockSessionService.CreateSessionAsync(
            sessionName,
            LockScope.Global,
            "Temporary session (auto-created)",
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to create temporary session: {result.Error.Message}");
        }

        await SetCurrentSessionAsync(sessionName, isTemporary: true, cancellationToken);
        return sessionName;
    }

    /// <summary>
    /// Creates a new named session and sets it as current.
    /// </summary>
    public async Task<string> CreateNamedSessionAsync(string name, string? description = null, CancellationToken cancellationToken = default)
    {
        var result = await _lockSessionService.CreateSessionAsync(
            name,
            LockScope.Global,
            description,
            cancellationToken);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to create session: {result.Error.Message}");
        }

        await SetCurrentSessionAsync(name, isTemporary: false, cancellationToken);
        return name;
    }

    /// <summary>
    /// Saves a temporary session with a permanent name.
    /// </summary>
    public async Task<Result> SaveTemporarySessionAsync(string currentName, string newName, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the current session
            var sessionResult = await _lockSessionService.GetSessionAsync(currentName, cancellationToken);
            if (sessionResult.IsFailure)
            {
                return Result.Failure(sessionResult.Error);
            }

            // Create new permanent session with same values
            var createResult = await _lockSessionService.CreateSessionAsync(
                newName,
                sessionResult.Value.Scope,
                sessionResult.Value.Description ?? "Saved from temporary session",
                cancellationToken);

            if (createResult.IsFailure)
            {
                return Result.Failure(createResult.Error);
            }

            // Copy all locked values
            foreach (var lockedValue in sessionResult.Value.LockedValues)
            {
                await _lockSessionService.SetValueAsync(
                    newName,
                    lockedValue.FieldPath,
                    lockedValue.Value,
                    lockedValue.Reason,
                    cancellationToken);
            }

            // Remove old temporary session
            await _lockSessionService.RemoveSessionAsync(currentName, cancellationToken);

            // Set new session as current
            await SetCurrentSessionAsync(newName, isTemporary: false, cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save temporary session");
            return Result.Failure(Error.Create("INTERNAL_ERROR", $"Failed to save session: {ex.Message}"));
        }
    }

    /// <summary>
    /// Checks if a session is temporary based on its metadata.
    /// </summary>
    public async Task<bool> IsTemporarySessionAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionStatePath = GetSessionStatePath();
            if (!File.Exists(sessionStatePath))
            {
                return false;
            }

            var json = await File.ReadAllTextAsync(sessionStatePath, cancellationToken);
            var sessionState = JsonSerializer.Deserialize<SessionState>(json);

            return sessionState?.CurrentSession == sessionName && sessionState.IsTemporary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if session is temporary");
            return false;
        }
    }

    /// <summary>
    /// Checks if this is the first value being set in a session.
    /// </summary>
    public async Task<bool> IsFirstValueInSessionAsync(string sessionName, CancellationToken cancellationToken = default)
    {
        var sessionResult = await _lockSessionService.GetSessionAsync(sessionName, cancellationToken);
        if (sessionResult.IsFailure)
        {
            return true;
        }

        return sessionResult.Value.LockedValues.Count == 1;
    }

    /// <summary>
    /// Cleans up expired temporary sessions.
    /// </summary>
    public async Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        return (await _lockSessionService.CleanupExpiredSessionsAsync(cancellationToken)).GetValueOrDefault(0);
    }

    /// <summary>
    /// Generates a friendly session name using adjective + animal + date.
    /// </summary>
    private string GenerateFriendlyName()
    {
        var random = new Random();
        var adjective = Adjectives[random.Next(Adjectives.Length)];
        var animal = Animals[random.Next(Animals.Length)];
        var year = DateTime.UtcNow.Year;

        return $"{adjective}_{animal}_{year}";
    }

    /// <summary>
    /// Gets the path to the session state file.
    /// </summary>
    private string GetSessionStatePath()
    {
        var configuredHome = Environment.GetEnvironmentVariable("PIDGEON_HOME");
        var pidgeonDir = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".pidgeon")
            : Path.GetFullPath(configuredHome);

        return Path.Combine(pidgeonDir, CurrentSessionFileName);
    }

    /// <summary>
    /// Internal class for persisting session state.
    /// </summary>
    private class SessionState
    {
        public string? CurrentSession { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsTemporary { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
