using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UbiSlot.Games;

public sealed class GameVariantResolver
{
    private static readonly Regex EditionSuffix = new(
        @"\s*(?:[-–—:]\s*)?(?<edition>" +
        @"game\s+of\s+the\s+year\s+edition|goty\s+edition|" +
        @"digital\s+deluxe\s+edition|collector(?:'|’)?s\s+edition|" +
        @"complete\s+edition|definitive\s+edition|anniversary\s+edition|" +
        @"ultimate\s+edition|deluxe\s+edition|gold\s+edition|" +
        @"platinum\s+edition|special\s+edition|premium\s+edition|" +
        @"standard\s+edition|enhanced\s+edition|extended\s+edition|legacy\s+edition|" +
        @"director(?:'|’)?s\s+cut|remastered|remaster|redux|remake|hd)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlatformTokens = new(
        @"\b(?:ubisoft\s+connect|uplay\s+connect|ubisoft|uplay|steam|epic(?:\s+games)?|gog)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex RegionTokens = new(
        @"\b(?:russian|rus|ru|czech|cze|cz|japanese|japan|jpn|jp|" +
        @"australia|aus|asia|cis|korean|korea|kor|kr|chinese|china|chn|cn|" +
        @"global|worldwide|ww)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReleaseTokens = new(
        @"\b(?:open\s*\+\s*closed\s+beta|closed\s+beta|open\s+beta|" +
        @"technical\s+test|public\s+test|beta|demo|trial)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DescriptorWords = new(
        @"\b(?:version|edition|store|for|on|the|open|closed|public|technical|test)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<uint, GameVariantInfo> _infoById = new();

    private readonly Dictionary<string, List<uint>> _idsByFamily =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<uint>> _idsByVariant =
        new(StringComparer.Ordinal);

    public GameVariantResolver(GameDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        foreach (KeyValuePair<string, string> game in database.GetAllGames())
        {
            if (!uint.TryParse(game.Key, out uint id) ||
                string.IsNullOrWhiteSpace(game.Value))
            {
                continue;
            }

            ParsedGameName parsed = ParseGameName(game.Value);
            string familyKey = NormalizeTitle(parsed.BaseTitle);

            if (string.IsNullOrWhiteSpace(familyKey))
            {
                familyKey = $"id:{id}";
            }

            string variantKey = BuildVariantKey(
                familyKey,
                parsed.Editions,
                parsed.Regions,
                parsed.ReleaseTags);

            var info = new GameVariantInfo(
                id,
                game.Value,
                familyKey,
                variantKey,
                parsed.Platforms,
                parsed.Regions,
                string.Join(", ", parsed.Editions),
                parsed.ReleaseTags);

            _infoById[id] = info;
            AddToIndex(_idsByFamily, familyKey, id);
            AddToIndex(_idsByVariant, variantKey, id);
        }

        SortIndex(_idsByFamily);
        SortIndex(_idsByVariant);
    }

    public string GetFamilyKey(uint id)
    {
        return _infoById.TryGetValue(id, out GameVariantInfo? info)
            ? info.FamilyKey
            : $"id:{id}";
    }

    public string GetVariantKey(uint id)
    {
        return _infoById.TryGetValue(id, out GameVariantInfo? info)
            ? info.VariantKey
            : $"id:{id}";
    }

    public GameVariantInfo GetVariantInfo(uint id)
    {
        if (_infoById.TryGetValue(id, out GameVariantInfo? info))
        {
            return info;
        }

        return new GameVariantInfo(
            id,
            $"Game {id}",
            $"id:{id}",
            $"id:{id}",
            Array.Empty<string>(),
            Array.Empty<string>(),
            string.Empty,
            Array.Empty<string>());
    }

    public IReadOnlyList<uint> GetFamilyMemberIds(uint id)
    {
        if (_idsByFamily.TryGetValue(GetFamilyKey(id), out List<uint>? ids))
        {
            return ids.ToArray();
        }

        return new[] { id };
    }

    public IReadOnlyList<uint> GetVariantIds(uint id)
    {
        return GetFamilyMemberIds(id);
    }

    public IReadOnlyList<uint> GetEquivalentVariantIds(uint id)
    {
        if (_idsByVariant.TryGetValue(GetVariantKey(id), out List<uint>? ids))
        {
            return ids.ToArray();
        }

        return new[] { id };
    }

    public bool IsSameFamily(uint a, uint b)
    {
        return string.Equals(
            GetFamilyKey(a),
            GetFamilyKey(b),
            StringComparison.Ordinal);
    }

    public bool IsSameVariant(uint a, uint b)
    {
        return string.Equals(
            GetVariantKey(a),
            GetVariantKey(b),
            StringComparison.Ordinal);
    }

    public IReadOnlyList<uint> GetFamilyFallbackIds(uint id)
    {
        GameVariantInfo source = GetVariantInfo(id);
        HashSet<uint> equivalentIds = GetEquivalentVariantIds(id).ToHashSet();

        return GetFamilyMemberIds(id)
            .Where(candidateId => candidateId != id && !equivalentIds.Contains(candidateId))
            .Select(candidateId => GetVariantInfo(candidateId))
            .OrderBy(candidate => GetVariantDifferenceScore(source, candidate))
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => candidate.Id)
            .ToArray();
    }

