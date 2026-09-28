using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using UbiSlot.Core;

using FormsFolderBrowserDialog =
    System.Windows.Forms.FolderBrowserDialog;

using FormsDialogResult =
    System.Windows.Forms.DialogResult;

using WpfMessageBox =
    System.Windows.MessageBox;

using WpfMessageBoxButton =
    System.Windows.MessageBoxButton;

using WpfMessageBoxImage =
    System.Windows.MessageBoxImage;

using WpfMessageBoxResult =
    System.Windows.MessageBoxResult;

namespace UbiSlot;

public partial class SettingsWindow : Window
{
    private UbiSlotSettings _settings;

    public bool SettingsChanged { get; private set; }
    public SettingsWindow()
    {
        InitializeComponent();

        _settings =
            UbiSlotSettings.Load();

        LoadSettingsIntoUi();

        ApplyWindowTheme(
            _settings.IsDarkTheme);
    }
    private void LoadSettingsIntoUi()
    {
        DarkModeToggle.IsChecked =
            _settings.IsDarkTheme;

        ShowGameNameToggle.IsChecked =
            _settings.ShowGameName;

        ShowCompletionToggle.IsChecked =
            _settings.ShowCompletion;

        HideZeroAchievementGamesToggle.IsChecked =
            _settings.HideZeroAchievementGames;

        AutoUpdateToggle.IsChecked =
            _settings.AutoUpdateCheck;

        BackupPathTextBox.Text =
            _settings.BackupPath;

        UpdateLastCheckedText();
    }

