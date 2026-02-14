using System.Windows;
using System.Windows.Threading;
using Velopack;

namespace OpenSoul;

/// <summary>
/// Application entry point.
/// Handles global exception handling, Velopack update hooks, and startup/shutdown lifecycle.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Velopack: must be called early in startup, before any UI is created.
        // Handles install/uninstall/update hooks that run silently.
        VelopackApp.Build()
            .WithFirstRun(v =>
            {
                // First run after fresh install — could show onboarding, etc.
                System.Diagnostics.Debug.WriteLine(
                    $"[OpenSoul] First run after install (v{v})");
            })
            .Run();

        base.OnStartup(e);

        // Global exception handlers to prevent crashes
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        // Log and swallow non-critical exceptions to keep the app running
        System.Diagnostics.Debug.WriteLine($"[OpenSoul] Unhandled exception: {e.Exception}");
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender,
        UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[OpenSoul] Domain unhandled exception: {e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[OpenSoul] Unobserved task exception: {e.Exception}");
        e.SetObserved();
    }
}
