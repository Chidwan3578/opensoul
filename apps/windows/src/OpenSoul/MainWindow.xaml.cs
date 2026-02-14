using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using OpenSoul.Gateway;
using OpenSoul.Protocol;
using OpenSoul.Services;

namespace OpenSoul;

/// <summary>
/// Main application window.
/// WPF shell with custom titlebar, system tray, and WebView2 hosting the Control UI.
/// </summary>
public partial class MainWindow : Window
{
    // --- Services ---
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<MainWindow> _logger;
    private readonly ControlChannel _controlChannel;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly ThemeService _themeService;
    private readonly BridgeService _bridgeService;
    private readonly NotificationService _notificationService;
    private readonly WindowStateService _windowStateService;
    private readonly HotkeyService _hotkeyService;
    private readonly BackdropService _backdropService;
    private readonly UpdateService _updateService;

    // --- State ---
    private AppSettings _settings = new();
    private bool _isShuttingDown;
    private bool _closeToTray = true;
    private bool _hasShownCloseToTrayNotice;
    private string _connectionState = "disconnected";

    public MainWindow()
    {
        InitializeComponent();

        // Create logging infrastructure
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        _logger = _loggerFactory.CreateLogger<MainWindow>();

        // Create services
        _themeService = new ThemeService();
        _bridgeService = new BridgeService(_loggerFactory.CreateLogger<BridgeService>());
        _notificationService = new NotificationService(_loggerFactory.CreateLogger<NotificationService>());
        _windowStateService = new WindowStateService(_loggerFactory.CreateLogger<WindowStateService>());
        _hotkeyService = new HotkeyService(_loggerFactory.CreateLogger<HotkeyService>());
        _backdropService = new BackdropService(_loggerFactory.CreateLogger<BackdropService>());
        _updateService = new UpdateService(_loggerFactory.CreateLogger<UpdateService>());

        // Create gateway infrastructure
        var nodeLocator = new NodeLocator(_loggerFactory.CreateLogger<NodeLocator>());
        var processManager = new GatewayProcessManager(
            _loggerFactory.CreateLogger<GatewayProcessManager>(), nodeLocator);
        var connection = new GatewayConnection(
            _loggerFactory.CreateLogger<GatewayConnection>(),
            _loggerFactory.CreateLogger<GatewayChannel>());

        _controlChannel = new ControlChannel(
            _loggerFactory.CreateLogger<ControlChannel>(),
            connection,
            processManager);

        // Wire up gateway events
        _controlChannel.StateChanged += OnControlChannelStateChanged;
        _controlChannel.ExecApprovalRequested += OnExecApprovalRequested;
        _controlChannel.DevicePairRequested += OnDevicePairRequested;

        // Wire up bridge events
        _bridgeService.ShellReady += OnBridgeShellReady;
        _bridgeService.ConnectionStateChanged += OnBridgeConnectionStateChanged;
        _bridgeService.WebThemeChanged += OnBridgeThemeChanged;
        _bridgeService.TabChanged += OnBridgeTabChanged;
        _bridgeService.NotifyRequested += OnBridgeNotifyRequested;
        _bridgeService.OpenExternalRequested += OnBridgeOpenExternal;
        _bridgeService.GatewayActionRequested += OnBridgeGatewayAction;
        _bridgeService.BadgeCountChanged += OnBridgeBadgeCountChanged;

        // Wire up theme changes
        _themeService.ThemeChanged += OnThemeChanged;

        // Wire up notification click
        _notificationService.NotificationActivated += OnNotificationActivated;

        // Wire up global hotkeys
        _hotkeyService.ToggleWindowRequested += () => Dispatcher.InvokeAsync(ToggleWindowVisibility);
        _hotkeyService.OpenChatRequested += () => Dispatcher.InvokeAsync(() =>
        {
            ShowAndFocusWindow();
            _ = _bridgeService.SendNavigateAsync("chat");
            _ = _bridgeService.SendFocusAsync("chat-input");
        });

        // Register window-level keyboard shortcuts
        RegisterKeyboardShortcuts();
    }

    // ═══════════ LIFECYCLE ═══════════

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Load and apply settings
        _settings = await _settingsStore.LoadAsync();
        _closeToTray = true;
        _hasShownCloseToTrayNotice = false;

