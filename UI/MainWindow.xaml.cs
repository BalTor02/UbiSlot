using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using UbiSlot.Core;
using UbiSlot.Games;
using UbiSlot.Ubisoft;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfImage = System.Windows.Controls.Image;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfStretch = System.Windows.Media.Stretch;
using WpfTextAlignment = System.Windows.TextAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace UbiSlot;

public partial class MainWindow : Window
{
    private readonly SpoolManager _spoolManager;

    private readonly AchievementCache _achievementCache;

    private readonly GameDatabase _gameDatabase;

    private readonly GameCoverService _gameCoverService;

    private readonly OwnershipManager _ownershipManager;

    private readonly List<GameCardData> _games = [];

    private readonly HashSet<uint> _pinnedGameIds;

    private readonly Dictionary<string, WpfImage> _coverImages =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, TextBlock> _coverFallbacks =
        new(StringComparer.OrdinalIgnoreCase);

    private UbiSlotSettings _settings;

    private int _coverLoadGeneration;

    public MainWindow()
    {
        InitializeComponent();

        _settings =
            UbiSlotSettings.Load();

        ApplyMainWindowTheme();

        _pinnedGameIds =
            LoadPinnedGameIds();

        LogActivity(
            "APP_START",
            $"UbiSlot started | Pinned games={_pinnedGameIds.Count}");

        _spoolManager =
            new SpoolManager();

        _achievementCache =
            new AchievementCache();

        _gameDatabase =
            new GameDatabase();

        _gameCoverService =
            new GameCoverService();

        _ownershipManager =
            new OwnershipManager(
                _gameDatabase);

        Loaded +=
            MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        Loaded -=
            MainWindow_Loaded;

        await LoadGamesAsync();

        _ = CheckForAutomaticUpdateAsync();
    }

