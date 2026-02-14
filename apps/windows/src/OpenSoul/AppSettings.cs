namespace OpenSoul;

/// <summary>
/// Application settings model persisted to %APPDATA%\OpenSoul\settings.json.
/// Extended from v0.1 to support theme, window behavior, and bridge configuration.
/// </summary>
public sealed class AppSettings
{
    // --- Connection ---

    /// <summary>Connection mode: "Local" or "Remote".</summary>
    public string ConnectionMode { get; set; } = "Local";

    /// <summary>Remote gateway WebSocket URL.</summary>
    public string RemoteUrl { get; set; } = "ws://127.0.0.1:3000";

    /// <summary>Active session key for chat.</summary>
    public string SessionKey { get; set; } = "main";

    /// <summary>Whether to auto-connect on app launch.</summary>
    public bool AutoConnectOnLaunch { get; set; } = true;

    /// <summary>Maximum chat history messages to load.</summary>
    public int HistoryLimit { get; set; } = 120;

    // --- Appearance ---

    /// <summary>Theme mode: "system", "light", or "dark".</summary>
    public string Theme { get; set; } = "system";

    /// <summary>Whether to enable Mica/Acrylic backdrop on Windows 11.</summary>
    public bool EnableBackdropEffect { get; set; } = false;

    // --- Window behavior ---

    /// <summary>Whether closing the window minimizes to tray instead of quitting.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Whether to show the app in the taskbar (vs tray-only).</summary>
    public bool ShowInTaskbar { get; set; } = true;

    // --- Advanced ---

    /// <summary>Custom Node.js binary path (null = auto-detect).</summary>
    public string? NodePath { get; set; }

    /// <summary>Custom gateway binary path (null = auto-detect).</summary>
    public string? GatewayPath { get; set; }

    /// <summary>Whether debug features are enabled.</summary>
    public bool DebugMode { get; set; } = false;
}
