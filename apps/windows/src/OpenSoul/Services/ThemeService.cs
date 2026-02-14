using System.Windows;
using Microsoft.Win32;

namespace OpenSoul.Services;

/// <summary>
/// Detects Windows system theme and manages app theme switching.
/// Synchronizes theme between WPF shell and WebView2 Control UI.
/// </summary>
public sealed class ThemeService : IDisposable
{
    /// <summary>Available theme modes.</summary>
    public enum ThemeMode { System, Light, Dark }

    /// <summary>Resolved theme after applying system preference.</summary>
    public enum ResolvedTheme { Light, Dark }

    /// <summary>Fired when the resolved theme changes.</summary>
    public event Action<ResolvedTheme>? ThemeChanged;

    private ThemeMode _mode = ThemeMode.System;
    private ResolvedTheme _resolved;
    private bool _disposed;

    /// <summary>Current theme mode setting.</summary>
    public ThemeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            ApplyTheme();
        }
    }

    /// <summary>Current resolved theme (light or dark).</summary>
    public ResolvedTheme Resolved => _resolved;

    public ThemeService()
    {
        // Listen for Windows system theme changes via registry
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _resolved = Resolve(_mode);
    }

    /// <summary>
    /// Apply the current theme to the WPF application resources.
    /// Call this during startup after setting the initial mode.
    /// </summary>
    public void ApplyTheme()
    {
        var next = Resolve(_mode);
        var changed = next != _resolved;
        _resolved = next;

        // Swap theme resource dictionary in Application.Resources
        var app = Application.Current;
        if (app is null) return;

        var themeUri = next == ResolvedTheme.Dark
            ? new Uri("Themes/Dark.xaml", UriKind.Relative)
            : new Uri("Themes/Light.xaml", UriKind.Relative);

        var themeDict = new ResourceDictionary { Source = themeUri };

        // Remove existing theme dictionaries and add the new one
        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var dict = merged[i];
            if (dict.Source is not null &&
                (dict.Source.OriginalString.Contains("Dark.xaml") ||
                 dict.Source.OriginalString.Contains("Light.xaml")))
            {
                merged.RemoveAt(i);
            }
        }

        merged.Add(themeDict);

        if (changed)
        {
            ThemeChanged?.Invoke(next);
        }
    }

    /// <summary>Detect the current Windows system theme (light or dark).</summary>
    public static ResolvedTheme DetectSystemTheme()
    {
        try
        {
            // Read AppsUseLightTheme from registry (0 = dark, 1 = light)
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int value)
            {
                return value == 0 ? ResolvedTheme.Dark : ResolvedTheme.Light;
            }
        }
        catch
        {
            // Fallback to dark if registry read fails
        }

        return ResolvedTheme.Dark;
    }

    /// <summary>Returns the CSS theme name for the WebView2 bridge.</summary>
    public string ResolvedCssThemeName => _resolved == ResolvedTheme.Dark ? "dark" : "light";

    private static ResolvedTheme Resolve(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ResolvedTheme.Light,
        ThemeMode.Dark => ResolvedTheme.Dark,
        _ => DetectSystemTheme(),
    };

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // UserPreferenceCategory.General fires on theme changes
        if (e.Category == UserPreferenceCategory.General && _mode == ThemeMode.System)
        {
            ApplyTheme();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
