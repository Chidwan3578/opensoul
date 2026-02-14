using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace OpenSoul.Services;

/// <summary>
/// Manages bidirectional communication between WPF shell and WebView2 Control UI.
/// Uses WebView2 postMessage protocol for all bridge messages.
/// </summary>
public sealed class BridgeService : IDisposable
{
    private readonly ILogger<BridgeService> _logger;
    private WebView2? _webView;
    private bool _isReady;
    private bool _disposed;

    /// <summary>JSON serializer options matching gateway protocol.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    // --- Events fired when messages arrive from WebView2 ---

    /// <summary>Control UI has finished initialization and is ready for messages.</summary>
    public event Action? ShellReady;

    /// <summary>Gateway connection state changed inside WebView2.</summary>
    public event Action<string>? ConnectionStateChanged;

    /// <summary>Theme changed inside WebView2.</summary>
    public event Action<string>? WebThemeChanged;

    /// <summary>Active tab changed inside WebView2.</summary>
    public event Action<string, string>? TabChanged;

    /// <summary>WebView2 requests a native notification.</summary>
    public event Action<string, string, string?>? NotifyRequested;

    /// <summary>WebView2 requests exec approval dialog.</summary>
    public event Action<JsonElement>? ExecApprovalRequested;

    /// <summary>WebView2 requests device pairing dialog.</summary>
    public event Action<JsonElement>? DevicePairRequested;

    /// <summary>WebView2 requests opening an external URL.</summary>
    public event Action<string>? OpenExternalRequested;

    /// <summary>WebView2 requests badge count update.</summary>
    public event Action<int>? BadgeCountChanged;

    /// <summary>WebView2 requests gateway action (restart/stop).</summary>
    public event Action<string>? GatewayActionRequested;

    /// <summary>Whether the Control UI bridge is initialized and ready.</summary>
    public bool IsReady => _isReady;

    public BridgeService(ILogger<BridgeService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Attach to a WebView2 control and begin listening for messages.
    /// Call after WebView2 CoreWebView2 initialization is complete.
    /// </summary>
    public void Attach(WebView2 webView)
    {
        if (_webView is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        _webView = webView;
        _isReady = false;

        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }
    }

    /// <summary>
    /// Send host.init message with initial configuration.
    /// Called once after receiving shell.ready from WebView2.
    /// </summary>
    public async Task SendInitAsync(string theme, string? gatewayUrl, string? token, object? settings = null)
    {
        await SendAsync("host.init", new
        {
            theme,
            gatewayUrl,
            token,
            settings,
            platform = "windows",
        });
    }

    /// <summary>Send a theme change to WebView2.</summary>
    public async Task SendThemeChangedAsync(string theme)
    {
        await SendAsync("host.themeChanged", new { theme });
    }

    /// <summary>Navigate WebView2 to a specific tab.</summary>
    public async Task SendNavigateAsync(string tab)
    {
        await SendAsync("host.navigate", new { tab });
    }

    /// <summary>Focus a specific element in WebView2.</summary>
    public async Task SendFocusAsync(string target)
    {
        await SendAsync("host.focus", new { target });
    }

    /// <summary>Send exec approval result back to WebView2.</summary>
    public async Task SendExecApprovalResultAsync(string requestId, bool approved, bool remember)
    {
        await SendAsync("host.execApprovalResult", new { requestId, approved, remember });
    }

    /// <summary>Send device pairing result back to WebView2.</summary>
    public async Task SendDevicePairResultAsync(string requestId, bool approved)
    {
        await SendAsync("host.devicePairResult", new { requestId, approved });
    }

    /// <summary>Notify WebView2 about dropped files.</summary>
    public async Task SendFileDropAsync(IEnumerable<FileDropInfo> files)
    {
        await SendAsync("host.fileDrop", new { files });
    }

    /// <summary>Notify WebView2 about window state changes.</summary>
    public async Task SendWindowStateAsync(string state)
    {
        await SendAsync("host.windowState", new { state });
    }

    /// <summary>Notify WebView2 about settings changes from native settings window.</summary>
    public async Task SendSettingsChangedAsync(object settings)
    {
        await SendAsync("host.settingsChanged", settings);
    }

    /// <summary>Send a typed bridge message to WebView2.</summary>
    private async Task SendAsync(string type, object? payload = null)
    {
        if (_webView?.CoreWebView2 is null)
        {
            _logger.LogWarning("Bridge.Send skipped: WebView2 not ready (type={Type})", type);
            return;
        }

        try
        {
            var message = new BridgeMessage { Type = type, Payload = payload };
            var json = JsonSerializer.Serialize(message, JsonOpts);
            await _webView.Dispatcher.InvokeAsync(() =>
            {
                _webView.CoreWebView2.PostWebMessageAsJson(json);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bridge.Send failed (type={Type})", type);
        }
    }

    /// <summary>Handle incoming messages from WebView2.</summary>
    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp) ||
                typeProp.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var type = typeProp.GetString() ?? "";
            var payload = root.TryGetProperty("payload", out var p) ? p : default;

            switch (type)
            {
                case "shell.ready":
                    _isReady = true;
                    ShellReady?.Invoke();
                    break;

                case "shell.connectionStateChanged":
                    var state = GetString(payload, "state") ?? "disconnected";
                    ConnectionStateChanged?.Invoke(state);
                    break;

                case "shell.themeChanged":
                    var theme = GetString(payload, "theme") ?? "dark";
                    WebThemeChanged?.Invoke(theme);
                    break;

                case "shell.tabChanged":
                    var tab = GetString(payload, "tab") ?? "";
                    var title = GetString(payload, "title") ?? "";
                    TabChanged?.Invoke(tab, title);
                    break;

                case "shell.notify":
                    var nTitle = GetString(payload, "title") ?? "";
                    var nBody = GetString(payload, "body") ?? "";
                    var nTag = GetString(payload, "tag");
                    NotifyRequested?.Invoke(nTitle, nBody, nTag);
                    break;

                case "shell.requestExecApproval":
                    ExecApprovalRequested?.Invoke(payload.Clone());
                    break;

                case "shell.requestDevicePair":
                    DevicePairRequested?.Invoke(payload.Clone());
                    break;

                case "shell.openExternal":
                    var url = GetString(payload, "url") ?? "";
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        OpenExternalRequested?.Invoke(url);
                    }
                    break;

                case "shell.badge":
                    if (payload.TryGetProperty("count", out var countProp) &&
                        countProp.TryGetInt32(out var count))
                    {
                        BadgeCountChanged?.Invoke(count);
                    }
                    break;

                case "shell.gatewayAction":
                    var action = GetString(payload, "action") ?? "";
                    if (!string.IsNullOrWhiteSpace(action))
                    {
                        GatewayActionRequested?.Invoke(action);
                    }
                    break;

                default:
                    _logger.LogDebug("Bridge: unhandled message type={Type}", type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bridge.OnWebMessageReceived failed");
        }
    }

    /// <summary>Helper to read a string property from a JsonElement.</summary>
    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (!element.TryGetProperty(property, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_webView?.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }

        _webView = null;
    }

    /// <summary>Bridge message envelope.</summary>
    private sealed class BridgeMessage
    {
        public string Type { get; init; } = "";
        public object? Payload { get; init; }
    }

    /// <summary>File information for drag-and-drop bridge.</summary>
    public sealed class FileDropInfo
    {
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";
        public long Size { get; init; }
    }
}
