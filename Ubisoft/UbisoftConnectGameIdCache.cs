using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UbiSlot.Ubisoft;

public sealed class UbisoftConnectGameIdCache
{
    private const long MaxLevelDbFileSize = 16L * 1024 * 1024;

    private static readonly Regex Tokens = new(
        "(?<open>\\{)|(?<close>\\})|(?<nul>\\x00)|" +
        "\"spaceId\"\\s*:\\s*\"(?<space>[a-f0-9-]+)\"|" +
        "\"ubisoftConnectGameId\"\\s*:\\s*(?<connect>\\d+)",
        RegexOptions.IgnoreCase |
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    public IReadOnlyDictionary<string, uint> ReadMappings()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        string http2Directory = Path.Combine(
            localAppData,
            "Ubisoft Game Launcher",
            "cache",
            "http2");

        var candidates = new List<string>();
        string defaultLevelDb = Path.Combine(
            http2Directory,
            "Default",
            "Local Storage",
            "leveldb");
        candidates.Add(defaultLevelDb);

        if (Directory.Exists(http2Directory))
        {
            try
            {
                foreach (string profileDirectory in Directory.EnumerateDirectories(http2Directory))
                {
                    string discovered = Path.Combine(profileDirectory, "Local Storage", "leveldb");
                    if (Directory.Exists(discovered))
                    {
                        candidates.Add(discovered);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Ownership] Could not enumerate Chromium profiles: {ex.Message}");
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in candidates)
        {
            if (!visited.Add(candidate))
            {
                continue;
            }

            if (!Directory.Exists(candidate))
            {
                Console.WriteLine(
                    $"[Ownership] Local Storage LevelDB path not found: {candidate}");
                continue;
            }

            IReadOnlyDictionary<string, uint> mappings =
                ReadMappings(candidate);

            if (mappings.Count > 0)
            {
                return mappings;
            }
        }

        Console.WriteLine(
            "[Ownership] No spaceId/ubisoftConnectGameId pairs found in the known Local Storage LevelDB locations.");

        return new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
    }

    internal IReadOnlyDictionary<string, uint> ReadMappings(string levelDbDirectory)
    {
        var empty = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(levelDbDirectory))
        {
            Console.WriteLine(
                $"[Ownership] Ubisoft Local Storage LevelDB not found: {levelDbDirectory}");
            return empty;
        }

        Console.WriteLine(
            $"[Ownership] Scanning Ubisoft Local Storage LevelDB: {levelDbDirectory}");

        string[] tableFiles;
        string[] logFiles;

        try
        {
            tableFiles = Directory.GetFiles(levelDbDirectory, "*.ldb")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            logFiles = Directory.GetFiles(levelDbDirectory, "*.log")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ownership] Could not enumerate Local Storage LevelDB: {ex.Message}");
            return empty;
        }

        string[] files = tableFiles
            .Concat(logFiles)
            .ToArray();

        Console.WriteLine(
            $"[Ownership] Local Storage files: {tableFiles.Length} .ldb; {logFiles.Length} .log");

        if (files.Length == 0)
        {
            Console.WriteLine(
                "[Ownership] No .ldb or .log files found in Local Storage LevelDB.");
            return empty;
        }

        var pairs = new List<(string SpaceId, uint ConnectId)>();
        int filesScanned = 0;
        int filesSkipped = 0;
        int spaceTokens = 0;
        int connectTokens = 0;

        foreach (string file in files)
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length <= 0 || info.Length > MaxLevelDbFileSize)
                {
                    filesSkipped++;
                    continue;
                }

