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

public partial class MainWindow : Window
{
    private readonly SpoolManager _spoolManager;
    private readonly AchievementCache _achievementCache;
    private readonly GameDatabase _gameDatabase;
    private readonly GameCoverService _gameCoverService;

    private readonly List<GameCardData> _games = [];

    public MainWindow()
    {
        InitializeComponent();

        _spoolManager = new SpoolManager();
        _achievementCache = new AchievementCache();
        _gameDatabase = new GameDatabase();
        _gameCoverService = new GameCoverService();

        Loaded +=
            async (_, _) =>
            {
                await LoadGamesAsync();
            };
    }

    private async Task LoadGamesAsync()
    {
        try
        {
            GameSearchBox.Clear();
            SearchPopup.IsOpen = false;

            GameGrid.Children.Clear();
            _games.Clear();

            StatusText.Text =
                "Scanning Ubisoft data...";

            List<string> spoolFiles =
                _spoolManager.FindSpoolFiles();

            if (spoolFiles.Count > 0)
            {
                _spoolManager.CreatePermanentBackups(
                    spoolFiles,
                    _spoolManager.GetSpoolRoot());
            }

            await Dispatcher.InvokeAsync(
                () => { },
                System.Windows.Threading.DispatcherPriority.Loaded);

            foreach (string spoolFile in spoolFiles)
            {
                string gameId =
                    _spoolManager.GetGameId(
                        spoolFile);

                List<SpoolRecord> spoolRecords =
                    _spoolManager.GetRecords(
                        spoolFile);

                List<AchievementDefinition> achievements =
                    _achievementCache.GetAchievementsForGame(
                        gameId,
                        spoolRecords);

                int unlocked =
                    achievements.Count(
                        achievement =>
                            achievement.IsUnlocked);

                int total =
                    achievements.Count;

                string gameName =
                    _gameDatabase.GetGameName(
                        gameId)
                    ?? $"Ubisoft Game {gameId}";

                string? coverPath =
                    await _gameCoverService.GetCoverAsync(
                        gameName);

                _games.Add(
                    new GameCardData
                    {
                        SpoolFile =
                            spoolFile,

                        GameId =
                            gameId,

                        GameName =
                            gameName,

                        Unlocked =
                            unlocked,

                        Total =
                            total,

                        CoverPath =
                            coverPath
                    });
            }

            UpdateGameList();

            StatusText.Text =
                "Ready";
        }
        catch
        {
            GameCountText.Text =
                "Unable to scan games.";

            StatusText.Text =
                "UbiSlot couldn't load your Ubisoft game data.";
        }
    }

