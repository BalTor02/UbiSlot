using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UbiSlot.Ubisoft;

public static class UbisoftInstalledGameRegistry
{
    private static readonly string[] InstallRegistryPaths =
    [
        @"SOFTWARE\Ubisoft\Launcher\Installs",
        @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs",
        @"SOFTWARE\Ubisoft Game Launcher\Installs"
    ];

    public static IReadOnlyList<UbisoftInstalledGame> ReadInstalledGames()
    {
        var discovered = new Dictionary<uint, UbisoftInstalledGame>();
        int rootsScanned = 0;
        int keysScanned = 0;
        int missingInstallDirectories = 0;

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

        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                RegistryKey? baseKey = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[GameIdentity] Registry root unavailable ({hive}/{view}): {ex.Message}");
                    continue;
                }

                using (baseKey)
                {
                    foreach (string registryPath in InstallRegistryPaths)
                    {
                        RegistryKey? installsKey = null;
                        try
                        {
                            installsKey = baseKey.OpenSubKey(registryPath, writable: false);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(
                                $"[GameIdentity] Registry path read failed ({hive}/{view}/{registryPath}): {ex.Message}");
                        }

                        if (installsKey == null)
                        {
                            continue;
                        }

                        using (installsKey)
                        {
                            rootsScanned++;
                            string[] subKeyNames;
                            try
                            {
                                subKeyNames = installsKey.GetSubKeyNames();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(
                                    $"[GameIdentity] Could not enumerate registry path '{registryPath}': {ex.Message}");
                                continue;
                            }

                            foreach (string subKeyName in subKeyNames)
                            {
                                if (!uint.TryParse(subKeyName, out uint gameId))
                                {
                                    continue;
                                }

                                keysScanned++;
                                try
                                {
                                    using RegistryKey? gameKey = installsKey.OpenSubKey(subKeyName, writable: false);
                                    if (gameKey == null)
                                    {
                                        continue;
                                    }

                                    string? installDirectory =
                                        gameKey.GetValue("InstallDir") as string ??
                                        gameKey.GetValue("InstallPath") as string;

                                    if (string.IsNullOrWhiteSpace(installDirectory))
                                    {
                                        missingInstallDirectories++;
                                        continue;
                                    }

                                    installDirectory = Environment.ExpandEnvironmentVariables(
                                        installDirectory.Trim().Trim('"'));

                                    if (!Directory.Exists(installDirectory))
                                    {
                                        missingInstallDirectories++;
                                        continue;
                                    }

                                    string source = $"{hive}/{view}/{registryPath}/{subKeyName}";
                                    discovered[gameId] = new UbisoftInstalledGame(
                                        gameId,
                                        Path.GetFullPath(installDirectory),
                                        source);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine(
                                        $"[GameIdentity] Skipped registry game key '{subKeyName}': {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }

        Console.WriteLine(
            $"[GameIdentity] Ubisoft install registry: roots={rootsScanned}; numeric keys={keysScanned}; installed IDs={discovered.Count}; missing install paths={missingInstallDirectories}.");

        foreach (UbisoftInstalledGame game in discovered.Values.OrderBy(value => value.GameId))
        {
            Console.WriteLine(
                $"[GameIdentity] Installed registry ID={game.GameId} | path={game.InstallDirectory}");
        }

        return discovered.Values
            .OrderBy(game => game.GameId)
            .ToArray();
    }
}

public sealed record UbisoftInstalledGame(
    uint GameId,
    string InstallDirectory,
    string RegistrySource);
