using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using QuickWheel.Models;
using QuickWheel.Services;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Views;

public partial class RadialMenuWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly DispatcherTimer _hoverTimer = new();
    private readonly Stack<WheelContext> _stack = new();

    private AppConfig _cfg = new();
    private ForegroundInfo? _target;
    private WheelRender? _render;
    private Point _center;
    private SlotVisual? _hover;
    private bool _hoverCenter;

    public bool IsMenuOpen { get; private set; }

    public event Action? MenuClosed;
    public event Action<Shortcut, Exception>? ActionFailed;

    /// <summary>Clic sur le centre de la roue principale : ouvrir QuickWheel (réglages)
    /// sur la roue de l'app au premier plan (nom de processus, null si inconnu).</summary>
    public event Action<string?>? SettingsRequested;

    private WheelContext Current => _stack.Peek();

    public RadialMenuWindow()
    {
        InitializeComponent();
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Ne jamais voler le focus à l'app ciblée ; pas d'entrée dans Alt+Tab
        int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        _hoverTimer.Tick += (_, _) => OnHoverDelay();

        MouseMove += (_, e) => UpdateHover(e.GetPosition(Root));
        MouseLeftButtonUp += (_, e) =>
        {
            UpdateHover(e.GetPosition(Root));
            if (_hoverCenter && _stack.Count > 1) GoBack();
            else if (_hoverCenter)
            {
                var process = _target?.ProcessName;
                CloseMenu();
                SettingsRequested?.Invoke(process);
            }
            else ExecuteHovered();
        };
        MouseRightButtonUp += (_, _) => { if (_stack.Count > 1) GoBack(); else CloseMenu(); };
    }

    public void ShowMenu(WheelContext ctx, ForegroundInfo target, AppConfig cfg)
    {
        _cfg = cfg;
        _target = target;
        _stack.Clear();
        _stack.Push(ctx);

        // Couvre l'écran de la souris (coordonnées physiques → pas de souci de DPI)
        GetCursorPos(out var cursor);
        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).Bounds;

        if (!IsVisible) Show();
        SetWindowPos(_hwnd, HWND_TOPMOST, screen.X, screen.Y, screen.Width, screen.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        UpdateLayout();

        _center = Root.PointFromScreen(new Point(cursor.X, cursor.Y));
        IsMenuOpen = true;
        Redraw(animate: true);
    }

    private void Redraw(bool animate)
    {
        var ctx = Current;
        double outer = WheelLogic.OuterRadius(ctx.Layout, _cfg.CenterRadius, _cfg.RingGap);

        // Garder toute la roue visible près des bords de l'écran
        double margin = outer + 8;
        var c = _center;
        c.X = Math.Clamp(c.X, margin, Math.Max(margin, ActualWidth - margin));
        c.Y = Math.Clamp(c.Y, margin, Math.Max(margin, ActualHeight - margin));
        _center = c;

        bool inFolder = _stack.Count > 1;
        string empty = ctx.Layout.Rings.Count == 0 ? "Aucun raccourci" : "";
        _render = WheelRenderer.Draw(Wheel, ctx, new WheelDrawOptions
        {
            CenterRadius = _cfg.CenterRadius,
            Gap = _cfg.RingGap,
            CenterTitle = inFolder ? string.Join(" › ", _stack.Reverse().Skip(1).Select(s => s.Title)) : ctx.Title,
            CenterText = inFolder ? "‹ Retour" : empty.Length > 0 ? empty : _target?.ProcessName ?? "",
            CenterLogo = !inFolder,
        });
        Canvas.SetLeft(Wheel, _center.X);
        Canvas.SetTop(Wheel, _center.Y);
        _hover = null;

        var dur = TimeSpan.FromMilliseconds(animate ? 120 : 90);
        var scale = new DoubleAnimation(animate ? 0.86 : 0.94, 1, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        WheelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        WheelScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale);
        Wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur));

        GetCursorPos(out var cur);
        UpdateHover(Root.PointFromScreen(new Point(cur.X, cur.Y)));
    }

    private void UpdateHover(Point p)
    {
        if (!IsMenuOpen || _render == null) return;
        double dx = p.X - _center.X, dy = p.Y - _center.Y;
        bool inCenter = Math.Sqrt(dx * dx + dy * dy) < _cfg.CenterRadius;
        var hit = WheelLogic.HitTest(Current.Layout, _cfg.CenterRadius, _cfg.RingGap, dx, dy);
        var slot = hit is { } h ? _render.Find(h.ring, h.index) : null;

        if (slot != _hover || inCenter != _hoverCenter)
        {
            _hover = slot;
            _hoverCenter = inCenter;
            _render.SetHover(slot);
            if (inCenter && _stack.Count == 1) _render.SetCenterHot();
            _hoverTimer.Stop();
            // Dossier survolé, ou centre survolé dans un dossier → déclenche après un court délai
            if ((slot?.Shortcut.IsFolder == true) || (inCenter && _stack.Count > 1))
            {
                _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(120, _cfg.FolderHoverMs));
                _hoverTimer.Start();
            }
        }
    }

    private void OnHoverDelay()
    {
        _hoverTimer.Stop();
        if (!IsMenuOpen) return;
        if (_hover?.Shortcut.IsFolder == true) OpenFolder(_hover.Shortcut);
        else if (_hoverCenter && _stack.Count > 1) GoBack();
    }

    private void OpenFolder(Shortcut folder)
    {
        _stack.Push(WheelLogic.FolderContext(folder, Current.App));
        Redraw(animate: false);
    }

    private void GoBack()
    {
        if (_stack.Count <= 1) return;
        _stack.Pop();
        Redraw(animate: false);
    }

    /// <summary>Exécute la case survolée ; un dossier s'ouvre ; rien survolé → ferme.</summary>
    public void ExecuteHovered()
    {
        if (!IsMenuOpen) return;
        GetCursorPos(out var cur);
        UpdateHover(Root.PointFromScreen(new Point(cur.X, cur.Y)));
        if (_hover == null) { CloseMenu(); return; }
        Execute(_hover.Shortcut);
    }

    /// <summary>Raccourci n° index (1 = premier, anneau par anneau depuis le centre).</summary>
    public void ExecuteIndex(int index)
    {
        if (!IsMenuOpen || _render == null) return;
        var ordered = _render.Slots.OrderBy(s => s.Ring).ThenBy(s => s.Index).ToList();
        if (index >= 0 && index < ordered.Count) Execute(ordered[index].Shortcut);
    }

    /// <summary>Échap : remonte d'un dossier, sinon ferme.</summary>
    public void Back()
    {
        if (_stack.Count > 1) GoBack(); else CloseMenu();
    }

    private void Execute(Shortcut sc)
    {
        if (sc.IsFolder) { OpenFolder(sc); return; }
        var target = _target;
        CloseMenu();
        _ = RunSafe(sc, target);
    }

    private async Task RunSafe(Shortcut sc, ForegroundInfo? target)
    {
        if (ActionRunner.IsBusy) App.Current.Notify($"« {sc.Label} » en attente", "Il partira dès que le raccourci en cours sera terminé.");
        try { await ActionRunner.RunAsync(sc, target); }
        catch (Exception ex) { ActionFailed?.Invoke(sc, ex); }
    }

    public void CloseMenu()
    {
        if (!IsMenuOpen) return;
        IsMenuOpen = false;
        _hoverTimer.Stop();
        Hide();
        Wheel.Children.Clear();
        _render = null;
        _stack.Clear();
        MenuClosed?.Invoke();
    }
}
