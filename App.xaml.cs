using System.Diagnostics;
using System.Windows;
using QuickWheel.Models;
using QuickWheel.Services;
using QuickWheel.Views;
using WinForms = System.Windows.Forms;

namespace QuickWheel;

public partial class App : Application
{
    private const int VK_ESCAPE = 0x1B, VK_CONTROL = 0x11, VK_SHIFT = 0x10, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    private Mutex? _mutex;
    private EventWaitHandle? _openSignal;
    private RegisteredWaitHandle? _openWait;
    private KeyboardHook? _hook;
    private WinForms.NotifyIcon? _tray;
    private RadialMenuWindow? _menu;
    private SettingsWindow? _settings;

    public AppConfig Config { get; private set; } = new();

    /// <summary>Désactive le déclencheur (ex. pendant l'enregistrement d'une touche dans les paramètres).</summary>
    public bool Suspended { get; set; }

    private long _triggerDownAt;
    private bool _triggerSwallowed;            // le keyup du déclencheur doit aussi être avalé
    private readonly HashSet<int> _swallowedDowns = new();

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Filet de sécurité : une erreur imprévue (fichier verrouillé…) s'affiche au lieu de fermer l'app
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Notify("Une erreur est survenue", args.Exception.Message, error: true);
        };
        ThemeMode = ThemeMode.Dark; // thème Fluent sombre de WPF (.NET 9) pour les contrôles standards

        _mutex = new Mutex(true, "QuickWheel.SingleInstance", out bool first);
        _openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "QuickWheel.OpenSettings");
        if (!first)
        {
            // Déjà lancé : on demande simplement à l'instance existante d'ouvrir ses réglages
            _openSignal.Set();
            Shutdown();
            return;
        }
        _openWait = ThreadPool.RegisterWaitForSingleObject(_openSignal,
            (_, _) => Dispatcher.BeginInvoke(() => OpenSettings()), null, Timeout.Infinite, executeOnlyOnce: false);

        Config = ConfigService.Load();
        try { ConfigService.RepairStartupPath(); } catch { /* registre inaccessible */ }
        if (ConfigService.LoadWarning is { } warn)
            Dispatcher.BeginInvoke(() => Notify("Configuration illisible", warn, error: true));

        _menu = new RadialMenuWindow();
        _menu.SettingsRequested += process => OpenSettings(process);
        _menu.ActionFailed += (item, ex) =>
            Dispatcher.BeginInvoke(() => Notify($"« {item.Label} » a échoué", ex.Message, error: true));

        _hook = new KeyboardHook { KeyHandler = OnKey };
        CreateTray();

        if (e.Args.Contains("--settings")) OpenSettings();
    }

    public string TriggerText()
    {
        var t = Config.Trigger;
        return KeyParser.FormatCombo(t.Ctrl, t.Alt, t.Shift, t.Win, t.VirtualKey, display: true);
    }

    public void ApplyConfig(AppConfig cfg)
    {
        Config = cfg;
        ConfigService.Save(cfg);
        if (_tray != null) _tray.Text = $"QuickWheel — {TriggerText()}";
    }

    // ---------- Gestion clavier (appelé sur le thread UI par le hook) ----------

    private bool OnKey(int vk, bool down, bool repeat)
    {
        var t = Config.Trigger;
        bool menuOpen = _menu?.IsMenuOpen == true;

        if (vk == t.VirtualKey)
        {
            if (down)
            {
                if (repeat && (menuOpen || _triggerSwallowed)) return true; // répétition automatique
                if (menuOpen)
                {
                    // Nouvel appui alors que le menu est ouvert (mode clic) → fermer
                    _triggerSwallowed = true;
                    _triggerDownAt = Environment.TickCount64;
                    Dispatcher.BeginInvoke(() => _menu!.CloseMenu());
                    return true;
                }
                if (Suspended || !ModifiersMatch(t)) return false;

                _triggerSwallowed = true;
                _triggerDownAt = Environment.TickCount64;
                if (t.Alt || t.Win) InputSender.TapDummy(); // évite que Alt/Win seuls ouvrent un menu
                OpenMenu();
                return true;
            }
            else
            {
                if (!_triggerSwallowed) return false;
                _triggerSwallowed = false;
                long held = Environment.TickCount64 - _triggerDownAt;
                // Maintenu puis relâché → exécute l'élément visé. Simple appui → le menu reste ouvert (mode clic).
                if (menuOpen && held >= Config.HoldThresholdMs)
                    Dispatcher.BeginInvoke(() => _menu!.ExecuteHovered());
                return true;
            }
        }

        if (menuOpen)
        {
            if (down)
            {
                int index = vk switch
                {
                    >= 0x31 and <= 0x39 => vk - 0x31,   // 1..9
                    >= 0x61 and <= 0x69 => vk - 0x61,   // pavé num. 1..9
                    _ => -1
                };
                if (index >= 0)
                {
                    _swallowedDowns.Add(vk);
                    Dispatcher.BeginInvoke(() => _menu!.ExecuteIndex(index));
                    return true;
                }
                if (vk == VK_ESCAPE)
                {
                    _swallowedDowns.Add(vk);
                    Dispatcher.BeginInvoke(() => _menu!.Back());
                    return true;
                }
            }
        }

        if (!down && _swallowedDowns.Remove(vk)) return true;
        return false;
    }

    private static bool ModifiersMatch(TriggerKey t)
    {
        bool ctrl = KeyboardHook.IsKeyDown(VK_CONTROL);
        bool shift = KeyboardHook.IsKeyDown(VK_SHIFT);
        bool alt = KeyboardHook.IsKeyDown(VK_MENU);
        bool win = KeyboardHook.IsKeyDown(VK_LWIN) || KeyboardHook.IsKeyDown(VK_RWIN);
        return ctrl == t.Ctrl && shift == t.Shift && alt == t.Alt && win == t.Win;
    }

    private void OpenMenu()
    {
        var target = ForegroundInfo.Capture();
        var ctx = WheelLogic.ForProcess(Config, target.ProcessName);
        var cfg = Config;
        Dispatcher.BeginInvoke(() => _menu!.ShowMenu(ctx, target, cfg));
    }

    // ---------- Icône de notification ----------

    private void CreateTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Paramètres…", null, (_, _) => OpenSettings());
        menu.Items.Add("Ouvrir le dossier de config", null, (_, _) =>
            Process.Start(new ProcessStartInfo { FileName = ConfigService.Folder, UseShellExecute = true }));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => Quit());

        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = $"QuickWheel — {TriggerText()}",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OpenSettings();
    }

    /// <summary>Icône du logo à la bonne taille pour la zone de notification (ressource WPF).</summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var res = GetResourceStream(new Uri("pack://application:,,,/Assets/QuickWheel.ico"));
            if (res != null)
            {
                using var s = res.Stream;
                return new System.Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
            }
        }
        catch { }
        return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
    }

    public void Notify(string title, string message = "", bool error = false) => Toast.Show(title, message, error);

    /// <summary>Ouvre l'éditeur. Avec <paramref name="processName"/>, affiche directement la roue de cette app (si elle en a une).</summary>
    public void OpenSettings(string? processName = null)
    {
        if (_settings is { IsLoaded: true })
        {
            if (processName != null && _settings.IsVisible) _settings.ShowWheelFor(processName);
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
            return;
        }
        _settings = new SettingsWindow(ConfigStore.DeepClone(Config));
        if (processName != null) _settings.ShowWheelFor(processName);
        _settings.Closed += (_, _) => { _settings = null; Suspended = false; };
        _settings.Show();
        _settings.Activate();
    }

    private void Quit()
    {
        _hook?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _tray?.Dispose();
        _openWait?.Unregister(null);
        _openSignal?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
