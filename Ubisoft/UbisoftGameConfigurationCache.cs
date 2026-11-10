using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UbiSlot.Ubisoft;

public sealed class UbisoftGameConfigurationCache
{
    private const int MinimumRecordSize = 500;
    private const long MaximumFileSize = 128L * 1024 * 1024;

    private static readonly Regex NameRegex = new(
        @"(?:^|\n)\s*name:\s*(.+?)(?:\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SpaceIdRegex = new(
        @"space_id:\s*([a-f0-9-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GameIdentifierRegex = new(
        @"game_identifier:\s*(.+?)(?:\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ThirdPartyPlatformRegex = new(
        @"third_party_platform:[^\S\n]*\n[^\S\n]*name:\s*(.+?)(?:\n|$)|third_party_platform:[^\S\n]*(\S.*?)(?:\n|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExecutableRegex = new(
        @"relative:\s*(.+?\.exe)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyDictionary<string, IReadOnlyList<UbisoftGameConfiguration>> ReadBySpaceId()
    {
        var empty = new Dictionary<string, IReadOnlyList<UbisoftGameConfiguration>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (string path in GetConfigurationCatalogCandidates())
        {
            Console.WriteLine($"[Ownership] Configuration catalog candidate: {path}");

            if (!File.Exists(path))
            {
                Console.WriteLine("[Ownership] Configuration catalog not found at this candidate.");
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > MaximumFileSize)
                {
                    Console.WriteLine(
                        $"[Ownership] Configuration catalog size rejected: {info.Length} bytes.");
                    continue;
                }

                byte[] data = ReadSharedFile(path);
                List<UbisoftGameConfiguration> records = Parse(data);

                var grouped = records
                    .Where(record => !string.IsNullOrWhiteSpace(record.SpaceId))
                    .GroupBy(record => NormalizeGuid(record.SpaceId), StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Key.Length > 0)
                    .ToDictionary(
                        group => group.Key,
                        group => (IReadOnlyList<UbisoftGameConfiguration>)group.ToArray(),
                        StringComparer.OrdinalIgnoreCase);

                Console.WriteLine(
                    $"[Ownership] Configuration catalog selected: {path}");
                Console.WriteLine(
                    $"[Ownership] Configuration records parsed: {records.Count}; unique spaces: {grouped.Count}");

                if (grouped.Count > 0)
                {
                    return grouped;
                }

                Console.WriteLine(
                    "[Ownership] Configuration catalog was readable but contained no recognized game records.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Ownership] Configuration catalog read failed: {ex.Message}");
            }
        }

        Console.WriteLine(
            "[Ownership] No readable Ubisoft Connect configuration catalog was found in discovered locations.");
        return empty;
    }

    private static IEnumerable<string> GetConfigurationCatalogCandidates()
    {
        var installDirectories = new List<string>();

        AddRunningProcessDirectory(installDirectories, "UbisoftGameLauncher");
        AddRunningProcessDirectory(installDirectories, "UbisoftConnect");
        AddRegistryInstallLocations(installDirectories);

        string programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        string programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        AddCandidateDirectory(
            installDirectories,
            Path.Combine(programFilesX86, "Ubisoft", "Ubisoft Game Launcher"));
        AddCandidateDirectory(
            installDirectories,
            Path.Combine(programFiles, "Ubisoft", "Ubisoft Game Launcher"));
        AddCandidateDirectory(
            installDirectories,
            Path.Combine(programFilesX86, "Ubisoft", "Ubisoft Connect"));
        AddCandidateDirectory(
            installDirectories,
            Path.Combine(programFiles, "Ubisoft", "Ubisoft Connect"));
        AddCandidateDirectory(
            installDirectories,
            Path.Combine(localAppData, "Programs", "Ubisoft Game Launcher"));
        AddCandidateDirectory(
            installDirectories,
            Path.Combine(localAppData, "Programs", "Ubisoft Connect"));

        var files = new List<string>();
        foreach (string directory in installDirectories)
        {
            AddCandidateFile(
                files,
                Path.Combine(directory, "cache", "configuration", "configurations"));
        }

        AddCandidateFile(
            files,
            Path.Combine(
                localAppData,
                "Ubisoft Game Launcher",
                "cache",
                "configuration",
                "configurations"));

        return files;
    }

