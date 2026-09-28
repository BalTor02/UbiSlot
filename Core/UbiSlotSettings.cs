using System;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UbiSlot.Core;

public sealed class UbiSlotSettings
{
    private const string SettingsFileName =
        "UbiSlot_Settings.ini";

    private const string GitHubReleasesUrl =
        "https://api.github.com/repos/BalTor02/UbiSlot/releases";

    public const string CurrentVersion =
        "0.3.0-beta";

    private static readonly HttpClient HttpClient =
        CreateHttpClient();

    public static string SettingsDirectory =>
        Path.Combine(
            AppContext.BaseDirectory,
            "UbiSlot_Cache");

    public static string SettingsFilePath =>
        Path.Combine(
            SettingsDirectory,
            SettingsFileName);

    public static string DefaultBackupPath
    {
        get
        {
            string localAppData =
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

            return Path.Combine(
                localAppData,
                "Ubisoft Game Launcher",
                "UbiSlot_Backup");
        }
    }

    public string Theme { get; set; } =
        "Dark";

    public bool ShowGameName { get; set; } =
        true;

    public bool ShowCompletion { get; set; } =
        true;

    public bool HideZeroAchievementGames { get; set; } =
        false;

    public string BackupPath { get; set; } =
        DefaultBackupPath;

    public bool AutoUpdateCheck { get; set; } =
        true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public bool IsDarkTheme =>
        !string.Equals(
            Theme,
            "Light",
            StringComparison.OrdinalIgnoreCase);

    public static UbiSlotSettings CreateDefault()
    {
        return new UbiSlotSettings();
    }

    public static UbiSlotSettings Load()
    {
        UbiSlotSettings settings =
            CreateDefault();

        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                settings.Save();

                return settings;
            }

