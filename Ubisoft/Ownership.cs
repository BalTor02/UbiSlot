using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UbiSlot.Games;

namespace UbiSlot.Ubisoft;

public sealed class OwnershipManager
{
    private const int ProtobufOffset = 264;

    private const string EntitlementsEndpoint =
        "https://public-ubiservices.ubi.com/v1/profiles/me/global/ubiconnect/entitlement/api/entitlements";

    private static readonly byte[] EntitlementsEndpointBytes =
        Encoding.UTF8.GetBytes(EntitlementsEndpoint);

    private const int MaxJsonSearchDistance = 8192;
    private const long MaxCacheEntrySizeBytes = 64L * 1024 * 1024;

    private readonly GameDatabase _gameDatabase;

    private IReadOnlyDictionary<uint, string> _latestSpaceIdsByProductId =
        new Dictionary<uint, string>();

    private IReadOnlyDictionary<string, uint> _runtimeConnectIdsBySpaceId =
        new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, IReadOnlyList<UbisoftGameConfiguration>> _runtimeConfigurationsBySpaceId =
        new Dictionary<string, IReadOnlyList<UbisoftGameConfiguration>>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<uint, string> LatestSpaceIdsByProductId =>
        _latestSpaceIdsByProductId;

    public IReadOnlyDictionary<string, uint> RuntimeConnectIdsBySpaceId =>
        _runtimeConnectIdsBySpaceId;

    public IReadOnlyDictionary<string, IReadOnlyList<UbisoftGameConfiguration>> RuntimeConfigurationsBySpaceId =>
        _runtimeConfigurationsBySpaceId;

    private readonly HashSet<string> _loggedEntitlementMetadata =
        new(StringComparer.Ordinal);

    public OwnershipManager(GameDatabase gameDatabase)
    {
        _gameDatabase = gameDatabase;
    }

    public List<uint> GetOwnedGameIds()
    {
        _latestSpaceIdsByProductId = new Dictionary<uint, string>();
        _runtimeConnectIdsBySpaceId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        _runtimeConfigurationsBySpaceId = new Dictionary<string, IReadOnlyList<UbisoftGameConfiguration>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (TryReadEntitlementCache(out List<uint> entitlementGameIds))
            {
                return entitlementGameIds;
            }

            Console.WriteLine(
                "[Ownership] No readable entitlement response was found in CacheStorage.");

            string? ownershipFile =
                LocateOwnershipFile();

            if (ownershipFile == null)
            {
                Console.WriteLine(
                    "[Ownership] No readable legacy ownership cache was found.");

                return [];
            }

            Console.WriteLine(
                $"[Ownership] Legacy cache: {ownershipFile}");

            byte[] data =
                File.ReadAllBytes(ownershipFile);

            List<OwnershipRecord> records =
                ParseOwnershipFile(data);

            Console.WriteLine(
                $"[Ownership] Legacy records parsed: {records.Count}");

            var candidateIds =
                new SortedSet<uint>();

            foreach (OwnershipRecord record in records)
            {
                if (record.Field1.HasValue)
                {
                    candidateIds.Add(
                        record.Field1.Value);
                }

                if (record.Field2.HasValue)
                {
                    candidateIds.Add(
                        record.Field2.Value);
                }
            }

            var ownedGameIds =
                new SortedSet<uint>();

            foreach (uint id in candidateIds)
            {
                if (_gameDatabase.GetGameName(
                        id.ToString()) != null)
                {
                    ownedGameIds.Add(id);
                }
            }

            Console.WriteLine(
                $"[Ownership] Legacy candidate IDs: {candidateIds.Count}");

            Console.WriteLine(
                $"[Ownership] Legacy matched game IDs: {ownedGameIds.Count}");

            foreach (uint id in ownedGameIds)
            {
                Console.WriteLine(
                    $"[Ownership] Owned Game ID: {id}");
            }

            return ownedGameIds.ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ownership] ERROR: {ex}");

