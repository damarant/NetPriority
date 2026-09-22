using System.Diagnostics;
using System.Windows;

namespace NetPriority;

public partial class AboutWindow : Window
{
    private const string MitLicenseUrl =
        "https://opensource.org/licenses/MIT";

    public AboutWindow()
    {
        InitializeComponent();
    }

    private void LicenseLink_Click(
        object sender,
        RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = MitLicenseUrl,
            UseShellExecute = true
        });
    }

    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }
}