        // Apply theme
        var themeMode = _settings.Theme switch
        {
            "light" => ThemeService.ThemeMode.Light,
            "dark" => ThemeService.ThemeMode.Dark,
            _ => ThemeService.ThemeMode.System,
        };
        _themeService.Mode = themeMode;
        _themeService.ApplyTheme();
        UpdateThemeIcon();

        // Restore window state
        _windowStateService.Restore(this);

        // Start splash entrance animation
        AnimateSplashIn();

        // Apply Mica/Acrylic backdrop on Windows 11 (graceful fallback on Win10)
        var isDark = _themeService.Resolved == ThemeService.ResolvedTheme.Dark;
        _backdropService.Apply(this, isDark);

        // Register system-wide global hotkeys (Ctrl+Shift+O, Ctrl+Shift+C)
        _hotkeyService.Register(this);

        // Start background update checks (Velopack)
        _updateService.UpdateReady += OnUpdateReady;
        _updateService.StartBackgroundChecks();

        // Initialize WebView2
        UpdateSplashStatus("Loading interface...");
        await InitializeWebViewAsync();

        // Auto-connect if configured
        if (_settings.AutoConnectOnLaunch)
        {
            await ConnectGatewayAsync();
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_isShuttingDown)
            return;

        // Close to tray behavior
        if (_closeToTray && !_isShuttingDown)
        {
            e.Cancel = true;
            Hide();

            // Show first-time notice
            if (!_hasShownCloseToTrayNotice)
            {
                _hasShownCloseToTrayNotice = true;
                _notificationService.Show(
                    "OpenSoul is still running",
                    "The app has been minimized to the system tray. Right-click the tray icon for options.",
                    tag: "close-to-tray");
            }
            return;
        }

        // Actually shutting down
        await ShutdownAsync();
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        _ = _bridgeService.SendWindowStateAsync("focused");
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        _ = _bridgeService.SendWindowStateAsync("blurred");
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        // Update maximize/restore icon
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";

        // Notify WebView2 about minimize
        if (WindowState == WindowState.Minimized)
        {
            _ = _bridgeService.SendWindowStateAsync("minimized");
        }