            return [];
        }
    }

    public void PrintOwnedGames()
    {
        Console.WriteLine();
        Console.WriteLine(
            "       UBISOFT OWNED GAMES");
        Console.WriteLine(
            "======================================");
        Console.WriteLine();

        List<uint> gameIds =
            GetOwnedGameIds();

        Console.WriteLine(
            $"Owned IDs found: {gameIds.Count}");

        Console.WriteLine();

        foreach (uint gameId in gameIds)
        {
            string id =
                gameId.ToString();

            string name =
                _gameDatabase.GetGameName(id)
                ?? "[UNKNOWN GAME]";

            Console.WriteLine(
                $"{gameId,-6} -> {name}");
        }

        Console.WriteLine();
    }

    private bool TryReadEntitlementCache(out List<uint> ownedGameIds)
    {
        ownedGameIds = [];

        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        string cacheStorageDirectory =
            Path.Combine(
                localAppData,
                "Ubisoft Game Launcher",
                "cache",
                "http2",
                "Default",
                "Service Worker",
                "CacheStorage");

        if (!Directory.Exists(cacheStorageDirectory))
        {
            Console.WriteLine(
                $"[Ownership] CacheStorage directory not found: {cacheStorageDirectory}");

            return false;
        }

        Console.WriteLine(
            $"[Ownership] Scanning Ubisoft CacheStorage: {cacheStorageDirectory}");

        var candidates =
            new List<EntitlementCacheCandidate>();

        IEnumerable<string> files;

        try
        {
            files =
                Directory.EnumerateFiles(
                    cacheStorageDirectory,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        ReturnSpecialDirectories = false
                    });
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ownership] Could not enumerate CacheStorage: {ex.Message}");

            return false;
        }

        int filesScanned = 0;
        int endpointMatches = 0;

        foreach (string file in files)
        {
            string fileName =
                Path.GetFileName(file);

            if (fileName.Equals("index", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("the-real-index", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                long fileSize =
                    new FileInfo(file).Length;

                if (fileSize <= EntitlementsEndpointBytes.Length ||
                    fileSize > MaxCacheEntrySizeBytes)
                {
                    continue;
                }

                byte[] data =
                    File.ReadAllBytes(file);

                filesScanned++;

                int searchPosition = 0;

                while (searchPosition < data.Length)
                {
                    int endpointPosition =
                        IndexOfBytes(
                            data,
                            EntitlementsEndpointBytes,
                            searchPosition);

                    if (endpointPosition < 0)
                    {
                        break;
                    }

                    endpointMatches++;

                    int jsonSearchStart =
                        endpointPosition + EntitlementsEndpointBytes.Length;

                    if (TryExtractEntitlementsJson(
                            data,
                            jsonSearchStart,
                            out JsonDocument? document))
                    {
                        using (document)
                        {
                            if (TryCreateEntitlementCandidate(
                                    document!,
                                    file,
                                    File.GetLastWriteTimeUtc(file),
                                    out EntitlementCacheCandidate? candidate))
                            {
                                candidates.Add(candidate!);
                            }
                        }
                    }

                    searchPosition =
                        endpointPosition + EntitlementsEndpointBytes.Length;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[Ownership] Skipped unreadable cache entry '{fileName}': {ex.Message}");
            }
        }

        Console.WriteLine(
            $"[Ownership] CacheStorage files scanned: {filesScanned}");

        Console.WriteLine(
            $"[Ownership] Entitlement endpoint matches: {endpointMatches}");

        if (candidates.Count == 0)
        {
            return false;
        }

        EntitlementCacheCandidate selected =
            candidates
                .OrderByDescending(candidate => candidate.LastWriteTimeUtc)
                .ThenByDescending(candidate => candidate.EntitlementCount)
                .First();

        ownedGameIds =
            selected.MatchedGameIds.ToList();

        _latestSpaceIdsByProductId =
            new Dictionary<uint, string>(selected.SpaceIdsByProductId);

        LogRuntimeConnectGameIds(selected);

        Console.WriteLine(
            $"[Ownership] Entitlement response: {selected.FilePath}");

        Console.WriteLine(
            $"[Ownership] Entitlement response modified: {selected.LastWriteTimeUtc:O}");

        Console.WriteLine(
            $"[Ownership] Entitlement records: {selected.EntitlementCount}");

        Console.WriteLine(
            $"[Ownership] Game records: {selected.GameRecordCount}");

        Console.WriteLine(
            $"[Ownership] Playable owned game records: {selected.PlayableOwnedGameCount}");

        Console.WriteLine(
            $"[Ownership] Matched game IDs: {ownedGameIds.Count}");

        if (selected.UnmatchedGameIds.Count > 0)
        {
            Console.WriteLine(
                "[Ownership] Game IDs not present in UplayGames.txt (skipped): " +
                string.Join(", ", selected.UnmatchedGameIds));
        }

        foreach (uint id in ownedGameIds)
        {
            string name =
                _gameDatabase.GetGameName(id.ToString()) ?? "[UNKNOWN GAME]";

            Console.WriteLine(
                $"[Ownership] Owned Game ID: {id} -> {name}");
        }

        return true;
    }

    private bool TryCreateEntitlementCandidate(
        JsonDocument document,
        string filePath,
        DateTime lastWriteTimeUtc,
        out EntitlementCacheCandidate? candidate)
    {
        candidate = null;

        JsonElement root =
            document.RootElement;

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("entitlements", out JsonElement entitlements) ||
            entitlements.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var matchedGameIds =
            new SortedSet<uint>();

        var unmatchedGameIds =
            new SortedSet<uint>();

        var spaceIdsByProductId =
            new Dictionary<uint, string>();

        int gameRecordCount = 0;
        int playableOwnedGameCount = 0;

        foreach (JsonElement entitlement in entitlements.EnumerateArray())
        {
            if (entitlement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetStringProperty(
                    entitlement,
                    "type",
                    out string type) ||
                !type.Equals("game", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            gameRecordCount++;

            LogEntitlementMetadata(entitlement);

            if (!TryGetStringProperty(
                    entitlement,
                    "accessLevel",
                    out string accessLevel) ||
                !accessLevel.Equals("owned", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!entitlement.TryGetProperty(
                    "playable",
                    out JsonElement playableElement) ||
                playableElement.ValueKind != JsonValueKind.True)
            {
                continue;
            }

            if (TryGetStringProperty(
                    entitlement,
                    "availability",
                    out string availability) &&
                !availability.Equals("playable", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryGetUInt32Property(
                    entitlement,
                    "productId",
                    out uint productId))
            {
                continue;
            }

            playableOwnedGameCount++;

            if (TryGetStringProperty(entitlement, "spaceId", out string spaceId) &&
                Guid.TryParse(spaceId, out Guid parsedSpaceId))
            {
                spaceIdsByProductId[productId] =
                    parsedSpaceId.ToString("D").ToLowerInvariant();
            }

            if (_gameDatabase.GetGameName(productId.ToString()) != null)
            {
                matchedGameIds.Add(productId);
            }
            else
            {
                unmatchedGameIds.Add(productId);
            }
        }

        candidate =
            new EntitlementCacheCandidate(
                filePath,
                lastWriteTimeUtc,
                entitlements.GetArrayLength(),
                gameRecordCount,
                playableOwnedGameCount,
                matchedGameIds,
                unmatchedGameIds,
                spaceIdsByProductId);

        return true;
    }

    private void LogRuntimeConnectGameIds(EntitlementCacheCandidate selected)
    {
        var levelDbCache = new UbisoftConnectGameIdCache();
        IReadOnlyDictionary<string, uint> connectIdsBySpaceId =
            levelDbCache.ReadMappings();

        _runtimeConnectIdsBySpaceId = connectIdsBySpaceId;

        int resolved = 0;

        foreach (uint productId in selected.MatchedGameIds)
        {
            string gameName =
                _gameDatabase.GetGameName(productId.ToString()) ??
                "[UNKNOWN GAME]";

            if (!selected.SpaceIdsByProductId.TryGetValue(
                    productId,
                    out string? spaceId))
            {
                Console.WriteLine(
                    $"[Ownership Runtime ID] productId={productId} | game={gameName} | spaceId missing in entitlement response | Connect ID NOT FOUND");
                continue;
            }

            if (!connectIdsBySpaceId.TryGetValue(spaceId, out uint connectGameId))
            {
                Console.WriteLine(
                    $"[Ownership Runtime ID] productId={productId} | game={gameName} | spaceId={spaceId} | Connect ID NOT FOUND or ambiguous");
                continue;
            }

            resolved++;
            Console.WriteLine(
                $"[Ownership Runtime ID] productId={productId} | game={gameName} | spaceId={spaceId} | ubisoftConnectGameId={connectGameId}");
        }

        Console.WriteLine(
            $"[Ownership] Runtime Connect IDs matched: {resolved}/{selected.MatchedGameIds.Count} (diagnostic only; launch IDs unchanged)");

        LogRuntimeConfigurationIds(selected);
    }

    private void LogRuntimeConfigurationIds(EntitlementCacheCandidate selected)
    {
        IReadOnlyDictionary<string, IReadOnlyList<UbisoftGameConfiguration>> configurations =
            new UbisoftGameConfigurationCache().ReadBySpaceId();

        _runtimeConfigurationsBySpaceId = configurations;

        int matched = 0;
        int ambiguous = 0;

        foreach (uint productId in selected.MatchedGameIds)
        {
            string gameName =
                _gameDatabase.GetGameName(productId.ToString()) ?? "[UNKNOWN GAME]";

            if (!selected.SpaceIdsByProductId.TryGetValue(productId, out string? spaceId))
            {
                Console.WriteLine(
                    $"[Ownership Runtime Config] productId={productId} | game={gameName} | entitlement spaceId missing");
                continue;
            }

            if (!configurations.TryGetValue(spaceId, out IReadOnlyList<UbisoftGameConfiguration>? records) ||
                records.Count == 0)
            {
                Console.WriteLine(
                    $"[Ownership Runtime Config] productId={productId} | game={gameName} | spaceId={spaceId} | configuration NOT FOUND");
                continue;
            }

            if (records.Count != 1)
            {
                ambiguous++;
                Console.WriteLine(
                    $"[Ownership Runtime Config] productId={productId} | game={gameName} | spaceId={spaceId} | AMBIGUOUS records={records.Count}");
                foreach (UbisoftGameConfiguration record in records)
                {
                    Console.WriteLine(
                        $"[Ownership Runtime Config Candidate] installId={record.InstallId} | launchId={record.LaunchId} | name={record.Name} | platform={record.ThirdPartyPlatform} | exe={record.Executable}");
                }
                continue;
            }

            UbisoftGameConfiguration config = records[0];
            matched++;
            Console.WriteLine(
                $"[Ownership Runtime Config] productId={productId} | game={gameName} | spaceId={spaceId} | installId={config.InstallId} | launchId={config.LaunchId} | configName={config.Name} | platform={config.ThirdPartyPlatform} | exe={config.Executable}");
        }

        Console.WriteLine(
            $"[Ownership] Runtime configuration matches: {matched}/{selected.MatchedGameIds.Count}; ambiguous: {ambiguous} (diagnostic only; launch IDs unchanged)");
    }

    private void LogEntitlementMetadata(JsonElement entitlement)
    {
        string productId =
            TryGetUInt32Property(entitlement, "productId", out uint parsedProductId)
                ? parsedProductId.ToString()
                : "unknown";

        var fields = new List<string>();
        AppendDiagnosticFields(entitlement, fields, depth: 0);

        string report =
            string.Join("; ", fields.OrderBy(value => value, StringComparer.Ordinal));

        string signature = productId + "|" + report;

        if (!_loggedEntitlementMetadata.Add(signature))
        {
            return;
        }

        Console.WriteLine(
            $"[Ownership Schema] Game productId={productId} | {report}");
    }

    private static void AppendDiagnosticFields(
        JsonElement element,
        List<string> fields,
        int depth)
    {
        if (depth > 3 || element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            string key = property.Name;
            string normalizedKey = key.ToLowerInvariant();

            if (normalizedKey.Contains("token") ||
                normalizedKey.Contains("ticket") ||
                normalizedKey.Contains("auth") ||
                normalizedKey.Contains("cookie") ||
                normalizedKey.Contains("session") ||
                normalizedKey.Contains("credential") ||
                normalizedKey.Contains("profile") ||
                normalizedKey.Contains("account") ||
                normalizedKey.Contains("email") ||
                normalizedKey.Contains("user"))
            {
                continue;
            }

            bool relevant =
                normalizedKey == "id" ||
                normalizedKey == "identifier" ||
                normalizedKey.Contains("product") ||
                normalizedKey.Contains("game") ||
                normalizedKey.Contains("uplay") ||
                normalizedKey.Contains("application") ||
                normalizedKey == "appid" ||
                normalizedKey.Contains("sku") ||
                normalizedKey.Contains("platform") ||
                normalizedKey.Contains("store") ||
                normalizedKey.Contains("space") ||
                normalizedKey.Contains("edition") ||
                normalizedKey.Contains("variant") ||
                normalizedKey.Contains("launch") ||
                normalizedKey.Contains("config") ||
                normalizedKey.Contains("title") ||
                normalizedKey == "name" ||
                normalizedKey == "type" ||
                normalizedKey.Contains("accesslevel") ||
                normalizedKey == "playable" ||
                normalizedKey == "availability";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                int initialCount = fields.Count;
                AppendDiagnosticFields(property.Value, fields, depth + 1);

                if (relevant && fields.Count == initialCount)
                {
                    fields.Add(key + "=<object>");
                }

                continue;
            }

            if (!relevant)
            {
                continue;
            }

            string value = property.Value.ValueKind switch
            {
                JsonValueKind.String => FormatDiagnosticString(
                    property.Value.GetString() ?? string.Empty),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "null",
                JsonValueKind.Array => $"<array:{property.Value.GetArrayLength()}>",
                _ => "<complex>"
            };

            fields.Add(key + "=" + value);
        }
    }

    private static string FormatDiagnosticString(string value)
    {
        string sanitized =
            value.Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (sanitized.Length > 120)
        {
            return $"<string:{sanitized.Length} chars>";
        }

        return sanitized;
    }

    private static bool TryExtractEntitlementsJson(
        byte[] data,
        int searchStart,
        out JsonDocument? document)
    {
        document = null;

        int searchEnd =
            Math.Min(
                data.Length,
                searchStart + MaxJsonSearchDistance);

        for (int position = searchStart; position < searchEnd; position++)
        {
            if (data[position] != (byte)'{')
            {
                continue;
            }

            ReadOnlySpan<byte> remaining =
                data.AsSpan(position);

            if (!TryGetJsonObjectLength(
                    remaining,
                    out int jsonLength))
            {
                continue;
            }

            try
            {
                JsonDocument candidate =
                    JsonDocument.Parse(
                        data.AsMemory(position, jsonLength));

                if (candidate.RootElement.ValueKind == JsonValueKind.Object &&
                    candidate.RootElement.TryGetProperty(
                        "entitlements",
                        out JsonElement entitlements) &&
                    entitlements.ValueKind == JsonValueKind.Array)
                {
                    document = candidate;
                    return true;
                }

                candidate.Dispose();
            }
            catch (JsonException)
            {
            }
        }

        return false;
    }

    private static bool TryGetJsonObjectLength(
        ReadOnlySpan<byte> data,
        out int jsonLength)
    {
        jsonLength = 0;

        try
        {
            var reader =
                new Utf8JsonReader(
                    data,
                    new JsonReaderOptions
                    {
                        AllowTrailingCommas = false,
                        CommentHandling = JsonCommentHandling.Disallow
                    });

            if (!reader.Read() ||
                reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject &&
                    reader.CurrentDepth == 0)
                {
                    jsonLength =
                        checked((int)reader.BytesConsumed);

                    return jsonLength > 0;
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (OverflowException)
        {
        }

        return false;
    }

    private static bool TryGetStringProperty(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(
                propertyName,
                out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value =
            property.GetString() ?? string.Empty;

        return value.Length > 0;
    }

    private static bool TryGetUInt32Property(
        JsonElement element,
        string propertyName,
        out uint value)
    {
        value = 0;

        if (!element.TryGetProperty(
                propertyName,
                out JsonElement property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number)
        {
            return property.TryGetUInt32(out value);
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return uint.TryParse(
                property.GetString(),
                out value);
        }

        return false;
    }

    private static int IndexOfBytes(
        byte[] data,
        byte[] pattern,
        int startIndex)
    {
        if (startIndex < 0 || startIndex >= data.Length)
        {
            return -1;
        }

        int relativeIndex =
            data.AsSpan(startIndex).IndexOf(pattern);

        return relativeIndex < 0
            ? -1
            : startIndex + relativeIndex;
    }

    private sealed class EntitlementCacheCandidate
    {
        public EntitlementCacheCandidate(
            string filePath,
            DateTime lastWriteTimeUtc,
            int entitlementCount,
            int gameRecordCount,
            int playableOwnedGameCount,
            SortedSet<uint> matchedGameIds,
            SortedSet<uint> unmatchedGameIds,
            Dictionary<uint, string> spaceIdsByProductId)
        {
            FilePath = filePath;
            LastWriteTimeUtc = lastWriteTimeUtc;
            EntitlementCount = entitlementCount;
            GameRecordCount = gameRecordCount;
            PlayableOwnedGameCount = playableOwnedGameCount;
            MatchedGameIds = matchedGameIds;
            UnmatchedGameIds = unmatchedGameIds;
            SpaceIdsByProductId = spaceIdsByProductId;
        }

        public string FilePath { get; }

        public DateTime LastWriteTimeUtc { get; }

        public int EntitlementCount { get; }

        public int GameRecordCount { get; }

        public int PlayableOwnedGameCount { get; }

        public SortedSet<uint> MatchedGameIds { get; }

        public SortedSet<uint> UnmatchedGameIds { get; }

        public Dictionary<uint, string> SpaceIdsByProductId { get; }
    }

    private string? LocateOwnershipFile()
    {
        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        string ownershipDirectory =
            Path.Combine(
                localAppData,
                "Ubisoft Game Launcher",
                "cache",
                "ownership");

        if (!Directory.Exists(
                ownershipDirectory))
        {
            return null;
        }

        var candidates =
            new List<(string Path, int Score, long Size)>();

        IEnumerable<string> files;

        try
        {
            files =
                Directory.EnumerateFiles(
                    ownershipDirectory,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        ReturnSpecialDirectories = false
                    });
        }
        catch
        {
            return null;
        }

        foreach (string file in files)
        {
            if (Path.GetExtension(file).Equals(
                    ".vdat",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            long size =
                GetFileSize(file);

            if (size <= ProtobufOffset)
                continue;

            try
            {
                byte[] data =
                    File.ReadAllBytes(file);

                List<OwnershipRecord> records =
                    ParseOwnershipFile(data);

                if (records.Count == 0)
                    continue;

                int score = 0;

                foreach (OwnershipRecord record in records)
                {
                    if (record.Field1.HasValue &&
                        _gameDatabase.GetGameName(
                            record.Field1.Value.ToString()) != null)
                    {
                        score++;
                    }

                    if (record.Field2.HasValue &&
                        _gameDatabase.GetGameName(
                            record.Field2.Value.ToString()) != null)
                    {
                        score++;
                    }
                }

                candidates.Add(
                    (file, score, size));
            }
            catch
            {
            }
        }

        return candidates
            .OrderByDescending(
                candidate => candidate.Score)
            .ThenByDescending(
                candidate => candidate.Size)
            .Select(
                candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static long GetFileSize(
        string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static List<OwnershipRecord>
        ParseOwnershipFile(byte[] data)
    {
        if (data.Length <= ProtobufOffset)
            return [];

        return ParseTopLevelRecords(
            data[ProtobufOffset..]);
    }

    private static List<OwnershipRecord>
        ParseTopLevelRecords(byte[] data)
    {
        var records =
            new List<OwnershipRecord>();

        int position = 0;

        while (position < data.Length)
        {
            int fieldStart =
                position;

            try
            {
                ulong tag =
                    ReadVarint(
                        data,
                        ref position);

                int fieldNumber =
                    checked(
                        (int)(tag >> 3));

                int wireType =
                    (int)(tag & 7);

                if (fieldNumber == 1 &&
                    wireType == 2)
                {
                    ulong length =
                        ReadVarint(
                            data,
                            ref position);

                    if (length > int.MaxValue ||
                        position + (long)length >
                        data.Length)
                    {
                        break;
                    }

                    int recordLength =
                        (int)length;

                    byte[] recordData =
                        new byte[recordLength];

                    Buffer.BlockCopy(
                        data,
                        position,
                        recordData,
                        0,
                        recordLength);

                    position +=
                        recordLength;

                    OwnershipRecord? record =
                        ParseRecord(recordData);

                    if (record != null &&
                        (record.Field1.HasValue ||
                         record.Field2.HasValue))
                    {
                        records.Add(record);
                    }
                }
                else
                {
                    SkipField(
                        data,
                        ref position,
                        wireType);
                }
            }
            catch
            {
                position =
                    fieldStart + 1;
            }
        }

        return records;
    }

    private static OwnershipRecord?
        ParseRecord(byte[] data)
    {
        var record =
            new OwnershipRecord();

        int position = 0;

        while (position < data.Length)
        {
            ulong tag =
                ReadVarint(
                    data,
                    ref position);

            int fieldNumber =
                checked(
                    (int)(tag >> 3));

            int wireType =
                (int)(tag & 7);

            switch (fieldNumber)
            {
                case 1 when wireType == 0:

                    record.Field1 =
                        checked(
                            (uint)ReadVarint(
                                data,
                                ref position));

                    break;

                case 2 when wireType == 0:

                    record.Field2 =
                        checked(
                            (uint)ReadVarint(
                                data,
                                ref position));

                    break;

                default:

                    SkipField(
                        data,
                        ref position,
                        wireType);

                    break;
            }
        }

        return record.Field1.HasValue ||
               record.Field2.HasValue
            ? record
            : null;
    }

    private static ulong ReadVarint(
        byte[] data,
        ref int position)
    {
        ulong result = 0;
        int shift = 0;

        while (position < data.Length)
        {
            byte b =
                data[position++];

            result |=
                (ulong)(b & 0x7F) <<
                shift;

            if ((b & 0x80) == 0)
                return result;

            shift += 7;

            if (shift >= 64)
            {
                throw new InvalidDataException(
                    "Invalid protobuf varint.");
            }
        }

        throw new EndOfStreamException(
            "Unexpected end of protobuf data.");
    }

    private static void SkipField(
        byte[] data,
        ref int position,
        int wireType)
    {
        switch (wireType)
        {
            case 0:

                ReadVarint(
                    data,
                    ref position);

                break;

            case 1:

                EnsureAvailable(
                    data,
                    position,
                    8);

                position += 8;

                break;

            case 2:

                ulong length =
                    ReadVarint(
                        data,
                        ref position);

                if (length > int.MaxValue)
                {
                    throw new InvalidDataException(
                        "Invalid protobuf length.");
                }

                EnsureAvailable(
                    data,
                    position,
                    (int)length);

                position +=
                    (int)length;

                break;

            case 5:

                EnsureAvailable(
                    data,
                    position,
                    4);

                position += 4;

                break;

            default:

                throw new InvalidDataException(
                    $"Unsupported protobuf wire type: {wireType}");
        }
    }

    private static void EnsureAvailable(
        byte[] data,
        int position,
        int count)
    {
        if (count < 0 ||
            position > data.Length - count)
        {
            throw new EndOfStreamException(
                "Protobuf field extends beyond file.");
        }
    }

    private sealed class OwnershipRecord
    {
        public uint? Field1 { get; set; }

        public uint? Field2 { get; set; }
    }
}
