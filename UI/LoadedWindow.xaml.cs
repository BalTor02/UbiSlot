using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UbiSlot.Core;
using UbiSlot.Games;
using UbiSlot.Ubisoft;

using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfImage = System.Windows.Controls.Image;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace UbiSlot;

public enum LoadedWindowLaunchRequestStatus
{
    None,
    Pending,
    Consumed,
    Cancelled,
    Failed
}

public partial class LoadedWindow : Window
{
    private string _spoolFile;
    private string _sessionGameId;
    private string _achievementGameId;
    private readonly uint? _managedSessionGameId;
    private readonly bool _isManagedInstance;
    private string _sessionStatusText = "Spool-only mode";

    private readonly SpoolManager _spoolManager;
    private readonly AchievementCache _achievementCache;
    private readonly GameDatabase _gameDatabase;

    private readonly List<AchievementDefinition> _achievements = [];

    private readonly List<WpfCheckBox> _selectionBoxes = [];

    private readonly Dictionary<
        AchievementDefinition,
        Border> _achievementCards = [];

    private UbiSlotSettings _settings;
    private byte[]? _originalSpoolData;
    private bool _originalSpoolExisted;
    private bool _ubislotCreatedSpool;
    private bool _hasSessionChanges;

    private bool _gameReady;
    private bool _sessionClosing;
    private bool _allowClose;

    private readonly List<AchievementDefinition> _defaultAchievementOrder = [];
    private bool _defaultOrderCaptured;

    private AchievementSortMode _sortMode =
        AchievementSortMode.ID;

    public LoadedWindow(
        string spoolFile,
        uint? managedSessionGameId = null,
        bool isManagedInstance = false)
    {
        InitializeComponent();

        _settings =
            UbiSlotSettings.Load();

        ApplyWindowTheme(
            _settings.IsDarkTheme);

        if (string.IsNullOrWhiteSpace(spoolFile))
        {
            throw new ArgumentException(
                "A spool file must be selected.",
                nameof(spoolFile));
        }

        _spoolFile = Path.GetFullPath(spoolFile);
        _managedSessionGameId = managedSessionGameId;
        _isManagedInstance = isManagedInstance;

        _sessionGameId = managedSessionGameId?.ToString()
            ?? string.Empty;

        _achievementGameId = string.Empty;

        _spoolManager = new SpoolManager();
        _achievementCache = new AchievementCache();
        _gameDatabase = new GameDatabase();
        Loaded += LoadedWindow_Loaded;
        Closing += LoadedWindow_Closing;
    }

    private async void LoadedWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            Loaded -= LoadedWindow_Loaded;

            if (!File.Exists(_spoolFile))
            {
                throw new FileNotFoundException(
                    "The selected Ubisoft spool file no longer exists.",
                    _spoolFile);
            }

            _achievementGameId =
                _spoolManager.GetGameId(_spoolFile);

            if (!uint.TryParse(
                    _achievementGameId,
                    out uint spoolGameId))
            {
                throw new InvalidDataException(
                    "The selected spool filename does not contain a valid numeric game ID.");
            }

            string spoolGameName =
                _gameDatabase.GetGameName(_achievementGameId)
                ?? $"Game {_achievementGameId}";

            GameTitleText.Text = spoolGameName;
            ProgressText.Text = "Starting Ubisoft game session...";
            AddLoadingMessage("Starting a game instance with the selected spool ID...");

            Console.WriteLine(
                $"[LoadedWindow] Selected spool: {_spoolFile}");
            Console.WriteLine(
                $"[LoadedWindow] Achievement/spool ID: {spoolGameId}");

            CaptureOriginalSpoolState();

            if (_isManagedInstance)
            {
                AttachManagedSession(
                    spoolGameId,
                    _managedSessionGameId);
            }
            else
            {
                bool handedOff = false;

                try
                {
                    handedOff =
                        await TryLaunchGameSessionAsync(spoolGameId);
                }
                catch (Exception launchException)
                {
                    Console.WriteLine(
                        "[LoadedWindow] Spool-ID bootstrap failed: " +
                        launchException);
                }

                if (handedOff)
                {
                    _allowClose = true;
                    Close();
                    return;
                }

                Dialogue.ShowError(
                    this,
                    "UbiSlot",
                    "UbiSlot couldn't start a Ubisoft game instance using the selected spool ID.\n\n" +
                    "Launch the game once through Ubisoft Connect and try again.");

                _allowClose = true;
                Close();
                return;
            }

