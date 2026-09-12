using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UbiSlot.Core;
using UbiSlot.Games;
using UbiSlot.Ubisoft;

namespace UbiSlot;

public partial class AchievementWindow : Window
{
    private readonly string _spoolFile;
    private readonly string _gameId;

    private readonly SpoolManager _spoolManager;
    private readonly AchievementCache _achievementCache;
    private readonly GameDatabase _gameDatabase;

    private readonly List<AchievementDefinition> _achievements = [];

    private readonly List<CheckBox> _selectionBoxes = [];

    private readonly Dictionary<AchievementDefinition, Border> _achievementCards = [];

    public AchievementWindow(
        string spoolFile,
        string gameId)
    {
        InitializeComponent();

        _spoolFile = spoolFile;
        _gameId = gameId;

        _spoolManager = new SpoolManager();
        _achievementCache = new AchievementCache();
        _gameDatabase = new GameDatabase();

        LoadAchievements();
    }

    private void LoadAchievements()
    {
        try
        {
            List<SpoolRecord> spoolRecords =
                _spoolManager.GetRecords(
                    _spoolFile);

            _achievements.Clear();

            _achievements.AddRange(
                _achievementCache.GetAchievementsForGame(
                    _gameId,
                    spoolRecords));

            int unlocked =
                _achievements.Count(
                    achievement =>
                        achievement.IsUnlocked);

            string gameName =
                _gameDatabase.GetGameName(
                    _gameId)
                ?? $"Game {_gameId}";

            GameTitleText.Text =
                gameName;

            ProgressText.Text =
                $"{unlocked}/{_achievements.Count} unlocked";

            AchievementList.Children.Clear();
            _selectionBoxes.Clear();
            _achievementCards.Clear();

            foreach (AchievementDefinition achievement in _achievements)
            {
                AddAchievement(achievement);
            }

            UpdateAchievementList();
            UpdateSelection();
        }
        catch
        {
            MessageBox.Show(
                "UbiSlot couldn't load this game's achievements.",
                "UbiSlot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Close();
        }
    }

    private void AddAchievement(
        AchievementDefinition achievement)
    {
        var card =
            new Border
            {
                Style =
                    (Style)FindResource(
                        "AchievementCardStyle")
            };

        var grid =
            new Grid();

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width =
                    new GridLength(
                        1,
                        GridUnitType.Star)
            });