                byte[] bytes = ReadSharedFile(file);
                string text = Encoding.UTF8.GetString(bytes);
                var scanner = new ObjectPairScanner();
                scanner.Scan(text);
                pairs.AddRange(scanner.Pairs);
                spaceTokens += scanner.SpaceTokenCount;
                connectTokens += scanner.ConnectTokenCount;
                filesScanned++;
            }
            catch (Exception ex)
            {
                filesSkipped++;
                Console.WriteLine(
                    $"[Ownership] Skipped LevelDB file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        Console.WriteLine(
            $"[Ownership] LevelDB files scanned: {filesScanned}; skipped: {filesSkipped}");
        Console.WriteLine(
            $"[Ownership] LevelDB key hits: spaceId={spaceTokens}; ubisoftConnectGameId={connectTokens}");
        Console.WriteLine(
            $"[Ownership] Raw spaceId/connect-ID pairs: {pairs.Count}");

        var idsBySpace = new Dictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string spaceId, uint connectId) in pairs)
        {
            string normalizedSpaceId = NormalizeGuid(spaceId);
            if (normalizedSpaceId.Length == 0)
            {
                continue;
            }

            if (!idsBySpace.TryGetValue(normalizedSpaceId, out HashSet<uint>? ids))
            {
                ids = new HashSet<uint>();
                idsBySpace[normalizedSpaceId] = ids;
            }

            ids.Add(connectId);
        }

        var oneIdPerSpace = idsBySpace
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Single(),
                StringComparer.OrdinalIgnoreCase);

        var spacesById = new Dictionary<uint, HashSet<string>>();
        foreach (KeyValuePair<string, uint> pair in oneIdPerSpace)
        {
            if (!spacesById.TryGetValue(pair.Value, out HashSet<string>? spaces))
            {
                spaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                spacesById[pair.Value] = spaces;
            }

            spaces.Add(pair.Key);
        }

        var clean = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, uint> pair in oneIdPerSpace)
        {
            if (spacesById[pair.Value].Count == 1)
            {
                clean[pair.Key] = pair.Value;
            }
        }

        int ambiguousSpaces = idsBySpace.Count(pair => pair.Value.Count != 1);
        int duplicateIds = spacesById.Count(pair => pair.Value.Count > 1);

        Console.WriteLine(
            $"[Ownership] Unique LevelDB mappings: {clean.Count}; ambiguous spaces: {ambiguousSpaces}; duplicated Connect IDs: {duplicateIds}");

        return clean;
    }

    private static byte[] ReadSharedFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length <= 0 || stream.Length > MaxLevelDbFileSize)
        {
            return Array.Empty<byte>();
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

    private static string NormalizeGuid(string value)
    {
        return Guid.TryParse(value, out Guid parsed)
            ? parsed.ToString("D").ToLowerInvariant()
            : string.Empty;
    }

    private sealed class ObjectPairScanner
    {
        private readonly List<Frame> _stack = new() { new Frame() };

        public List<(string SpaceId, uint ConnectId)> Pairs { get; } = new();
        public int SpaceTokenCount { get; private set; }
        public int ConnectTokenCount { get; private set; }

        public void Scan(string text)
        {
            foreach (Match match in Tokens.Matches(text))
            {
                if (match.Groups["open"].Success)
                {
                    _stack.Add(new Frame());
                }
                else if (match.Groups["close"].Success)
                {
                    PopFrame();
                }
                else if (match.Groups["nul"].Success)
                {
                    Unwind();
                    _stack[0] = new Frame();
                }
                else if (match.Groups["space"].Success)
                {
                    SpaceTokenCount++;
                    FillSpace(match.Groups["space"].Value);
                }
                else if (match.Groups["connect"].Success)
                {
                    ConnectTokenCount++;
                    if (uint.TryParse(match.Groups["connect"].Value, out uint connectId))
                    {
                        FillConnect(connectId);
                    }
                }
            }

            Unwind();
        }

        private void FillSpace(string value)
        {
            Frame frame = _stack[^1];
            frame.SpaceId = value;
            EmitIfComplete(frame);
        }

        private void FillConnect(uint value)
        {
            Frame frame = _stack[^1];
            frame.ConnectId = value;
            EmitIfComplete(frame);
        }

        private void EmitIfComplete(Frame frame)
        {
            if (frame.SpaceId == null || !frame.ConnectId.HasValue)
            {
                return;
            }

            Pairs.Add((frame.SpaceId, frame.ConnectId.Value));
            frame.SpaceId = null;
            frame.ConnectId = null;
        }

        private void PopFrame()
        {
            if (_stack.Count <= 1)
            {
                return;
            }

            Frame child = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            Frame parent = _stack[^1];

            if (child.SpaceId != null && parent.SpaceId == null)
            {
                FillSpace(child.SpaceId);
            }

            if (child.ConnectId.HasValue && !parent.ConnectId.HasValue)
            {
                FillConnect(child.ConnectId.Value);
            }
        }

        private void Unwind()
        {
            while (_stack.Count > 1)
            {
                PopFrame();
            }
        }

        private sealed class Frame
        {
            public string? SpaceId { get; set; }
            public uint? ConnectId { get; set; }
        }
    }
}
