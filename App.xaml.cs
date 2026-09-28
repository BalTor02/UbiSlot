using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using UbiSlot.Ubisoft;

namespace UbiSlot;

public partial class App : System.Windows.Application
{
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

        if (string.Equals(
                parentName,
                "UbisoftGameLauncher",
                StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                "[UbiSlot] Ubisoft-managed instance.");

            if (e.Args.Length == 0 ||
                !uint.TryParse(
                    e.Args[0],
                    out uint managedGameId))
            {
                Console.WriteLine(
                    "[UbiSlot] Managed instance has no valid game ID.");

                Shutdown();
                return;
            }

            Console.WriteLine(
                $"[UbiSlot] Opening AchievementWindow for game {managedGameId}.");

            var achievementWindow =
                new AchievementWindow(
                    string.Empty,
                    managedGameId.ToString());

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
                bool started =
                    UbisoftGameLauncher.Launch(gameId);

                if (!started)
                {
                    System.Windows.MessageBox.Show(
                        "Ubisoft game session could not be started.",
                        "UbiSlot",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);

                    Shutdown();
                    return;
                }

                Console.WriteLine(
                    "[UbiSlot] Ubisoft bootstrap session started.");

                Shutdown();
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[UbiSlot] Bootstrap error: {ex}");

                System.Windows.MessageBox.Show(
                    "UbiSlot couldn't start the Ubisoft game session.\n\n" +
                    ex.Message,
                    "UbiSlot",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

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