        // Save window state on change
        if (WindowState != WindowState.Minimized)
        {
            _ = _windowStateService.SaveAsync(this);
        }
    }

    private async Task ShutdownAsync()
    {
        _isShuttingDown = true;

        // Save window state
        await _windowStateService.SaveAsync(this);

        // Save settings
        await _settingsStore.SaveAsync(_settings);

        // Disconnect gateway
        try
        {
            var mode = string.Equals(_settings.ConnectionMode, "Remote", StringComparison.OrdinalIgnoreCase)
                ? ConnectionMode.Remote
                : ConnectionMode.Local;
            await _controlChannel.StopAsync(stopGateway: mode == ConnectionMode.Local);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping control channel");
        }

        // Cleanup services
        try
        {
            _hotkeyService.Dispose();
            _bridgeService.Dispose();
            _notificationService.Dispose();
            _themeService.Dispose();
            await _controlChannel.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cleanup");
        }

        // Dispose tray icon
        TrayIcon?.Dispose();
        _loggerFactory.Dispose();
    }

    // ═══════════ WEBVIEW2 INITIALIZATION ═══════════

    private async Task InitializeWebViewAsync()
    {
        try
        {
            // Create WebView2 environment with custom data folder
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenSoul", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder);

            await WebView.EnsureCoreWebView2Async(env);

            // Configure WebView2 settings
            var settings = WebView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;

            #if DEBUG
            settings.AreDevToolsEnabled = true;
            #else
            settings.AreDevToolsEnabled = false;
            #endif

            // Attach bridge service
            _bridgeService.Attach(WebView);

            // Inject bridge initialization script before page load
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GetBridgeInitScript());

            // Navigate to Control UI
            var controlUiUrl = GetControlUiUrl();
            _logger.LogInformation("Navigating WebView2 to: {Url}", controlUiUrl);
            WebView.CoreWebView2.Navigate(controlUiUrl);

            // Handle navigation completed
            WebView.CoreWebView2.NavigationCompleted += OnWebViewNavigationCompleted;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _logger.LogError("WebView2 runtime not found");
            ShowWebView2Fallback();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebView2 initialization failed");
            ShowWebView2Fallback();
        }
    }

    /// <summary>
    /// Returns the URL for the Control UI.
    /// In development, connects to the Vite dev server.
    /// In production, loads from the bundled dist/control-ui directory.
    /// </summary>
    private string GetControlUiUrl()
    {
        // Check for dev server override
        var devUrl = Environment.GetEnvironmentVariable("OPENSOUL_CONTROL_UI_URL");
        if (!string.IsNullOrWhiteSpace(devUrl))
        {
            return devUrl;
        }

        // Check for local dev server (Vite default port)
        #if DEBUG
        return "http://localhost:5173";
        #else
        // Production: load from bundled files
        // The Control UI is built to dist/control-ui relative to the gateway
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var controlUiPath = Path.Combine(baseDir, "control-ui", "index.html");
        if (File.Exists(controlUiPath))
        {
            return new Uri(controlUiPath).AbsoluteUri;
        }

        // Fallback: try gateway's dist
        var gatewayControlUi = Path.GetFullPath(
            Path.Combine(baseDir, "..", "..", "..", "..", "dist", "control-ui", "index.html"));
        if (File.Exists(gatewayControlUi))
        {
            return new Uri(gatewayControlUi).AbsoluteUri;
        }

        _logger.LogWarning("Control UI files not found, falling back to localhost");
        return "http://localhost:5173";
        #endif
    }

    /// <summary>
    /// JavaScript code injected into WebView2 before page load.
    /// Sets up the bridge communication channel on the web side.
    /// </summary>
    private static string GetBridgeInitScript()
    {
        return """
            // OpenSoul Windows Bridge - injected by WPF shell
            (function() {
                'use strict';

                // Bridge message handler registry
                const handlers = new Map();

                // Listen for messages from WPF host
                window.chrome.webview.addEventListener('message', (event) => {
                    const msg = event.data;
                    if (!msg || !msg.type) return;

                    const handler = handlers.get(msg.type);
                    if (handler) {
                        handler(msg.payload);
                    }

                    // Also dispatch as a custom event for flexibility
                    window.dispatchEvent(new CustomEvent('opensoul-bridge', {
                        detail: msg
                    }));
                });

                // Public API for the Control UI to use
                window.__opensoul_bridge = {
                    // Send a message to WPF shell
                    send(type, payload) {
                        window.chrome.webview.postMessage({ type, payload });
                    },

                    // Register a handler for a specific message type from WPF
                    on(type, handler) {
                        handlers.set(type, handler);
                    },

                    // Remove a handler
                    off(type) {
                        handlers.delete(type);
                    },

                    // Check if running inside WPF shell
                    isDesktop: true,
                    platform: 'windows',
                };

                // Notify WPF that the bridge script is loaded
                // (shell.ready is sent later by Control UI after full init)
                console.log('[opensoul-bridge] Bridge script initialized');
            })();
            """;
    }

    private void OnWebViewNavigationCompleted(object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _logger.LogInformation("WebView2 navigation completed successfully");
        }
        else
        {
            _logger.LogError("WebView2 navigation failed: {Status}", e.WebErrorStatus);
        }

        // Hide splash with fade animation
        HideSplash();
    }

    /// <summary>Kick off splash entrance animations (icon scale-in + text fade).</summary>
    private void AnimateSplashIn()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(400);

        // Icon scales from 0.8 → 1.0
        SplashIconScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.8, 1.0, duration) { EasingFunction = ease });
        SplashIconScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.8, 1.0, duration) { EasingFunction = ease });

        // Brand text fades in after a short delay
        var brandFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(150),
            EasingFunction = ease,
        };
        SplashBrand.BeginAnimation(OpacityProperty, brandFade);

        // Status text fades in after brand
        var statusFade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            BeginTime = TimeSpan.FromMilliseconds(300),
            EasingFunction = ease,
        };
        SplashStatus.BeginAnimation(OpacityProperty, statusFade);
    }

    /// <summary>Update the splash status text (e.g., "Connecting...").</summary>
    private void UpdateSplashStatus(string text)
    {
        if (SplashOverlay.Visibility == Visibility.Visible)
        {
            SplashStatus.Text = text;
        }
    }

    /// <summary>Fade out splash overlay with a smooth multi-phase animation.</summary>
    private void HideSplash()
    {
        if (SplashOverlay.Visibility == Visibility.Collapsed) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        // Phase 1: Icon scales up slightly (1.0 → 1.05) as a "done" effect
        var iconPulse = new DoubleAnimation(1.0, 1.05, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = ease,
            AutoReverse = true,
        };
        SplashIconScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty, iconPulse);
        SplashIconScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty, iconPulse);

        // Phase 2: Whole overlay fades out after a brief pause
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350))
        {
            BeginTime = TimeSpan.FromMilliseconds(200),
            EasingFunction = ease,
        };
        fadeOut.Completed += (_, _) =>
        {
            SplashOverlay.Visibility = Visibility.Collapsed;
        };
        SplashOverlay.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ShowWebView2Fallback()
    {
        WebView.Visibility = Visibility.Collapsed;
        SplashOverlay.Visibility = Visibility.Collapsed;
        WebView2Fallback.Visibility = Visibility.Visible;
    }

    // ═══════════ BRIDGE EVENT HANDLERS ═══════════

    private async void OnBridgeShellReady()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            _logger.LogInformation("Bridge: shell.ready received from Control UI");

            // Determine gateway connection info
            string? gatewayUrl = null;
            string? token = null;

            if (string.Equals(_settings.ConnectionMode, "Remote", StringComparison.OrdinalIgnoreCase))
            {
                gatewayUrl = _settings.RemoteUrl;
            }
            else
            {
                // For local mode, read from gateway state files
                var port = OpenSoulPaths.ReadGatewayPort();
                var stateToken = OpenSoulPaths.ReadGatewayToken();
                if (port > 0)
                {
                    gatewayUrl = $"ws://127.0.0.1:{port}";
                    token = stateToken;
                }
            }

            // Send init message with all configuration
            await _bridgeService.SendInitAsync(
                theme: _themeService.ResolvedCssThemeName,
                gatewayUrl: gatewayUrl,
                token: token,
                settings: new
                {
                    sessionKey = _settings.SessionKey,
                    historyLimit = _settings.HistoryLimit,
                });
        });
    }

    private void OnBridgeConnectionStateChanged(string state)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _connectionState = state;
            UpdateConnectionStatusDisplay(state);
            UpdateTrayIconState(state);
        });
    }

    private void OnBridgeThemeChanged(string theme)
    {
        // WebView2 changed its theme, but we keep WPF theme in sync via ThemeService
        // This handles the case where theme is changed within the web UI
        Dispatcher.InvokeAsync(() =>
        {
            var mode = theme == "light" ? ThemeService.ThemeMode.Light : ThemeService.ThemeMode.Dark;
            _themeService.Mode = mode;
            _settings.Theme = theme;
            UpdateThemeIcon();
            _ = _settingsStore.SaveAsync(_settings);
        });
    }

    private void OnBridgeTabChanged(string tab, string title)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? "OpenSoul"
                : $"OpenSoul - {title}";
        });
    }

    private void OnBridgeNotifyRequested(string title, string body, string? tag)
    {
        // Only show native notification when window is not focused
        if (!IsActive)
        {
            _notificationService.Show(title, body, tag, action: "show");
        }
    }

    private void OnBridgeOpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open external URL: {Url}", url);
        }
    }

    private async void OnBridgeGatewayAction(string action)
    {
        _logger.LogInformation("Bridge gateway action: {Action}", action);
        // Gateway restart/stop requests from WebView2
        // These will be handled when we wire up the ControlChannel to bridge
    }

    private void OnBridgeBadgeCountChanged(int count)
    {
        Dispatcher.InvokeAsync(() =>
        {
            // Update tray tooltip with badge count
            if (count > 0)
            {
                TrayIcon.ToolTipText = $"OpenSoul - {count} pending";
            }
            else
            {
                UpdateTrayTooltip(_connectionState);
            }
        });
    }

    // ═══════════ GATEWAY EVENT HANDLERS ═══════════

    private void OnControlChannelStateChanged(ControlChannelState state)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var stateStr = state switch
            {
                ControlChannelState.Connected => "connected",
                ControlChannelState.Connecting => "connecting",
                ControlChannelState.Degraded => "degraded",
                _ => "disconnected",
            };

            _connectionState = stateStr;

            // Update splash status text during startup
            UpdateSplashStatus(state switch
            {
                ControlChannelState.Connecting => "Connecting to gateway...",
                ControlChannelState.Connected => "Connected!",
                _ => "Waiting for gateway...",
            });

            UpdateConnectionStatusDisplay(stateStr);
            UpdateTrayIconState(stateStr);
        });
    }

    private void OnExecApprovalRequested(ExecApprovalRequestParams request)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            // Show urgent notification if minimized
            if (WindowState == WindowState.Minimized || !IsVisible)
            {
                _notificationService.ShowUrgent(
                    "Command Approval Required",
                    $"Command: {request.Command ?? "(unknown)"}",
                    action: "exec-approval");
            }

            // Show native dialog
            var dialog = new Windows.ExecApprovalDialog(request)
            {
                Owner = IsVisible ? this : null,
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    await _controlChannel.RequestVoidAsync(
                        GatewayMethod.ExecApprovalResolve,
                        new ExecApprovalResolveParams
                        {
                            RequestId = request.RequestId ?? "",
                            Approved = dialog.Approved,
                            Remember = dialog.Remember,
                        });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exec approval resolve failed");
                }
            }
        });
    }

    private void OnDevicePairRequested(DevicePairRequestedEvent request)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            // Show notification if minimized
            if (WindowState == WindowState.Minimized || !IsVisible)
            {
                _notificationService.ShowUrgent(
                    "Device Pairing Request",
                    $"Device: {request.DeviceName ?? request.DeviceId ?? "Unknown"}",
                    action: "device-pair");
            }

            // Show native dialog
            var dialog = new Windows.DevicePairingDialog(request)
            {
                Owner = IsVisible ? this : null,
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.Approved)
                    {
                        await _controlChannel.RequestVoidAsync(
                            GatewayMethod.DevicePairApprove,
                            new DevicePairApproveParams { RequestId = request.RequestId ?? "" });
                    }
                    else
                    {
                        await _controlChannel.RequestVoidAsync(
                            GatewayMethod.DevicePairReject,
                            new DevicePairRejectParams { RequestId = request.RequestId ?? "" });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Device pairing resolve failed");
                }
            }
        });
    }

    // ═══════════ GATEWAY CONNECTION ═══════════

    private async Task ConnectGatewayAsync()
    {
        try
        {
            if (string.Equals(_settings.ConnectionMode, "Remote", StringComparison.OrdinalIgnoreCase))
            {
                await _controlChannel.StartAsync(ConnectionMode.Remote, new RemoteConnectionOptions
                {
                    Url = _settings.RemoteUrl,
                });
            }
            else
            {
                await _controlChannel.StartAsync(ConnectionMode.Local);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway connection failed");
            _notificationService.Show("Connection Failed", ex.Message, tag: "connect-error");
        }
    }

    // ═══════════ UI STATE UPDATES ═══════════

    private void UpdateConnectionStatusDisplay(string state)
    {
        StatusText.Text = state switch
        {
            "connected" => "Connected",
            "connecting" => "Connecting...",
            "degraded" => "Degraded",
            _ => "Disconnected",
        };

        StatusDot.Fill = state switch
        {
            "connected" => FindResource("SuccessBrush") as Brush ?? Brushes.Green,
            "connecting" => FindResource("WarningBrush") as Brush ?? Brushes.Orange,
            "degraded" => FindResource("WarningBrush") as Brush ?? Brushes.Orange,
            _ => FindResource("MutedBrush") as Brush ?? Brushes.Gray,
        };

        StatusText.Foreground = state switch
        {
            "connected" => FindResource("SuccessBrush") as Brush ?? Brushes.Green,
            "degraded" => FindResource("WarningBrush") as Brush ?? Brushes.Orange,
            _ => FindResource("MutedBrush") as Brush ?? Brushes.Gray,
        };

        // Update tray context menu
        TrayMenuGatewayStatus.Header = $"Gateway: {StatusText.Text}";
    }

    private void UpdateTrayIconState(string state)
    {
        var iconName = state switch
        {
            "connected" => "tray-active",
            "degraded" => "tray-error",
            _ => "tray-idle",
        };

        try
        {
            TrayIcon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri($"pack://application:,,,/Resources/{iconName}.ico"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update tray icon to {Icon}", iconName);
        }

        UpdateTrayTooltip(state);
    }

    private void UpdateTrayTooltip(string state)
    {
        TrayIcon.ToolTipText = state switch
        {
            "connected" => "OpenSoul - Connected",
            "connecting" => "OpenSoul - Connecting...",
            "degraded" => "OpenSoul - Degraded",
            _ => "OpenSoul - Disconnected",
        };
    }

    private void UpdateThemeIcon()
    {
        ThemeIcon.Text = _themeService.Resolved == ThemeService.ResolvedTheme.Dark ? "☀" : "🌙";
    }

    // ═══════════ THEME ═══════════

    private void OnThemeChanged(ThemeService.ResolvedTheme theme)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            UpdateThemeIcon();

            // Update Mica backdrop dark/light mode
            _backdropService.UpdateDarkMode(theme == ThemeService.ResolvedTheme.Dark);

            // If Mica is active, keep window background transparent
            if (_backdropService.IsActive)
            {
                Background = System.Windows.Media.Brushes.Transparent;
            }

            // Sync to WebView2
            if (_bridgeService.IsReady)
            {
                await _bridgeService.SendThemeChangedAsync(
                    theme == ThemeService.ResolvedTheme.Dark ? "dark" : "light");
            }
        });
    }

    // ═══════════ TITLEBAR BUTTONS ═══════════

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle between light and dark
        var next = _themeService.Resolved == ThemeService.ResolvedTheme.Dark
            ? ThemeService.ThemeMode.Light
            : ThemeService.ThemeMode.Dark;
        _themeService.Mode = next;
        _settings.Theme = next == ThemeService.ThemeMode.Dark ? "dark" : "light";
        _ = _settingsStore.SaveAsync(_settings);
    }

    // ═══════════ SYSTEM TRAY ═══════════

    private void TrayIcon_LeftClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowVisibility();
    }

    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e)
    {
        ShowAndFocusWindow();
        _ = _bridgeService.SendNavigateAsync("chat");
        _ = _bridgeService.SendFocusAsync("chat-input");
    }

    private void TrayMenuDashboard_Click(object sender, RoutedEventArgs e)
    {
        ShowAndFocusWindow();
        _ = _bridgeService.SendNavigateAsync("overview");
    }

    private void TrayMenuChat_Click(object sender, RoutedEventArgs e)
    {
        ShowAndFocusWindow();
        _ = _bridgeService.SendNavigateAsync("chat");
    }

    private void TrayMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsWindow();
    }

    private void TrayMenuAbout_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            $"OpenSoul Desktop\nVersion {GetType().Assembly.GetName().Version}\n\nAI agent companion for your digital life.",
            "About OpenSoul",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TrayMenuQuit_Click(object sender, RoutedEventArgs e)
    {
        _closeToTray = false;
        _isShuttingDown = true;
        Close();
    }

    private void ToggleWindowVisibility()
    {
        if (IsVisible)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
            }
            else
            {
                Hide();
            }
        }
        else
        {
            ShowAndFocusWindow();
        }
    }

    private void ShowAndFocusWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Focus();
    }

    // ═══════════ DRAG AND DROP ═══════════

    private void WebView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void WebView_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var fileInfos = files.Select(f =>
            {
                var info = new FileInfo(f);
                return new BridgeService.FileDropInfo
                {
                    Name = info.Name,
                    Path = info.FullName,
                    Size = info.Exists ? info.Length : 0,
                };
            });

            _ = _bridgeService.SendFileDropAsync(fileInfos);
            e.Handled = true;
        }
    }

    // ═══════════ KEYBOARD SHORTCUTS ═══════════

    private void RegisterKeyboardShortcuts()
    {
        // Window-scoped shortcuts
        var bindings = CommandBindings;

        // Ctrl+, → Settings
        var settingsCmd = new RoutedCommand();
        settingsCmd.InputGestures.Add(new KeyGesture(Key.OemComma, ModifierKeys.Control));
        bindings.Add(new CommandBinding(settingsCmd, (_, _) => OpenSettingsWindow()));

        // Ctrl+Q → Quit
        var quitCmd = new RoutedCommand();
        quitCmd.InputGestures.Add(new KeyGesture(Key.Q, ModifierKeys.Control));
        bindings.Add(new CommandBinding(quitCmd, (_, _) =>
        {
            _closeToTray = false;
            _isShuttingDown = true;
            Close();
        }));

        // Ctrl+K → Command Palette (forwarded to WebView2 via bridge)
        var paletteCmd = new RoutedCommand();
        paletteCmd.InputGestures.Add(new KeyGesture(Key.K, ModifierKeys.Control));
        bindings.Add(new CommandBinding(paletteCmd, (_, _) =>
        {
            _ = _bridgeService.SendCommandPaletteAsync();
        }));

        // Ctrl+D → Open Dashboard
        var dashboardCmd = new RoutedCommand();
        dashboardCmd.InputGestures.Add(new KeyGesture(Key.D, ModifierKeys.Control));
        bindings.Add(new CommandBinding(dashboardCmd, (_, _) =>
        {
            _ = _bridgeService.SendNavigateAsync("overview");
        }));

        // Ctrl+1..4 → Switch tabs
        var tabKeys = new[] { Key.D1, Key.D2, Key.D3, Key.D4 };
        var tabNames = new[] { "chat", "overview", "devices", "config" };
        for (var i = 0; i < tabKeys.Length; i++)
        {
            var tabName = tabNames[i];
            var tabCmd = new RoutedCommand();
            tabCmd.InputGestures.Add(new KeyGesture(tabKeys[i], ModifierKeys.Control));
            bindings.Add(new CommandBinding(tabCmd, (_, _) =>
            {
                _ = _bridgeService.SendNavigateAsync(tabName);
            }));
        }

        // F11 → Toggle fullscreen
        var fullscreenCmd = new RoutedCommand();
        fullscreenCmd.InputGestures.Add(new KeyGesture(Key.F11));
        bindings.Add(new CommandBinding(fullscreenCmd, (_, _) => ToggleFullscreen()));

        // Ctrl+Shift+I → DevTools (debug only)
        #if DEBUG
        var devToolsCmd = new RoutedCommand();
        devToolsCmd.InputGestures.Add(new KeyGesture(Key.I, ModifierKeys.Control | ModifierKeys.Shift));
        bindings.Add(new CommandBinding(devToolsCmd, (_, _) =>
        {
            WebView.CoreWebView2?.OpenDevToolsWindow();
        }));
        #endif
    }

    private void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None && WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    // ═══════════ SETTINGS WINDOW ═══════════

    private Windows.SettingsWindow? _settingsWindow;

    private void OpenSettingsWindow()
    {
        // Show the native settings window (reuse if already open)
        if (_settingsWindow is not null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Activate();
            return;
        }

        ShowAndFocusWindow();

        _settingsWindow = new Windows.SettingsWindow(_settings, _settingsStore, _themeService)
        {
            Owner = this,
        };

        _settingsWindow.SettingsSaved += OnSettingsSaved;
        _settingsWindow.Closed += (_, _) =>
        {
            _settingsWindow.SettingsSaved -= OnSettingsSaved;
            _settingsWindow = null;
        };

        _settingsWindow.Show();
    }

    private void OnSettingsSaved(AppSettings settings)
    {
        _settings = settings;

        // Apply close-to-tray setting
        _closeToTray = settings.CloseToTray;

        // Apply show-in-taskbar setting
        ShowInTaskbar = settings.ShowInTaskbar;

        // Sync theme to WebView2
        _ = _bridgeService.SendThemeChangedAsync(_themeService.ResolvedCssThemeName);

        // Sync settings to WebView2
        _ = _bridgeService.SendSettingsChangedAsync(new
        {
            sessionKey = settings.SessionKey,
            historyLimit = settings.HistoryLimit,
            theme = _themeService.ResolvedCssThemeName,
        });
    }

    // ═══════════ NOTIFICATION HANDLER ═══════════

    private void OnNotificationActivated(string action)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ShowAndFocusWindow();

            switch (action)
            {
                case "show":
                    break;
                case "exec-approval":
                    // Bring to front; the dialog should already be showing
                    break;
                case "device-pair":
                    // Bring to front; the dialog should already be showing
                    break;
                default:
                    // Try to navigate to the action as a tab
                    _ = _bridgeService.SendNavigateAsync(action);
                    break;
            }
        });
    }

    // ═══════════ WEBVIEW2 FALLBACK ═══════════

    private void DownloadWebView2Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                "https://go.microsoft.com/fwlink/p/?LinkId=2124703")
            { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open WebView2 download URL");
        }
    }
}
