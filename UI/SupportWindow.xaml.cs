using System.Diagnostics;
using System.Windows;
using UbiSlot.Core;

namespace UbiSlot;

public partial class SupportWindow : Window
{
    private const string GitHubSponsorsUrl =
        "https://github.com/sponsors/BalTor02";

    private readonly UbiSlotSettings _settings;

    public SupportWindow()
    {
        InitializeComponent();

        _settings =
            UbiSlotSettings.Load();

        ApplyWindowTheme(
            _settings.IsDarkTheme);
    }

    private void ApplyWindowTheme(
        bool dark)
    {
        SetResourceBrushColor(
            "WindowBackground",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    16,
                    16,
                    20)
                : System.Windows.Media.Color.FromRgb(
                    245,
                    245,
                    248));

        SetResourceBrushColor(
            "PanelBackground",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    21,
                    21,
                    26)
                : System.Windows.Media.Color.FromRgb(
                    255,
                    255,
                    255));

        SetResourceBrushColor(
            "FieldBackground",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    36,
                    36,
                    44)
                : System.Windows.Media.Color.FromRgb(
                    239,
                    239,
                    244));

        SetResourceBrushColor(
            "HoverBackground",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    52,
                    52,
                    61)
                : System.Windows.Media.Color.FromRgb(
                    232,
                    232,
                    238));

        SetResourceBrushColor(
            "BorderColor",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    51,
                    42,
                    61)
                : System.Windows.Media.Color.FromRgb(
                    218,
                    218,
                    225));

        SetResourceBrushColor(
            "PrimaryText",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    242,
                    242,
                    245)
                : System.Windows.Media.Color.FromRgb(
                    25,
                    25,
                    30));

        SetResourceBrushColor(
            "SecondaryText",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    154,
                    147,
                    163)
                : System.Windows.Media.Color.FromRgb(
                    92,
                    92,
                    102));

        SetResourceBrushColor(
            "MutedText",
            dark
                ? System.Windows.Media.Color.FromRgb(
                    122,
                    116,
                    130)
                : System.Windows.Media.Color.FromRgb(
                    115,
                    115,
                    125));

        SetResourceBrushColor(
            "AccentColor",
            System.Windows.Media.Color.FromRgb(
                61,
                27,
                93));

        SetResourceBrushColor(
            "AccentHoverColor",
            System.Windows.Media.Color.FromRgb(
                81,
                40,
                120));

        Background =
            GetResourceBrush(
                "WindowBackground");

        Foreground =
            GetResourceBrush(
                "PrimaryText");
    }

    private void SetResourceBrushColor(
        string key,
        System.Windows.Media.Color color)
    {
        Resources[key] =
            new System.Windows.Media.SolidColorBrush(
                color);
    }

    private System.Windows.Media.SolidColorBrush GetResourceBrush(
        string key)
    {
        return Resources[key]
                   as System.Windows.Media.SolidColorBrush
               ?? new System.Windows.Media.SolidColorBrush(
                   System.Windows.Media.Color.FromRgb(
                       25,
                       25,
                       30));
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
            System.Windows.MessageBox.Show(
                "UbiSlot couldn't open GitHub.",
                "Support",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
