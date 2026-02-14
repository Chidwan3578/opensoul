using System.Windows;
using OpenSoul.Protocol;

namespace OpenSoul.Windows;

/// <summary>
/// Native WPF dialog for device pairing requests.
/// Shows device details and allows the user to approve or reject.
/// </summary>
public partial class DevicePairingDialog : Window
{
    /// <summary>Whether the user approved the pairing.</summary>
    public bool Approved { get; private set; }

    public DevicePairingDialog(DevicePairRequestedEvent request)
    {
        InitializeComponent();

        // Populate device details
        DeviceNameText.Text = request.DeviceName ?? request.DeviceId ?? "Unknown Device";

        if (!string.IsNullOrWhiteSpace(request.Platform))
        {
            PlatformPanel.Visibility = Visibility.Visible;
            PlatformText.Text = request.Platform;
        }

        if (!string.IsNullOrWhiteSpace(request.Ip))
        {
            IpPanel.Visibility = Visibility.Visible;
            IpText.Text = request.Ip;
        }

        // Focus reject button (safe default)
        Loaded += (_, _) => RejectButton.Focus();
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        Approved = true;
        DialogResult = true;
        Close();
    }

    private void RejectButton_Click(object sender, RoutedEventArgs e)
    {
        Approved = false;
        DialogResult = true;
        Close();
    }
}