    private void SaveSettingsFromUi()
    {
        _settings.Theme =
            DarkModeToggle.IsChecked == true
                ? "Dark"
                : "Light";

        _settings.ShowGameName =
            ShowGameNameToggle.IsChecked == true;

        _settings.ShowCompletion =
            ShowCompletionToggle.IsChecked == true;

        _settings.HideZeroAchievementGames =
            HideZeroAchievementGamesToggle.IsChecked == true;

        _settings.AutoUpdateCheck =
            AutoUpdateToggle.IsChecked == true;

        string backupPath =
            BackupPathTextBox.Text.Trim();

        _settings.BackupPath =
            string.IsNullOrWhiteSpace(
                backupPath)
                ? UbiSlotSettings.DefaultBackupPath
                : backupPath;

        _settings.Save();

        SettingsChanged =
            true;
    }
    private void ThemeToggle_Checked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyWindowTheme(
            dark: true);
    }
    private void ThemeToggle_Unchecked(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsInitialized)
        {
            return;
        }

        ApplyWindowTheme(
            dark: false);
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
                "BorderColor",
                Color.FromRgb(
                    41,
                    41,
                    49));

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
                "ToggleOffTrack",
                Color.FromRgb(
                    41,
                    41,
                    49));

            SetResourceBrushColor(
                "ToggleKnob",
                Color.FromRgb(
                    237,
                    237,
                    242));

            Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        16,
                        16,
                        20));

            Foreground =
                Brushes.White;
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
                    250,
                    250,
                    252));

            SetResourceBrushColor(
                "HoverBackground",
                Color.FromRgb(
                    238,
                    238,
                    243));

            SetResourceBrushColor(
                "BorderColor",
                Color.FromRgb(
                    215,
                    215,
                    222));

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
                "PrimaryText",
                Color.FromRgb(
                    25,
                    25,
                    30));

            SetResourceBrushColor(
                "SecondaryText",
                Color.FromRgb(
                    80,
                    80,
                    90));

            SetResourceBrushColor(
                "MutedText",
                Color.FromRgb(
                    105,
                    105,
                    115));

            SetResourceBrushColor(
                "ToggleOffTrack",
                Color.FromRgb(
                    215,
                    215,
                    222));

            SetResourceBrushColor(
                "ToggleKnob",
                Color.FromRgb(
                    255,
                    255,
                    255));

            Background =
                new SolidColorBrush(
                    Color.FromRgb(
                        245,
                        245,
                        248));

            Foreground =
                new SolidColorBrush(
                    Color.FromRgb(
                        25,
                        25,
                        30));
        }
    }
    private void SetResourceBrushColor(
        string key,
        Color color)
    {
        Resources[key] =
            new SolidColorBrush(
                color);
    }

    private void ChangeBackupFolderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        using var dialog =
            new FormsFolderBrowserDialog
            {
                Description =
                    "Choose the folder where UbiSlot stores its backups.",

                UseDescriptionForTitle =
                    true,

                ShowNewFolderButton =
                    true,

                SelectedPath =
                    Directory.Exists(
                        BackupPathTextBox.Text)
                        ? BackupPathTextBox.Text
                        : UbiSlotSettings.DefaultBackupPath
            };

        FormsDialogResult result =
            dialog.ShowDialog();

        if (result !=
            FormsDialogResult.OK)
        {
            return;
        }

        BackupPathTextBox.Text =
            dialog.SelectedPath;

        SettingsChanged =
            true;
    }
    private void OpenBackupFolderButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            string folder =
                BackupPathTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(
                    folder))
            {
                folder =
                    UbiSlotSettings.DefaultBackupPath;
            }

            Directory.CreateDirectory(
                folder);

            Process.Start(
                new ProcessStartInfo
                {
                    FileName =
                        folder,

                    UseShellExecute =
                        true
                });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                "UbiSlot couldn't open the backup folder.\n\n" +
                ex.Message,
                "UbiSlot Settings",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }
    private void DeleteLogButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        WpfMessageBoxResult result =
            WpfMessageBox.Show(
                "Delete the UbiSlot activity log?\n\n" +
                "Pinned-game state is reconstructed from this log, " +
                "so deleting it will also reset the pinned games.",
                "Delete Activity Log",
                WpfMessageBoxButton.YesNo,
                WpfMessageBoxImage.Warning);

        if (result !=
            WpfMessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            string logPath =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "UbiSlot_Cache",
                    "UbiSlot.log");

            if (File.Exists(
                    logPath))
            {
                File.Delete(
                    logPath);
            }

            SettingsChanged =
                true;

            WpfMessageBox.Show(
                "The activity log has been deleted.",
                "UbiSlot Settings",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                "UbiSlot couldn't delete the activity log.\n\n" +
                ex.Message,
                "UbiSlot Settings",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }
    private async void CheckUpdatesButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled =
            false;

        UpdateStatusText.Text =
            "Checking for updates...";

        try
        {
            GitHubReleaseInfo? update =
                await _settings.CheckForUpdateAsync();

            _settings.LastUpdateCheckUtc =
                DateTimeOffset.UtcNow;

            _settings.Save();

            UpdateLastCheckedText();

            if (update == null)
            {
                UpdateStatusText.Text =
                    "You are using the latest available version.";

                return;
            }

            UpdateStatusText.Text =
                $"Update available: {update.TagName}";

            WpfMessageBoxResult result =
                WpfMessageBox.Show(
                    $"A newer UbiSlot version is available.\n\n" +
                    $"{update.Name}\n" +
                    $"{update.TagName}\n\n" +
                    "Open the GitHub release page?",
                    "UbiSlot Update",
                    WpfMessageBoxButton.YesNo,
                    WpfMessageBoxImage.Information);

            if (result ==
                    WpfMessageBoxResult.Yes &&
                !string.IsNullOrWhiteSpace(
                    update.HtmlUrl))
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName =
                            update.HtmlUrl,

                        UseShellExecute =
                            true
                    });
            }
        }
        catch (Exception ex)
        {
            UpdateStatusText.Text =
                "Update check failed.";

            WpfMessageBox.Show(
                "UbiSlot couldn't check for updates.\n\n" +
                ex.Message,
                "UbiSlot Settings",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
        finally
        {
            CheckUpdatesButton.IsEnabled =
                true;
        }
    }
    private void SaveButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SaveSettingsFromUi();

        DialogResult =
            true;
    }
    private void CloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        DialogResult =
            false;
    }
    private void UpdateLastCheckedText()
    {
        if (_settings.LastUpdateCheckUtc == null)
        {
            LastCheckedText.Text =
                "Never checked";

            return;
        }

        LastCheckedText.Text =
            "Last checked: " +
            $"{_settings.LastUpdateCheckUtc.Value.LocalDateTime:g}";
    }
}