    private static int GetVariantDifferenceScore(
        GameVariantInfo source,
        GameVariantInfo candidate)
    {
        int score = 0;

        score += SetDifferenceCount(source.Regions, candidate.Regions) * 8;

        string sourceEdition = NormalizeDescriptorValue(source.Edition);
        string candidateEdition = NormalizeDescriptorValue(candidate.Edition);
        if (!string.Equals(sourceEdition, candidateEdition, StringComparison.Ordinal))
        {
            score += 4;
        }

        score += SetDifferenceCount(source.ReleaseTags, candidate.ReleaseTags) * 6;

        return score;
    }

    private static int SetDifferenceCount(
        IReadOnlyCollection<string> left,
        IReadOnlyCollection<string> right)
    {
        var leftSet = new HashSet<string>(left, StringComparer.Ordinal);
        var rightSet = new HashSet<string>(right, StringComparer.Ordinal);

        return leftSet.Except(rightSet).Count() +
               rightSet.Except(leftSet).Count();
    }

    public bool IsSameGame(uint a, uint b)
    {
        return IsSameVariant(a, b);
    }

    public IReadOnlyList<uint> GetAvailableAchievementIdsForOwnershipId(
        uint ownershipId,
        IEnumerable<uint> availableSpoolIds)
    {
        ArgumentNullException.ThrowIfNull(availableSpoolIds);

        var available = new HashSet<uint>(availableSpoolIds);

        return GetEquivalentVariantIds(ownershipId)
            .Where(available.Contains)
            .OrderBy(id => id)
            .ToArray();
    }

    public bool IsAchievementIdForOwnershipId(uint ownershipId, uint achievementId)
    {
        return IsSameVariant(ownershipId, achievementId);
    }

    public static string ComputeFamilyKey(string name)
    {
        ParsedGameName parsed = ParseGameName(name);
        return NormalizeTitle(parsed.BaseTitle);
    }

    public static string ComputeVariantKey(string name)
    {
        ParsedGameName parsed = ParseGameName(name);
        string familyKey = NormalizeTitle(parsed.BaseTitle);
        return BuildVariantKey(
            familyKey,
            parsed.Editions,
            parsed.Regions,
            parsed.ReleaseTags);
    }

    private static ParsedGameName ParseGameName(string name)
    {
        string title = (name ?? string.Empty).Trim();
        var platforms = new SortedSet<string>(StringComparer.Ordinal);
        var regions = new SortedSet<string>(StringComparer.Ordinal);
        var releaseTags = new SortedSet<string>(StringComparer.Ordinal);
        var editions = new SortedSet<string>(StringComparer.Ordinal);

        while (TryExtractTrailingGroup(title, out string prefix, out string descriptor))
        {
            if (TryParseDescriptorGroup(
                    descriptor,
                    platforms,
                    regions,
                    releaseTags,
                    editions))
            {
                title = prefix.TrimEnd();
                continue;
            }

            break;
        }

        Match editionMatch = EditionSuffix.Match(title);
        if (editionMatch.Success)
        {
            editions.Add(NormalizeDescriptorValue(editionMatch.Groups["edition"].Value));
            title = title[..editionMatch.Index].TrimEnd(' ', '-', '–', '—', ':');
        }

        string baseTitle = NormalizeDisplayTitle(title);

        return new ParsedGameName(
            baseTitle,
            platforms.ToArray(),
            regions.ToArray(),
            editions.ToArray(),
            releaseTags.ToArray());
    }

    private static bool TryParseDescriptorGroup(
        string descriptor,
        ISet<string> platforms,
        ISet<string> regions,
        ISet<string> releaseTags,
        ISet<string> editions)
    {
        string remainder = descriptor.ToLowerInvariant();
        var parsedPlatforms = new SortedSet<string>(StringComparer.Ordinal);
        var parsedRegions = new SortedSet<string>(StringComparer.Ordinal);
        var parsedReleaseTags = new SortedSet<string>(StringComparer.Ordinal);
        var parsedEditions = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Match match in PlatformTokens.Matches(remainder).Cast<Match>().ToArray())
        {
            string token = NormalizeDescriptorValue(match.Value);
            if (token.Contains("steam", StringComparison.Ordinal))
            {
                parsedPlatforms.Add("steam");
            }
            else if (token.Contains("epic", StringComparison.Ordinal))
            {
                parsedPlatforms.Add("epic");
            }
            else if (token == "gog")
            {
                parsedPlatforms.Add("gog");
            }
            else
            {
                parsedPlatforms.Add("ubisoft-connect");
            }
        }
        remainder = PlatformTokens.Replace(remainder, " ");

        foreach (Match match in RegionTokens.Matches(remainder).Cast<Match>().ToArray())
        {
            parsedRegions.Add(NormalizeRegion(match.Value));
        }
        remainder = RegionTokens.Replace(remainder, " ");

