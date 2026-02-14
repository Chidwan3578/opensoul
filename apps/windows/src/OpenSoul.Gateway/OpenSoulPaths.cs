namespace OpenSoul.Gateway;

/// <summary>
/// Resolves OpenSoul file system paths on Windows.
/// Uses an app-specific state directory to avoid conflicting with global CLI state.
/// </summary>
public static class OpenSoulPaths
{
    /// <summary>
    /// State directory: %OPENSOUL_STATE_DIR% or %LOCALAPPDATA%\OpenSoul\state\
    /// </summary>
    public static string StateDir =>
        Environment.GetEnvironmentVariable("OPENSOUL_STATE_DIR")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenSoul",
            "state");

    /// <summary>
    /// Config file: %OPENSOUL_CONFIG_PATH% or {StateDir}\opensoul.json
    /// </summary>
    public static string ConfigPath =>
        Environment.GetEnvironmentVariable("OPENSOUL_CONFIG_PATH")
        ?? Path.Combine(StateDir, "opensoul.json");

    /// <summary>Workspace directory: {StateDir}\workspace\</summary>
    public static string WorkspaceDir => Path.Combine(StateDir, "workspace");

    /// <summary>Logs directory: {StateDir}\logs\</summary>
    public static string LogsDir => Path.Combine(StateDir, "logs");

    /// <summary>Gateway PID file: {StateDir}\gateway.pid</summary>
    public static string GatewayPidFile => Path.Combine(StateDir, "gateway.pid");

    /// <summary>Gateway port file: {StateDir}\gateway.port</summary>
    public static string GatewayPortFile => Path.Combine(StateDir, "gateway.port");

    /// <summary>Gateway token file: {StateDir}\gateway.token</summary>
    public static string GatewayTokenFile => Path.Combine(StateDir, "gateway.token");

    /// <summary>Settings file (Windows-specific, stored in AppData)</summary>
    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenSoul", "settings.json");

    /// <summary>
    /// Ensure all required directories exist.
    /// </summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(StateDir);
        Directory.CreateDirectory(WorkspaceDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
    }

    /// <summary>
    /// Read the gateway port from the state file.
    /// Returns 0 if the file doesn't exist or is invalid.
    /// </summary>
    public static int ReadGatewayPort()
    {
        try
        {
            if (!File.Exists(GatewayPortFile))
                return 0;

            var text = File.ReadAllText(GatewayPortFile).Trim();
            return int.TryParse(text, out var port) ? port : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Read the gateway token from the state file.
    /// Returns null if the file doesn't exist or is empty.
    /// </summary>
    public static string? ReadGatewayToken()
    {
        try
        {
            if (!File.Exists(GatewayTokenFile))
                return null;

            var text = File.ReadAllText(GatewayTokenFile).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