    private async Task LoadGamesAsync()
    {
        int coverGeneration =
            ++_coverLoadGeneration;

        LogActivity(
            "LIBRARY_LOAD",
            "Loading Ubisoft library");

        try
        {
            GameGrid.Children.Clear();

            _games.Clear();


            GameRunningText.Text =
                string.Empty;

            GameRunningText.Visibility =
                Visibility.Collapsed;


            StatusText.Text =
                "Loading Ubisoft ownership...";


            GameCountText.Text =
                "Scanning...";

            List<uint> ownedGameIds =
                _ownershipManager.GetOwnedGameIds();


            Console.WriteLine(
                $"[MainWindow] Owned games: {ownedGameIds.Count}");

            LogActivity(
                "OWNERSHIP_SCAN",
                $"Ownership scan completed | Games={ownedGameIds.Count}");


            if (ownedGameIds.Count == 0)
            {
                GameCountText.Text =
                    "No owned games found";

                StatusText.Text =
                    "No Ubisoft ownership data found.";

                return;
            }

            StatusText.Text =
                "Scanning achievement data...";


            List<string> spoolFiles =
                _spoolManager.FindSpoolFiles();


            Console.WriteLine(
                $"[MainWindow] Spool files: {spoolFiles.Count}");

            LogActivity(
                "SPOOL_SCAN",
                $"Spool scan completed | Files={spoolFiles.Count}");


            var spoolEntries =
                new List<SpoolEntry>();


            foreach (
                string spoolFile
                in spoolFiles)
            {
                try
                {
                    string spoolGameId =
                        _spoolManager.GetGameId(
                            spoolFile);


                    string? spoolGameName =
                        _gameDatabase.GetGameName(
                            spoolGameId);


                    if (string.IsNullOrWhiteSpace(
                            spoolGameName))
                    {
                        Console.WriteLine(
                            $"[Spool] Unknown ID: {spoolGameId}");

                        continue;
                    }


                    spoolEntries.Add(
                        new SpoolEntry
                        {
                            GameId =
                                spoolGameId,

                            GameName =
                                spoolGameName,

                            SpoolFile =
                                spoolFile,

                            NormalizedName =
                                NormalizeGameName(
                                    spoolGameName)
                        });


                    Console.WriteLine(
                        $"[Spool] {spoolGameId} -> {spoolGameName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Spool] Failed: {ex.Message}");
                }
            }

            if (spoolFiles.Count > 0)
            {
                try
                {
                    _spoolManager.CreatePermanentBackups(
                        spoolFiles,
                        _spoolManager.GetSpoolRoot());
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[Spool] Backup failed: {ex.Message}");
                }
            }

            var addedNames =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);


            foreach (
                uint ownedId
                in ownedGameIds)
            {
                string ownershipGameId =
                    ownedId.ToString();


                string? gameName =
                    _gameDatabase.GetGameName(
                        ownershipGameId);


                if (string.IsNullOrWhiteSpace(
                        gameName))
                {
                    Console.WriteLine(
                        "[MainWindow] Unknown ownership ID: " +
                        $"{ownershipGameId}");

                    continue;
                }


                string normalizedName =
                    NormalizeGameName(
                        gameName);


                if (!addedNames.Add(
                        normalizedName))
                {
                    Console.WriteLine(
                        "[MainWindow] Duplicate skipped: " +
                        $"{gameName}");

                    continue;
                }

                SpoolEntry? matchingSpool =
                    FindMatchingSpool(
                        ownershipGameId,
                        normalizedName,
                        spoolEntries);


                string spoolFile =
                    matchingSpool?.SpoolFile
                    ?? string.Empty;


                string achievementGameId =
                    matchingSpool?.GameId
                    ?? ownershipGameId;


                int unlocked =
                    0;


                int total =
                    0;


                if (!string.IsNullOrWhiteSpace(
                        spoolFile) &&
                    File.Exists(
                        spoolFile))
                {
                    try
                    {
                        List<SpoolRecord> records =
                            _spoolManager.GetRecords(
                                spoolFile);


                        List<AchievementDefinition> achievements =
                            _achievementCache
                                .GetAchievementsForGame(
                                    achievementGameId,
                                    records);


                        total =
                            achievements.Count;


                        unlocked =
                            achievements.Count(
                                achievement =>
                                    achievement.IsUnlocked);


                        Console.WriteLine(
                            "[Achievements] " +
                            $"{gameName} -> " +
                            $"{unlocked}/{total} " +
                            $"using spool {achievementGameId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            "[Achievements] Failed for " +
                            $"{gameName}: {ex.Message}");
                    }
                }
                else
                {
                    try
                    {
                        List<AchievementDefinition> achievements =
                            _achievementCache.ReadAchievements(
                                achievementGameId);


                        total =
                            achievements.Count;


                        unlocked =
                            0;


                        Console.WriteLine(
                            "[Achievements] " +
                            $"{gameName} -> " +
                            $"No spool, {total} definitions");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(
                            "[Achievements] " +
                            $"No-spool archive lookup failed for " +
                            $"{gameName}: {ex.Message}");
                    }
                }


                if (_settings.HideZeroAchievementGames &&
                    total == 0)
                {
                    Console.WriteLine(
                        $"[MainWindow] Hidden zero-achievement game: {gameName}");

                    LogActivity(
                        "GAME_HIDDEN",
                        $"Zero-achievement game hidden | ID={ownershipGameId} | Name={gameName}");

                    continue;
                }

                string? coverPath =
                    null;

                _games.Add(
                    new GameCardData
                    {
                        GameId =
                            ownershipGameId,

                        AchievementGameId =
                            achievementGameId,

                        GameName =
                            gameName,

                        SpoolFile =
                            spoolFile,

                        Unlocked =
                            unlocked,

                        Total =
                            total,

                        CoverPath =
                            coverPath,

                        IsPinned =
                            uint.TryParse(
                                ownershipGameId,
                                out uint parsedGameId) &&
                            _pinnedGameIds.Contains(
                                parsedGameId)
                    });
            }

            UpdateGameList();
            CheckRunningGame();

            StatusText.Text =
                "Ready";

            LogActivity(
                "LIBRARY_READY",
                $"Library ready | Games={_games.Count} | Pinned={_pinnedGameIds.Count}");

            _ = LoadMissingCoversAsync(
                coverGeneration);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[MainWindow] Load error: {ex}");

            LogActivity(
                "LIBRARY_ERROR",
                $"Library load failed | Error={ex}");

            GameGrid.Children.Clear();


            GameCountText.Text =
                "Unable to load games.";


            StatusText.Text =
                "UbiSlot couldn't load your Ubisoft game data.";
        }
    }

    private void CheckRunningGame()
    {
        try
        {
            uint? runningGameId =
                UbisoftGameLauncher.GetRunningUbisoftGameId();

            if (!runningGameId.HasValue)
            {
                GameRunningText.Text =
                    string.Empty;

                GameRunningText.Visibility =
                    Visibility.Collapsed;

                Console.WriteLine(
                    "[Game Running Check] No Ubisoft game detected.");

                return;
            }

            string runningGameName =
                _gameDatabase.GetGameName(
                    runningGameId.Value.ToString())
                ?? $"Game {runningGameId.Value}";

            GameRunningText.Text =
                $"{runningGameName} is currently running";

            GameRunningText.Visibility =
                Visibility.Visible;

            Console.WriteLine(
                "[Game Running Check] " +
                $"Running game: {runningGameName} " +
                $"(Ubisoft ID {runningGameId.Value})");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[Game Running Check] " +
                $"Failed: {ex.Message}");

            GameRunningText.Text =
                "Unable to determine whether a Ubisoft game is running";

            GameRunningText.Visibility =
                Visibility.Visible;
        }
    }

    private static SpoolEntry? FindMatchingSpool(
        string ownershipGameId,
        string normalizedName,
        List<SpoolEntry> spoolEntries)
    {
        SpoolEntry? exact =
            spoolEntries.FirstOrDefault(
                spool =>
                    string.Equals(
                        spool.GameId,
                        ownershipGameId,
                        StringComparison.OrdinalIgnoreCase));


        if (exact != null)
        {
            return exact;
        }


        return spoolEntries.FirstOrDefault(
            spool =>
                string.Equals(
                    spool.NormalizedName,
                    normalizedName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeGameName(
        string name)
    {
        string normalized =
            name
                .Replace("®", "")
                .Replace("™", "")
                .Replace("©", "")
                .Replace("(Steam)", "")
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();


        while (normalized.Contains(
                   "  ",
                   StringComparison.Ordinal))
        {
            normalized =
                normalized.Replace(
                    "  ",
                    " ");
        }


        return normalized;
    }

    private void UpdateGameList()
    {
        GameGrid.Children.Clear();

        _coverImages.Clear();
        _coverFallbacks.Clear();


        string search =
            GameSearchBox.Text.Trim();


        IEnumerable<GameCardData> filtered =
            _games;


        if (!string.IsNullOrWhiteSpace(
                search))
        {
            filtered =
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
            filtered
                .OrderByDescending(
                    game => game.IsPinned)
                .ToList();


        foreach (
            GameCardData game
            in visibleGames)
        {
            AddGameCard(
                game.SpoolFile,
                game.GameId,
                game.AchievementGameId,
                game.GameName,
                game.Unlocked,
                game.Total,
                game.CoverPath,
                game.IsPinned);
        }


        GameCountText.Text =
            visibleGames.Count == 1
                ? "1 game found"
                : $"{visibleGames.Count} games found";

        GameScrollViewer.ScrollToTop();
        UpdateDisclaimerVisibility();
    }

    private void GameScrollViewer_ScrollChanged(
        object sender,
        ScrollChangedEventArgs e)
    {
        UpdateDisclaimerVisibility();
    }


    private void UpdateDisclaimerVisibility()
    {
        if (GameScrollViewer.ScrollableHeight <= 0)
        {
            DisclaimerContainer.Opacity = 0;
            return;
        }

        const double bottomThreshold = 2.0;

        bool atBottom =
            GameScrollViewer.VerticalOffset >=
            GameScrollViewer.ScrollableHeight - bottomThreshold;

        DisclaimerContainer.Opacity =
            atBottom ? 1 : 0;
    }

    private void SearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (SearchPopup.IsOpen)
        {
            SearchPopup.IsOpen =
                false;

            return;
        }


        SearchPopup.IsOpen =
            true;

        LogActivity(
            "SEARCH_OPEN",
            "Search opened");


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
        WpfKeyEventArgs e)
    {
        if (e.Key ==
            Key.Enter)
        {
            LogActivity(
                "SEARCH",
                $"Search submitted | Query=\"{GameSearchBox.Text.Trim()}\"");

            e.Handled =
                true;
        }
    }

    private void AddGameCard(
        string spoolFile,
        string gameId,
        string achievementGameId,
        string gameName,
        int unlocked,
        int total,
        string? coverPath,
        bool isPinned)
    {
        double columnWidth =
            GameGrid.ActualWidth / 5.0;

        if (columnWidth <= 0)
        {
            columnWidth =
                220;
        }

        double coverWidth =
            Math.Max(
                170,
                Math.Min(
                    270,
                    columnWidth - 35));

        double coverHeight =
            coverWidth * 1.5;

        var button =
            new WpfButton
            {
                Style =
                    (Style)FindResource(
                        "GameButtonStyle"),

                Background =
                    WpfBrushes.Transparent,

                BorderBrush =
                    WpfBrushes.Transparent,

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
                    WpfHorizontalAlignment.Center,

                VerticalContentAlignment =
                    WpfVerticalAlignment.Top,

                Cursor =
                    WpfCursors.Hand,

                FocusVisualStyle =
                    null,

                RenderTransformOrigin =
                    new WpfPoint(
                        0.5,
                        0.5)
            };

        button.Template =
            CreateTransparentGameCardTemplate();

        var cardScale =
            new ScaleTransform(
                1.0,
                1.0);

        var cardSkew =
            new SkewTransform(
                0.0,
                0.0);

        var cardTranslate =
            new TranslateTransform(
                0.0,
                0.0);

        var cardTransform =
            new TransformGroup();

        cardTransform.Children.Add(
            cardScale);

        cardTransform.Children.Add(
            cardSkew);

        cardTransform.Children.Add(
            cardTranslate);

        button.RenderTransform =
            cardTransform;

        System.Windows.Shapes.Ellipse cursorHalo =
            new System.Windows.Shapes.Ellipse
            {
                Width =
                    180,

                Height =
                    180,

                Fill =
                    new RadialGradientBrush
                    {
                        GradientOrigin =
                            new WpfPoint(0.5, 0.5),

                        Center =
                            new WpfPoint(0.5, 0.5),

                        RadiusX =
                            0.5,

                        RadiusY =
                            0.5,

                        GradientStops =
                            new GradientStopCollection
                            {
                                new GradientStop(
                                    WpfColor.FromArgb(85, 61, 27, 93),
                                    0.0),

                                new GradientStop(
                                    WpfColor.FromArgb(95, 61, 27, 93),
                                    0.30),

                                new GradientStop(
                                    WpfColor.FromArgb(58, 61, 27, 93),
                                    0.62),

                                new GradientStop(
                                    WpfColor.FromArgb(0, 61, 27, 93),
                                    1.0)
                            }
                    },

                Opacity =
                    0.0,

                IsHitTestVisible =
                    false
            };


        var content =
            new StackPanel
            {
                HorizontalAlignment =
                    WpfHorizontalAlignment.Center
            };


        var cardLayout =
            new Grid();

        cardLayout.Children.Add(
            content);

        WpfButton pinButton =
            CreatePinButton(
                gameId,
                gameName,
                isPinned);

        Grid.SetZIndex(
            pinButton,
            10);

        cardLayout.Children.Add(
            pinButton);

        var cover =
            new Border
            {
                Width =
                    coverWidth,

                Height =
                    coverHeight,

                HorizontalAlignment =
                    WpfHorizontalAlignment.Center,

                Background =
                    new SolidColorBrush(
                        WpfColor.FromRgb(
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


        var coverLayout =
            new Grid();


        var fallbackText =
            new TextBlock
            {
                Text =
                    gameId,

                HorizontalAlignment =
                    WpfHorizontalAlignment.Center,

                VerticalAlignment =
                    WpfVerticalAlignment.Center,

                FontSize =
                    24,

                Foreground =
                    new SolidColorBrush(
                        WpfColor.FromRgb(
                            130,
                            130,
                            140))
            };


        coverLayout.Children.Add(
            fallbackText);


        var coverImage =
            new WpfImage
            {
                Stretch =
                    WpfStretch.Uniform,

                HorizontalAlignment =
                    WpfHorizontalAlignment.Stretch,

                VerticalAlignment =
                    WpfVerticalAlignment.Stretch,

                Visibility =
                    Visibility.Collapsed
            };


        if (!string.IsNullOrWhiteSpace(
                coverPath) &&
            File.Exists(
                coverPath))
        {
            try
            {
                coverImage.Source =
                    new BitmapImage(
                        new Uri(
                            coverPath,
                            UriKind.Absolute));

                coverImage.Visibility =
                    Visibility.Visible;

                fallbackText.Visibility =
                    Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Cover] Cached image failed for {gameName}: " +
                    $"{ex.Message}");
            }
        }


        coverLayout.Children.Add(
            coverImage);

        cover.Child =
            coverLayout;


        _coverImages[gameId] =
            coverImage;

        _coverFallbacks[gameId] =
            fallbackText;

        var cursorHaloLayer =
            new Canvas
            {
                HorizontalAlignment =
                    WpfHorizontalAlignment.Stretch,

                VerticalAlignment =
                    WpfVerticalAlignment.Stretch,

                IsHitTestVisible =
                    false,

                Clip =
                    new RectangleGeometry(
                        new Rect(
                            0,
                            0,
                            coverWidth,
                            coverHeight),
                        10,
                        10)
            };

        Canvas.SetLeft(
            cursorHalo,
            -cursorHalo.Width / 2.0);

        Canvas.SetTop(
            cursorHalo,
            -cursorHalo.Height / 2.0);

        cursorHaloLayer.Children.Add(
            cursorHalo);

        coverLayout.Children.Add(
            cursorHaloLayer);


        button.MouseMove +=
            (_, e) =>
            {
                UpdateGameCardHover(
                    button,
                    cardScale,
                    cardSkew,
                    cardTranslate,
                    cover,
                    cursorHalo,
                    e);
            };

        button.MouseLeave +=
            (_, _) =>
            {
                ResetGameCardHover(
                    cardScale,
                    cardSkew,
                    cardTranslate,
                    cursorHalo);
            };


        content.Children.Add(
            cover);


        if (_settings.ShowGameName)
        {
            var gameNameText =
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
                        WpfHorizontalAlignment.Center,

                    TextAlignment =
                        WpfTextAlignment.Center,

                    FontSize =
                        17,

                    FontWeight =
                        FontWeights.SemiBold,

                    Foreground =
                        new SolidColorBrush(
                            _settings.IsDarkTheme
                                ? WpfColor.FromRgb(
                                    135,
                                    206,
                                    235)
                                : WpfColor.FromRgb(
                                    61,
                                    27,
                                    93)),

                    TextWrapping =
                        TextWrapping.Wrap,

                    Effect =
                        _settings.IsDarkTheme
                            ? new DropShadowEffect
                            {
                                Color =
                                    WpfColor.FromRgb(
                                        16,
                                        16,
                                        20),

                                BlurRadius =
                                    1.5,

                                ShadowDepth =
                                    0,

                                Opacity =
                                    1.0
                            }
                            : null
                };

            content.Children.Add(
                gameNameText);
        }

        content.Children.Add(
            new TextBlock
            {
                Text =
                    total > 0
                        ? $"{unlocked}/{total} achievements"
                        : "No achievements",

                Margin =
                    new Thickness(
                        0,
                        _settings.ShowGameName ? 6 : 14,
                        0,
                        0),

                HorizontalAlignment =
                    WpfHorizontalAlignment.Center,

                TextAlignment =
                    WpfTextAlignment.Center,

                Foreground =
                    new SolidColorBrush(
                        WpfColor.FromRgb(
                            255,
                            127,
                            80)),

                FontSize =
                    14,

                FontWeight =
                    FontWeights.Medium
            });

        if (_settings.ShowCompletion)
        {
            double completion =
                total > 0
                    ? (double)unlocked / total * 100.0
                    : 0.0;

            var completionBackground =
                new Border
                {
                    Background =
                        new SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(
                                185,
                                10,
                                10,
                                14)),

                    CornerRadius =
                        new CornerRadius(8),

                    Padding =
                        new Thickness(
                            10,
                            5,
                            10,
                            5),

                    HorizontalAlignment =
                        WpfHorizontalAlignment.Center,

                    VerticalAlignment =
                        WpfVerticalAlignment.Bottom,

                    Margin =
                        new Thickness(
                            0,
                            0,
                            0,
                            10),

                    IsHitTestVisible =
                        false
                };

            completionBackground.Child =
                new TextBlock
                {
                    Text =
                        total > 0
                            ? $"{completion:0.#}%"
                            : "0%",

                    HorizontalAlignment =
                        WpfHorizontalAlignment.Center,

                    TextAlignment =
                        WpfTextAlignment.Center,

                    Foreground =
                        WpfBrushes.White,

                    FontSize =
                        13,

                    FontWeight =
                        FontWeights.SemiBold
                };

            Grid.SetZIndex(
                completionBackground,
                5);

            coverLayout.Children.Add(
                completionBackground);
        }


        button.Content =
            cardLayout;

        button.Click +=
            async (_, _) =>
            {
                LogActivity(
                    "GAME_CARD_CLICK",
                    $"Game card clicked | ID={gameId} | Name={gameName}");

                await OpenGameAsync(
                    spoolFile,
                    gameId);
            };


        GameGrid.Children.Add(
            button);
    }

    private static ControlTemplate CreateTransparentGameCardTemplate()
    {
        var template =
            new ControlTemplate(
                typeof(WpfButton));


        var root =
            new FrameworkElementFactory(
                typeof(Border));

        root.SetValue(
            Border.BackgroundProperty,
            WpfBrushes.Transparent);

        root.SetValue(
            Border.BorderThicknessProperty,
            new Thickness(0));

        root.SetValue(
            Border.PaddingProperty,
            new Thickness(0));


        var contentPresenter =
            new FrameworkElementFactory(
                typeof(ContentPresenter));

        contentPresenter.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            WpfHorizontalAlignment.Center);

        contentPresenter.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            WpfVerticalAlignment.Top);


        root.AppendChild(
            contentPresenter);

        template.VisualTree =
            root;

        return template;
    }

    private async Task LoadMissingCoversAsync(
        int generation)
    {
        List<GameCardData> games =
            _games.ToList();

        foreach (GameCardData game in games)
        {
            if (generation != _coverLoadGeneration)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(game.CoverPath) &&
                File.Exists(game.CoverPath))
            {
                continue;
            }

            try
            {
                string? coverPath =
                    await _gameCoverService.GetCoverAsync(
                        game.GameName);

                if (generation != _coverLoadGeneration)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(coverPath) ||
                    !File.Exists(coverPath))
                {
                    continue;
                }

                game.CoverPath =
                    coverPath;

                await Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (generation != _coverLoadGeneration)
                        {
                            return;
                        }

                        UpdateCoverImage(
                            game.GameId,
                            coverPath);
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Cover] Background load failed for " +
                    $"{game.GameName}: {ex.Message}");
            }
        }
    }


    private void UpdateCoverImage(
        string gameId,
        string coverPath)
    {
        if (!_coverImages.TryGetValue(
                gameId,
                out WpfImage? image))
        {
            return;
        }

        try
        {
            var bitmap =
                new BitmapImage();

            bitmap.BeginInit();

            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;

            bitmap.UriSource =
                new Uri(
                    coverPath,
                    UriKind.Absolute);

            bitmap.EndInit();

            bitmap.Freeze();

            image.Source =
                bitmap;

            image.Visibility =
                Visibility.Visible;

            if (_coverFallbacks.TryGetValue(
                    gameId,
                    out TextBlock? fallback))
            {
                fallback.Visibility =
                    Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Cover] UI update failed for {gameId}: " +
                $"{ex.Message}");
        }
    }

    private static void UpdateGameCardHover(
        WpfButton button,
        ScaleTransform scale,
        SkewTransform skew,
        TranslateTransform translate,
        Border cover,
        System.Windows.Shapes.Ellipse cursorHalo,
        WpfMouseEventArgs e)
    {
        if (button.ActualWidth <= 0 ||
            button.ActualHeight <= 0)
        {
            return;
        }


        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            null);

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            null);

        skew.BeginAnimation(
            SkewTransform.AngleXProperty,
            null);

        skew.BeginAnimation(
            SkewTransform.AngleYProperty,
            null);

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            null);

        translate.BeginAnimation(
            TranslateTransform.YProperty,
            null);


        WpfPoint position =
            e.GetPosition(
                button);


        double normalizedX =
            Math.Clamp(
                position.X / button.ActualWidth,
                0.0,
                1.0);

        double normalizedY =
            Math.Clamp(
                position.Y / button.ActualHeight,
                0.0,
                1.0);


        double x =
            (normalizedX - 0.5) * 2.0;

        double y =
            (normalizedY - 0.5) * 2.0;


        double distance =
            Math.Min(
                1.0,
                Math.Sqrt(
                    (x * x + y * y) / 2.0));

        if (cover.IsMouseOver)
        {
            WpfPoint coverPosition =
                e.GetPosition(
                    cover);

            Canvas.SetLeft(
                cursorHalo,
                coverPosition.X - cursorHalo.Width / 2.0);

            Canvas.SetTop(
                cursorHalo,
                coverPosition.Y - cursorHalo.Height / 2.0);

            cursorHalo.Opacity =
                0.78;
        }
        else
        {
            cursorHalo.Opacity =
                0.0;
        }


        double scaleAmount =
            1.0 +
            (0.012 * distance);

        scale.ScaleX =
            scaleAmount;

        scale.ScaleY =
            scaleAmount;


        skew.AngleX =
            -y * 2.2;

        skew.AngleY =
            x * 2.2;


        translate.X =
            x * 3.5;

        translate.Y =
            y * 3.5;
    }


    private static void ResetGameCardHover(
        ScaleTransform scale,
        SkewTransform skew,
        TranslateTransform translate,
        System.Windows.Shapes.Ellipse cursorHalo)
    {
        const double durationMilliseconds =
            140.0;


        cursorHalo.Opacity =
            0.0;


        var easing =
            new CubicEase
            {
                EasingMode =
                    EasingMode.EaseOut
            };


        var scaleXAnimation =
            new DoubleAnimation
            {
                To =
                    1.0,

                Duration =
                    TimeSpan.FromMilliseconds(
                        durationMilliseconds),

                EasingFunction =
                    easing
            };


        var scaleYAnimation =
            new DoubleAnimation
            {
                To =
                    1.0,

                Duration =
                    TimeSpan.FromMilliseconds(
                        durationMilliseconds),

                EasingFunction =
                    easing
            };


        var skewXAnimation =
            new DoubleAnimation
            {
                To =
                    0.0,

                Duration =
                    TimeSpan.FromMilliseconds(
                        durationMilliseconds),

                EasingFunction =
                    easing
            };


        var skewYAnimation =
            new DoubleAnimation
            {
                To =
                    0.0,

                Duration =
                    TimeSpan.FromMilliseconds(
                        durationMilliseconds),

                EasingFunction =
                    easing
            };


        var translateXAnimation =
            new DoubleAnimation
            {
                To =
                    0.0,

                Duration =
                    TimeSpan.FromMilliseconds(
                        durationMilliseconds),

                EasingFunction =
                    easing
            };


        var translateYAnimation =
            new DoubleAnimation
            {
                To =
                    0.0,

                Duration =
                    TimeSpan.FromMilliseconds(
                        durationMilliseconds),

                EasingFunction =
                    easing
            };


        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            scaleXAnimation);

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scaleYAnimation);

        skew.BeginAnimation(
            SkewTransform.AngleXProperty,
            skewXAnimation);

        skew.BeginAnimation(
            SkewTransform.AngleYProperty,
            skewYAnimation);

        translate.BeginAnimation(
            TranslateTransform.XProperty,
            translateXAnimation);


        translateYAnimation.Completed +=
            (_, _) =>
            {
                scale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    null);

                scale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    null);

                skew.BeginAnimation(
                    SkewTransform.AngleXProperty,
                    null);

                skew.BeginAnimation(
                    SkewTransform.AngleYProperty,
                    null);

                translate.BeginAnimation(
                    TranslateTransform.XProperty,
                    null);

                translate.BeginAnimation(
                    TranslateTransform.YProperty,
                    null);


                scale.ScaleX =
                    1.0;

                scale.ScaleY =
                    1.0;

                skew.AngleX =
                    0.0;

                skew.AngleY =
                    0.0;

                translate.X =
                    0.0;

                translate.Y =
                    0.0;
            };


        translate.BeginAnimation(
            TranslateTransform.YProperty,
            translateYAnimation);
    }

    private WpfButton CreatePinButton(
        string gameId,
        string gameName,
        bool isPinned)
    {
        var pinButton =
            new WpfButton
            {
                Width = 30,
                Height = 30,
                HorizontalAlignment =
                    WpfHorizontalAlignment.Right,
                VerticalAlignment =
                    WpfVerticalAlignment.Top,
                Margin =
                    new Thickness(0, 2, 2, 0),
                Background =
                    WpfBrushes.Transparent,
                BorderThickness =
                    new Thickness(0),
                Padding =
                    new Thickness(0),
                Cursor =
                    WpfCursors.Hand,
                Focusable =
                    false,
                ToolTip =
                    isPinned
                        ? "Unpin game"
                        : "Pin game"
            };

        pinButton.Template =
            CreateInvisiblePinButtonTemplate();

        pinButton.Content =
            CreateHeartIcon(
                isPinned);

        pinButton.Click +=
            (_, e) =>
            {
                e.Handled = true;
                TogglePinnedGame(
                    gameId,
                    gameName);
            };

        return pinButton;
    }


    private static ControlTemplate CreateInvisiblePinButtonTemplate()
    {
        var template =
            new ControlTemplate(
                typeof(WpfButton));

        var borderFactory =
            new FrameworkElementFactory(
                typeof(Border));

        borderFactory.SetValue(
            Border.BackgroundProperty,
            WpfBrushes.Transparent);

        borderFactory.SetValue(
            Border.BorderThicknessProperty,
            new Thickness(0));

        borderFactory.SetValue(
            Border.PaddingProperty,
            new Thickness(0));

        var contentPresenterFactory =
            new FrameworkElementFactory(
                typeof(ContentPresenter));

        contentPresenterFactory.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            WpfHorizontalAlignment.Center);

        contentPresenterFactory.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            WpfVerticalAlignment.Center);

        borderFactory.AppendChild(
            contentPresenterFactory);

        template.VisualTree =
            borderFactory;

        return template;
    }


    private static System.Windows.Shapes.Path CreateHeartIcon(
        bool isPinned)
    {
        var geometry =
            Geometry.Parse(
                "M 12 20 " +
                "C 11.7 19.75 3 14.05 3 8.5 " +
                "C 3 5.45 5.15 3 8 3 " +
                "C 9.65 3 11.05 3.9 12 5.15 " +
                "C 12.95 3.9 14.35 3 16 3 " +
                "C 18.85 3 21 5.45 21 8.5 " +
                "C 21 14.05 12.3 19.75 12 20 " +
                "Z");

        return new System.Windows.Shapes.Path
        {
            Width = 18,
            Height = 18,
            Data = geometry,

            Fill =
                isPinned
                    ? new SolidColorBrush(
                        WpfColor.FromRgb(
                            133,
                            133,
                            143))
                    : WpfBrushes.Transparent,

            Stroke =
                new SolidColorBrush(
                    WpfColor.FromRgb(
                        133,
                        133,
                        143)),

            StrokeThickness = 1.35,
            Stretch = WpfStretch.Uniform,

            HorizontalAlignment =
                WpfHorizontalAlignment.Center,

            VerticalAlignment =
                WpfVerticalAlignment.Center,

            IsHitTestVisible = false
        };
    }

    private void TogglePinnedGame(
        string gameId,
        string gameName)
    {
        if (!uint.TryParse(
                gameId,
                out uint parsedGameId))
        {
            return;
        }

        bool pinned;

        if (_pinnedGameIds.Contains(
                parsedGameId))
        {
            _pinnedGameIds.Remove(
                parsedGameId);
            pinned = false;
        }
        else
        {
            _pinnedGameIds.Add(
                parsedGameId);
            pinned = true;
        }

        GameCardData? existingGame =
            _games.FirstOrDefault(
                game =>
                    string.Equals(
                        game.GameId,
                        gameId,
                        StringComparison.OrdinalIgnoreCase));

        if (existingGame != null)
        {
            existingGame.IsPinned =
                pinned;
        }

        WritePinChange(
            pinned,
            parsedGameId,
            gameName);

        Console.WriteLine(
            $"[MainWindow] " +
            $"Game {(pinned ? "pinned" : "unpinned")}: " +
            $"{gameName} ({parsedGameId})");

        UpdateGameList();
    }

    private async Task OpenGameAsync(
        string spoolFile,
        string gameId)
    {
        SearchPopup.IsOpen =
            false;

        if (!uint.TryParse(
                gameId,
                out uint selectedGameId))
        {
            WpfMessageBox.Show(
                "The selected Ubisoft game ID is invalid.",
                "UbiSlot",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);

            return;
        }

        string selectedGameName =
            _gameDatabase.GetGameName(
                gameId)
            ?? $"Game {gameId}";

        Console.WriteLine();
        Console.WriteLine(
            "========== GAME SELECTION ==========");
        Console.WriteLine(
            $"[MainWindow] Selected game: {selectedGameName}");
        Console.WriteLine(
            $"[MainWindow] Selected Ubisoft ID: {selectedGameId}");

        LogActivity(
            "GAME_SELECT",
            $"Game selected | ID={selectedGameId} | Name={selectedGameName}");

        try
        {
            uint? runningGameId =
                UbisoftGameLauncher.GetRunningUbisoftGameId();

            if (!runningGameId.HasValue)
            {
                Console.WriteLine(
                    "[MainWindow] No Ubisoft game is currently running.");

                GameRunningText.Text =
                    string.Empty;

                GameRunningText.Visibility =
                    Visibility.Collapsed;

                LogActivity(
                    "GAME_BOOTSTRAP_REQUEST",
                    $"No game running; bootstrap requested | ID={selectedGameId} | Name={selectedGameName}");

                await StartBootstrapAsync(
                    gameId);

                return;
            }

            string runningGameName =
                _gameDatabase.GetGameName(
                    runningGameId.Value.ToString())
                ?? $"Game {runningGameId.Value}";

            Console.WriteLine(
                $"[MainWindow] Running game: {runningGameName}");
            Console.WriteLine(
                $"[MainWindow] Running Ubisoft ID: {runningGameId.Value}");

            if (runningGameId.Value == selectedGameId)
            {
                Console.WriteLine(
                    "[MainWindow] Running game matches selected game by Ubisoft ID.");

                Console.WriteLine(
                    "[MainWindow] Opening AchievementWindow directly.");

                Console.WriteLine(
                    "[MainWindow] UPLAY_Startup() will NOT be called.");

                LogActivity(
                    "GAME_ATTACH",
                    $"Existing game session attached | ID={selectedGameId} | Name={selectedGameName}");

                GameRunningText.Text =
                    $"{runningGameName} is running";

                GameRunningText.Visibility =
                    Visibility.Visible;

                await OpenExistingGameAsync(
                    spoolFile,
                    gameId);

                return;
            }

            Console.WriteLine(
                "[MainWindow] Running Ubisoft game does not match selected game.");

            Console.WriteLine(
                "[MainWindow] Launch blocked.");

            GameRunningText.Text =
                $"{runningGameName} is running, please close the game and try again";

            GameRunningText.Visibility =
                Visibility.Visible;

            StatusText.Text =
                $"Close {runningGameName} before opening {selectedGameName}.";

            LogActivity(
                "GAME_BLOCKED",
                $"Game launch blocked | SelectedID={selectedGameId} | Selected={selectedGameName} | RunningID={runningGameId.Value} | Running={runningGameName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[MainWindow] Game launch error: {ex}");

            WpfMessageBox.Show(
                "UbiSlot couldn't open this game.\n\n" +
                ex.Message,
                "UbiSlot",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }

    private async Task OpenExistingGameAsync(
        string spoolFile,
        string gameId)
    {
        try
        {
            var achievementWindow =
                new AchievementWindow(
                    spoolFile,
                    gameId);

            achievementWindow.ShowDialog();


            LogActivity(
                "ACHIEVEMENT_WINDOW_CLOSE",
                $"Achievement window closed | ID={gameId}");

            await LoadGamesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[MainWindow] " +
                $"AchievementWindow error: {ex}");


            WpfMessageBox.Show(
                "UbiSlot couldn't open the achievement window.\n\n" +
                ex.Message,
                "UbiSlot",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }
    }
    private async Task StartBootstrapAsync(
        string gameId)
    {
        Console.WriteLine(
            $"[MainWindow] " +
            $"Starting UbiSlot bootstrap for game {gameId}.");


        try
        {
            string executablePath =
                Environment.ProcessPath
                ??
                Process.GetCurrentProcess()
                    .MainModule?
                    .FileName
                ??
                throw new InvalidOperationException(
                    "Could not determine UbiSlot executable path.");


            var startInfo =
                new ProcessStartInfo
                {
                    FileName =
                        executablePath,

                    Arguments =
                        gameId,

                    WorkingDirectory =
                        AppContext.BaseDirectory,

                    UseShellExecute =
                        false
                };


            Process.Start(
                startInfo);


            Console.WriteLine(
                "[MainWindow] " +
                $"Bootstrap UbiSlot started for game {gameId}.");

            LogActivity(
                "BOOTSTRAP",
                $"Bootstrap started | ID={gameId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[MainWindow] " +
                $"Bootstrap launch error: {ex}");


            WpfMessageBox.Show(
                "UbiSlot couldn't start the game session.\n\n" +
                ex.Message,
                "UbiSlot",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Error);
        }


        await Task.CompletedTask;
    }
    private async Task CheckForAutomaticUpdateAsync()
    {
        if (!_settings.AutoUpdateCheck)
        {
            return;
        }

        if (_settings.LastUpdateCheckUtc.HasValue &&
            DateTimeOffset.UtcNow -
                _settings.LastUpdateCheckUtc.Value <
            TimeSpan.FromDays(1))
        {
            return;
        }

        try
        {
            GitHubReleaseInfo? update =
                await _settings.CheckForUpdateAsync();

            _settings.LastUpdateCheckUtc =
                DateTimeOffset.UtcNow;

            _settings.Save();

            if (update == null)
            {
                return;
            }

            WpfMessageBoxResult result =
                WpfMessageBox.Show(
                    $"A newer UbiSlot version is available.\n\n" +
                    $"{update.Name}\n" +
                    $"{update.TagName}\n\n" +
                    "Open the GitHub release page?",
                    "UbiSlot Update",
                    WpfMessageBoxButton.YesNo,
                    WpfMessageBoxImage.Information);

            if (result == WpfMessageBoxResult.Yes &&
                !string.IsNullOrWhiteSpace(update.HtmlUrl))
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = update.HtmlUrl,
                        UseShellExecute = true
                    });
            }
        }
        catch
        {
            //fetching
        }
    }

    private void ApplyMainWindowTheme()
    {
        if (_settings.IsDarkTheme)
        {
            Background =
                new SolidColorBrush(
                    WpfColor.FromRgb(
                        16,
                        16,
                        20));

            Foreground =
                WpfBrushes.White;
        }
        else
        {
            Background =
                new SolidColorBrush(
                    WpfColor.FromRgb(
                        245,
                        245,
                        248));

            Foreground =
                new SolidColorBrush(
                    WpfColor.FromRgb(
                        25,
                        25,
                        30));
        }
    }

    private async void RefreshButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LogActivity(
            "REFRESH",
            "Library refresh requested");

        GameSearchBox.Clear();


        SearchPopup.IsOpen =
            false;


        await LoadGamesAsync();
    }

    private void SupportButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LogActivity(
            "SUPPORT_OPEN",
            "Support window opened");

        var supportWindow =
            new SupportWindow
            {
                Owner =
                    this
            };


        supportWindow.ShowDialog();
    }

    private void SettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        LogActivity(
            "SETTINGS_OPEN",
            "Settings window opened");

        var settingsWindow =
            new SettingsWindow
            {
                Owner =
                    this
            };

        bool? result =
            settingsWindow.ShowDialog();

        if (result != true)
        {
            return;
        }

        _settings =
            UbiSlotSettings.Load();

        ApplyMainWindowTheme();

        LogActivity(
            "SETTINGS_SAVE",
            "Settings saved");

        _ = ReloadAfterSettingsAsync();
    }


    private async Task ReloadAfterSettingsAsync()
    {
        GameSearchBox.Clear();
        SearchPopup.IsOpen = false;

        await LoadGamesAsync();
    }

    private static readonly object ActivityLogSync =
        new();

    private static readonly Regex PinEventRegex =
        new(
            @"^\[[^\]]+\]\s+\[(PIN_GAME|UNPIN_GAME)\]\s+ID=(\d+)",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

    private static readonly Regex WindowsPathRegex =
        new(
            @"(?i)(?<![A-Za-z0-9_])(?:[A-Z]:\\|\\\\)[^\r\n\t""<>|]*",
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant);

    private static string ActivityLogDirectory =>
        Path.Combine(
            AppContext.BaseDirectory,
            "UbiSlot_Cache");

    private static string ActivityLogFilePath =>
        Path.Combine(
            ActivityLogDirectory,
            "UbiSlot.log");

    private static void LogActivity(
        string eventName,
        string message)
    {
        try
        {
            string line =
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] " +
                $"[{eventName}] " +
                SanitizeLogText(message);

            lock (ActivityLogSync)
            {
                Directory.CreateDirectory(
                    ActivityLogDirectory);

                File.AppendAllText(
                    ActivityLogFilePath,
                    line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging shouldn't break it
        }
    }

    private static void WritePinChange(
        bool pinned,
        uint gameId,
        string gameName)
    {
        LogActivity(
            pinned
                ? "PIN_GAME"
                : "UNPIN_GAME",
            $"ID={gameId} | Name={gameName}");
    }

    private static HashSet<uint> LoadPinnedGameIds()
    {
        var pinnedIds =
            new HashSet<uint>();

        try
        {
            if (!File.Exists(ActivityLogFilePath))
            {
                return pinnedIds;
            }

            lock (ActivityLogSync)
            {
                foreach (string rawLine in
                         File.ReadLines(ActivityLogFilePath))
                {
                    Match match =
                        PinEventRegex.Match(rawLine);

                    if (!match.Success)
                    {
                        continue;
                    }

                    if (!uint.TryParse(
                            match.Groups[2].Value,
                            out uint gameId))
                    {
                        continue;
                    }

                    if (string.Equals(
                            match.Groups[1].Value,
                            "PIN_GAME",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        pinnedIds.Add(gameId);
                    }
                    else
                    {
                        pinnedIds.Remove(gameId);
                    }
                }
            }
        }
        catch
        {
            // Library loading no matter what!
        }

        return pinnedIds;
    }

    private static string SanitizeLogText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string sanitized =
            value;

        ReplacePathRoot(
            ref sanitized,
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "[LOCALAPPDATA]");

        ReplacePathRoot(
            ref sanitized,
            Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData),
            "[APPDATA]");

        ReplacePathRoot(
            ref sanitized,
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "[PROGRAMDATA]");

        ReplacePathRoot(
            ref sanitized,
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "[USERPROFILE]");

        ReplacePathRoot(
            ref sanitized,
            Environment.GetFolderPath(
                Environment.SpecialFolder.DesktopDirectory),
            "[DESKTOP]");

        ReplacePathRoot(
            ref sanitized,
            Path.GetTempPath(),
            "[TEMP]");

        ReplacePathRoot(
            ref sanitized,
            AppContext.BaseDirectory,
            "[APP]");

        string? processPath =
            Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            ReplacePathRoot(
                ref sanitized,
                Path.GetDirectoryName(processPath),
                "[APP]");
        }

        ReplaceText(
            ref sanitized,
            Environment.UserName,
            "[USER]");

        ReplaceText(
            ref sanitized,
            Environment.MachineName,
            "[MACHINE]");

        if (!string.IsNullOrWhiteSpace(
                Environment.UserDomainName))
        {
            ReplaceText(
                ref sanitized,
                Environment.UserDomainName,
                "[DOMAIN]");
        }

        sanitized =
            WindowsPathRegex.Replace(
                sanitized,
                "[PATH]");

        return sanitized;
    }

    private static void ReplacePathRoot(
        ref string value,
        string? root,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        string normalizedRoot =
            root.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalizedRoot))
        {
            return;
        }

        value =
            value.Replace(
                normalizedRoot,
                replacement,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ReplaceText(
        ref string value,
        string? text,
        string replacement)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        value =
            value.Replace(
                text,
                replacement,
                StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SpoolEntry
    {
        public string GameId { get; init; }
            = string.Empty;


        public string GameName { get; init; }
            = string.Empty;


        public string NormalizedName { get; init; }
            = string.Empty;


        public string SpoolFile { get; init; }
            = string.Empty;
    }

    private sealed class GameCardData
    {
        public string GameId { get; init; }
            = string.Empty;


        public string AchievementGameId { get; init; }
            = string.Empty;


        public string GameName { get; init; }
            = string.Empty;


        public string SpoolFile { get; init; }
            = string.Empty;


        public int Unlocked { get; init; }


        public int Total { get; init; }


        public string? CoverPath { get; set; }


        public bool IsPinned { get; set; }
    }
}
