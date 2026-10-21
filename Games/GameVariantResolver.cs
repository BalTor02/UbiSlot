using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UbiSlot.Games;

public sealed class GameVariantResolver
{
    private static readonly Regex StorefrontMarker =
        new(
            @"\s*\((?:uplay\s*\+\s*steam|uplay\s+connect|uplay|steam)(?:\s+version)?\)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<uint, string> _keyById = new();

    private readonly Dictionary<string, List<uint>> _idsByKey =
        new(StringComparer.Ordinal);

    public GameVariantResolver(GameDatabase database)
    {
        foreach (KeyValuePair<string, string> game in database.GetAllGames())
        {
            if (!uint.TryParse(game.Key, out uint id))
            {
                continue;
            }

            string key = ComputeFamilyKey(game.Value);

            _keyById[id] = key;

            if (!_idsByKey.TryGetValue(key, out List<uint>? ids))
            {
                ids = new List<uint>();
                _idsByKey[key] = ids;
            }

            ids.Add(id);
        }
    }

    public string GetFamilyKey(uint id)
    {
        return _keyById.TryGetValue(id, out string? key)
            ? key
            : $"id:{id}";
    }

    public IReadOnlyList<uint> GetVariantIds(uint id)
    {
        if (_keyById.TryGetValue(id, out string? key) &&
            _idsByKey.TryGetValue(key, out List<uint>? ids))
        {
            return ids;
        }

        return new[] { id };
    }

    public bool IsSameGame(uint a, uint b)
    {
        return a == b || GetVariantIds(a).Contains(b);
    }

    public static string ComputeFamilyKey(string name)
    {
        string cleaned =
            StorefrontMarker.Replace(
                name,
                string.Empty);

        cleaned =
            cleaned
                .Replace("®", string.Empty)
                .Replace("™", string.Empty)
                .Replace("©", string.Empty)
                .Replace("’", string.Empty)
                .Replace("'", string.Empty)
                .Replace('_', ' ')
                .Replace('-', ' ')
                .Replace(':', ' ');

        return string.Join(
            ' ',
            cleaned
                .ToLowerInvariant()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries));
    }
}