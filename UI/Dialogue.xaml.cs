using System.Windows;
using System.Windows.Media;
using UbiSlot.Core;

namespace UbiSlot;

public enum DialogueIconKind
{
    Warning,
    Information,
    Error
}

public sealed class DialogueResult
{
    public bool Confirmed { get; init; }
}

public partial class Dialogue : Window
{
    private readonly UbiSlotSettings _settings;

    public bool Confirmed { get; private set; }

    public Dialogue()
    {
        InitializeComponent();

        _settings =
            UbiSlotSettings.Load();

        ApplyWindowTheme(
            _settings.IsDarkTheme);
    }

    public static bool ShowConfirmation(
        Window? owner,
        string title,
        string message,
        string primaryText = "OK",
        string cancelText = "Cancel",
        DialogueIconKind iconKind = DialogueIconKind.Warning)
    {
        var dialogue =
            new Dialogue();

        dialogue.Configure(
            title,
            message,
            primaryText,
            cancelText,
            showCancel: true,
            iconKind);

        if (owner != null)
        {
            dialogue.Owner =
                owner;
        }

        dialogue.ShowDialog();

        return dialogue.Confirmed;
    }

    public static void ShowInformation(
        Window? owner,
        string title,
        string message)
    {
        var dialogue =
            new Dialogue();

        dialogue.Configure(
            title,
            message,
            "OK",
            string.Empty,
            showCancel: false,
            DialogueIconKind.Information);

        if (owner != null)
        {
            dialogue.Owner =
                owner;
        }

        dialogue.ShowDialog();
    }

    public static void ShowError(
        Window? owner,
        string title,
        string message)
    {
        var dialogue =
            new Dialogue();

        dialogue.Configure(
            title,
            message,
            "OK",
            string.Empty,
            showCancel: false,
            DialogueIconKind.Error);

        if (owner != null)
        {
            dialogue.Owner =
                owner;
        }

        dialogue.ShowDialog();
    }

    private void Configure(
        string title,
        string message,
        string primaryText,
        string cancelText,
        bool showCancel,
        DialogueIconKind iconKind)
    {
        Title =
            title;

        DialogTitleText.Text =
            title;

        DialogMessageText.Text =
            message;

        PrimaryButton.Content =
            string.IsNullOrWhiteSpace(
                primaryText)
                ? "OK"
                : primaryText;

        CancelButton.Content =
            string.IsNullOrWhiteSpace(
                cancelText)
                ? "Cancel"
                : cancelText;

        CancelButton.Visibility =
            showCancel
                ? Visibility.Visible
                : Visibility.Collapsed;

        ConfigureIcon(
            iconKind);
    }

    private void ConfigureIcon(
        DialogueIconKind iconKind)
    {
        switch (iconKind)
        {
            case DialogueIconKind.Error:

                DialogIconBorder.Background =
                    GetSolidBrush(
                        Color.FromRgb(
                            108,
                            37,
                            43));

                DialogIconText.Text =
                    "×";

                break;

            case DialogueIconKind.Information:

                DialogIconBorder.Background =
                    GetSolidBrush(
                        Color.FromRgb(
                            61,
                            49,
                            113));

                DialogIconText.Text =
                    "i";

                break;

            default:

                DialogIconBorder.Background =
                    GetSolidBrush(
                        Color.FromRgb(
                            107,
                            78,
                            16));

                DialogIconText.Text =
                    "!";

                break;
        }
    }

    private void PrimaryButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Confirmed =
            true;

        DialogResult =
            true;
    }


    private void CancelButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        Confirmed =
            false;

        DialogResult =
            false;
    }


    protected override void OnClosing(
        System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult != true)
        {
            Confirmed =
                false;
        }

        base.OnClosing(
            e);
    }

    private void ApplyWindowTheme(
        bool dark)
    {
        if (dark)
        {
            SetResourceBrushColor(
                "WindowBackground",
                Color.FromRgb(
                    16,
                    16,
                    20));

            SetResourceBrushColor(
                "PanelBackground",
                Color.FromRgb(
                    21,
                    21,
                    26));

            SetResourceBrushColor(
                "FieldBackground",
                Color.FromRgb(
                    29,
                    29,
                    36));

            SetResourceBrushColor(
                "HoverBackground",
                Color.FromRgb(
                    36,
                    36,
                    44));

            SetResourceBrushColor(
                "PressedBackground",
                Color.FromRgb(
                    28,
                    28,
                    34));

            SetResourceBrushColor(
                "BorderColor",
                Color.FromRgb(
                    41,
                    41,
                    49));

            SetResourceBrushColor(
                "PrimaryText",
                Color.FromRgb(
                    242,
                    242,
                    245));

            SetResourceBrushColor(
                "SecondaryText",
                Color.FromRgb(
                    133,
                    133,
                    143));

            SetResourceBrushColor(
                "MutedText",
                Color.FromRgb(
                    102,
                    102,
                    112));

            SetResourceBrushColor(
                "DisabledText",
                Color.FromRgb(
                    119,
                    119,
                    128));
        }
        else
        {
            SetResourceBrushColor(
                "WindowBackground",
                Color.FromRgb(
                    245,
                    245,
                    248));

            SetResourceBrushColor(
                "PanelBackground",
                Color.FromRgb(
                    255,
                    255,
                    255));

            SetResourceBrushColor(
                "FieldBackground",
                Color.FromRgb(
                    238,
                    238,
                    243));

            SetResourceBrushColor(
                "HoverBackground",
                Color.FromRgb(
                    230,
                    230,
                    236));

            SetResourceBrushColor(
                "PressedBackground",
                Color.FromRgb(
                    224,
                    224,
                    231));

            SetResourceBrushColor(
                "BorderColor",
                Color.FromRgb(
                    214,
                    214,
                    222));

            SetResourceBrushColor(
                "PrimaryText",
                Color.FromRgb(
                    25,
                    25,
                    30));

            SetResourceBrushColor(
                "SecondaryText",
                Color.FromRgb(
                    95,
                    95,
                    107));

            SetResourceBrushColor(
                "MutedText",
                Color.FromRgb(
                    119,
                    119,
                    132));

            SetResourceBrushColor(
                "DisabledText",
                Color.FromRgb(
                    155,
                    155,
                    165));
        }

        SetResourceBrushColor(
            "AccentColor",
            Color.FromRgb(
                61,
                27,
                93));

        SetResourceBrushColor(
            "AccentHoverColor",
            Color.FromRgb(
                81,
                40,
                120));

        SetResourceBrushColor(
            "AccentPressedColor",
            Color.FromRgb(
                43,
                19,
                63));

        Background =
            GetThemeBrush(
                "WindowBackground");

        Foreground =
            GetThemeBrush(
                "PrimaryText");
    }


    private void SetResourceBrushColor(
        string key,
        Color color)
    {
        Resources[key] =
            new SolidColorBrush(
                color);
    }


    private SolidColorBrush GetThemeBrush(
        string key)
    {
        return Resources[key]
                   as SolidColorBrush
               ?? new SolidColorBrush(
                   Color.FromRgb(
                       25,
                       25,
                       30));
    }


    private SolidColorBrush GetSolidBrush(
        Color color)
    {
        return new SolidColorBrush(
            color);
    }
}
