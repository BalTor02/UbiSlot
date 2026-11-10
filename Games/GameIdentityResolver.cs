using System;
using System.Collections.Generic;
using System.Linq;
using UbiSlot.Ubisoft;

namespace UbiSlot.Games;

public sealed class GameIdentityResolver
{
    private readonly GameDatabase _database;
    private readonly GameVariantResolver _variantResolver;
    private readonly IReadOnlyDictionary<uint, string> _spaceIdsByProductId;
    private readonly IReadOnlyDictionary<string, uint> _connectIdsBySpaceId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<UbisoftGameConfiguration>> _configurationsBySpaceId;
    private readonly HashSet<uint> _installedRegistryIds;

    public GameIdentityResolver(
        GameDatabase database,
        GameVariantResolver variantResolver,
        IReadOnlyDictionary<uint, string> spaceIdsByProductId,
        IReadOnlyDictionary<string, uint> connectIdsBySpaceId,
        IReadOnlyDictionary<string, IReadOnlyList<UbisoftGameConfiguration>> configurationsBySpaceId)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _variantResolver = variantResolver ?? throw new ArgumentNullException(nameof(variantResolver));
        _spaceIdsByProductId = spaceIdsByProductId ??
            new Dictionary<uint, string>();
        _connectIdsBySpaceId = connectIdsBySpaceId ??
            new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        _configurationsBySpaceId = configurationsBySpaceId ??
            new Dictionary<string, IReadOnlyList<UbisoftGameConfiguration>>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<UbisoftInstalledGame> installedGames =
            UbisoftInstalledGameRegistry.ReadInstalledGames();

