using System.Diagnostics;
using System.Windows;

namespace UbiSlot;

public partial class SupportWindow : Window
{
    private const string GitHubSponsorsUrl =
        "https://github.com/sponsors/BalTor02";

    public SupportWindow()
    {
        InitializeComponent();
    }

    private void GitHubSponsorsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        GitHubSponsorsUrl,

                    UseShellExecute =
                        true
                });
        }
        catch
        {
            MessageBox.Show(
                "UbiSlot couldn't open GitHub.",
                "Support",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}