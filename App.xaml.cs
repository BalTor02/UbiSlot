
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;
using UbiSlot.Ubisoft;

namespace UbiSlot;

public partial class App : System.Windows.Application
{
    private NamedPipeClientStream? _bootstrapStatusPipe;
    private StreamWriter? _bootstrapStatusWriter;

    [DllImport(
        "ntdll.dll",
        SetLastError = true)]
    private static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        ref PROCESS_BASIC_INFORMATION processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        Console.WriteLine(
            $"[UbiSlot] PID: {Environment.ProcessId}");

        Console.WriteLine(
            $"[UbiSlot] Args: {string.Join(" | ", e.Args)}");

        int parentProcessId =
            GetParentProcessId();

        string parentName =
            GetProcessName(parentProcessId);

        Console.WriteLine(
            $"[UbiSlot] Parent PID: {parentProcessId}");

        Console.WriteLine(
            $"[UbiSlot] Parent: {parentName}");

        if (e.Args.Length >= 3 &&
            uint.TryParse(e.Args[0], out _) &&
            string.Equals(
                parentName,
                "UbiSlot",
                StringComparison.OrdinalIgnoreCase))
        {
            ConnectBootstrapStatusPipe(e.Args[2]);
        }

        bool isManualSpoolBootstrap =
            e.Args.Length > 0 &&
            string.Equals(
                e.Args[0],
                "--load-spool",
                StringComparison.OrdinalIgnoreCase);

        if (isManualSpoolBootstrap &&
            string.Equals(
                parentName,
                "UbisoftGameLauncher",
                StringComparison.OrdinalIgnoreCase))
        {
            bool registeredExecutable =
                UbisoftGameLauncher.IsCurrentExecutableRegisteredByUbisoft(
                    parentProcessId);

            Console.WriteLine(
                registeredExecutable
                    ? "[UbiSlot] Duplicate manual-load SDK host confirmed: Ubisoft Connect registered UbiSlot.exe as the game executable."
                    : "[UbiSlot] Manual-load process was relaunched by UbisoftGameLauncher.");

            Console.WriteLine(
                "[UbiSlot] Exiting the managed relaunch without calling UPLAY_Startup again.");

            Shutdown();
            return;
        }