        grid.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        var iconBorder =
            new Border
            {
                Width = 64,
                Height = 64,
                CornerRadius =
                    new CornerRadius(5),
                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            36,
                            36,
                            44))
            };

        if (!string.IsNullOrWhiteSpace(
                achievement.IconPath) &&
            File.Exists(
                achievement.IconPath))
        {
            var image =
                new Image
                {
                    Stretch =
                        Stretch.Uniform
                };

            image.Source =
                new BitmapImage(
                    new Uri(
                        achievement.IconPath!,
                        UriKind.Absolute));

            iconBorder.Child =
                image;
        }
        else
        {
            var text =
                new TextBlock
                {
                    Text = "?",
                    FontSize = 24,
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                120,
                                120,
                                130)),
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            iconBorder.Child =
                text;
        }

        Grid.SetColumn(
            iconBorder,
            0);

        grid.Children.Add(
            iconBorder);

        var textPanel =
            new StackPanel
            {
                Margin =
                    new Thickness(
                        16,
                        2,
                        16,
                        2)
            };

        var name =
            new TextBlock
            {
                Text =
                    achievement.Name,
                FontSize = 15,
                FontWeight =
                    FontWeights.SemiBold
            };

        textPanel.Children.Add(
            name);

        var description =
            new TextBlock
            {
                Text =
                    achievement.Description,
                Margin =
                    new Thickness(
                        0,
                        5,
                        0,
                        0),
                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            135,
                            135,
                            145)),
                FontSize = 12,
                TextWrapping =
                    TextWrapping.Wrap
            };

        textPanel.Children.Add(
            description);

        if (!achievement.IsUnlocked)
        {
            var lockedText =
                new TextBlock
                {
                    Text = "Locked",
                    Margin =
                        new Thickness(
                            0,
                            7,
                            0,
                            0),
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                130,
                                130,
                                140)),
                    FontSize = 11
                };

            textPanel.Children.Add(
                lockedText);
        }

        Grid.SetColumn(
            textPanel,
            1);

        grid.Children.Add(
            textPanel);

        _achievementCards[achievement] =
            card;

        if (achievement.IsUnlocked)
        {
            var unlockDateText =
                new TextBlock
                {
                    Text =
                        achievement.UnlockTimeUtc.HasValue
                            ? achievement.UnlockTimeUtc.Value.ToLocalTime().ToString(
                                "dd MMM yyyy, HH:mm")
                            : string.Empty,
                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                80,
                                190,
                                120)),
                    FontSize = 15,
                    FontWeight =
                        FontWeights.SemiBold,
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    HorizontalAlignment =
                        HorizontalAlignment.Center
                };

            Grid.SetColumn(
                unlockDateText,
                2);

            grid.Children.Add(
                unlockDateText);
        }
        else
        {
            var checkBox =
                new CheckBox
                {
                    Width = 28,
                    Height = 28,
                    RenderTransform =
                        new ScaleTransform(1.8, 1.8),
                    RenderTransformOrigin =
                        new Point(0.5, 0.5),
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    HorizontalAlignment =
                        HorizontalAlignment.Center
                };

            checkBox.Tag =
                achievement;

            checkBox.Checked +=
                SelectionChanged;

            checkBox.Unchecked +=
                SelectionChanged;

            _selectionBoxes.Add(
                checkBox);

            Grid.SetColumn(
                checkBox,
                2);

            grid.Children.Add(
                checkBox);
        }

        card.Child =
            grid;

        AchievementList.Children.Add(
            card);
    }

    private void SelectionChanged(
        object sender,
        RoutedEventArgs e)
    {
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        int selected =
            _selectionBoxes.Count(
                checkBox =>
                    checkBox.IsChecked == true);

        int locked =
            _selectionBoxes.Count(
                checkBox =>
                    checkBox.IsEnabled);

        int lockedSelected =
            _selectionBoxes.Count(
                checkBox =>
                    checkBox.IsEnabled &&
                    checkBox.IsChecked == true);

        SelectionText.Text =
            $"{selected} selected";

        GetAchievementButton.IsEnabled =
            selected > 0;

        if (locked > 0 &&
            lockedSelected == locked)
        {
            SelectAllLockedButton.Content =
                "Deselect All";
        }
        else
        {
            SelectAllLockedButton.Content =
                "Select All Locked";
        }
    }

    private void SelectAllLockedButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        int lockedSelected =
            _selectionBoxes.Count(
                checkBox =>
                    checkBox.IsEnabled &&
                    checkBox.IsChecked == true);

        int locked =
            _selectionBoxes.Count(
                checkBox =>
                    checkBox.IsEnabled);

        bool shouldDeselect =
            locked > 0 &&
            lockedSelected == locked;

        foreach (CheckBox checkBox in _selectionBoxes)
        {
            if (!checkBox.IsEnabled)
            {
                continue;
            }

            checkBox.IsChecked =
                !shouldDeselect;
        }

        UpdateSelection();
    }

    private void SearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SearchPopup.IsOpen)
        {
            SearchPopup.IsOpen = false;
            return;
        }

        SearchPopup.IsOpen = true;

        Dispatcher.BeginInvoke(
            new Action(
                () =>
                {
                    AchievementSearchBox.Focus();

                    Keyboard.Focus(
                        AchievementSearchBox);

                    AchievementSearchBox.SelectAll();
                }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SearchPopup_Closed(
    object? sender,
    EventArgs e)
    {
    }

    private void AchievementSearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateAchievementList();
    }

    private void UpdateAchievementList()
    {
        string search =
            AchievementSearchBox.Text.Trim();

        foreach (KeyValuePair<AchievementDefinition, Border> item in
                 _achievementCards)
        {
            AchievementDefinition achievement =
                item.Key;

            bool visible =
                string.IsNullOrWhiteSpace(search)
                ||
                achievement.Name.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase)
                ||
                achievement.AchievementId
                    .ToString()
                    .Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase);

            item.Value.Visibility =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private void RestoreOriginalButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBoxResult result =
            MessageBox.Show(
                "Restore the original achievement data for this game?\n\n" +
                "Any achievement changes made by UbiSlot will be removed. "+
                "This does not lock the achievements that has been already synchronized.",
                "Restore Original",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            _spoolManager.RestoreOriginal(
                _spoolFile);

            MessageBox.Show(
                "Original achievement data restored.",
                "UbiSlot",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            LoadAchievements();
        }
        catch
        {
            MessageBox.Show(
                "UbiSlot couldn't restore the original achievement data.",
                "UbiSlot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void BackButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        AchievementSearchBox.Clear();
        SearchPopup.IsOpen = false;

        Close();
    }

    private void GetAchievementButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        List<AchievementDefinition> selected =
            _selectionBoxes
                .Where(
                    checkBox =>
                        checkBox.IsChecked == true)
                .Select(
                    checkBox =>
                        (AchievementDefinition)
                        checkBox.Tag!)
                .ToList();

        if (selected.Count == 0)
        {
            return;
        }

        IEnumerable<string> displayedNames =
            selected
                .Take(5)
                .Select(
                    achievement =>
                        $"• {achievement.Name}");

        string names =
            string.Join(
                "\n",
                displayedNames);

        int remaining =
            selected.Count - 5;

        if (remaining > 0)
        {
            names +=
                $"\n(+{remaining})";
        }

        string message =
            "Do you want to unlock the following achievement(s):"
            + "\n\n"
            + names
            + "\n\n"
            + "Achievement(s) once unlocked CANNOT be locked again.";

        MessageBoxResult result =
            MessageBox.Show(
                message,
                "Confirm Achievement Unlock",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

        if (result != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            UnlockAchievements(
                selected);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "UbiSlot couldn't access this game's data.\n\n" +
                "Please close the game and try again.",
                "UbiSlot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (IOException)
        {
            MessageBox.Show(
                "UbiSlot couldn't save the achievement.\n\n" +
                "Please close the game and try again.",
                "UbiSlot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            MessageBox.Show(
                "Something went wrong while saving the achievement.\n\n" +
                "Nothing was changed.",
                "UbiSlot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void UnlockAchievements(
        List<AchievementDefinition> selected)
    {
        List<SpoolRecord> existingRecords =
            _spoolManager.GetRecords(
                _spoolFile);

        var existingIds =
            existingRecords
                .Select(
                    record =>
                        record.AchievementId)
                .ToHashSet();

        var newRecords =
            new List<SpoolRecord>();

        foreach (AchievementDefinition achievement in selected)
        {
            if (existingIds.Contains(
                    achievement.AchievementId))
            {
                continue;
            }

            SpoolRecord record =
                _spoolManager.CreateAchievementRecord(
                    achievement.AchievementId);

            newRecords.Add(
                record);
        }

        if (newRecords.Count == 0)
        {
            return;
        }

        List<SpoolRecord> finalRecords =
            _spoolManager.PrepareRecordsForWrite(
                existingRecords,
                newRecords);

        byte[] serializedData =
            _spoolManager.SerializeRecords(
                finalRecords);

        _spoolManager.ValidateSerializedRecords(
            serializedData);

        _spoolManager.WriteSpoolFile(
            _spoolFile,
            serializedData);

        MessageBox.Show(
            "Achievement(s) granted.\n\n" +
            "Please run the game to synchronise " +
            "to cloud.",
            "UbiSlot",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        AchievementSearchBox.Clear();
        SearchPopup.IsOpen = false;

        Close();
    }
}