    private void UpdateGameList()
    {
        GameGrid.Children.Clear();

        string search =
            GameSearchBox.Text.Trim();

        IEnumerable<GameCardData> filteredGames =
            _games;

        if (!string.IsNullOrWhiteSpace(
                search))
        {
            filteredGames =
                _games.Where(
                    game =>
                        game.GameName.Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        game.GameId.Contains(
                            search,
                            StringComparison.OrdinalIgnoreCase));
        }

        List<GameCardData> visibleGames =
            filteredGames.ToList();

        foreach (GameCardData game in visibleGames)
        {
            AddGameCard(
                game.SpoolFile,
                game.GameId,
                game.GameName,
                game.Unlocked,
                game.Total,
                game.CoverPath);
        }

        if (string.IsNullOrWhiteSpace(
                search))
        {
            GameCountText.Text =
                _games.Count == 1
                    ? "1 game found"
                    : $"{_games.Count} games found";
        }
        else
        {
            GameCountText.Text =
                visibleGames.Count == 1
                    ? "1 game found"
                    : $"{visibleGames.Count} games found";
        }
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
                    GameSearchBox.Focus();

                    Keyboard.Focus(
                        GameSearchBox);

                    GameSearchBox.SelectAll();
                }),
            System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SearchPopup_Closed(
    object? sender,
    EventArgs e)
    {
    }

    private void GameSearchBox_TextChanged(
    object sender,
    TextChangedEventArgs e)
    {
        if (_games.Count == 0)
        {
            return;
        }

        UpdateGameList();
    }

    private void GameSearchBox_KeyDown(
    object sender,
    KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
        }
    }

    private void AddGameCard(
        string spoolFile,
        string gameId,
        string gameName,
        int unlocked,
        int total,
        string? coverPath)
    {
        double columnWidth =
            GameGrid.ActualWidth / 5.0;

        double coverWidth =
            Math.Max(
                170,
                Math.Min(
                    270,
                    columnWidth - 35));

        double coverHeight =
            coverWidth * 1.5;

        var button =
            new Button
            {
                Style =
                    (Style)FindResource(
                        "GameButtonStyle"),

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            61,
                            27,
                            93)),

                BorderBrush =
                    new SolidColorBrush(
                        Color.FromRgb(
                            61,
                            27,
                            93)),

                BorderThickness =
                    new Thickness(0),

                Padding =
                    new Thickness(0),

                Margin =
                    new Thickness(
                        6,
                        8,
                        6,
                        20),

                HorizontalContentAlignment =
                    HorizontalAlignment.Center,

                VerticalContentAlignment =
                    VerticalAlignment.Top,

                Cursor =
                    Cursors.Hand
            };

        var content =
            new StackPanel
            {
                HorizontalAlignment =
                    HorizontalAlignment.Center
            };

        var cover =
            new Border
            {
                Width =
                    coverWidth,

                Height =
                    coverHeight,

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                Background =
                    new SolidColorBrush(
                        Color.FromRgb(
                            36,
                            36,
                            44)),

                CornerRadius =
                    new CornerRadius(10)
            };

        cover.Clip =
            new RectangleGeometry(
                new Rect(
                    0,
                    0,
                    coverWidth,
                    coverHeight),
                10,
                10);

        if (!string.IsNullOrWhiteSpace(
                coverPath) &&
            File.Exists(
                coverPath))
        {
            var image =
                new Image
                {
                    Stretch =
                        Stretch.UniformToFill,

                    HorizontalAlignment =
                        HorizontalAlignment.Stretch,

                    VerticalAlignment =
                        VerticalAlignment.Stretch
                };

            image.Source =
                new BitmapImage(
                    new Uri(
                        coverPath!,
                        UriKind.Absolute));

            cover.Child =
                image;
        }
        else
        {
            var gameIdText =
                new TextBlock
                {
                    Text =
                        gameId,

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    FontSize =
                        24,

                    Foreground =
                        new SolidColorBrush(
                            Color.FromRgb(
                                130,
                                130,
                                140))
                };

            cover.Child =
                gameIdText;
        }

        content.Children.Add(
            cover);

        var title =
            new TextBlock
            {
                Text =
                    gameName,

                Width =
                    coverWidth,

                Margin =
                    new Thickness(
                        0,
                        14,
                        0,
                        0),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                TextAlignment =
                    TextAlignment.Center,

                FontSize =
                    17,

                FontWeight =
                    FontWeights.SemiBold,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            135,
                            206,
                            235)),

                TextWrapping =
                    TextWrapping.Wrap
            };

        content.Children.Add(
            title);

        var achievementText =
            new TextBlock
            {
                Text =
                    $"{unlocked}/{total} achievements",

                Margin =
                    new Thickness(
                        0,
                        6,
                        0,
                        0),

                HorizontalAlignment =
                    HorizontalAlignment.Center,

                TextAlignment =
                    TextAlignment.Center,

                Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            255,
                            127,
                            80)),

                FontSize =
                    14,

                FontWeight =
                    FontWeights.Medium
            };

        content.Children.Add(
            achievementText);

        button.Content =
            content;

        button.Click +=
            async (_, _) =>
            {
                await OpenGameAsync(
                    spoolFile,
                    gameId);
            };

        GameGrid.Children.Add(
            button);
    }

    private async Task OpenGameAsync(
        string spoolFile,
        string gameId)
    {
        SearchPopup.IsOpen = false;

        var achievementWindow =
            new AchievementWindow(
                spoolFile,
                gameId)
            {
                Owner =
                    this
            };

        achievementWindow.ShowDialog();

        await LoadGamesAsync();
    }

    private async void RefreshButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        GameSearchBox.Clear();

        SearchPopup.IsOpen = false;

        await LoadGamesAsync();
    }

    private void SupportButton_Click(
    object sender,
    RoutedEventArgs e)
    {
        var supportWindow =
            new SupportWindow
            {
                Owner = this
            };

        supportWindow.ShowDialog();
    }

    private class GameCardData
    {
        public string SpoolFile { get; init; } = string.Empty;
        public string GameId { get; init; } = string.Empty;
        public string GameName { get; init; } = string.Empty;
        public int Unlocked { get; init; }
        public int Total { get; init; }
        public string? CoverPath { get; init; }
    }
}