using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace OpenSoul.Services;

/// <summary>
/// Manages Windows toast notifications for gateway events, chat messages,
/// exec approvals, and device pairing requests.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly ILogger<NotificationService> _logger;
    private bool _disposed;

    /// <summary>Fired when user clicks a notification with an action.</summary>
    public event Action<string>? NotificationActivated;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;

        // Listen for notification activations (clicks)
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    /// <summary>
    /// Show a simple toast notification with title and body text.
    /// </summary>
    /// <param name="title">Notification title.</param>
    /// <param name="body">Notification body text.</param>
    /// <param name="tag">Optional tag for deduplication.</param>
    /// <param name="action">Optional action string passed on click.</param>
    public void Show(string title, string body, string? tag = null, string? action = null)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddAttributionText("OpenSoul");

            // Add launch action if provided
            if (!string.IsNullOrWhiteSpace(action))
            {
                builder.AddArgument("action", action);
            }

            // Show the notification using the compat API
            builder.Show(toast =>
            {
                // Set tag for deduplication
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    toast.Tag = tag.Length > 64 ? tag[..64] : tag;
                    toast.Group = "opensoul";
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show notification: {Title}", title);
        }
    }

    /// <summary>
    /// Show an urgent notification that appears above other notifications.
    /// Used for exec approval and device pairing requests.
    /// </summary>
    public void ShowUrgent(string title, string body, string action)
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddAttributionText("OpenSoul - Action Required")
                .AddArgument("action", action)
                .SetToastScenario(ToastScenario.Reminder);

            builder.Show(toast =>
            {
                toast.Tag = $"urgent-{action}";
                toast.Group = "opensoul-urgent";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show urgent notification: {Title}", title);
        }
    }

    /// <summary>Handle toast notification activation (user clicked).</summary>
    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("action", out var action) && !string.IsNullOrWhiteSpace(action))
            {
                NotificationActivated?.Invoke(action);
            }
            else
            {
                // Default: bring app to front
                NotificationActivated?.Invoke("show");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toast activation handler failed");
        }
    }

    /// <summary>Clear all OpenSoul notifications.</summary>
    public void ClearAll()
    {
        try
        {
            ToastNotificationManagerCompat.History.Clear();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clear notifications");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
