using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UbiSlot.Games;

namespace UbiSlot.Ubisoft;

public static class UbisoftGameLauncher
{
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;
    private const int ProcessCommandLineInformation = 60;

    private static bool _running;
    private static uint _currentGameId;
    private static bool _externalGameRunning;

    private static readonly HashSet<string>
        UbisoftSessionProcesses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "UbisoftGameLauncher",
                "upc"
            };

    private static readonly Regex
        GameIdArgumentRegex =
            new(
                @"(?:^|\s)-upc_uplay_id(?:\s+|=)(\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex
        ExePathArgumentRegex =
            new(
                @"(?:^|\s)-upc_exe_path(?:\s+|=)(?:""([^""]+)""|(\S+))",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool Launch(uint gameId)
    {
        if (_running)
        {
            Console.WriteLine(
                "[Ubisoft Game Session] " +
                $"UbiSlot session already running for game {_currentGameId}.");

            return _currentGameId == gameId;
        }

        uint? runningGameId =
            GetRunningUbisoftGameId();

        if (runningGameId.HasValue)
        {
            if (runningGameId.Value != gameId)
            {
                Console.WriteLine(
                    "[Ubisoft Safety] " +
                    $"Selected game ID: {gameId}");

                Console.WriteLine(
                    "[Ubisoft Safety] " +
                    $"Running game ID: {runningGameId.Value}");

                Console.WriteLine(
                    "[Ubisoft Safety] " +
                    "Different Ubisoft game is already running.");

                return false;
            }

            _externalGameRunning = true;
            _running = false;
            _currentGameId = gameId;

            Console.WriteLine(
                "[Ubisoft Game Session] " +
                $"Selected game {gameId} is already running.");

            Console.WriteLine(
                "[Ubisoft Game Session] " +
                "Using existing legitimate Ubisoft session.");

            Console.WriteLine(
                "[Ubisoft Game Session] " +
                "UbiSlot will NOT call UPLAY_Startup().");

            return true;
        }

        Console.WriteLine(
            "[Ubisoft Game Session] " +
            $"Starting UbiSlot session for game {gameId}...");

        _externalGameRunning = false;
        _running = false;
        _currentGameId = 0;

        runningGameId =
            GetRunningUbisoftGameId();

        if (runningGameId.HasValue)
        {
            if (runningGameId.Value != gameId)
            {
                Console.WriteLine(
                    "[Ubisoft Safety] " +
                    $"Different Ubisoft game appeared: {runningGameId.Value}");

                return false;
            }

            _externalGameRunning = true;
            _running = false;
            _currentGameId = gameId;

            Console.WriteLine(
                "[Ubisoft Game Session] " +
                $"Game {gameId} started before UPLAY_Startup().");

            Console.WriteLine(
                "[Ubisoft Game Session] " +
                "Using existing legitimate session.");

            return true;
        }

        bool initialized =
            UbisoftConnectSdk.Initialize(gameId);

        if (!initialized)
        {
            Console.WriteLine(
                "[Ubisoft Game Session] " +
                "SDK initialization failed.");

            _running = false;
            _externalGameRunning = false;
            _currentGameId = 0;

            return false;
        }

        _currentGameId = gameId;
        _running = true;
        _externalGameRunning = false;

        Console.WriteLine(
            "[Ubisoft Game Session] " +
            $"UbiSlot session started for game {gameId}.");

        return true;
    }

    private static string GetGameName(uint gameId)
    {
        try
        {
            var database =
                new GameDatabase();

            return database.GetGameName(
                       gameId.ToString())
                   ?? $"Game {gameId}";
        }
        catch
        {
            return $"Game {gameId}";
        }
    }

    public static bool GamesMatch(
        string selectedGame,
        string runningGame)
    {
        return NormalizeGameName(selectedGame)
               == NormalizeGameName(runningGame);
    }

    private static string NormalizeGameName(string name)
    {
        string normalized =
            name
                .Replace("®", "")
                .Replace("™", "")
                .Replace("©", "")
                .Replace("(Steam)", "")
                .Replace("_", " ")
                .Replace("-", " ")
                .Trim();

        while (normalized.Contains(
                   "  ",
                   StringComparison.Ordinal))
        {
            normalized =
                normalized.Replace(
                    "  ",
                    " ");
        }

        return normalized;
    }

    public static bool IsExternalGameRunning() =>
        _externalGameRunning;

    public static bool OwnsActiveSession() =>
        _running;

    public static void Shutdown()
    {
        if (_externalGameRunning)
        {
            Console.WriteLine(
                "[Ubisoft Game Session] " +
                "External legitimate game owns the session.");

            Console.WriteLine(
                "[Ubisoft Game Session] " +
                "Skipping UPLAY_Quit().");

            _externalGameRunning = false;
            _currentGameId = 0;

            return;
        }

        if (!_running)
        {
            return;
        }

        Console.WriteLine(
            "[Ubisoft Game Session] " +
            $"Ending UbiSlot session for game {_currentGameId}...");

        try
        {
            UbisoftConnectSdk.Quit();
            UbisoftConnectSdk.Release();
        }
        finally
        {
            _running = false;
            _currentGameId = 0;
            _externalGameRunning = false;

            Console.WriteLine(
                "[Ubisoft Game Session] " +
                "UbiSlot session ended.");
        }
    }

    public static bool IsRunning() =>
        _running;

    public static uint? CurrentGameId()
    {
        if (!_running)
        {
            return null;
        }

        return _currentGameId;
    }

    public static uint? GetRunningUbisoftGameId()
    {
        Process[] launcherProcesses;

        try
        {
            launcherProcesses =
                Process.GetProcesses();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Could not enumerate Windows processes.",
                ex);
        }

        var candidates =
            new List<(uint GameId, string ExePath, string SourceProcess)>();

        foreach (Process process in launcherProcesses)
        {
            string processName;

            try
            {
                processName =
                    process.ProcessName;
            }
            catch
            {
                continue;
            }

            if (!UbisoftSessionProcesses.Contains(
                    processName))
            {
                continue;
            }

            string? commandLine =
                TryGetProcessCommandLine(
                    process.Id);

            if (string.IsNullOrWhiteSpace(commandLine))
            {
                continue;
            }

            uint? gameId =
                TryParseGameId(commandLine);

            if (!gameId.HasValue)
            {
                continue;
            }

            string? exePath =
                TryParseExecutablePath(commandLine);

            if (string.IsNullOrWhiteSpace(exePath))
            {
                continue;
            }

            candidates.Add(
                (
                    gameId.Value,
                    exePath,
                    processName
                ));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            if (!IsGameProcessRunning(
                    candidate.ExePath))
            {
                continue;
            }

            string gameName =
                GetGameName(
                    candidate.GameId);

            Console.WriteLine(
                "[Ubisoft Safety] " +
                $"Verified active game process: " +
                $"{gameName} ({candidate.GameId})");

            Console.WriteLine(
                "[Ubisoft Safety] " +
                $"Game executable: {candidate.ExePath}");

            Console.WriteLine(
                "[Ubisoft Safety] " +
                $"Detected through: {candidate.SourceProcess}");

            return candidate.GameId;
        }

        Console.WriteLine(
            "[Ubisoft Safety] " +
            "Ubisoft Connect is running, but no matching " +
            "game executable is active.");

        return null;
    }

    public static async Task<uint?> WaitForRunningUbisoftGameIdAsync(
        uint expectedGameId,
        int attempts = 30,
        int delayMilliseconds = 500)
    {
        if (attempts < 1)
        {
            attempts = 1;
        }

        if (delayMilliseconds < 1)
        {
            delayMilliseconds = 1;
        }

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            uint? runningGameId =
                GetRunningUbisoftGameId();

            if (runningGameId.HasValue)
            {
                if (runningGameId.Value == expectedGameId)
                {
                    Console.WriteLine(
                        "[Ubisoft Safety] " +
                        $"Verified expected game {expectedGameId} " +
                        $"on attempt {attempt}/{attempts}.");

                    return runningGameId;
                }

                Console.WriteLine(
                    "[Ubisoft Safety] " +
                    $"Different game {runningGameId.Value} detected " +
                    $"while waiting for {expectedGameId}.");

                return runningGameId;
            }

            if (attempt < attempts)
            {
                await Task.Delay(
                    delayMilliseconds);
            }
        }

        Console.WriteLine(
            "[Ubisoft Safety] " +
            $"Timed out waiting for game {expectedGameId}.");

        return null;
    }

    private static string? TryParseExecutablePath(
        string commandLine)
    {
        Match match =
            ExePathArgumentRegex.Match(
                commandLine);

        if (!match.Success)
        {
            return null;
        }

        string encodedPath =
            match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value;

        if (string.IsNullOrWhiteSpace(
                encodedPath))
        {
            return null;
        }

        return TryDecodeExecutablePath(
            encodedPath);
    }

    private static string? TryDecodeExecutablePath(
        string encodedPath)
    {
        try
        {
            byte[] bytes =
                Convert.FromBase64String(
                    encodedPath);

            var candidates =
                new List<string>();

            string utf8 =
                Encoding.UTF8.GetString(
                    bytes)
                    .TrimEnd('\0')
                    .Trim();

            if (!string.IsNullOrWhiteSpace(utf8))
            {
                candidates.Add(utf8);
            }

            if (bytes.Length % 2 == 0)
            {
                string unicode =
                    Encoding.Unicode.GetString(
                        bytes)
                        .TrimEnd('\0')
                        .Trim();

                if (!string.IsNullOrWhiteSpace(unicode))
                {
                    candidates.Add(unicode);
                }
            }

            foreach (string candidate in candidates.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                if (candidate.EndsWith(
                        ".exe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }
        catch
        {
            // Invalid/stale base64 is treated as no executable path
        }

        return null;
    }

    private static bool IsGameProcessRunning(
        string expectedExePath)
    {
        string fullExpectedPath;

        try
        {
            fullExpectedPath =
                Path.GetFullPath(
                    expectedExePath);
        }
        catch
        {
            return false;
        }

        string expectedProcessName;

        try
        {
            expectedProcessName =
                Path.GetFileNameWithoutExtension(
                    fullExpectedPath);

            if (string.IsNullOrWhiteSpace(
                    expectedProcessName))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        Process[] gameProcesses;

        try
        {
            gameProcesses =
                Process.GetProcessesByName(
                    expectedProcessName);
        }
        catch
        {
            return false;
        }

        foreach (Process process in gameProcesses)
        {
            using (process)
            {
                try
                {
                    string? imagePath =
                        process.MainModule?.FileName;

                    if (!string.IsNullOrWhiteSpace(imagePath) &&
                        string.Equals(
                            Path.GetFullPath(imagePath),
                            fullExpectedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Fall through to command-line verification.
                }

                try
                {
                    string? commandLine =
                        TryGetProcessCommandLine(
                            process.Id);

                    if (CommandLineReferencesExecutable(
                            commandLine,
                            fullExpectedPath))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Ignore inaccessible processes.
                }
            }
        }

        return false;
    }

    private static bool CommandLineReferencesExecutable(
        string? commandLine,
        string expectedExePath)
    {
        if (string.IsNullOrWhiteSpace(
                commandLine))
        {
            return false;
        }

        string normalizedCommandLine =
            commandLine.Trim();

        string quotedPath =
            "\"" +
            expectedExePath +
            "\"";

        if (normalizedCommandLine.StartsWith(
                quotedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedCommandLine.StartsWith(
            expectedExePath,
            StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetRunningUbisoftGameName()
    {
        uint? gameId =
            GetRunningUbisoftGameId();

        if (!gameId.HasValue)
        {
            return null;
        }

        return GetGameName(gameId.Value);
    }

    private static uint? TryParseGameId(
        string commandLine)
    {
        Match match =
            GameIdArgumentRegex.Match(
                commandLine);

        if (!match.Success)
        {
            return null;
        }

        return uint.TryParse(
                match.Groups[1].Value,
                out uint gameId)
            ? gameId
            : null;
    }

    private static string? TryGetProcessCommandLine(
        int processId)
    {
        nint processHandle =
            OpenProcess(
                PROCESS_QUERY_INFORMATION | PROCESS_VM_READ,
                false,
                unchecked((uint)processId));

        if (processHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            int bufferSize =
                4096;

            while (bufferSize <= 1024 * 1024)
            {
                nint buffer =
                    Marshal.AllocHGlobal(bufferSize);

                try
                {
                    int status =
                        NtQueryInformationProcess(
                            processHandle,
                            ProcessCommandLineInformation,
                            buffer,
                            bufferSize,
                            out int returnLength);

                    if (status != 0)
                    {
                        if (returnLength > bufferSize)
                        {
                            bufferSize = returnLength + 1024;
                            continue;
                        }

                        return null;
                    }

                    UNICODE_STRING commandLine =
                        Marshal.PtrToStructure<UNICODE_STRING>(
                            buffer);

                    if (commandLine.Buffer == nint.Zero ||
                        commandLine.Length == 0)
                    {
                        return null;
                    }

                    return Marshal.PtrToStringUni(
                        commandLine.Buffer,
                        commandLine.Length / 2);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(processHandle);
        }

        return null;
    }

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    private static extern nint OpenProcess(
        uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)]
        bool bInheritHandle,
        uint dwProcessId);

    [DllImport(
        "kernel32.dll",
        SetLastError = true)]
    private static extern bool CloseHandle(
        nint hObject);

    [DllImport(
        "ntdll.dll",
        SetLastError = true)]
    private static extern int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        nint processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public nint Buffer;
    }
}
