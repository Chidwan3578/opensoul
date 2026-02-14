using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;

namespace OpenSoul.Services;

/// <summary>
/// Registers and manages system-wide global hotkeys via Win32 API.
/// These hotkeys work even when the app is in the background or minimized to tray.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // Win32 interop for RegisterHotKey / UnregisterHotKey
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier flags
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    // Virtual key codes
    private const uint VK_O = 0x4F;
    private const uint VK_C = 0x43;

    // Hotkey IDs (must be unique per app)
    private const int HOTKEY_TOGGLE_WINDOW = 9001;
    private const int HOTKEY_OPEN_CHAT = 9002;

    // WM_HOTKEY message constant
    private const int WM_HOTKEY = 0x0312;

    private readonly ILogger<HotkeyService> _logger;
    private IntPtr _windowHandle;
    private HwndSource? _hwndSource;
    private bool _registered;
    private bool _disposed;

    /// <summary>Fired when Ctrl+Shift+O is pressed (toggle main window).</summary>
    public event Action? ToggleWindowRequested;

    /// <summary>Fired when Ctrl+Shift+C is pressed (open chat).</summary>
    public event Action? OpenChatRequested;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Register global hotkeys. Must be called after the window handle is available.
    /// Typically called in Window.Loaded or after WindowInteropHelper.EnsureHandle().
    /// </summary>
    /// <param name="window">The main window to attach hotkey listeners to.</param>
    public void Register(Window window)
    {
        if (_registered) return;

        try
        {
            var helper = new WindowInteropHelper(window);
            _windowHandle = helper.EnsureHandle();

            _hwndSource = HwndSource.FromHwnd(_windowHandle);
            _hwndSource?.AddHook(WndProc);

            // Ctrl+Shift+O → Toggle main window
            var toggleOk = RegisterHotKey(
                _windowHandle,
                HOTKEY_TOGGLE_WINDOW,
                MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
                VK_O);

            if (!toggleOk)
            {
                _logger.LogWarning(
                    "Failed to register Ctrl+Shift+O hotkey (error: {Error})",
                    Marshal.GetLastWin32Error());
            }

            // Ctrl+Shift+C → Open chat
            var chatOk = RegisterHotKey(
                _windowHandle,
                HOTKEY_OPEN_CHAT,
                MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
                VK_C);

            if (!chatOk)
            {
                _logger.LogWarning(
                    "Failed to register Ctrl+Shift+C hotkey (error: {Error})",
                    Marshal.GetLastWin32Error());
            }

            _registered = toggleOk || chatOk;

            if (_registered)
            {
                _logger.LogInformation("Global hotkeys registered successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register global hotkeys");
        }
    }

    /// <summary>Unregister all global hotkeys.</summary>
    public void Unregister()
    {
        if (!_registered || _windowHandle == IntPtr.Zero) return;

        try
        {
            UnregisterHotKey(_windowHandle, HOTKEY_TOGGLE_WINDOW);
            UnregisterHotKey(_windowHandle, HOTKEY_OPEN_CHAT);
            _hwndSource?.RemoveHook(WndProc);
            _registered = false;
            _logger.LogInformation("Global hotkeys unregistered");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error unregistering global hotkeys");
        }
    }

    /// <summary>Window message hook to intercept WM_HOTKEY messages.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var hotkeyId = wParam.ToInt32();
            switch (hotkeyId)
            {
                case HOTKEY_TOGGLE_WINDOW:
                    ToggleWindowRequested?.Invoke();
                    handled = true;
                    break;

                case HOTKEY_OPEN_CHAT:
                    OpenChatRequested?.Invoke();
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unregister();
    }
}
