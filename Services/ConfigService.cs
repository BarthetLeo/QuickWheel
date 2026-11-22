using System.IO;
using Microsoft.Win32;
using QuickWheel.Models;

namespace QuickWheel.Services;

public static class ConfigService
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickWheel");

    public static string FilePath { get; } = Path.Combine(Folder, "config.json");

    /// <summary>
    /// Copie une image dans %APPDATA%\QuickWheel\icons et renvoie la copie :
    /// l'icône reste valable même si l'original est déplacé ou supprimé.
    /// </summary>
    public static string IconsFolder { get; } = Path.Combine(Folder, "icons");

    public static string ImportImage(string source)
    {
        var dir = IconsFolder;
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Guid.NewGuid().ToString("N")[..8] + "-" + Path.GetFileName(source));
        File.Copy(source, dest);
        return dest;
    }

    /// <summary>Problème rencontré au chargement, à signaler à l'utilisateur (null si tout va bien).</summary>
    public static string? LoadWarning { get; private set; }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var text = File.ReadAllText(FilePath);
                var cfg = ConfigStore.Deserialize(text);
                if (!text.Contains("\"Version\""))
                {
                    // Ancien format : on garde une copie avant de réécrire
                    File.Copy(FilePath, FilePath + ".v1.bak", true);
                    Save(cfg);
                }
                return cfg;
            }
        }
        catch (Exception ex)
        {
            // Config illisible : on la met de côté plutôt que de l'écraser
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
            LoadWarning = $"Une copie a été gardée dans config.json.bak. Configuration par défaut chargée. ({ex.Message})";
        }
        var def = WheelLogic.CreateDefault();
        Save(def);
        return def;
    }

    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, ConfigStore.Serialize(cfg));
        File.Move(tmp, FilePath, true);
    }

    // --- Démarrage avec Windows ---
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Si le démarrage auto pointe vers un exe qui n'existe plus (dossier déplacé), le fait pointer ici.</summary>
    public static void RepairStartupPath()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (k?.GetValue("QuickWheel") is not string stored) return;
        if (!File.Exists(stored.Trim('"'))) k.SetValue("QuickWheel", $"\"{Environment.ProcessPath}\"");
    }

    public static bool StartWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue("QuickWheel") != null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("QuickWheel", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("QuickWheel", false);
        }
    }
}
