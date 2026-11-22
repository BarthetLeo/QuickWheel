using System.Diagnostics;
using System.Text;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Services;

public record ForegroundInfo(IntPtr Handle, string ProcessName, string Title)
{
    public static ForegroundInfo Capture()
    {
        var hwnd = GetForegroundWindow();
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return new ForegroundInfo(hwnd, ProcessNameOf(hwnd), sb.ToString());
    }

    /// <summary>
    /// Processus d'une fenêtre. Les apps du Store (Calculatrice, Paramètres…) sont toutes affichées
    /// dans une fenêtre d'ApplicationFrameHost : on renvoie alors le processus de l'app hébergée.
    /// </summary>
    public static string ProcessNameOf(IntPtr hwnd)
    {
        var name = NameOfPid(PidOf(hwnd));
        if (!name.Equals(FrameHost, StringComparison.OrdinalIgnoreCase)) return name;

        uint host = PidOf(hwnd), hosted = 0;
        EnumChildWindows(hwnd, (child, _) =>
        {
            var pid = PidOf(child);
            if (pid == host) return true;
            hosted = pid;
            return false;
        }, IntPtr.Zero);
        return hosted != 0 ? NameOfPid(hosted) : name; // app réduite : l'hôte seul, faute de mieux
    }

    public const string FrameHost = "ApplicationFrameHost";

    private static uint PidOf(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    private static string NameOfPid(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return ""; /* processus protégé ou déjà fermé */ }
    }
}

public record RunningApp(string ProcessName, string Title, string? ExePath);

public static class RunningApps
{
    /// <summary>Chemin de l'exe d'un des processus s'il tourne, sinon null.</summary>
    public static string? FindExe(IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { if (p.MainModule?.FileName is { } path) return path; }
                catch { /* accès refusé (app admin) */ }
                finally { p.Dispose(); }
            }
        return null;
    }

    /// <summary>Processus ayant une fenêtre visible (pour choisir une app dans les paramètres).</summary>
    public static List<RunningApp> List()
    {
        var self = Environment.ProcessId;
        var result = new List<RunningApp>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.MainWindowHandle == IntPtr.Zero) continue;
                if (p.ProcessName.Equals(ForegroundInfo.FrameHost, StringComparison.OrdinalIgnoreCase)) continue; // ajouté ci-dessous, app par app
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* accès refusé (app admin) */ }
                result.Add(new RunningApp(p.ProcessName, p.MainWindowTitle, path));
            }
            catch { }
            finally { p.Dispose(); }
        }
        result.AddRange(StoreApps());
        return result
            .GroupBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Apps du Store ouvertes : chaque fenêtre ApplicationFrameHost visible → l'app qu'elle affiche.</summary>
    private static List<RunningApp> StoreApps()
    {
        var found = new List<RunningApp>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var name = ForegroundInfo.ProcessNameOf(hwnd);
            if (name.Length == 0 || name.Equals(ForegroundInfo.FrameHost, StringComparison.OrdinalIgnoreCase)) return true;
            GetWindowThreadProcessId(hwnd, out var hostPid);
            try
            {
                using var host = Process.GetProcessById((int)hostPid);
                if (!host.ProcessName.Equals(ForegroundInfo.FrameHost, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; }
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            found.Add(new RunningApp(name, title.ToString(), FindExe(new[] { name })));
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