    private static void AddRunningProcessDirectory(
        List<string> candidates,
        string processName)
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return;
        }

        foreach (Process process in processes)
        {
            using (process)
            {
                try
                {
                    string? executablePath = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executablePath))
                    {
                        AddCandidateDirectory(
                            candidates,
                            Path.GetDirectoryName(executablePath));
                    }
                }
                catch
                {
                }
            }
        }
    }

    private static void AddRegistryInstallLocations(List<string> candidates)
    {
        RegistryHive[] hives =
        [
            RegistryHive.LocalMachine,
            RegistryHive.CurrentUser
        ];
        RegistryView[] views =
        [
            RegistryView.Registry32,
            RegistryView.Registry64
        ];
        const string uninstallPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        string[] launcherRoots =
        [
            @"SOFTWARE\Ubisoft\Launcher",
            @"SOFTWARE\Ubisoft Game Launcher",
            @"SOFTWARE\Ubisoft\Ubisoft Connect",
            @"SOFTWARE\Ubisoft\Ubisoft Game Launcher"
        ];

        string[] installValueNames =
        [
            "InstallDir",
            "InstallPath",
            "InstallLocation",
            "InstallDirectory",
            "Path"
        ];

        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                try
                {
                    using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);

                    foreach (string launcherRoot in launcherRoots)
                    {
                        try
                        {
                            using RegistryKey? launcherKey = baseKey.OpenSubKey(launcherRoot);
                            if (launcherKey == null)
                            {
                                continue;
                            }

                            foreach (string valueName in installValueNames)
                            {
                                AddCandidateDirectory(
                                    candidates,
                                    launcherKey.GetValue(valueName) as string);
                            }
                        }
                        catch
                        {
                        }
                    }

                    using RegistryKey? uninstallKey = baseKey.OpenSubKey(uninstallPath);
                    if (uninstallKey == null)
                    {
                        continue;
                    }

                    foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using RegistryKey? key = uninstallKey.OpenSubKey(subKeyName);
                            if (key == null)
                            {
                                continue;
                            }

                            string? displayName = key.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName) ||
                                (!displayName.Contains("Ubisoft Connect", StringComparison.OrdinalIgnoreCase) &&
                                 !displayName.Contains("Ubisoft Game Launcher", StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }

                            AddCandidateDirectory(
                                candidates,
                                key.GetValue("InstallLocation") as string);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {

                }
            }
        }
    }

    private static void AddCandidateDirectory(List<string> candidates, string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        try
        {
            string fullPath = Path.GetFullPath(directory.Trim());
            if (!candidates.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(fullPath);
            }
        }
        catch
        {
        }
    }

    private static void AddCandidateFile(List<string> files, string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!files.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(fullPath);
            }
        }
        catch
        {
        }
    }

    private static List<UbisoftGameConfiguration> Parse(byte[] data)
    {
        var results = new List<UbisoftGameConfiguration>();
        int offset = 0;

        while (offset < data.Length)
        {
            if (data[offset] != 0x0A)
            {
                offset++;
                continue;
            }

            int recordStart = offset;
            try
            {
                int sizeOffset = offset + 1;
                ulong rawSize = ReadVarint(data, ref sizeOffset);
                ulong decodedSize = DecodeCompactVarint(rawSize);

                if (decodedSize < MinimumRecordSize || decodedSize > int.MaxValue)
                {
                    offset = recordStart + 1;
                    continue;
                }

                long recordEndLong = (long)sizeOffset + (long)decodedSize;
                if (recordEndLong > data.Length)
                {
                    break;
                }

                int recordEnd = (int)recordEndLong;
                byte[] record = data.AsSpan(sizeOffset, (int)decodedSize).ToArray();
                UbisoftGameConfiguration? parsed = ParseRecord(record);
                if (parsed != null && parsed.SpaceId.Length > 0)
                {
                    results.Add(parsed);
                }

                offset = recordEnd;
            }
            catch (Exception)
            {
                offset = recordStart + 1;
            }
        }

        return results;
    }

    private static UbisoftGameConfiguration? ParseRecord(byte[] record)
    {
        int position = 0;
        uint installId = 0;
        uint launchId = 0;

        if (position < record.Length && record[position] == 0x08)
        {
            position++;
            ulong raw = ReadVarint(record, ref position);
            installId = ToUInt32(DecodeCompactVarint(raw));
        }

        if (position < record.Length && record[position] == 0x10)
        {
            position++;
            ulong raw = ReadVarint(record, ref position);
            launchId = ToUInt32(DecodeCompactVarint(raw));
        }

        if (launchId == 0 || launchId == installId)
        {
            launchId = installId;
        }

        int yamlMarker = Array.IndexOf(record, (byte)0x1A, position);
        if (yamlMarker < 0)
        {
            return null;
        }

        int lengthOffset = yamlMarker + 1;
        ulong rawLength = ReadVarint(record, ref lengthOffset);
        ulong decodedLength = DecodeCompactVarint(rawLength);
        if (decodedLength > int.MaxValue || lengthOffset > record.Length)
        {
            return null;
        }

        int available = record.Length - lengthOffset;
        int yamlLength = (int)Math.Min(decodedLength, (ulong)available);
        string yaml = Encoding.UTF8.GetString(record, lengthOffset, yamlLength);
        yaml = Regex.Replace(yaml, @"[\x00-\x08\x0b\x0c\x0e-\x1f]", string.Empty);

        if (!yaml.Contains("start_game", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string spaceId = Extract(SpaceIdRegex, yaml);
        if (!Guid.TryParse(spaceId, out Guid parsedSpaceId))
        {
            return null;
        }

        string name = Extract(NameRegex, yaml);
        string gameIdentifier = Extract(GameIdentifierRegex, yaml);
        Match platformMatch = ThirdPartyPlatformRegex.Match(yaml);
        string platform = platformMatch.Success
            ? (platformMatch.Groups[1].Success
                ? platformMatch.Groups[1].Value
                : platformMatch.Groups[2].Value).Trim().Trim('\'', '"')
            : string.Empty;
        string executable = Extract(ExecutableRegex, yaml);

        return new UbisoftGameConfiguration(
            installId,
            launchId,
            parsedSpaceId.ToString("D").ToLowerInvariant(),
            name,
            gameIdentifier,
            platform,
            executable);
    }

    private static string Extract(Regex regex, string text)
    {
        Match match = regex.Match(text);
        return match.Success
            ? match.Groups[1].Value.Trim().Trim('\'', '"')
            : string.Empty;
    }

    private static byte[] ReadSharedFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length > int.MaxValue)
        {
            throw new IOException("File is too large to read into memory.");
        }

        byte[] data = new byte[(int)stream.Length];
        int totalRead = 0;
        while (totalRead < data.Length)
        {
            int read = stream.Read(data, totalRead, data.Length - totalRead);
            if (read == 0)
            {
                Array.Resize(ref data, totalRead);
                break;
            }
            totalRead += read;
        }

        return data;
    }

    private static ulong ReadVarint(byte[] data, ref int position)
    {
        ulong value = 0;
        int shift = 0;

        while (position < data.Length && shift < 64)
        {
            byte current = data[position++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0)
            {
                return value;
            }
            shift += 7;
        }

        throw new InvalidDataException("Invalid or truncated configuration varint.");
    }

    private static ulong DecodeCompactVarint(ulong raw)
    {
        if (raw > 256UL * 256UL)
        {
            raw = SubtractCeilingMultiple(raw, 128UL * 256UL, 256UL * 256UL);
            raw = SubtractCeilingMultiple(raw, 128UL, 256UL);
        }
        else if (raw > 256UL)
        {
            raw = SubtractCeilingMultiple(raw, 128UL, 256UL);
        }

        return raw;
    }

    private static ulong SubtractCeilingMultiple(ulong value, ulong multiplier, ulong divisor)
    {
        ulong quotient = value / divisor;
        if (value % divisor != 0)
        {
            quotient++;
        }

        ulong subtraction = checked(multiplier * quotient);
        return subtraction > value ? 0 : value - subtraction;
    }

    private static uint ToUInt32(ulong value)
    {
        if (value > uint.MaxValue)
        {
            throw new InvalidDataException("Configuration ID exceeds UInt32.");
        }

        return (uint)value;
    }

    private static string NormalizeGuid(string value)
    {
        return Guid.TryParse(value, out Guid parsed)
            ? parsed.ToString("D").ToLowerInvariant()
            : string.Empty;
    }
}

public sealed record UbisoftGameConfiguration(
    uint InstallId,
    uint LaunchId,
    string SpaceId,
    string Name,
    string GameIdentifier,
    string ThirdPartyPlatform,
    string Executable);
