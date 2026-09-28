using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace UbiSlot.Ubisoft;

public static class UbisoftConnectSdk
{
    private const string DllName =
        "uplay_r164.dll";

    private const uint LoadLibrarySearchDllLoadDir =
        0x00000100;

    private const uint LoadLibrarySearchDefaultDirs =
        0x00001000;

    private static nint _dll =
        nint.Zero;

    private static UPLAY_StartupDelegate? _startup;
    private static UPLAY_QuitDelegate? _quit;
    private static UPLAY_ReleaseDelegate? _release;

    private static UPLAY_USER_IsConnectedDelegate? _isConnected;
    private static UPLAY_USER_GetStringDelegate? _getAccountId;
    private static UPLAY_USER_GetStringDelegate? _getTicket;

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern nint LoadLibraryExW(
        string lpFileName,
        nint hFile,
        uint dwFlags);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int UPLAY_StartupDelegate(
        uint gameId);

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int UPLAY_QuitDelegate();

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int UPLAY_ReleaseDelegate();

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate int UPLAY_USER_IsConnectedDelegate();

    [UnmanagedFunctionPointer(
        CallingConvention.Cdecl)]
    private delegate nint UPLAY_USER_GetStringDelegate();

    public static bool Initialize(
        uint gameId)
    {
        try
        {
            EnsureLoaded();

            if (_startup == null)
            {
                Console.WriteLine(
                    "[Ubisoft SDK] UPLAY_Startup is unavailable.");

                return false;
            }

            int result =
                _startup(
                    gameId);

            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"UPLAY_Startup({gameId}) returned: {result}");

            return result == 0 ||
                   result == 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"Initialization error: {ex}");

