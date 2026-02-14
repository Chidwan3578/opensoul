using Microsoft.Extensions.Logging;
using Velopack;

namespace OpenSoul.Services;

/// <summary>
/// Manages application updates via Velopack.
/// Checks for updates from GitHub Releases, downloads delta packages,
/// and prompts the user to restart when an update is ready.
/// </summary>
public sealed class UpdateService
{
    /// <summary>
    /// URL pattern for GitHub Releases. Replace with actual org/repo.
    /// Velopack uses this to discover release assets.
    /// </summary>
    private const string GITHUB_RELEASES_URL = "https://github.com/NJX-njx/opensoul/releases";

    /// <summary>Check interval during normal operation (6 hours).</summary>
    private static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromHours(6);

    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager _updateManager;
    private CancellationTokenSource? _cts;
    private UpdateInfo? _pendingUpdate;

    /// <summary>Current app version string (e.g. "0.1.0").</summary>
    public string CurrentVersion => _updateManager.CurrentVersion?.ToString() ?? "0.1.0";

    /// <summary>Whether the app is installed (vs. running from dev build).</summary>
    public bool IsInstalled => _updateManager.IsInstalled;

    /// <summary>Whether there is a downloaded update ready to apply.</summary>
    public bool HasPendingUpdate => _pendingUpdate is not null;

    /// <summary>Fired when an update is available and downloaded.</summary>
    public event Action<string>? UpdateReady;

    /// <summary>Fired when the update check status changes.</summary>
    public event Action<UpdateStatus>? StatusChanged;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _updateManager = new UpdateManager(GITHUB_RELEASES_URL);
    }

    /// <summary>
    /// Start the background update check loop.
    /// Checks immediately, then periodically.
    /// </summary>
    public void StartBackgroundChecks()
    {
        if (!_updateManager.IsInstalled)
        {
            _logger.LogInformation("App is not installed via Velopack; skipping update checks");
            return;
        }

        _cts = new CancellationTokenSource();
        _ = BackgroundCheckLoop(_cts.Token);
    }

    /// <summary>Stop the background update check loop.</summary>
    public void StopBackgroundChecks()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>
    /// Check for updates once.
    /// Returns true if an update is available and has been downloaded.
    /// </summary>
    public async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            StatusChanged?.Invoke(UpdateStatus.Checking);
            _logger.LogInformation("Checking for updates...");

            var updateInfo = await _updateManager.CheckForUpdatesAsync();

            if (updateInfo is null)
            {
                _logger.LogInformation("No updates available");
                StatusChanged?.Invoke(UpdateStatus.UpToDate);
                return false;
            }

            var newVersion = updateInfo.TargetFullRelease.Version.ToString();
            _logger.LogInformation("Update available: {Version}", newVersion);
            StatusChanged?.Invoke(UpdateStatus.Downloading);

            // Download the update (Velopack handles delta/full logic automatically)
            await _updateManager.DownloadUpdatesAsync(updateInfo);

            _pendingUpdate = updateInfo;
            _logger.LogInformation("Update {Version} downloaded and ready", newVersion);
            StatusChanged?.Invoke(UpdateStatus.Ready);
            UpdateReady?.Invoke(newVersion);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            StatusChanged?.Invoke(UpdateStatus.Error);
            return false;
        }
    }

    /// <summary>
    /// Apply the pending update and restart the application.
    /// Call this only when the user has confirmed the restart.
    /// </summary>
    public void ApplyUpdateAndRestart()
    {
        if (_pendingUpdate is null)
        {
            _logger.LogWarning("No pending update to apply");
            return;
        }

        try
        {
            _logger.LogInformation("Applying update and restarting...");
            _updateManager.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply update");
        }
    }

    /// <summary>
    /// Apply the pending update when the application exits naturally.
    /// The update will be applied after the process terminates.
    /// </summary>
    public void ApplyUpdateOnExit()
    {
        if (_pendingUpdate is null) return;

        try
        {
            _logger.LogInformation("Queuing update to apply on exit...");
            _updateManager.WaitExitThenApplyUpdates(
                _pendingUpdate.TargetFullRelease,
                silent: true,
                restart: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to queue update for exit");
        }
    }

    /// <summary>Background loop that checks for updates periodically.</summary>
    private async Task BackgroundCheckLoop(CancellationToken ct)
    {
        try
        {
            // Initial check after a short startup delay
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            await CheckForUpdateAsync();

            // Periodic checks
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(CHECK_INTERVAL, ct);
                if (!HasPendingUpdate)
                {
                    await CheckForUpdateAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background update check loop crashed");
        }
    }

    /// <summary>Update check status.</summary>
    public enum UpdateStatus
    {
        /// <summary>Checking for updates...</summary>
        Checking,
        /// <summary>App is up to date.</summary>
        UpToDate,
        /// <summary>Downloading update...</summary>
        Downloading,
        /// <summary>Update downloaded and ready to apply.</summary>
        Ready,
        /// <summary>Update check or download failed.</summary>
        Error,
    }
}
