using System.IO;
using System.IO.Compression;
using UbiSlot.Core;

namespace UbiSlot.Ubisoft;

public class AchievementCache
{
    public string GetCacheRoot()
    {
        string programData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);

        return Path.Combine(
            programData,
            "Ubisoft",
            "Ubisoft Game Launcher",
            "cache",
            "achievements");
    }

    public string? FindAchievementArchive(string gameId)
    {
        string cacheRoot =
            GetCacheRoot();

        if (!Directory.Exists(cacheRoot))
        {
            return null;
        }

        string prefix =
            $"{gameId}_";

        return Directory
            .EnumerateFiles(
                cacheRoot,
                $"{prefix}*",
                SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    public List<AchievementDefinition> ReadAchievements(
        string gameId)
    {
        string? archivePath =
            FindAchievementArchive(gameId);

        if (archivePath == null)
        {
            return [];
        }

        using ZipArchive archive =
            ZipFile.OpenRead(archivePath);

        ZipArchiveEntry? localizationFile =
            archive.Entries.FirstOrDefault(
                entry =>
                    entry.FullName.Equals(
                        "en-US_loc.txt",
                        StringComparison.OrdinalIgnoreCase));

        if (localizationFile == null)
        {
            return [];
        }

        var achievements =
            new List<AchievementDefinition>();

        using Stream stream =
            localizationFile.Open();

        using var reader =
            new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            string? line =
                reader.ReadLine();

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] parts =
                line.Split('\t');

            if (parts.Length < 2)
            {
                continue;
            }

            if (!uint.TryParse(
                    parts[0].Trim(),
                    out uint achievementId))
            {
                continue;
            }

            string name =
                parts[1].Trim();

            string description =
                parts.Length >= 3
                    ? string.Join(
                        "\t",
                        parts.Skip(2)).Trim()
                    : string.Empty;

            string? iconPath =
                ExtractIcon(
                    gameId,
                    archive,
                    achievementId);

            achievements.Add(
                new AchievementDefinition
                {
                    AchievementId =
                        achievementId,

                    Name =
                        name,

                    Description =
                        description,

                    IconPath =
                        iconPath,

                    IsUnlocked =
                        false,

                    UnlockTimeUtc =
                        null
                });
        }

        return achievements
            .OrderBy(
                achievement =>
                    achievement.AchievementId)
            .ToList();
    }

    public List<AchievementDefinition> GetAchievementsForGame(
        string gameId,
        List<SpoolRecord> spoolRecords)
    {
        List<AchievementDefinition> achievements =
            ReadAchievements(gameId);

        Dictionary<uint, SpoolRecord> recordsById =
            spoolRecords
                .GroupBy(
                    record =>
                        record.AchievementId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.First());

        foreach (AchievementDefinition achievement in achievements)
        {
            if (!recordsById.TryGetValue(
                    achievement.AchievementId,
                    out SpoolRecord? record))
            {
                continue;
            }

            achievement.IsUnlocked =
                true;

            achievement.UnlockTimeUtc =
                record.UnlockTimeUtc;
        }

        return achievements;
    }

    private string? ExtractIcon(
        string gameId,
        ZipArchive archive,
        uint achievementId)
    {
        string iconName =
            $"{achievementId}.png";

        ZipArchiveEntry? icon =
            archive.Entries.FirstOrDefault(
                entry =>
                    entry.FullName.Equals(
                        iconName,
                        StringComparison.OrdinalIgnoreCase));

        if (icon == null)
        {
            return null;
        }

        string iconDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "UbiSlot_Cache",
                "Icons",
                gameId);

        Directory.CreateDirectory(
            iconDirectory);

        string iconPath =
            Path.Combine(
                iconDirectory,
                iconName);

        if (!File.Exists(iconPath))
        {
            using Stream source =
                icon.Open();

            using FileStream destination =
                File.Create(iconPath);

            source.CopyTo(destination);
        }

        return iconPath;
    }
}