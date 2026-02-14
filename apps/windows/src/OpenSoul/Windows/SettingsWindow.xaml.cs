using System.Windows;
using System.Windows.Controls;
using OpenSoul.Services;

namespace OpenSoul.Windows;

/// <summary>
/// Native WPF settings window for Windows-specific configuration.
/// Organized into General, Connection, Appearance, Advanced, and About panels.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _settingsStore;
    private readonly ThemeService _themeService;

    /// <summary>Fired when settings have been saved.</summary>
    public event Action<AppSettings>? SettingsSaved;

    public SettingsWindow(AppSettings settings, AppSettingsStore store, ThemeService themeService)
    {
        InitializeComponent();

        _settings = settings;
        _settingsStore = store;
        _themeService = themeService;

        LoadSettingsToUi();
    }

    /// <summary>Populate UI controls from current settings.</summary>
    private void LoadSettingsToUi()
    {
        // General
        CloseToTrayCheck.IsChecked = _settings.CloseToTray;
        ShowInTaskbarCheck.IsChecked = _settings.ShowInTaskbar;
        SessionKeyInput.Text = _settings.SessionKey;
        HistoryLimitInput.Text = _settings.HistoryLimit.ToString();

        // Connection
        SelectComboByTag(ModeCombo, _settings.ConnectionMode);
        AutoConnectCheck.IsChecked = _settings.AutoConnectOnLaunch;
        RemoteUrlInput.Text = _settings.RemoteUrl;

        // Appearance
        SelectComboByTag(ThemeCombo, _settings.Theme);

        // Advanced
        DebugModeCheck.IsChecked = _settings.DebugMode;
        NodePathInput.Text = _settings.NodePath ?? "";
        GatewayPathInput.Text = _settings.GatewayPath ?? "";

        // About
        var version = GetType().Assembly.GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3) ?? "0.1.0"}";
    }

    /// <summary>Read UI controls back into settings and save.</summary>
    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // General
        _settings.CloseToTray = CloseToTrayCheck.IsChecked == true;
        _settings.ShowInTaskbar = ShowInTaskbarCheck.IsChecked == true;
        _settings.SessionKey = string.IsNullOrWhiteSpace(SessionKeyInput.Text)
            ? "main"
            : SessionKeyInput.Text.Trim();
        if (int.TryParse(HistoryLimitInput.Text.Trim(), out var limit))
        {
            _settings.HistoryLimit = Math.Clamp(limit, 1, 1000);
        }

        // Connection
        _settings.ConnectionMode = GetComboTag(ModeCombo) ?? "Local";
        _settings.AutoConnectOnLaunch = AutoConnectCheck.IsChecked == true;
        _settings.RemoteUrl = string.IsNullOrWhiteSpace(RemoteUrlInput.Text)
            ? "ws://127.0.0.1:3000"
            : RemoteUrlInput.Text.Trim();

        // Appearance
        var theme = GetComboTag(ThemeCombo) ?? "system";
        _settings.Theme = theme;

        // Apply theme immediately
        var themeMode = theme switch
        {
            "light" => ThemeService.ThemeMode.Light,
            "dark" => ThemeService.ThemeMode.Dark,
            _ => ThemeService.ThemeMode.System,
        };
        _themeService.Mode = themeMode;

        // Advanced
        _settings.DebugMode = DebugModeCheck.IsChecked == true;
        _settings.NodePath = string.IsNullOrWhiteSpace(NodePathInput.Text)
            ? null
            : NodePathInput.Text.Trim();
        _settings.GatewayPath = string.IsNullOrWhiteSpace(GatewayPathInput.Text)
            ? null
            : GatewayPathInput.Text.Trim();

        // Persist
        await _settingsStore.SaveAsync(_settings);

        // Notify main window
        SettingsSaved?.Invoke(_settings);
    }

    // ═══════ Navigation ═══════

    private void NavGeneral_Checked(object sender, RoutedEventArgs e) => ShowPanel("General");
    private void NavConnection_Checked(object sender, RoutedEventArgs e) => ShowPanel("Connection");
    private void NavAppearance_Checked(object sender, RoutedEventArgs e) => ShowPanel("Appearance");
    private void NavShortcuts_Checked(object sender, RoutedEventArgs e) => ShowPanel("Shortcuts");
    private void NavPrivacy_Checked(object sender, RoutedEventArgs e) => ShowPanel("Privacy");
    private void NavAdvanced_Checked(object sender, RoutedEventArgs e) => ShowPanel("Advanced");
    private void NavAbout_Checked(object sender, RoutedEventArgs e) => ShowPanel("About");

    private void ShowPanel(string name)
    {
        PanelGeneral.Visibility = name == "General" ? Visibility.Visible : Visibility.Collapsed;
        PanelConnection.Visibility = name == "Connection" ? Visibility.Visible : Visibility.Collapsed;
        PanelAppearance.Visibility = name == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        PanelShortcuts.Visibility = name == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        PanelPrivacy.Visibility = name == "Privacy" ? Visibility.Visible : Visibility.Collapsed;
        PanelAdvanced.Visibility = name == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        PanelAbout.Visibility = name == "About" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ═══════ Privacy actions ═══════

    private void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will delete all locally cached chat messages.\nThis action cannot be undone.\n\nContinue?",
            "Clear Cache",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var cacheDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OpenSoul", "cache");
                if (System.IO.Directory.Exists(cacheDir))
                {
                    System.IO.Directory.Delete(cacheDir, recursive: true);
                }
                MessageBox.Show("Local chat cache cleared.", "Done",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear cache: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ClearCredentialsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will remove all saved tokens, API keys, and session data.\nYou will need to re-authenticate.\n\nContinue?",
            "Clear Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                var credDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OpenSoul", "credentials");
                if (System.IO.Directory.Exists(credDir))
                {
                    System.IO.Directory.Delete(credDir, recursive: true);
                }
                MessageBox.Show("Saved credentials cleared.", "Done",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear credentials: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ═══════ Helpers ═══════

    private static void SelectComboByTag(ComboBox combo, string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return;
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static string? GetComboTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }
}
