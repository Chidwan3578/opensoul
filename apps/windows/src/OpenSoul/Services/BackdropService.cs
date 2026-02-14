using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.Logging;

namespace OpenSoul.Services;

/// <summary>
/// Applies Mica or Acrylic backdrop effects on Windows 11+.
/// Falls back gracefully to opaque background on older Windows versions.
///
/// Windows 11 22H2+ → Mica Alt (preferred for borderless windows)
/// Windows 11 21H2  → Mica
/// Windows 10       → No effect (solid background)
/// </summary>
public sealed class BackdropService
{
    // ─── DWM API interop ───

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        DwmWindowAttribute attribute,
        ref int pvAttribute,
        int cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd,
        ref Margins pMargins);

    /// <summary>DWM window attributes used for backdrop effects.</summary>
    private enum DwmWindowAttribute : uint
    {
        /// <summary>Enable immersive dark mode (Windows 10 20H1+).</summary>
        UseImmersiveDarkMode = 20,

        /// <summary>Set system backdrop type (Windows 11 22H2+, Build 22621).</summary>
        SystemBackdropType = 38,

        /// <summary>Set Mica effect (Windows 11 21H2, Build 22000).</summary>
        MicaEffect = 1029,
    }

    /// <summary>System backdrop types for DwmSetWindowAttribute(38).</summary>
    private enum SystemBackdropType
    {
        Auto = 0,
        None = 1,
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4,  // Mica Alt — subtler, great for always-on-top / borderless
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    private readonly ILogger<BackdropService> _logger;
    private IntPtr _hwnd;
    private bool _applied;

    /// <summary>Whether backdrop was successfully applied.</summary>
    public bool IsActive => _applied;

    public BackdropService(ILogger<BackdropService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Apply the best available backdrop effect to the given window.
    /// Must be called after the window handle is available (e.g., in Loaded event).
    /// </summary>
    /// <param name="window">The WPF window to apply the backdrop to.</param>
    /// <param name="useDarkMode">Whether to use dark mode styling for the backdrop.</param>
    /// <returns>True if any backdrop effect was applied.</returns>
    public bool Apply(Window window, bool useDarkMode)
    {
        try
        {
            _hwnd = new WindowInteropHelper(window).EnsureHandle();

            // Step 1: Set immersive dark/light mode flag for the window frame
            SetImmersiveDarkMode(useDarkMode);

            // Step 2: Extend frame into client area (required for Mica to show through)
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(_hwnd, ref margins);

            // Step 3: Try applying backdrop in order of preference
            if (TryApplyMicaAlt())
            {
                _applied = true;
                _logger.LogInformation("Applied Mica Alt backdrop (Windows 11 22H2+)");

                // Make WPF background transparent so Mica shows through
                window.Background = Brushes.Transparent;
                return true;
            }

            if (TryApplyMica())
            {
                _applied = true;
                _logger.LogInformation("Applied Mica backdrop (Windows 11 21H2)");
                window.Background = Brushes.Transparent;
                return true;
            }

            // Windows 10 or older — no backdrop available
            _logger.LogInformation("No backdrop available on this OS version, using solid background");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply backdrop effect");
            return false;
        }
    }

    /// <summary>
    /// Update the dark/light mode flag for the backdrop.
    /// Call this when the app theme changes.
    /// </summary>
    public void UpdateDarkMode(bool useDarkMode)
    {
        if (_hwnd == IntPtr.Zero) return;
        SetImmersiveDarkMode(useDarkMode);
    }

    /// <summary>Set the immersive dark mode flag on the window.</summary>
    private void SetImmersiveDarkMode(bool dark)
    {
        if (_hwnd == IntPtr.Zero) return;

        var value = dark ? 1 : 0;
        DwmSetWindowAttribute(
            _hwnd,
            DwmWindowAttribute.UseImmersiveDarkMode,
            ref value,
            sizeof(int));
    }

    /// <summary>Try applying Mica Alt (Build 22621+, attribute 38).</summary>
    private bool TryApplyMicaAlt()
    {
        var value = (int)SystemBackdropType.MicaAlt;
        var hr = DwmSetWindowAttribute(
            _hwnd,
            DwmWindowAttribute.SystemBackdropType,
            ref value,
            sizeof(int));

        return hr == 0; // S_OK
    }

    /// <summary>Try applying Mica via legacy attribute (Build 22000+, attribute 1029).</summary>
    private bool TryApplyMica()
    {
        var value = 1; // enable
        var hr = DwmSetWindowAttribute(
            _hwnd,
            DwmWindowAttribute.MicaEffect,
            ref value,
            sizeof(int));

        return hr == 0; // S_OK
    }
}