        if (e.Args.Length > 0 &&
            string.Equals(
                e.Args[0],
                "--load-spool",
                StringComparison.OrdinalIgnoreCase))
        {
            if (e.Args.Length < 3 ||
                !uint.TryParse(e.Args[1], out uint spoolGameId) ||
                spoolGameId == 0)
            {
                System.Windows.MessageBox.Show(
                    "The selected spool ID is invalid. Launch the game once through Ubisoft Connect and try again.",
                    "UbiSlot",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                Shutdown();
                return;
            }

            string selectedSpoolFile = e.Args[2];

            try
            {
                if (!File.Exists(selectedSpoolFile))
                {
                    throw new FileNotFoundException(
                        "The selected spool file no longer exists.",
                        selectedSpoolFile);
                }

                string fileSpoolId =
                    new SpoolManager().GetGameId(selectedSpoolFile);

                if (!uint.TryParse(fileSpoolId, out uint parsedFileSpoolId) ||
                    parsedFileSpoolId != spoolGameId)
                {
                    throw new InvalidDataException(
                        "The supplied spool ID does not match the selected spool filename.");
                }

                Console.WriteLine(
                    $"[UbiSlot] Manual LoadedWindow bootstrap. Using spool ID {spoolGameId} directly.");

                bool started =
                    UbisoftGameLauncher.Launch(spoolGameId);

                if (!started)
                {
                    System.Windows.MessageBox.Show(
                        "UbiSlot couldn't start a Ubisoft game instance using the selected spool ID.\n\n" +
                        "Launch the game once through Ubisoft Connect and try again.",
                        "UbiSlot",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);

                    Shutdown();
                    return;
                }

                Console.WriteLine(
                    $"[UbiSlot] Ubisoft session started for selected spool ID {spoolGameId}. " +
                    "Opening LoadedWindow.");

                var loadedWindow =
                    new LoadedWindow(
                        selectedSpoolFile,
                        spoolGameId,
                        isManagedInstance: true);

                MainWindow = loadedWindow;
                loadedWindow.Show();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[UbiSlot] Manual LoadedWindow bootstrap failed: {ex}");

                System.Windows.MessageBox.Show(
                    "UbiSlot couldn't start a Ubisoft game instance using the selected spool ID.\n\n" +
                    "Launch the game once through Ubisoft Connect and try again.\n\n" +
                    ex.Message,
                    "UbiSlot",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                Shutdown();
                return;
            }
        }

        if (string.Equals(
                parentName,
                "UbisoftGameLauncher",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                "[UbiSlot] Ubisoft-managed instance.");

            uint? managedGameId = null;

            if (e.Args.Length > 0 &&
                uint.TryParse(e.Args[0], out uint parsedManagedGameId))
            {
                managedGameId = parsedManagedGameId;
            }
            else
            {
                Console.WriteLine(
                    "[UbiSlot] Managed instance did not receive a valid game ID. " +
                    "Checking for a pending manual spool request.");
            }

            LoadedWindowLaunchRequestStatus requestStatus =
                LoadedWindow.TryConsumePendingLaunchRequest(
                    managedGameId,
                    out string selectedSpoolFile,
                    out uint? requestedSessionGameId);

            if (requestStatus == LoadedWindowLaunchRequestStatus.Consumed)
            {
                Console.WriteLine(
                    "[UbiSlot] Opening LoadedWindow for selected spool " +
                    $"(managed game ID: {managedGameId?.ToString() ?? "NOT FOUND"}).");

                uint? effectiveSessionGameId =
                    managedGameId ?? requestedSessionGameId;

                var loadedWindow =
                    new LoadedWindow(
                        selectedSpoolFile,
                        effectiveSessionGameId,
                        isManagedInstance: true);

                MainWindow = loadedWindow;
                loadedWindow.Show();
                return;
            }

            if (requestStatus == LoadedWindowLaunchRequestStatus.Cancelled ||
                requestStatus == LoadedWindowLaunchRequestStatus.Failed)
            {
                Console.WriteLine(
                    "[UbiSlot] Pending manual spool handoff was cancelled or failed. " +
                    "The managed child will not open an unrelated AchievementWindow.");

                Shutdown();
                return;
            }

            if (LoadedWindow.HasPendingLaunchRequest())
            {
                Console.WriteLine(
                    "[UbiSlot] A manual spool request is pending, but this managed game ID " +
                    "does not match it. Skipping the normal AchievementWindow; the original " +
                    "LoadedWindow will fall back to the selected spool.");

                Shutdown();
                return;
            }

            if (UbisoftGameLauncher.IsCurrentExecutableRegisteredByUbisoft(
                    parentProcessId))
            {
                Console.WriteLine(
                    "[UbiSlot] Ubisoft Connect attempted to start UbiSlot.exe as its own game executable. " +
                    "This is a duplicate SDK-host launch; no second achievement window will be opened.");

                Shutdown();
                return;
            }

            if (!managedGameId.HasValue)
            {
                Console.WriteLine(
                    "[UbiSlot] Managed instance has no valid game ID and no pending spool request.");

                Shutdown();
                return;
            }

            Console.WriteLine(
                $"[UbiSlot] Opening AchievementWindow for game {managedGameId.Value}.");

            var achievementWindow =
                new AchievementWindow(
                    string.Empty,
                    managedGameId.Value.ToString());

            MainWindow = achievementWindow;
            achievementWindow.Show();

            return;
        }

        if (e.Args.Length > 0 &&
            uint.TryParse(
                e.Args[0],
                out uint gameId))
        {
            Console.WriteLine(
                $"[UbiSlot] Bootstrap instance for game {gameId}.");

            try
            {
                string selectedSpoolFile =
                    e.Args.Length > 1
                        ? e.Args[1]
                        : string.Empty;

                if (!string.IsNullOrWhiteSpace(selectedSpoolFile) &&
                    !File.Exists(selectedSpoolFile))
                {
                    Console.WriteLine(
                        "[UbiSlot] Supplied spool file no longer exists; " +
                        "AchievementWindow will use its no-spool fallback: " +
                        selectedSpoolFile);

                    selectedSpoolFile = string.Empty;
                }

                Console.WriteLine(
                    string.IsNullOrWhiteSpace(selectedSpoolFile)
                        ? "[UbiSlot] Opening AchievementWindow before SDK startup; no spool supplied."
                        : "[UbiSlot] Opening AchievementWindow before SDK startup for spool " +
                          Path.GetFileName(selectedSpoolFile) + ".");

                var achievementWindow = new AchievementWindow(
                    selectedSpoolFile,
                    gameId.ToString(),
                    startManagedSession: true);

                MainWindow = achievementWindow;
                achievementWindow.Show();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[UbiSlot] Bootstrap error: {ex}");

                bool manualSpoolRequest =
                    LoadedWindow.TryFailPendingLaunchRequest(gameId);

                if (manualSpoolRequest)
                {
                    Console.WriteLine(
                        "[UbiSlot] Manual spool bootstrap failed. " +
                        "The original LoadedWindow will use its spool-only fallback.");
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "UbiSlot couldn't start the Ubisoft game session.\n\n" +
                        ex.Message,
                        "UbiSlot",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }

                Shutdown();
                return;
            }
        }

        Console.WriteLine(
            "[UbiSlot] Normal application instance.");

        var window =
            new MainWindow();

        MainWindow =
            window;

        window.Show();
    }

    protected override void OnExit(
        ExitEventArgs e)
    {
        try
        {
            if (_bootstrapStatusWriter != null &&
                UbisoftGameLauncher.OwnsActiveSession())
            {
                try
                {
                    _bootstrapStatusWriter.WriteLine("QUITTING");
                    _bootstrapStatusWriter.Flush();
                }
                catch (Exception pipeException)
                {
                    Console.WriteLine(
                        $"[UbiSlot] Could not notify MainWindow about SDK shutdown: {pipeException.Message}");
                }
            }

            bool quitSucceeded =
                UbisoftGameLauncher.Shutdown();

            if (!quitSucceeded)
            {
                Console.WriteLine(
                    "[UbiSlot] Ubisoft SDK shutdown did not report success. " +
                    "The bootstrap will exit after UPLAY_Quit has returned.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[UbiSlot] SDK shutdown error: {ex}");
        }
        finally
        {
            _bootstrapStatusWriter?.Dispose();
            _bootstrapStatusPipe?.Dispose();
        }

        base.OnExit(e);
    }

    private void ConnectBootstrapStatusPipe(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return;
        }

        try
        {
            _bootstrapStatusPipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous);

            _bootstrapStatusPipe.Connect(5000);

            _bootstrapStatusWriter = new StreamWriter(
                _bootstrapStatusPipe)
            {
                AutoFlush = true
            };

            Console.WriteLine(
                "[UbiSlot] Connected to MainWindow bootstrap status channel.");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[UbiSlot] Could not connect to MainWindow status channel: {ex.Message}");

            _bootstrapStatusWriter?.Dispose();
            _bootstrapStatusWriter = null;
            _bootstrapStatusPipe?.Dispose();
            _bootstrapStatusPipe = null;
        }
    }

    private static int GetParentProcessId()
    {
        using Process process =
            Process.GetCurrentProcess();

        PROCESS_BASIC_INFORMATION info =
            new();

        int returnLength;

        int status =
            NtQueryInformationProcess(
                process.Handle,
                0,
                ref info,
                Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(),
                out returnLength);

        if (status != 0)
        {
            return 0;
        }

        return info.InheritedFromUniqueProcessId.ToInt32();
    }

    private static string GetProcessName(
        int processId)
    {
        if (processId <= 0)
        {
            return string.Empty;
        }

        try
        {
            using Process process =
                Process.GetProcessById(processId);

            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