            foreach (
                string rawLine
                in File.ReadLines(SettingsFilePath))
            {
                string line =
                    rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) ||
                    line.StartsWith(
                        "#",
                        StringComparison.Ordinal) ||
                    line.StartsWith(
                        ";",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                int separator =
                    line.IndexOf('=');

                if (separator <= 0)
                {
                    continue;
                }

                string key =
                    line[..separator].Trim();

                string value =
                    line[(separator + 1)..].Trim();

                switch (key.ToLowerInvariant())
                {
                    case "theme":
                        {
                            settings.Theme =
                                string.Equals(
                                    value,
                                    "Light",
                                    StringComparison.OrdinalIgnoreCase)
                                    ? "Light"
                                    : "Dark";

                            break;
                        }

                    case "showgamename":
                        {
                            if (bool.TryParse(
                                    value,
                                    out bool result))
                            {
                                settings.ShowGameName =
                                    result;
                            }

                            break;
                        }

                    case "showcompletion":
                        {
                            if (bool.TryParse(
                                    value,
                                    out bool result))
                            {
                                settings.ShowCompletion =
                                    result;
                            }

                            break;
                        }

                    case "hidezeroachievementgames":
                        {
                            if (bool.TryParse(
                                    value,
                                    out bool result))
                            {
                                settings.HideZeroAchievementGames =
                                    result;
                            }

                            break;
                        }

                    case "backuppath":
                        {
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                settings.BackupPath =
                                    value;
                            }

                            break;
                        }

                    case "autoupdatecheck":
                        {
                            if (bool.TryParse(
                                    value,
                                    out bool result))
                            {
                                settings.AutoUpdateCheck =
                                    result;
                            }

                            break;
                        }

                    case "lastupdatecheckutc":
                        {
                            if (DateTimeOffset.TryParse(
                                    value,
                                    CultureInfo.InvariantCulture,
                                    DateTimeStyles.RoundtripKind,
                                    out DateTimeOffset result))
                            {
                                settings.LastUpdateCheckUtc =
                                    result;
                            }

                            break;
                        }
                }
            }

            if (string.IsNullOrWhiteSpace(
                    settings.BackupPath))
            {
                settings.BackupPath =
                    DefaultBackupPath;
            }
        }
        catch
        {
            return CreateDefault();
        }

        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(
                SettingsDirectory);

            string temporaryPath =
                SettingsFilePath +
                ".tmp";

            string lastUpdateCheck =
                LastUpdateCheckUtc?.ToString(
                    "O",
                    CultureInfo.InvariantCulture)
                ?? string.Empty;

            string contents =
                $"""
                # UbiSlot Settings

                Theme={Theme}
                ShowGameName={ShowGameName}
                ShowCompletion={ShowCompletion}
                HideZeroAchievementGames={HideZeroAchievementGames}
                BackupPath={BackupPath}
                AutoUpdateCheck={AutoUpdateCheck}
                LastUpdateCheckUtc={lastUpdateCheck}
                """;

            File.WriteAllText(
                temporaryPath,
                contents);

            File.Move(
                temporaryPath,
                SettingsFilePath,
                true);
        }
        catch
        {
    
        }
    }

    public async Task<GitHubReleaseInfo?> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response =
                await HttpClient.GetAsync(
                    GitHubReleasesUrl,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            await using Stream stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            using JsonDocument document =
                await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);

            if (document.RootElement.ValueKind !=
                JsonValueKind.Array)
            {
                return null;
            }

            GitHubReleaseInfo? newestRelease =
                null;

            foreach (
                JsonElement releaseElement
                in document.RootElement.EnumerateArray())
            {
                if (releaseElement.TryGetProperty(
                        "draft",
                        out JsonElement draftElement) &&
                    draftElement.GetBoolean())
                {
                    continue;
                }

                string tagName =
                    GetString(
                        releaseElement,
                        "tag_name");

                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                string releaseName =
                    GetString(
                        releaseElement,
                        "name");

                string htmlUrl =
                    GetString(
                        releaseElement,
                        "html_url");

                bool isPrerelease =
                    releaseElement.TryGetProperty(
                        "prerelease",
                        out JsonElement prereleaseElement) &&
                    prereleaseElement.GetBoolean();

                DateTimeOffset? publishedAt =
                    GetDateTime(
                        releaseElement,
                        "published_at");

                DateTimeOffset? createdAt =
                    GetDateTime(
                        releaseElement,
                        "created_at");

                var release =
                    new GitHubReleaseInfo
                    {
                        TagName =
                            tagName,

                        Name =
                            string.IsNullOrWhiteSpace(
                                releaseName)
                                ? tagName
                                : releaseName,

                        HtmlUrl =
                            htmlUrl,

                        IsPrerelease =
                            isPrerelease,

                        PublishedAt =
                            publishedAt ??
                            createdAt
                    };

                if (newestRelease == null)
                {
                    newestRelease =
                        release;

                    continue;
                }

                int comparison =
                    CompareVersions(
                        release.TagName,
                        newestRelease.TagName);

                if (comparison > 0)
                {
                    newestRelease =
                        release;
                }
                else if (
                    comparison == 0 &&
                    release.PublishedAt >
                    newestRelease.PublishedAt)
                {
                    newestRelease =
                        release;
                }
            }

            if (newestRelease == null)
            {
                return null;
            }

            return CompareVersions(
                       newestRelease.TagName,
                       CurrentVersion) > 0
                ? newestRelease
                : null;
        }
        catch
        {
            return null;
        }
    }

    public static int CompareVersions(
        string left,
        string right)
    {
        ParsedVersion leftVersion =
            ParseVersion(left);

        ParsedVersion rightVersion =
            ParseVersion(right);

        int result =
            leftVersion.Major.CompareTo(
                rightVersion.Major);

        if (result != 0)
        {
            return result;
        }

        result =
            leftVersion.Minor.CompareTo(
                rightVersion.Minor);

        if (result != 0)
        {
            return result;
        }

        result =
            leftVersion.Patch.CompareTo(
                rightVersion.Patch);

        if (result != 0)
        {
            return result;
        }

        if (leftVersion.PreRelease == null &&
            rightVersion.PreRelease != null)
        {
            return 1;
        }

        if (leftVersion.PreRelease != null &&
            rightVersion.PreRelease == null)
        {
            return -1;
        }

        if (leftVersion.PreRelease == null &&
            rightVersion.PreRelease == null)
        {
            return 0;
        }

        return ComparePrerelease(
            leftVersion.PreRelease!,
            rightVersion.PreRelease!);
    }

    private static ParsedVersion ParseVersion(
        string value)
    {
        string normalized =
            value.Trim();

        if (normalized.StartsWith(
                "v",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized =
                normalized[1..];
        }

        Match match =
            Regex.Match(
                normalized,
                @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[^+]+))?");

        if (!match.Success)
        {
            return new ParsedVersion();
        }

        return new ParsedVersion
        {
            Major =
                int.Parse(
                    match.Groups["major"].Value,
                    CultureInfo.InvariantCulture),

            Minor =
                int.Parse(
                    match.Groups["minor"].Value,
                    CultureInfo.InvariantCulture),

            Patch =
                int.Parse(
                    match.Groups["patch"].Value,
                    CultureInfo.InvariantCulture),

            PreRelease =
                match.Groups["pre"].Success
                    ? match.Groups["pre"].Value
                    : null
        };
    }

    private static int ComparePrerelease(
        string left,
        string right)
    {
        string[] leftParts =
            left.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries);

        string[] rightParts =
            right.Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries);

        int count =
            Math.Min(
                leftParts.Length,
                rightParts.Length);

        for (int i = 0; i < count; i++)
        {
            string leftPart =
                leftParts[i];

            string rightPart =
                rightParts[i];

            bool leftNumeric =
                int.TryParse(
                    leftPart,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int leftNumber);

            bool rightNumeric =
                int.TryParse(
                    rightPart,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int rightNumber);

            if (leftNumeric && rightNumeric)
            {
                int result =
                    leftNumber.CompareTo(
                        rightNumber);

                if (result != 0)
                {
                    return result;
                }

                continue;
            }

            if (leftNumeric && !rightNumeric)
            {
                return -1;
            }

            if (!leftNumeric && rightNumeric)
            {
                return 1;
            }

            int stringResult =
                string.CompareOrdinal(
                    leftPart,
                    rightPart);

            if (stringResult != 0)
            {
                return stringResult;
            }
        }

        return leftParts.Length.CompareTo(
            rightParts.Length);
    }

    private static HttpClient CreateHttpClient()
    {
        var client =
            new HttpClient();

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(
                "UbiSlot",
                CurrentVersion));

        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/vnd.github+json"));

        return client;
    }

    private static string GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out JsonElement value))
        {
            return string.Empty;
        }

        return value.GetString() ??
               string.Empty;
    }

    private static DateTimeOffset? GetDateTime(
        JsonElement element,
        string propertyName)
    {
        string value =
            GetString(
                element,
                propertyName);

        return DateTimeOffset.TryParse(
                   value,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out DateTimeOffset result)
            ? result
            : null;
    }

    private sealed class ParsedVersion
    {
        public int Major { get; init; }

        public int Minor { get; init; }

        public int Patch { get; init; }

        public string? PreRelease { get; init; }
    }
}

public sealed class GitHubReleaseInfo
{
    public string TagName { get; init; } =
        string.Empty;

    public string Name { get; init; } =
        string.Empty;

    public string HtmlUrl { get; init; } =
        string.Empty;

    public bool IsPrerelease { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }
}