        _installedRegistryIds = installedGames
            .Select(game => game.GameId)
            .ToHashSet();
    }

    public GameIdentityResolution Resolve(uint entitlementProductId)
    {
        string gameName = _database.GetGameName(entitlementProductId.ToString())
            ?? $"Game {entitlementProductId}";

        if (GameIdMappingOverrides.TryResolveSessionGameId(
                entitlementProductId,
                _database,
                _variantResolver,
                out uint overriddenSessionGameId))
        {
            IReadOnlyList<uint> mappedAchievementIds =
                GameIdMappingOverrides.GetPreferredAchievementIds(
                    entitlementProductId,
                    _database,
                    _variantResolver);

            return Resolved(
                entitlementProductId,
                overriddenSessionGameId,
                "Code mapping override",
                $"Achievement and spool IDs: {string.Join(", ", mappedAchievementIds)}.");
        }

        IReadOnlyList<uint> equivalentIds =
            _variantResolver.GetEquivalentVariantIds(entitlementProductId);

        HashSet<uint> equivalentIdSet = equivalentIds
            .Append(entitlementProductId)
            .ToHashSet();

        _spaceIdsByProductId.TryGetValue(entitlementProductId, out string? spaceId);

        Console.WriteLine();
        Console.WriteLine(
            $"[Game Identity] Checking {gameName} (product ID: {entitlementProductId}).");

        Console.WriteLine(
            $"Related game versions: {(equivalentIds.Count > 0 ? string.Join(", ", equivalentIds.OrderBy(id => id)) : "None found")}.");

        uint[] installedEquivalentIds = equivalentIds
            .Where(_installedRegistryIds.Contains)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        Console.WriteLine(
            $"Installed matching game versions: {(installedEquivalentIds.Length > 0 ? string.Join(", ", installedEquivalentIds) : "None found")}.");

        if (_installedRegistryIds.Contains(entitlementProductId) &&
            equivalentIdSet.Contains(entitlementProductId))
        {
            return Resolved(
                entitlementProductId,
                entitlementProductId,
                "Ubisoft installed-game registry",
                "The owned product ID matches an installed Ubisoft game.");
        }

        if (installedEquivalentIds.Length == 1)
        {
            return Resolved(
                entitlementProductId,
                installedEquivalentIds[0],
                "Ubisoft installed-game registry",
                "A single installed version matches this game.");
        }

        if (installedEquivalentIds.Length > 1)
        {
            Console.WriteLine(
                "[Game Identity] Multiple installed versions were found. " +
                "Checking Ubisoft account data to identify the correct one.");
        }

        if (!string.IsNullOrWhiteSpace(spaceId) &&
            _connectIdsBySpaceId.TryGetValue(spaceId, out uint connectGameId))
        {
            if (IsValidEquivalentId(entitlementProductId, connectGameId, equivalentIdSet))
            {
                if (installedEquivalentIds.Length > 1 &&
                    installedEquivalentIds.Contains(connectGameId))
                {
                    Console.WriteLine(
                        $"The Ubisoft account data confirms game ID {connectGameId} as the correct installed version.");
                }

                return Resolved(
                    entitlementProductId,
                    connectGameId,
                    "Ubisoft Local Storage",
                    $"Matched using the game's space ID: {spaceId}.");
            }

            Console.WriteLine(
                $"[Game Identity] Ubisoft account data returned game ID {connectGameId}, " +
                $"but it does not match a known version of {gameName}. Ignoring this result.");
        }
        else if (!string.IsNullOrWhiteSpace(spaceId))
        {
            Console.WriteLine(
                "[Game Identity] No matching game ID was found in Ubisoft Local Storage.");
        }

        if (!string.IsNullOrWhiteSpace(spaceId) &&
            _configurationsBySpaceId.TryGetValue(spaceId, out IReadOnlyList<UbisoftGameConfiguration>? configurations))
        {
            uint[] configurationIds = configurations
                .Select(configuration => configuration.LaunchId != 0
                    ? configuration.LaunchId
                    : configuration.InstallId)
                .Where(id => id != 0 && IsValidEquivalentId(entitlementProductId, id, equivalentIdSet))
                .Distinct()
                .OrderBy(id => id)
                .ToArray();

            Console.WriteLine(
                $"Game IDs found in Ubisoft configuration: {(configurationIds.Length > 0 ? string.Join(", ", configurationIds) : "None found")}.");

            if (configurationIds.Length == 1)
            {
                return Resolved(
                    entitlementProductId,
                    configurationIds[0],
                    "Ubisoft configuration catalog",
                    $"Matched using the game's space ID: {spaceId}.");
            }

            if (configurationIds.Length > 1)
            {
                Console.WriteLine(
                    $"[Game Identity] Multiple launch IDs were found for {gameName}. " +
                    "Unable to determine which one is correct.");
            }
        }

        if (installedEquivalentIds.Length > 1)
        {
            return Unresolved(
                entitlementProductId,
                "Multiple installed versions match this game, and Ubisoft account information could not identify the correct one.");
        }

        return Resolved(
            entitlementProductId,
            entitlementProductId,
            "Owned product ID",
            $"No alternative game ID could be verified. Using the owned product ID instead. " +
            $"Related game versions: {string.Join(", ", equivalentIds.OrderBy(id => id))}.");
    }

    private bool IsValidEquivalentId(
        uint entitlementProductId,
        uint candidateId,
        ISet<uint> knownEquivalentIds)
    {
        return candidateId != 0 &&
               knownEquivalentIds.Contains(candidateId) &&
               _database.GetGameName(candidateId.ToString()) != null &&
               _variantResolver.IsSameVariant(entitlementProductId, candidateId);
    }

    private static GameIdentityResolution Resolved(
        uint productId,
        uint sessionId,
        string source,
        string detail)
    {
        Console.WriteLine(
            $"[Game Identity] {detail} Session ID: {sessionId} (Source: {source}).");

        return new GameIdentityResolution(
            productId,
            sessionId,
            source,
            detail);
    }

    private static GameIdentityResolution Unresolved(
        uint productId,
        string detail)
    {
        Console.WriteLine(
            $"[Game Identity] Could not determine the correct session ID for game {productId}. {detail}");

        return new GameIdentityResolution(
            productId,
            null,
            "Unresolved",
            detail);
    }
}

public sealed record GameIdentityResolution(
    uint EntitlementProductId,
    uint? SessionGameId,
    string Source,
    string Detail)
{
    public bool IsResolved => SessionGameId.HasValue;
}