            LoadSelectedSpool();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LoadedWindow] Startup error: {ex}");

            Dialogue.ShowError(
                this,
                "UbiSlot",
                "UbiSlot couldn't open the selected spool file.\n\n" +
                ex.Message);

            _allowClose = true;
            Close();
        }
    }

    private void AttachManagedSession(
        uint spoolGameId,
        uint? requestedSessionGameId)
    {
        _sessionGameId = spoolGameId.ToString();

        if (requestedSessionGameId != spoolGameId)
        {
            _sessionStatusText =
                "Game ID does not match selected spool ID";

            Console.WriteLine(
                "[LoadedWindow] Refusing to associate a different session ID with the selected spool. " +
                $"Spool ID={spoolGameId}, supplied session ID={requestedSessionGameId?.ToString() ?? "NOT FOUND"}.");
            return;
        }

        _sessionStatusText =
            "Ready";

        try
        {
            uint? runningGameId =
                UbisoftGameLauncher.GetRunningUbisoftGameId();

            if (runningGameId == spoolGameId)
            {
                _sessionStatusText =
                    "Matched Ubisoft game instance";

                Console.WriteLine(
                    $"[LoadedWindow] Verified running game ID {runningGameId.Value} " +
                    $"matches selected spool ID {spoolGameId}.");
            }
            else if (runningGameId.HasValue)
            {
                Console.WriteLine(
                    $"[LoadedWindow] Detected game ID {runningGameId.Value}, but the selected spool " +
                    $"and initialized SDK session remain bound to spool ID {spoolGameId}.");
            }
            else
            {
                Console.WriteLine(
                    $"[LoadedWindow] SDK session is active for spool ID {spoolGameId}; " +
                    "no separate Ubisoft game executable was detected.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LoadedWindow] Runtime game detection failed: {ex.Message}");
        }
    }

    private void LoadSelectedSpool()
    {
        if (!File.Exists(_spoolFile))
        {
            throw new FileNotFoundException(
                "The selected Ubisoft spool file is no longer available.",
                _spoolFile);
        }

        LoadAchievements();

        if (_allowClose)
        {
            return;
        }

        CaptureDefaultAchievementOrder();
        _gameReady = true;

        ProgressText.Text =
            $"{_achievements.Count(a => a.IsUnlocked)}/{_achievements.Count} unlocked · {_sessionStatusText}";

        UpdateSelection();
    }

    private Task<bool> TryLaunchGameSessionAsync(
        uint spoolGameId)
    {
        try
        {
            string executablePath =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(executablePath) ||
                !File.Exists(executablePath))
            {
                Console.WriteLine(
                    "[LoadedWindow] Could not resolve the UbiSlot executable path.");
                return Task.FromResult(false);
            }

            Console.WriteLine(
                $"[LoadedWindow] Starting manual LoadedWindow bootstrap with spool ID {spoolGameId}. " +
                "Ownership/product IDs and alternative game IDs will not be consulted.");

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("--load-spool");
            startInfo.ArgumentList.Add(spoolGameId.ToString());
            startInfo.ArgumentList.Add(_spoolFile);

            using Process? bootstrapProcess =
                Process.Start(startInfo);

            if (bootstrapProcess == null)
            {
                Console.WriteLine(
                    "[LoadedWindow] Windows did not start the manual spool bootstrap process.");
                return Task.FromResult(false);
            }

            Console.WriteLine(
                $"[LoadedWindow] Manual spool bootstrap process started (PID {bootstrapProcess.Id}); " +
                $"spool ID={spoolGameId}, spool={_spoolFile}.");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[LoadedWindow] Could not start the manual spool bootstrap: " +
                ex);

            return Task.FromResult(false);
        }
    }

    private static string GetLaunchRequestPath(uint sessionGameId)
    {
        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(
            localAppData,
            "UbiSlot",
            "LoadedWindowLaunchRequests",
            $"{sessionGameId}.json");
    }

    private sealed class PendingLoadedWindowLaunchRequest
    {
        public PendingLoadedWindowLaunchRequest()
        {
        }

        public uint SessionGameId { get; set; }
        public string SpoolFile { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
        public string Status { get; set; } = "Pending";
    }

    public static void CreatePendingLaunchRequest(
        uint sessionGameId,
        string spoolFile)
    {
        var request = new PendingLoadedWindowLaunchRequest
        {
            SessionGameId = sessionGameId,
            SpoolFile = Path.GetFullPath(spoolFile),
            CreatedUtc = DateTime.UtcNow,
            Status = "Pending"
        };

        WriteLaunchRequest(request);
    }

    public static bool HasPendingLaunchRequest()
    {
        try
        {
            string requestDirectory =
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "UbiSlot",
                    "LoadedWindowLaunchRequests");

            if (!Directory.Exists(requestDirectory))
            {
                return false;
            }

            foreach (string path in Directory.GetFiles(
                         requestDirectory,
                         "*.json",
                         SearchOption.TopDirectoryOnly))
            {
                PendingLoadedWindowLaunchRequest? request =
                    ReadLaunchRequest(path);

                if (request == null ||
                    !request.Status.Equals(
                        "Pending",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (DateTime.UtcNow - request.CreatedUtc <= TimeSpan.FromMinutes(1) &&
                    File.Exists(request.SpoolFile))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[LoadedWindow] Pending spool request check failed: " +
                ex.Message);
        }

        return false;
    }

    public static bool TryFailPendingLaunchRequest(
        uint sessionGameId)
    {
        try
        {
            PendingLoadedWindowLaunchRequest? request =
                ReadLaunchRequest(
                    GetLaunchRequestPath(sessionGameId));

            if (request == null ||
                !request.Status.Equals(
                    "Pending",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            request.Status = "Failed";
            WriteLaunchRequest(request);

            Console.WriteLine(
                "[LoadedWindow] Reported bootstrap failure to the waiting spool window " +
                $"(session ID {sessionGameId}).");

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[LoadedWindow] Could not report bootstrap failure: " +
                ex.Message);
            return false;
        }
    }

    public static LoadedWindowLaunchRequestStatus TryConsumePendingLaunchRequest(
        uint? managedSessionGameId,
        out string spoolFile,
        out uint? requestedSessionGameId)
    {
        spoolFile = string.Empty;
        requestedSessionGameId = null;

        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        string requestDirectory =
            Path.Combine(
                localAppData,
                "UbiSlot",
                "LoadedWindowLaunchRequests");

        if (!Directory.Exists(requestDirectory))
        {
            return LoadedWindowLaunchRequestStatus.None;
        }

        string[] requestPaths;
        try
        {
            requestPaths = Directory.GetFiles(
                requestDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return LoadedWindowLaunchRequestStatus.None;
        }

        var requests = new List<PendingLoadedWindowLaunchRequest>();

        foreach (string path in requestPaths)
        {
            PendingLoadedWindowLaunchRequest? request =
                ReadLaunchRequest(path);

            if (request == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(request.Status))
            {
                request.Status = "Failed";
            }

            if (DateTime.UtcNow - request.CreatedUtc > TimeSpan.FromMinutes(1))
            {
                if (request.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    request.Status = "Failed";
                    WriteLaunchRequest(request);
                }

                requests.Add(request);
                continue;
            }

            requests.Add(request);
        }

        if (requests.Count == 0)
        {
            return LoadedWindowLaunchRequestStatus.None;
        }

        var database = new GameDatabase();
        var variants = new GameVariantResolver(database);
        var spoolManager = new SpoolManager();

        var validPending = new List<PendingLoadedWindowLaunchRequest>();
        var validTerminal = new List<PendingLoadedWindowLaunchRequest>();

        foreach (PendingLoadedWindowLaunchRequest request in requests)
        {
            string state = request.Status;

            if (!File.Exists(request.SpoolFile) ||
                !uint.TryParse(
                    spoolManager.GetGameId(request.SpoolFile),
                    out uint spoolGameId) ||
                !variants.IsSameFamily(
                    request.SessionGameId,
                    spoolGameId))
            {
                if (state.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                {
                    request.Status = "Failed";
                    WriteLaunchRequest(request);
                    validTerminal.Add(request);
                }

                continue;
            }

            if (state.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                validPending.Add(request);
            }
            else if (state.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) ||
                     state.Equals("Failed", StringComparison.OrdinalIgnoreCase) ||
                     state.Equals("Consumed", StringComparison.OrdinalIgnoreCase))
            {
                validTerminal.Add(request);
            }
        }

        PendingLoadedWindowLaunchRequest? selectedRequest = null;

        if (managedSessionGameId.HasValue)
        {
            List<PendingLoadedWindowLaunchRequest> matchingPending =
                validPending
                    .Where(request =>
                        variants.IsSameFamily(
                            managedSessionGameId.Value,
                            request.SessionGameId))
                    .ToList();

            if (matchingPending.Count == 1)
            {
                selectedRequest = matchingPending[0];
            }
            else if (matchingPending.Count > 1)
            {
                Console.WriteLine(
                    "[LoadedWindow] More than one pending spool request matches this managed game family. " +
                    "No request will be consumed automatically.");
                return LoadedWindowLaunchRequestStatus.None;
            }
            else
            {
                PendingLoadedWindowLaunchRequest? terminalMatch =
                    validTerminal.FirstOrDefault(request =>
                        variants.IsSameFamily(
                            managedSessionGameId.Value,
                            request.SessionGameId));

                if (terminalMatch != null)
                {
                    return terminalMatch.Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                        || terminalMatch.Status.Equals("Consumed", StringComparison.OrdinalIgnoreCase)
                        ? LoadedWindowLaunchRequestStatus.Cancelled
                        : LoadedWindowLaunchRequestStatus.Failed;
                }

                return LoadedWindowLaunchRequestStatus.None;
            }
        }
        else
        {
            if (validPending.Count == 1)
            {
                selectedRequest = validPending[0];
            }
            else if (validPending.Count > 1)
            {
                Console.WriteLine(
                    "[LoadedWindow] Multiple pending spool requests exist but the managed game ID is missing. " +
                    "The spool request is ambiguous.");
                return LoadedWindowLaunchRequestStatus.None;
            }
            else if (validTerminal.Count == 1)
            {
                return validTerminal[0].Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                    || validTerminal[0].Status.Equals("Consumed", StringComparison.OrdinalIgnoreCase)
                    ? LoadedWindowLaunchRequestStatus.Cancelled
                    : LoadedWindowLaunchRequestStatus.Failed;
            }
            else
            {
                return LoadedWindowLaunchRequestStatus.None;
            }
        }

        if (selectedRequest == null)
        {
            return LoadedWindowLaunchRequestStatus.None;
        }

        selectedRequest.Status = "Consumed";
        WriteLaunchRequest(selectedRequest);

        spoolFile = selectedRequest.SpoolFile;
        requestedSessionGameId = selectedRequest.SessionGameId;

        return LoadedWindowLaunchRequestStatus.Consumed;
    }

    private static LoadedWindowLaunchRequestStatus GetLaunchRequestStatus(
        uint sessionGameId,
        string spoolFile)
    {
        PendingLoadedWindowLaunchRequest? request =
            ReadLaunchRequest(
                GetLaunchRequestPath(sessionGameId));

        if (request == null ||
            !string.Equals(
                Path.GetFullPath(request.SpoolFile),
                Path.GetFullPath(spoolFile),
                StringComparison.OrdinalIgnoreCase))
        {
            return LoadedWindowLaunchRequestStatus.None;
        }

        return request.Status.ToLowerInvariant() switch
        {
            "pending" => LoadedWindowLaunchRequestStatus.Pending,
            "consumed" => LoadedWindowLaunchRequestStatus.Consumed,
            "cancelled" => LoadedWindowLaunchRequestStatus.Cancelled,
            "failed" => LoadedWindowLaunchRequestStatus.Failed,
            _ => LoadedWindowLaunchRequestStatus.None
        };
    }

    private static void SetLaunchRequestStatus(
        uint sessionGameId,
        string spoolFile,
        LoadedWindowLaunchRequestStatus status)
    {
        try
        {
            string requestPath =
                GetLaunchRequestPath(sessionGameId);

            PendingLoadedWindowLaunchRequest? request =
                ReadLaunchRequest(requestPath);

            if (request == null ||
                !string.Equals(
                    Path.GetFullPath(request.SpoolFile),
                    Path.GetFullPath(spoolFile),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (request.Status.Equals("Consumed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            request.Status = status.ToString();
            WriteLaunchRequest(request);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LoadedWindow] Could not update launch request state: {ex.Message}");
        }
    }

    private static PendingLoadedWindowLaunchRequest? ReadLaunchRequest(
        string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PendingLoadedWindowLaunchRequest>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteLaunchRequest(
        PendingLoadedWindowLaunchRequest request)
    {
        string path =
            GetLaunchRequestPath(request.SessionGameId);

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "Could not resolve the launch-request directory.");
        }

        Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";
        string json = JsonSerializer.Serialize(request);

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, true);
    }

    private void AddLoadingMessage(
        string message)
    {
        AchievementList.Children.Clear();

        var text =
            new TextBlock
            {
                Text = message,

                FontSize = 16,

                Foreground =
                    GetThemeBrush(
                        "SecondaryText"),

                HorizontalAlignment =
                    WpfHorizontalAlignment.Center,

                VerticalAlignment =
                    VerticalAlignment.Center,

                TextAlignment =
                    TextAlignment.Center,

                Margin =
                    new Thickness(20)
            };

        AchievementList.Children.Add(text);
    }

    private void ReplaceLoadingMessage(
        string message)
    {
        AddLoadingMessage(message);
    }

    private void LoadAchievements()
    {
        try
        {
            if (!File.Exists(_spoolFile))
            {
                LoadAchievementsWithoutSpool();
                return;
            }

            List<SpoolRecord> spoolRecords =
                _spoolManager.GetRecords(
                    _spoolFile);

            _achievements.Clear();

            _achievements.AddRange(
                _achievementCache.GetAchievementsForGame(
                    _achievementGameId,
                    spoolRecords));

            BuildAchievementList();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LoadedWindow] " +
                $"Achievement load error: {ex}");

            Dialogue.ShowError(
                this,
                "UbiSlot",
                "UbiSlot couldn't load this game's achievements.");

            _allowClose = true;
            Close();
        }
    }

    private void LoadAchievementsWithoutSpool()
    {
        try
        {
            _achievements.Clear();

            _achievements.AddRange(
                _achievementCache.ReadAchievements(
                    _achievementGameId));

            BuildAchievementList();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LoadedWindow] " +
                $"Achievement load error: {ex}");

            Dialogue.ShowError(
                this,
                "UbiSlot",
                "UbiSlot couldn't load this game's achievements.");

            _allowClose = true;
            Close();
        }
    }

    private void BuildAchievementList()
    {
        int unlocked =
            _achievements.Count(
                achievement =>
                    achievement.IsUnlocked);

        string gameName =
            _gameDatabase.GetGameName(_achievementGameId)
            ?? _gameDatabase.GetGameName(_sessionGameId)
            ?? $"Game {_achievementGameId}";

        GameTitleText.Text =
            gameName;

        ProgressText.Text =
            $"{unlocked}/{_achievements.Count} unlocked · {_sessionStatusText}";

        AchievementList.Children.Clear();

        _selectionBoxes.Clear();
        _achievementCards.Clear();

        foreach (
            AchievementDefinition achievement
            in _achievements)
        {
            AddAchievement(
                achievement);
        }

        UpdateAchievementList();
        UpdateSelection();
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
                Width =
                    GridLength.Auto
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
                Width =
                    GridLength.Auto
            });

        var iconBorder =
            new Border
            {
                Width = 64,
                Height = 64,

                CornerRadius =
                    new CornerRadius(5),

                Background =
                    GetThemeBrush(
                        "FieldBackground")
            };

        if (!string.IsNullOrWhiteSpace(
                achievement.IconPath) &&
            File.Exists(
                achievement.IconPath))
        {
            var image =
                new WpfImage
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
            iconBorder.Child =
                new TextBlock
                {
                    Text = "?",

                    FontSize = 24,

                    Foreground =
                        GetThemeBrush(
                            "MutedText"),

                    HorizontalAlignment =
                        WpfHorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center
                };
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

        textPanel.Children.Add(
            new TextBlock
            {
                Text =
                    achievement.Name,

                FontSize = 15,

                FontWeight =
                    FontWeights.SemiBold
            });

        textPanel.Children.Add(
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
                    GetThemeBrush(
                        "SecondaryText"),

                FontSize = 12,

                TextWrapping =
                    TextWrapping.Wrap
            });

        if (!achievement.IsUnlocked)
        {
            textPanel.Children.Add(
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
                        GetThemeBrush(
                            "MutedText"),

                    FontSize = 11
                });
        }

        Grid.SetColumn(
            textPanel,
            1);

        grid.Children.Add(
            textPanel);

        _achievementCards[
            achievement] =
            card;

        if (achievement.IsUnlocked)
        {
            var unlockDateText =
                new TextBlock
                {
                    Text =
                        achievement.UnlockTimeUtc.HasValue
                            ? achievement.UnlockTimeUtc
                                .Value
                                .ToLocalTime()
                                .ToString(
                                    "dd MMM yyyy, HH:mm")
                            : string.Empty,

                    Foreground =
                        GetThemeBrush(
                            "SuccessText"),

                    FontSize = 15,

                    FontWeight =
                        FontWeights.SemiBold,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    HorizontalAlignment =
                        WpfHorizontalAlignment.Center
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
                new WpfCheckBox
                {
                    Width = 28,
                    Height = 28,

                    RenderTransform =
                        new ScaleTransform(
                            1.8,
                            1.8),

                    RenderTransformOrigin =
                        new WpfPoint(
                            0.5,
                            0.5),

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    HorizontalAlignment =
                        WpfHorizontalAlignment.Center
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
            selected > 0 &&
            _gameReady &&
            !_sessionClosing;

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
        if (!_gameReady ||
            _sessionClosing)
        {
            return;
        }

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

        foreach (
            WpfCheckBox checkBox
            in _selectionBoxes)
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

    private void SortButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_gameReady ||
            _sessionClosing)
        {
            return;
        }

        SortPopup.IsOpen =
            !SortPopup.IsOpen;
    }

    private void SortOption_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            string.IsNullOrWhiteSpace(
                button.Tag?.ToString()))
        {
            return;
        }

        if (!Enum.TryParse(
                button.Tag!.ToString(),
                true,
                out AchievementSortMode sortMode))
        {
            return;
        }

        _sortMode =
            sortMode;

        SortPopup.IsOpen =
            false;

        ApplyAchievementSort();
    }

    private void ApplyAchievementSort()
    {
        HashSet<uint> selectedAchievementIds =
            _selectionBoxes
                .Where(
                    checkBox =>
                        checkBox.IsChecked == true)
                .Select(
                    checkBox =>
                        ((AchievementDefinition)
                            checkBox.Tag!)
                            .AchievementId)
                .ToHashSet();

        IEnumerable<AchievementDefinition> sorted;

        switch (_sortMode)
        {
            case AchievementSortMode.Name:

                sorted =
                    _achievements
                        .OrderBy(
                            achievement =>
                                achievement.Name,
                            StringComparer.OrdinalIgnoreCase)
                        .ThenBy(
                            achievement =>
                                achievement.AchievementId);

                break;

            case AchievementSortMode.DateAchieved:

                sorted =
                    _achievements
                        .OrderBy(
                            achievement =>
                                achievement.UnlockTimeUtc ??
                                DateTime.MaxValue)
                        .ThenBy(
                            achievement =>
                                achievement.Name,
                            StringComparer.OrdinalIgnoreCase);

                break;

            case AchievementSortMode.Locked:

                sorted =
                    _achievements
                        .OrderByDescending(
                            achievement =>
                                !achievement.IsUnlocked)
                        .ThenBy(
                            achievement =>
                                achievement.Name,
                            StringComparer.OrdinalIgnoreCase);

                break;

            case AchievementSortMode.Unlocked:

                sorted =
                    _achievements
                        .OrderByDescending(
                            achievement =>
                                achievement.IsUnlocked)
                        .ThenBy(
                            achievement =>
                                achievement.Name,
                            StringComparer.OrdinalIgnoreCase);

                break;

            default:

                sorted =
                    _defaultAchievementOrder;

                break;
        }

        List<AchievementDefinition> newOrder =
            sorted.ToList();

        _achievements.Clear();

        _achievements.AddRange(
            newOrder);

        BuildAchievementList();

        foreach (
            WpfCheckBox checkBox
            in _selectionBoxes)
        {
            if (checkBox.Tag is AchievementDefinition achievement)
            {
                checkBox.IsChecked =
                    selectedAchievementIds.Contains(
                        achievement.AchievementId);
            }
        }

        UpdateSelection();
    }

    private void CaptureDefaultAchievementOrder()
    {
        if (_defaultOrderCaptured)
        {
            return;
        }

        _defaultAchievementOrder.Clear();

        _defaultAchievementOrder.AddRange(
            _achievements);

        _defaultOrderCaptured =
            true;
    }

    private void SearchButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_gameReady ||
            _sessionClosing)
        {
            return;
        }

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
        if (!_gameReady ||
            _sessionClosing)
        {
            return;
        }

        UpdateAchievementList();
    }

    private void UpdateAchievementList()
    {
        string search =
            AchievementSearchBox.Text.Trim();

        foreach (
            KeyValuePair<
                AchievementDefinition,
                Border> item
            in _achievementCards)
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

    private void CaptureOriginalSpoolState()
    {
        _originalSpoolData = null;
        _originalSpoolExisted =
            !string.IsNullOrWhiteSpace(_spoolFile) &&
            File.Exists(_spoolFile);

        _ubislotCreatedSpool = false;
        _hasSessionChanges = false;

        if (!_originalSpoolExisted)
        {
            Console.WriteLine(
                "[LoadedWindow] " +
                "Original spool state: NO SPOOL.");

            return;
        }

        try
        {
            _originalSpoolData =
                File.ReadAllBytes(
                    _spoolFile);

            _spoolManager.ValidateSerializedRecords(
                _originalSpoolData);

            Console.WriteLine(
                "[LoadedWindow] " +
                $"Original spool state captured: {_spoolFile}");
        }
        catch (Exception ex)
        {
            _originalSpoolData = null;

            Console.WriteLine(
                "[LoadedWindow] " +
                $"Original spool snapshot failed: {ex.Message}");

            throw;
        }
    }

    private void WriteModifiedSpool(
        byte[] serializedData)
    {
        if (string.IsNullOrWhiteSpace(_spoolFile))
        {
            throw new InvalidOperationException(
                "No target spool file is available.");
        }

        if (File.Exists(_spoolFile))
        {
            _spoolManager.WriteSpoolFile(
                _spoolFile,
                serializedData);

            return;
        }

        string? directory =
            Path.GetDirectoryName(
                _spoolFile);

        if (!string.IsNullOrWhiteSpace(
                directory))
        {
            Directory.CreateDirectory(
                directory);
        }

        string temporaryFile =
            _spoolFile +
            ".ubislot.tmp";

        try
        {
            File.WriteAllBytes(
                temporaryFile,
                serializedData);

            byte[] writtenData =
                File.ReadAllBytes(
                    temporaryFile);

            _spoolManager.ValidateSerializedRecords(
                writtenData);

            File.Move(
                temporaryFile,
                _spoolFile,
                true);

            _ubislotCreatedSpool = true;
        }
        finally
        {
            if (File.Exists(
                    temporaryFile))
            {
                File.Delete(
                    temporaryFile);
            }
        }
    }

    private void RestoreSessionOriginalSpool()
    {
        if (string.IsNullOrWhiteSpace(
                _spoolFile))
        {
            throw new InvalidOperationException(
                "No target spool file is available.");
        }

        if (_originalSpoolExisted &&
            _originalSpoolData != null)
        {
            string? directory =
                Path.GetDirectoryName(
                    _spoolFile);

            if (!string.IsNullOrWhiteSpace(
                    directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            string temporaryFile =
                _spoolFile +
                ".ubislot.restore.tmp";

            try
            {
                File.WriteAllBytes(
                    temporaryFile,
                    _originalSpoolData);

                byte[] restoredData =
                    File.ReadAllBytes(
                        temporaryFile);

                _spoolManager.ValidateSerializedRecords(
                    restoredData);

                File.Move(
                    temporaryFile,
                    _spoolFile,
                    true);

                _ubislotCreatedSpool = false;
                _hasSessionChanges = false;

                Console.WriteLine(
                    "[LoadedWindow] " +
                    $"Restored original spool: {_spoolFile}");

                return;
            }
            finally
            {
                if (File.Exists(
                        temporaryFile))
                {
                    File.Delete(
                        temporaryFile);
                }
            }
        }

        if (_ubislotCreatedSpool &&
            File.Exists(_spoolFile))
        {
            File.Delete(
                _spoolFile);

            _ubislotCreatedSpool = false;
            _hasSessionChanges = false;

            Console.WriteLine(
                "[LoadedWindow] " +
                $"Removed UbiSlot-created spool: {_spoolFile}");

            return;
        }

        throw new InvalidOperationException(
            "There are no UbiSlot changes to restore.");
    }

    private void RestoreOriginalButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_gameReady ||
            _sessionClosing)
        {
            return;
        }

        if (!_hasSessionChanges)
        {
            Dialogue.ShowInformation(
                this,
                "Restore Original",
                "There are no UbiSlot achievement changes to restore.");

            return;
        }

        bool confirmed =
            Dialogue.ShowConfirmation(
                this,
                "Restore Original",
                "Restore the achievement data from when UbiSlot opened this game?\n\n" +
                "Any achievement changes made by UbiSlot during this session will be removed.\n\n" +
                "This does not lock achievements that have already been synchronized.",
                "Restore",
                "Cancel",
                DialogueIconKind.Warning);

        if (!confirmed)
        {
            return;
        }

        try
        {
            RestoreSessionOriginalSpool();

            if (File.Exists(
                    _spoolFile))
            {
                LoadAchievements();
            }
            else
            {
                LoadAchievementsWithoutSpool();
            }

            AchievementSearchBox.Clear();
            SearchPopup.IsOpen = false;

            ProgressText.Text =
                "Original local achievement data restored.";

            UpdateSelection();

            Dialogue.ShowInformation(
                this,
                "Restore Original",
                "The original local achievement data has been restored.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[LoadedWindow] " +
                $"Restore error: {ex}");

            Dialogue.ShowError(
                this,
                "Restore Original",
                "UbiSlot couldn't restore the original achievement data.\n\n" +
                ex.Message);
        }
    }

    private void GetAchievementButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!_gameReady ||
            _sessionClosing)
        {
            return;
        }

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
            + "The changes will be written immediately."
            + "\n"
            + "Keep this window open if you want to restore the original data."
            + "\n\n"
            + "Once the window is closed and the Ubisoft game session ends, "
            + "synchronized achievements cannot be locked again through UbiSlot."
            + "\n"
            + "Avoid unlocking all achievements at once. This is untested and may carry additional risk.";

        bool confirmed =
            Dialogue.ShowConfirmation(
                this,
                "Confirm Achievement Unlock",
                message,
                "Unlock",
                "Cancel",
                DialogueIconKind.Warning);

        if (!confirmed)
        {
            return;
        }

        try
        {
            PrepareAchievementChanges(
                selected);
        }
        catch (UnauthorizedAccessException)
        {
            Dialogue.ShowError(
                this,
                "Unlock Achievement",
                "UbiSlot couldn't access this game's data.\n\n" +
                "Please close the game and try again.");
        }
        catch (IOException)
        {
            Dialogue.ShowError(
                this,
                "Unlock Achievement",
                "UbiSlot couldn't save the achievement.\n\n" +
                "Please close the game and try again.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LoadedWindow] Unlock error: {ex}");

            Dialogue.ShowError(
                this,
                "Unlock Achievement",
                "Something went wrong while preparing the achievement data.\n\n" +
                "Nothing was changed.");
        }
    }

    private void PrepareAchievementChanges(
        List<AchievementDefinition> selected)
    {
        List<SpoolRecord> existingRecords =
            File.Exists(_spoolFile)
                ? _spoolManager.GetRecords(
                    _spoolFile)
                : [];

        var existingIds =
            existingRecords
                .Select(
                    record =>
                        record.AchievementId)
                .ToHashSet();

        var newRecords =
            new List<SpoolRecord>();

        foreach (
            AchievementDefinition achievement
            in selected)
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

        WriteModifiedSpool(
            serializedData);

        _hasSessionChanges = true;

        Console.WriteLine(
            "[LoadedWindow] " +
            $"Achievement changes written immediately: {_spoolFile}");

        Console.WriteLine(
            "[LoadedWindow] " +
            "Achievement window remains open.");

        AchievementSearchBox.Clear();
        SearchPopup.IsOpen = false;

        if (File.Exists(
                _spoolFile))
        {
            LoadAchievements();
        }
        else
        {
            LoadAchievementsWithoutSpool();
        }

        ProgressText.Text =
            "Changes saved locally. Keep this window open to restore them.";

        UpdateSelection();
    }

    private void LoadedWindow_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        _sessionClosing = true;
        e.Cancel = false;
    }

    private System.Windows.Media.SolidColorBrush GetThemeBrush(
        string key)
    {
        if (FindResource(key) is System.Windows.Media.SolidColorBrush brush)
        {
            return brush;
        }

        return new System.Windows.Media.SolidColorBrush(
            _settings.IsDarkTheme
                ? WpfColor.FromRgb(255, 255, 255)
                : WpfColor.FromRgb(25, 25, 30));
    }

    private void ApplyWindowTheme(
        bool dark)
    {
        SetThemeBrush(
            "WindowBackground",
            dark
                ? WpfColor.FromRgb(16, 16, 20)
                : WpfColor.FromRgb(245, 245, 248));

        SetThemeBrush(
            "PanelBackground",
            dark
                ? WpfColor.FromRgb(21, 21, 26)
                : WpfColor.FromRgb(255, 255, 255));

        SetThemeBrush(
            "CardBackground",
            dark
                ? WpfColor.FromRgb(25, 25, 31)
                : WpfColor.FromRgb(255, 255, 255));

        SetThemeBrush(
            "FieldBackground",
            dark
                ? WpfColor.FromRgb(36, 36, 44)
                : WpfColor.FromRgb(239, 239, 244));

        SetThemeBrush(
            "BorderColor",
            dark
                ? WpfColor.FromRgb(41, 41, 49)
                : WpfColor.FromRgb(218, 218, 225));

        SetThemeBrush(
            "PrimaryText",
            dark
                ? WpfColor.FromRgb(242, 242, 245)
                : WpfColor.FromRgb(25, 25, 30));

        SetThemeBrush(
            "SecondaryText",
            dark
                ? WpfColor.FromRgb(133, 133, 143)
                : WpfColor.FromRgb(92, 92, 102));

        SetThemeBrush(
            "MutedText",
            dark
                ? WpfColor.FromRgb(102, 102, 112)
                : WpfColor.FromRgb(115, 115, 125));

        SetThemeBrush(
            "DisabledText",
            dark
                ? WpfColor.FromRgb(119, 119, 128)
                : WpfColor.FromRgb(155, 155, 165));

        SetThemeBrush(
            "HoverBackground",
            dark
                ? WpfColor.FromRgb(36, 36, 44)
                : WpfColor.FromRgb(232, 232, 238));

        SetThemeBrush(
            "PressedBackground",
            dark
                ? WpfColor.FromRgb(28, 28, 34)
                : WpfColor.FromRgb(224, 224, 231));

        SetThemeBrush(
            "AccentColor",
            WpfColor.FromRgb(61, 27, 93));

        SetThemeBrush(
            "AccentHoverColor",
            WpfColor.FromRgb(81, 40, 120));

        SetThemeBrush(
            "AccentPressedColor",
            WpfColor.FromRgb(43, 19, 63));

        SetThemeBrush(
            "IconColor",
            dark
                ? WpfColor.FromRgb(102, 102, 112)
                : WpfColor.FromRgb(74, 74, 84));

        SetThemeBrush(
            "SuccessText",
            dark
                ? WpfColor.FromRgb(80, 190, 120)
                : WpfColor.FromRgb(38, 132, 74));

        Background =
            GetThemeBrush(
                "WindowBackground");

        Foreground =
            GetThemeBrush(
                "PrimaryText");
    }

    private void SetThemeBrush(
        string key,
        WpfColor color)
    {
        Resources[key] =
            new System.Windows.Media.SolidColorBrush(
                color);
    }

}