        foreach (Match match in ReleaseTokens.Matches(remainder).Cast<Match>().ToArray())
        {
            parsedReleaseTags.Add(NormalizeDescriptorValue(match.Value));
        }
        remainder = ReleaseTokens.Replace(remainder, " ");
        remainder = Regex.Replace(remainder, @"[,;:+/&|()\[\]\\.\-]+", " ");

        Match groupedEditionMatch = EditionSuffix.Match(remainder);
        if (groupedEditionMatch.Success)
        {
            parsedEditions.Add(
                NormalizeDescriptorValue(groupedEditionMatch.Groups["edition"].Value));
            remainder = remainder[..groupedEditionMatch.Index];
        }

        remainder = DescriptorWords.Replace(remainder, " ");

        remainder = Regex.Replace(remainder, @"[\s,;:+/&|()\[\]\\.\-]", string.Empty);
        bool recognized = remainder.Length == 0 &&
                          (parsedPlatforms.Count > 0 ||
                           parsedRegions.Count > 0 ||
                           parsedReleaseTags.Count > 0 ||
                           parsedEditions.Count > 0);

        if (recognized)
        {
            foreach (string platform in parsedPlatforms) platforms.Add(platform);
            foreach (string region in parsedRegions) regions.Add(region);
            foreach (string releaseTag in parsedReleaseTags) releaseTags.Add(releaseTag);
            foreach (string edition in parsedEditions) editions.Add(edition);
        }

        return recognized;
    }

    private static bool TryExtractTrailingGroup(
        string value,
        out string prefix,
        out string content)
    {
        prefix = value;
        content = string.Empty;

        string trimmed = value.TrimEnd();
        if (trimmed.Length < 2)
        {
            return false;
        }

        char close = trimmed[^1];
        char open = close switch
        {
            ')' => '(',
            ']' => '[',
            _ => '\0'
        };

        if (open == '\0')
        {
            return false;
        }

        int depth = 0;
        for (int index = trimmed.Length - 1; index >= 0; index--)
        {
            if (trimmed[index] == close)
            {
                depth++;
            }
            else if (trimmed[index] == open)
            {
                depth--;
                if (depth == 0)
                {
                    prefix = trimmed[..index];
                    content = trimmed[(index + 1)..^1];
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeRegion(string value)
    {
        string normalized = NormalizeDescriptorValue(value);
        return normalized switch
        {
            "russian" or "ru" => "rus",
            "czech" or "cze" => "cz",
            "japanese" or "japan" or "jp" => "jpn",
            "australia" => "aus",
            "korean" => "kor",
            "china" or "chinese" => "chn",
            "worldwide" => "global",
            _ => normalized
        };
    }

    private static string NormalizeDescriptorValue(string value)
    {
        return string.Join(
            " ",
            Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeDisplayTitle(string value)
    {
        return value
            .Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("™", string.Empty, StringComparison.Ordinal)
            .Replace("©", string.Empty, StringComparison.Ordinal)
            .Replace("’", "'", StringComparison.Ordinal)
            .Trim();
    }

    private static string NormalizeTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string cleaned = value
            .Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("™", string.Empty, StringComparison.Ordinal)
            .Replace("©", string.Empty, StringComparison.Ordinal)
            .Replace("’", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace('_', ' ');

        var normalized = new StringBuilder(cleaned.Length);
        foreach (char character in cleaned)
        {
            normalized.Append(
                char.IsLetterOrDigit(character) || char.IsWhiteSpace(character)
                    ? character
                    : ' ');
        }

        return string.Join(
            " ",
            normalized.ToString().ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildVariantKey(
        string familyKey,
        IReadOnlyCollection<string> editions,
        IReadOnlyCollection<string> regions,
        IReadOnlyCollection<string> releaseTags)
    {
        if (familyKey.StartsWith("id:", StringComparison.Ordinal))
        {
            return familyKey;
        }

        return string.Join(
            "|",
            familyKey,
            $"edition:{string.Join(",", editions.OrderBy(x => x, StringComparer.Ordinal))}",
            $"region:{string.Join(",", regions.OrderBy(x => x, StringComparer.Ordinal))}",
            $"release:{string.Join(",", releaseTags.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    private static void AddToIndex(
        IDictionary<string, List<uint>> index,
        string key,
        uint id)
    {
        if (!index.TryGetValue(key, out List<uint>? ids))
        {
            ids = new List<uint>();
            index[key] = ids;
        }

        if (!ids.Contains(id))
        {
            ids.Add(id);
        }
    }

    private static void SortIndex(IDictionary<string, List<uint>> index)
    {
        foreach (List<uint> ids in index.Values)
        {
            ids.Sort();
        }
    }

    private sealed record ParsedGameName(
        string BaseTitle,
        IReadOnlyList<string> Platforms,
        IReadOnlyList<string> Regions,
        IReadOnlyList<string> Editions,
        IReadOnlyList<string> ReleaseTags);
}

public sealed record GameVariantInfo(
    uint Id,
    string Name,
    string FamilyKey,
    string VariantKey,
    IReadOnlyList<string> Storefronts,
    IReadOnlyList<string> Regions,
    string Edition,
    IReadOnlyList<string> ReleaseTags);
