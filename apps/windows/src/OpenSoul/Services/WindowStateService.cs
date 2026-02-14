using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace OpenSoul.Services;

/// <summary>
/// Persists and restores window position, size, and maximized state across sessions.
/// State is saved to %APPDATA%\OpenSoul\window-state.json.
/// </summary>
public sealed class WindowStateService
{
    private readonly ILogger<WindowStateService> _logger;
    private readonly string _statePath;

    public WindowStateService(ILogger<WindowStateService> logger)
    {
        _logger = logger;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "OpenSoul");
        Directory.CreateDirectory(dir);
        _statePath = Path.Combine(dir, "window-state.json");
    }

    /// <summary>Save current window state to disk.</summary>
    public async Task SaveAsync(Window window)
    {
        try
        {
            var state = new WindowState
            {
                Left = window.Left,
                Top = window.Top,
                Width = window.Width,
                Height = window.Height,
                IsMaximized = window.WindowState == System.Windows.WindowState.Maximized,
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_statePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save window state");
        }
    }

    /// <summary>Restore saved window state. Returns true if state was applied.</summary>
    public bool Restore(Window window)
    {
        try
        {
            if (!File.Exists(_statePath))
                return false;

            var json = File.ReadAllText(_statePath);
            var state = JsonSerializer.Deserialize<WindowState>(json);
            if (state is null)
                return false;

            // Validate the saved position is within virtual screen bounds
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualWidth = SystemParameters.VirtualScreenWidth;
            var virtualHeight = SystemParameters.VirtualScreenHeight;

            // Ensure window is at least partially visible on screen
            if (state.Left + state.Width > virtualLeft &&
                state.Left < virtualLeft + virtualWidth &&
                state.Top + state.Height > virtualTop &&
                state.Top < virtualTop + virtualHeight)
            {
                window.Left = state.Left;
                window.Top = state.Top;
                window.Width = Math.Max(state.Width, window.MinWidth);
                window.Height = Math.Max(state.Height, window.MinHeight);
            }

            if (state.IsMaximized)
            {
                window.WindowState = System.Windows.WindowState.Maximized;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restore window state");
            return false;
        }
    }

    /// <summary>Serializable window state model.</summary>
    private sealed class WindowState
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; } = 1360;
        public double Height { get; set; } = 860;
        public bool IsMaximized { get; set; }
    }
}