            return false;
        }
    }

    public static bool IsConnected()
    {
        try
        {
            EnsureLoaded();

            if (_isConnected == null)
            {
                Console.WriteLine(
                    "[Ubisoft SDK] " +
                    "UPLAY_USER_IsConnected is unavailable.");

                return false;
            }

            int result =
                _isConnected();

            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"UPLAY_USER_IsConnected: {result}");

            return result != 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"IsConnected error: {ex}");

            return false;
        }
    }

    public static string? GetAccountId()
    {
        try
        {
            EnsureLoaded();

            if (_getAccountId == null)
            {
                Console.WriteLine(
                    "[Ubisoft SDK] " +
                    "UPLAY_USER_GetAccountIdUtf8 is unavailable.");

                return null;
            }

            nint pointer =
                _getAccountId();

            if (pointer == nint.Zero)
            {
                return null;
            }

            return Marshal.PtrToStringUTF8(
                pointer);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"GetAccountId error: {ex}");

            return null;
        }
    }

    public static string? GetTicket()
    {
        try
        {
            EnsureLoaded();

            if (_getTicket == null)
            {
                Console.WriteLine(
                    "[Ubisoft SDK] " +
                    "UPLAY_USER_GetTicketUtf8 is unavailable.");

                return null;
            }

            nint pointer =
                _getTicket();

            if (pointer == nint.Zero)
            {
                return null;
            }

            return Marshal.PtrToStringUTF8(
                pointer);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"GetTicket error: {ex}");

            return null;
        }
    }

    public static void Quit()
    {
        if (_dll == nint.Zero ||
            _quit == null)
        {
            return;
        }

        try
        {
            int result =
                _quit();

            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"UPLAY_Quit returned: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"UPLAY_Quit error: {ex}");
        }
    }

    public static void Release()
    {
        if (_dll == nint.Zero ||
            _release == null)
        {
            return;
        }

        try
        {
            int result =
                _release();

            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"UPLAY_Release returned: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[Ubisoft SDK] " +
                $"UPLAY_Release error: {ex}");
        }
    }

    private static void EnsureLoaded()
    {
        if (_dll != nint.Zero &&
            _startup != null &&
            _quit != null &&
            _release != null)
        {
            return;
        }

        string dllPath =
            LocateDll();

        nint handle =
            LoadLibraryExW(
                dllPath,
                nint.Zero,
                LoadLibrarySearchDllLoadDir |
                LoadLibrarySearchDefaultDirs);

        if (handle == nint.Zero)
        {
            int error =
                Marshal.GetLastWin32Error();

            throw new DllNotFoundException(
                $"Failed to load the installed Ubisoft SDK. " +
                $"Win32 error: {error}");
        }

        _dll =
            handle;

        try
        {
            _startup =
                GetRequiredDelegate<
                    UPLAY_StartupDelegate>(
                        "UPLAY_Startup");

            _quit =
                GetRequiredDelegate<
                    UPLAY_QuitDelegate>(
                        "UPLAY_Quit");

            _release =
                GetRequiredDelegate<
                    UPLAY_ReleaseDelegate>(
                        "UPLAY_Release");

            _isConnected =
                TryGetDelegate<
                    UPLAY_USER_IsConnectedDelegate>(
                        "UPLAY_USER_IsConnected");

            _getAccountId =
                TryGetDelegate<
                    UPLAY_USER_GetStringDelegate>(
                        "UPLAY_USER_GetAccountIdUtf8");

            _getTicket =
                TryGetDelegate<
                    UPLAY_USER_GetStringDelegate>(
                        "UPLAY_USER_GetTicketUtf8");

            Console.WriteLine(
                "[Ubisoft SDK] " +
                "Installed Ubisoft SDK loaded.");
        }
        catch
        {
            NativeLibrary.Free(
                _dll);

            _dll =
                nint.Zero;

            _startup = null;
            _quit = null;
            _release = null;
            _isConnected = null;
            _getAccountId = null;
            _getTicket = null;

            throw;
        }
    }

    private static string LocateDll()
    {
        var candidates =
            new List<string>();

        AddRunningProcessDirectory(
            candidates,
            "UbisoftGameLauncher");

        AddRunningProcessDirectory(
            candidates,
            "UbisoftConnect");

        AddRegistryInstallLocations(
            candidates);

        string programFilesX86 =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);

        string programFiles =
            Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);

        string localAppData =
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

        AddCandidateDirectory(
            candidates,
            Path.Combine(
                programFilesX86,
                "Ubisoft",
                "Ubisoft Game Launcher"));

        AddCandidateDirectory(
            candidates,
            Path.Combine(
                programFiles,
                "Ubisoft",
                "Ubisoft Game Launcher"));

        AddCandidateDirectory(
            candidates,
            Path.Combine(
                programFilesX86,
                "Ubisoft",
                "Ubisoft Connect"));

        AddCandidateDirectory(
            candidates,
            Path.Combine(
                programFiles,
                "Ubisoft",
                "Ubisoft Connect"));

        AddCandidateDirectory(
            candidates,
            Path.Combine(
                localAppData,
                "Programs",
                "Ubisoft Game Launcher"));

        AddCandidateDirectory(
            candidates,
            Path.Combine(
                localAppData,
                "Programs",
                "Ubisoft Connect"));

        foreach (string directory in candidates)
        {
            string dllPath =
                Path.Combine(
                    directory,
                    DllName);

            if (File.Exists(
                    dllPath))
            {
                return Path.GetFullPath(
                    dllPath);
            }
        }

        throw new FileNotFoundException(
            "Could not locate the installed Ubisoft Connect SDK " +
            "(uplay_r164.dll).");
    }

    private static void AddRunningProcessDirectory(
        List<string> candidates,
        string processName)
    {
        Process[] processes;

        try
        {
            processes =
                Process.GetProcessesByName(
                    processName);
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
                    string? executablePath =
                        process.MainModule?.FileName;

                    if (string.IsNullOrWhiteSpace(
                            executablePath))
                    {
                        continue;
                    }

                    string? directory =
                        Path.GetDirectoryName(
                            executablePath);

                    AddCandidateDirectory(
                        candidates,
                        directory);
                }
                catch
                {
                    // Process exited?
                }
            }
        }
    }

    private static void AddRegistryInstallLocations(
        List<string> candidates)
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

        foreach (RegistryHive hive in hives)
        {
            foreach (RegistryView view in views)
            {
                try
                {
                    using RegistryKey baseKey =
                        RegistryKey.OpenBaseKey(
                            hive,
                            view);

                    using RegistryKey? uninstallKey =
                        baseKey.OpenSubKey(
                            uninstallPath);

                    if (uninstallKey == null)
                    {
                        continue;
                    }

                    foreach (string subKeyName in
                             uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using RegistryKey? key =
                                uninstallKey.OpenSubKey(
                                    subKeyName);

                            if (key == null)
                            {
                                continue;
                            }

                            string? displayName =
                                key.GetValue(
                                    "DisplayName")
                                as string;

                            if (string.IsNullOrWhiteSpace(
                                    displayName))
                            {
                                continue;
                            }

                            bool isUbisoft =
                                displayName.Contains(
                                    "Ubisoft Connect",
                                    StringComparison.OrdinalIgnoreCase)
                                ||
                                displayName.Contains(
                                    "Ubisoft Game Launcher",
                                    StringComparison.OrdinalIgnoreCase);

                            if (!isUbisoft)
                            {
                                continue;
                            }

                            string? installLocation =
                                key.GetValue(
                                    "InstallLocation")
                                as string;

                            AddCandidateDirectory(
                                candidates,
                                installLocation);
                        }
                        catch
                        {
                            // Ignore malformed registry entries lol.
                        }
                    }
                }
                catch
                {
                    // Ignore unavailable registry views.
                }
            }
        }
    }

    private static void AddCandidateDirectory(
        List<string> candidates,
        string? directory)
    {
        if (string.IsNullOrWhiteSpace(
                directory))
        {
            return;
        }

        try
        {
            string fullPath =
                Path.GetFullPath(
                    directory.Trim());

            if (!candidates.Contains(
                    fullPath,
                    StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(
                    fullPath);
            }
        }
        catch
        {
            // Ignore invalid paths.
        }
    }

    private static T GetRequiredDelegate<T>(
        string exportName)
        where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(
                _dll,
                exportName,
                out nint address))
        {
            throw new EntryPointNotFoundException(
                $"Ubisoft SDK export not found: {exportName}");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(
            address);
    }

    private static T? TryGetDelegate<T>(
        string exportName)
        where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(
                _dll,
                exportName,
                out nint address))
        {
            return null;
        }

        return Marshal.GetDelegateForFunctionPointer<T>(
            address);
    }
}
