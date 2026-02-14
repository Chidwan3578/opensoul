using System.Windows;
using System.Windows.Media;
using OpenSoul.Protocol;

namespace OpenSoul.Windows;

/// <summary>
/// Native WPF dialog for exec approval requests.
/// Shows command details and allows the user to approve or reject.
/// </summary>
public partial class ExecApprovalDialog : Window
{
    /// <summary>Whether the user approved the command.</summary>
    public bool Approved { get; private set; }

    /// <summary>Whether the user wants to remember this decision.</summary>
    public bool Remember { get; private set; }

    public ExecApprovalDialog(ExecApprovalRequestParams request)
    {
        InitializeComponent();

        // Populate command details
        CommandText.Text = request.Command ?? "(unknown command)";

        if (!string.IsNullOrWhiteSpace(request.Cwd))
        {
            CwdPanel.Visibility = Visibility.Visible;
            CwdText.Text = request.Cwd;
        }

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            ReasonPanel.Visibility = Visibility.Visible;
            ReasonText.Text = request.Reason;
        }

        if (!string.IsNullOrWhiteSpace(request.RiskLevel))
        {
            RiskPanel.Visibility = Visibility.Visible;
            RiskText.Text = request.RiskLevel;

            // Color-code risk level
            RiskDot.Fill = request.RiskLevel.ToLowerInvariant() switch
            {
                "low" => FindResource("SuccessBrush") as Brush ?? Brushes.Green,
                "medium" => FindResource("WarningBrush") as Brush ?? Brushes.Orange,
                "high" or "critical" => FindResource("DangerBrush") as Brush ?? Brushes.Red,
                _ => FindResource("MutedBrush") as Brush ?? Brushes.Gray,
            };
        }

        // Focus reject button (safe default)
        Loaded += (_, _) => RejectButton.Focus();
    }

    private void ApproveButton_Click(object sender, RoutedEventArgs e)
    {
        Approved = true;
        Remember = RememberCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void RejectButton_Click(object sender, RoutedEventArgs e)
    {
        Approved = false;
        Remember = RememberCheckBox.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
