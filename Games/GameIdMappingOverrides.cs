using System;
using System.Collections.Generic;
using System.Linq;

namespace UbiSlot.Games;

public static class GameIdMappingOverrides
{
    private static readonly IReadOnlyDictionary<uint, uint[]> SessionToAchievementIds =
        new Dictionary<uint, uint[]>
        {
            [40] = new uint[] { 59 },
            [46] = new uint[] { 101 },
            [5183] = new uint[] { 5184 }
        };

    public static bool TryResolveSessionGameId(
        uint entitlementProductId,
        GameDatabase database,
        GameVariantResolver variantResolver,
        out uint sessionGameId)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(variantResolver);

        sessionGameId = 0;

        GameIdMappingOverride[] matches = GetValidMappings(database, variantResolver)
            .Where(mapping =>
                mapping.SessionGameId == entitlementProductId ||
                mapping.AchievementGameIds.Contains(entitlementProductId))
            .ToArray();

        if (matches.Length != 1)
        {
            if (matches.Length > 1)
            {
                Console.WriteLine(
                    $"[Game ID Mapping] Multiple mappings were found for product ID {entitlementProductId}. " +
                    "Checking other available game information instead.");
            }

            return false;
        }

        sessionGameId = matches[0].SessionGameId;

        Console.WriteLine(
            $"[Game ID Mapping] Using a predefined mapping for product ID {entitlementProductId}. " +
            $"Session ID: {sessionGameId} | " +
            $"Achievement IDs: {string.Join(", ", matches[0].AchievementGameIds)}.");

        return true;
    }

    public static IReadOnlyList<uint> GetPreferredAchievementIds(
        uint productOrSessionId,
        GameDatabase database,
        GameVariantResolver variantResolver)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(variantResolver);

        GameIdMappingOverride[] matches = GetValidMappings(database, variantResolver)
            .Where(mapping =>
                mapping.SessionGameId == productOrSessionId ||
                mapping.AchievementGameIds.Contains(productOrSessionId))
            .ToArray();

        return matches.Length == 1
            ? matches[0].AchievementGameIds
            : Array.Empty<uint>();
    }

    public static IReadOnlyList<uint> GetSpoolSearchOrder(
        uint entitlementProductId,
        uint? sessionGameId,
        GameDatabase database,
        GameVariantResolver variantResolver)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(variantResolver);

        var orderedIds = new List<uint>();

        void Add(uint id)
        {
            if (id != 0 &&
                database.GetGameName(id.ToString()) != null &&
                !orderedIds.Contains(id))
            {
                orderedIds.Add(id);
            }
        }

        void AddMapping(uint aliasId)
        {
            foreach (GameIdMappingOverride mapping in GetValidMappings(database, variantResolver))
            {
                if (mapping.SessionGameId == aliasId ||
                    mapping.AchievementGameIds.Contains(aliasId))
                {
                    foreach (uint achievementId in mapping.AchievementGameIds)
                    {
                        Add(achievementId);
                    }

                    Add(mapping.SessionGameId);
                }
            }
        }

        AddMapping(entitlementProductId);

        if (sessionGameId.HasValue)
        {
            AddMapping(sessionGameId.Value);
        }

        Add(entitlementProductId);

        if (sessionGameId.HasValue)
        {
            Add(sessionGameId.Value);
        }

        if (sessionGameId.HasValue)
        {
            foreach (uint id in variantResolver.GetEquivalentVariantIds(sessionGameId.Value))
            {
                Add(id);
            }
        }

        foreach (uint id in variantResolver.GetEquivalentVariantIds(entitlementProductId))
        {
            Add(id);
        }

        if (sessionGameId.HasValue)
        {
            foreach (uint id in variantResolver.GetFamilyFallbackIds(sessionGameId.Value))
            {
                Add(id);
            }
        }

        foreach (uint id in variantResolver.GetFamilyFallbackIds(entitlementProductId))
        {
            Add(id);
        }

        return orderedIds;
    }

    private static IEnumerable<GameIdMappingOverride> GetValidMappings(
        GameDatabase database,
        GameVariantResolver variantResolver)
    {
        foreach (KeyValuePair<uint, uint[]> entry in SessionToAchievementIds)
        {
            uint sessionGameId = entry.Key;
            uint[] achievementIds = entry.Value;

            if (database.GetGameName(sessionGameId.ToString()) == null ||
                achievementIds.Length == 0)
            {
                Console.WriteLine(
                    $"[Game ID Mapping] Skipping the mapping for session ID {sessionGameId}. " +
                    "The game ID is missing from the database or no achievement IDs have been configured.");

                continue;
            }

            bool valid = achievementIds.All(achievementId =>
                database.GetGameName(achievementId.ToString()) != null &&
                variantResolver.IsSameVariant(sessionGameId, achievementId));

            if (!valid)
            {
                Console.WriteLine(
                    $"[Game ID Mapping] Skipping the mapping for session ID {sessionGameId}. " +
                    "One or more achievement IDs are missing from the database or belong to a different game version.");

                continue;
            }

            yield return new GameIdMappingOverride(
                sessionGameId,
                achievementIds);
        }
    }
}

public sealed record GameIdMappingOverride(
    uint SessionGameId,
    IReadOnlyList<uint> AchievementGameIds);