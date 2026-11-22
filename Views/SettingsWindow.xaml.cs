using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickWheel.Models;
using QuickWheel.Services;

namespace QuickWheel.Views;

/// <summary>Entrée de la liste des roues (globale ou app).</summary>
public sealed class WheelEntry
{
    public AppTarget? App { get; init; }
    public required string Name { get; init; }
    public required string Sub { get; init; }
    public required FrameworkElement IconElement { get; init; }
}

public partial class SettingsWindow : Window
{
    private AppConfig _cfg;
    private string _savedJson;
    private TriggerKey _trigger;

    /// <summary>Chemin de navigation : roue de premier niveau puis dossiers ouverts.</summary>
    private readonly List<WheelContext> _path = new();
    private Shortcut? _selected;
    private WheelRender? _render;
    private readonly DispatcherTimer _redrawSoon = new() { Interval = TimeSpan.FromMilliseconds(60) };

    // Glisser-déposer
    private string? _dragId;
    private Point _dragStart;
    private bool _dragging;
    private DropTarget? _dropTarget;

    private Point _center;
    private double _scale = 1;

    private WheelContext Ctx => _path[^1];
    private bool InFolder => Ctx.Folder != null;

    public SettingsWindow(AppConfig cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        _savedJson = ConfigStore.Serialize(cfg);
        _trigger = CopyTrigger(cfg.Trigger);

        _redrawSoon.Tick += (_, _) => { _redrawSoon.Stop(); DrawWheel(); RefreshWheelListLabels(); };

        ClickButtonBox.ItemsSource = new[]
        {
            new KeyValuePair<ClickButton, string>(ClickButton.Left, "Clic gauche"),
            new KeyValuePair<ClickButton, string>(ClickButton.Double, "Double-clic"),
            new KeyValuePair<ClickButton, string>(ClickButton.Right, "Clic droit"),
            new KeyValuePair<ClickButton, string>(ClickButton.Middle, "Clic milieu"),
        };
        Closed += (_, _) => { _recorder?.Dispose(); _badge?.Close(); };
        BrandLogo.Content = Logo.Create(26);

        BuildSwatches();
        LoadGeneral();
        RebuildWheelList(select: null);
        Closing += OnClosing;
    }

    private static TriggerKey CopyTrigger(TriggerKey t) =>
        new() { VirtualKey = t.VirtualKey, Ctrl = t.Ctrl, Alt = t.Alt, Shift = t.Shift, Win = t.Win };

    // =====================================================================
    //  Contexte (roue / dossier) affiché
    // =====================================================================

    private void OnContextChanged()
    {
        var app = _path[0].App;
        UpdateWheelInspector();
        ExportButton.ToolTip = app != null ? "Exporter cette roue dans un fichier" : "Exporter toute la configuration";

        BuildBreadcrumb();
        RebuildRings();
        DrawWheel();
        UpdateHint();
    }

    private void BuildBreadcrumb()
    {
        Breadcrumb.Children.Clear();
        for (int i = 0; i < _path.Count; i++)
        {
            int depth = i;
            bool last = i == _path.Count - 1;
            if (i > 0) Breadcrumb.Children.Add(new TextBlock { Text = "›", Foreground = Palette.DimBrush, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 16 });
            if (last)
            {
                Breadcrumb.Children.Add(new TextBlock { Text = _path[i].Title, Style = (Style)FindResource("H1"), VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                var b = new Button { Content = _path[i].Title, Style = (Style)FindResource("Quiet"), FontSize = 15 };
                b.Click += (_, _) => GoTo(depth);
                Breadcrumb.Children.Add(b);
            }
        }
    }

    private void GoTo(int depth)
    {
        while (_path.Count > depth + 1) _path.RemoveAt(_path.Count - 1);
        Select(null);
        OnContextChanged();
    }

    private void EnterFolder(Shortcut folder)
    {
        _path.Add(WheelLogic.FolderContext(folder, _path[0].App));
        Select(null);
        OnContextChanged();
    }

    private void UpdateHint()
    {
        CanvasHint.Text = Ctx.Layout.Rings.Count == 0
            ? "Cette roue est vide. Ajoute un raccourci avec le bouton en haut à droite."
            : InFolder
                ? "Glisse une case pour la déplacer. Clique le centre pour remonter."
                : "Glisse une case pour la déplacer. Lâche-la au-delà du dernier anneau pour en créer un nouveau. Double-clique un dossier pour l'ouvrir.";
    }

    // =====================================================================
    //  Dessin de la roue
    // =====================================================================

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawWheel();

    private void DrawWheel(WheelLayout? preview = null)
    {
        if (_path.Count == 0 || CanvasHost.ActualWidth < 10) return;
        var ctx = Ctx;
        var layout = preview ?? ctx.Layout;
        var drawCtx = preview == null ? ctx : new WheelContext { Title = ctx.Title, Layout = preview, App = ctx.App, Folder = ctx.Folder, Resolve = ctx.Resolve };

        double outer = WheelLogic.OuterRadius(layout, _cfg.CenterRadius, _cfg.RingGap) + 26;
        double room = Math.Min(CanvasHost.ActualWidth, CanvasHost.ActualHeight) / 2 - 30;
        _scale = Math.Min(1.25, room / Math.Max(outer + 50, 120)); // garde de la place pour viser « au-delà »
        _center = new Point(CanvasHost.ActualWidth / 2, CanvasHost.ActualHeight / 2 - 8);

        CanvasScale.ScaleX = CanvasScale.ScaleY = _scale;
        CanvasOffset.X = _center.X;
        CanvasOffset.Y = _center.Y;

        _render = WheelRenderer.Draw(EditorCanvas, drawCtx, new WheelDrawOptions
        {
            CenterRadius = _cfg.CenterRadius,
            Gap = _cfg.RingGap,
            Editor = true,
            SelectedId = _selected?.Id,
            GhostId = _dragging ? _dragId : null,
            CenterTitle = InFolder ? "Dossier" : (ctx.App == null ? "Toutes les apps" : "App"),
            CenterText = InFolder ? "‹ Remonter" : ctx.Title,
        });

        // Anneau fantôme : là où un nouvel anneau serait créé
        if (_dragging && _dropTarget is { NewRing: true })
        {
            double r = WheelLogic.OuterRadius(ctx.Layout, _cfg.CenterRadius, _cfg.RingGap) + _cfg.RingGap + WheelLogic.DefaultThickness / 2;
            var ghost = new System.Windows.Shapes.Ellipse
            {
                Width = r * 2, Height = r * 2, Stroke = Palette.BrassBrush, StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 4 }, IsHitTestVisible = false, Opacity = 0.8
            };
            Canvas.SetLeft(ghost, -r); Canvas.SetTop(ghost, -r);
            EditorCanvas.Children.Insert(0, ghost);
        }
    }

    private void RequestRedraw()
    {
        _redrawSoon.Stop();
        _redrawSoon.Start();
    }

    /// <summary>Cartes d'anneaux sous la roue (épaisseur réglable). Reconstruites seulement quand la structure change.</summary>
    private void RebuildRings()
    {
        RingsPanel.Children.Clear();
        var rings = Ctx.Layout.Rings;
        for (int i = 0; i < rings.Count; i++)
        {
            var ring = rings[i];
            var slider = new Slider { Minimum = 40, Maximum = 140, Value = ring.Thickness, Width = 130, TickFrequency = 2, IsSnapToTickEnabled = true };
            slider.ValueChanged += (_, ev) => { ring.Thickness = ev.NewValue; RequestRedraw(); };
            int n = ring.Items.Count;
            var card = new Border
            {
                Style = (Style)FindResource("Panel"), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 8, 0),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = i == 0 ? "Anneau intérieur" : i == rings.Count - 1 ? "Anneau extérieur" : $"Anneau {i + 1}", FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = $"{n} case{(n > 1 ? "s" : "")}, épaisseur", Foreground = Palette.DimBrush, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 4) },
                        slider
                    }
                }
            };
            RingsPanel.Children.Add(card);
        }
    }

    // =====================================================================
    //  Souris : sélection, glisser-déposer
    // =====================================================================

    private (double dx, double dy) ToWheel(Point p) => ((p.X - _center.X) / _scale, (p.Y - _center.Y) / _scale);

    private SlotVisual? SlotAt(Point p)
    {
        if (_render == null) return null;
        var (dx, dy) = ToWheel(p);
        var hit = WheelLogic.HitTest(Ctx.Layout, _cfg.CenterRadius, _cfg.RingGap, dx, dy);
        return hit is { } h ? _render.Find(h.ring, h.index) : null;
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
        var (dx, dy) = ToWheel(p);
        if (Math.Sqrt(dx * dx + dy * dy) < _cfg.CenterRadius)
        {
            if (InFolder) GoTo(_path.Count - 2);
            return;
        }

        var slot = SlotAt(p);
        if (slot == null) { Select(null); DrawWheel(); return; }

        if (e.ClickCount == 2 && slot.Shortcut.IsFolder)
        {
            EnterFolder(slot.Shortcut);
            return;
        }

        Select(slot.Shortcut);
        DrawWheel();
        _dragId = slot.Shortcut.Id;
        _dragStart = p;
        _dragging = false;
        CanvasHost.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(CanvasHost);
        if (_dragId != null && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragging && (p - _dragStart).Length > 5)
            {
                _dragging = true;
                CanvasHost.Cursor = Cursors.SizeAll;
            }
            if (_dragging)
            {
                var (dx, dy) = ToWheel(p);
                var target = WheelLogic.DropAt(Ctx.Layout, _cfg.CenterRadius, _cfg.RingGap, dx, dy, _dragId);
                if (target != _dropTarget)
                {
                    _dropTarget = target;
                    if (target is { } t)
                    {
                        // Aperçu en direct du résultat
                        var preview = Ctx.Layout.CloneWith();
                        WheelLogic.Move(preview, _dragId, t);
                        DrawWheel(preview);
                    }
                    else DrawWheel();
                }
            }
            return;
        }
        _render?.SetHover(SlotAt(p));
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        CanvasHost.ReleaseMouseCapture();
        CanvasHost.Cursor = Cursors.Arrow;
        if (_dragging && _dragId != null && _dropTarget is { } t)
        {
            int ringsBefore = Ctx.Layout.Rings.Count;
            WheelLogic.Move(Ctx.Layout, _dragId, t);
            _dragging = false;
            RebuildRings();
            if (Ctx.Layout.Rings.Count != ringsBefore)
                CanvasHint.Text = Ctx.Layout.Rings.Count > ringsBefore ? "Nouvel anneau créé." : "Anneau vide retiré.";
        }
        _dragging = false;
        _dragId = null;
        _dropTarget = null;
        DrawWheel();
        RefreshWheelListLabels();
    }

    private void Canvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_dragging) _render?.SetHover(null);
    }

    // =====================================================================
    //  Ajout / suppression de raccourcis
    // =====================================================================

    private void AddShortcut_Click(object sender, RoutedEventArgs e) => AddNew(folder: false);
    private void AddFolder_Click(object sender, RoutedEventArgs e) => AddNew(folder: true);

    private void AddNew(bool folder)
    {
        if (_path.Count == 0) return;
        var sc = new Shortcut
        {
            Label = folder ? "Nouveau dossier" : "Nouveau raccourci",
            Icon = folder ? "mdi:folder-outline" : "mdi:star-outline",
            Kind = folder ? ShortcutKind.Folder : ShortcutKind.Action
        };
        if (!folder) sc.Actions.Add(new ActionStep { Type = ActionType.Keys });

        if (InFolder)
        {
            Ctx.Folder!.Folder.Items.Add(sc);
        }
        else
        {
            var dlg = new ScopeDialog(_cfg.Apps, Ctx.App,
                folder ? "Où doit apparaître ce dossier ?" : "Où doit apparaître ce raccourci ?",
                folder ? "Créer le dossier" : "Créer le raccourci") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            sc.Scope = dlg.Scope;
            sc.AppIds = new ObservableCollection<string>(dlg.AppIds);
            _cfg.Shortcuts.Add(sc);
        }

        WheelLogic.Sync(_cfg);
        if (!WheelLogic.BelongsTo(sc, Ctx.App) && !InFolder)
            CanvasHint.Text = "Raccourci créé, mais il n'apparaît pas dans cette roue (portée choisie).";
        Select(Ctx.Layout.AllIds().Contains(sc.Id) ? sc : null);
        RebuildRings();
        DrawWheel();
        RefreshWheelListLabels();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var copy = _selected.CloneNew();
        copy.Label += " (copie)";
        if (InFolder) Ctx.Folder!.Folder.Items.Add(copy);
        else _cfg.Shortcuts.Add(copy);
        WheelLogic.Sync(_cfg);

        // Place la copie juste après l'original
        var rings = Ctx.Layout.Rings;
        for (int r = 0; r < rings.Count; r++)
        {
            int idx = rings[r].Items.IndexOf(_selected.Id);
            if (idx >= 0) { WheelLogic.Move(Ctx.Layout, copy.Id, new DropTarget(r, idx + 1, false)); break; }
        }
        Select(copy);
        RebuildRings();
        DrawWheel();
        RefreshWheelListLabels();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var sc = _selected;
        if (sc == null) return;

        if (InFolder)
        {
            if (!Dialog.Confirm(this, $"Supprimer « {sc.Label} » ?", "Le raccourci sera retiré de ce dossier.", "Supprimer", DialogKind.Danger)) return;
            Ctx.Folder!.Folder.Items.Remove(sc);
        }
        else if (sc.Scope == ShortcutScope.Apps && sc.AppIds.Count > 1 && Ctx.App != null)
        {
            int answer = Dialog.Show(this, DialogKind.Question, $"Retirer « {sc.Label} » ?",
                $"Ce raccourci est partagé entre {sc.AppIds.Count} apps.",
                new DialogButton("Annuler", Cancel: true),
                new DialogButton("Supprimer partout"),
                new DialogButton($"Seulement de « {Ctx.App.Name} »", Primary: true));
            if (answer <= 0) return;
            if (answer == 2) sc.AppIds.Remove(Ctx.App.Id);
            else _cfg.Shortcuts.Remove(sc);
        }
        else
        {
            string where = sc.Scope == ShortcutScope.Global ? " de toutes les roues" : "";
            if (!Dialog.Confirm(this, $"Supprimer « {sc.Label} »{where} ?", "", "Supprimer", DialogKind.Danger)) return;
            _cfg.Shortcuts.Remove(sc);
        }

        WheelLogic.Sync(_cfg);
        Select(null);
        RebuildRings();
        DrawWheel();
        RefreshWheelListLabels();
    }

    // =====================================================================
    //  Inspecteur
    // =====================================================================

    private void Select(Shortcut? sc)
    {
        if (_selected != null) _selected.PropertyChanged -= Selected_PropertyChanged;
        _selected = sc;
        if (sc != null) sc.PropertyChanged += Selected_PropertyChanged;

        Inspector.DataContext = sc;
        Inspector.Visibility = sc != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateWheelInspector();
        if (sc == null) return;

        ScopeSection.Visibility = InFolder ? Visibility.Collapsed : Visibility.Visible;
        _settingKind = true;
        KindAction.IsChecked = !sc.IsFolder;
        KindFolder.IsChecked = sc.IsFolder;
        _settingKind = false;
        UpdateKindSections();
        UpdateIconPreview();
        UpdateScopeSummary();
        StepsList.SelectedIndex = sc.Actions.Count > 0 ? 0 : -1;
        UpdateStepEditor();
        TestStatus.Text = "Passe sur l'app visée pendant le décompte.";
    }

    private void Selected_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Shortcut.Icon) or nameof(Shortcut.IconColor) or nameof(Shortcut.Color)) UpdateIconPreview();
        RequestRedraw();
    }

    private void UpdateIconPreview()
    {
        if (_selected == null) return;
        var bg = IconFactory.ParseColor(_selected.Color) ?? Palette.Raised;
        var fg = IconFactory.ParseColor(_selected.IconColor) ?? (IconFactory.Luminance(bg) > 0.62 ? Palette.Surface : Palette.Text);
        IconPreview.Content = new Border
        {
            Width = 62, Height = 62, CornerRadius = new CornerRadius(6), Background = IconFactory.Brush(bg),
            Child = IconFactory.Create(_selected.Icon, 30, IconFactory.Brush(fg))
        };
    }

    private void PickIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var dlg = new IconPickerWindow(_selected.Icon) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result != null) _selected.Icon = dlg.Result;
    }

    private void BuildSwatches()
    {
        foreach (var hex in Palette.Swatches)
        {
            var b = new Button { Style = (Style)FindResource("Swatch"), Background = IconFactory.Brush(IconFactory.ParseColor(hex)!.Value), ToolTip = hex, Tag = hex };
            b.Click += (_, _) => { if (_selected != null) _selected.Color = (string)b.Tag; };
            SwatchPanel.Children.Add(b);
        }

        var iconColors = new (string? hex, string tip)[]
        {
            (null, "Automatique"), ("#E4E6EB", "Clair"), ("#16181D", "Sombre"), ("#C9A86A", "Laiton"),
            ("#7FB3E6", "Bleu"), ("#8FCB9B", "Vert"), ("#E08A7A", "Rouge"), ("#C3A6E8", "Violet"),
        };
        foreach (var (hex, tip) in iconColors)
        {
            var b = new Button { Style = (Style)FindResource("Swatch"), ToolTip = tip, Tag = hex };
            if (hex == null)
            {
                b.Width = double.NaN;
                b.Padding = new Thickness(8, 0, 8, 0);
                b.Content = "Auto";
                b.FontSize = 11.5;
            }
            else b.Background = IconFactory.Brush(IconFactory.ParseColor(hex)!.Value);
            b.Click += (_, _) => { if (_selected != null) _selected.IconColor = (string?)b.Tag; };
            IconColorPanel.Children.Add(b);
        }
    }

    private void DefaultColor_Click(object sender, RoutedEventArgs e)
    {
        if (_selected != null) _selected.Color = null;
    }

    private void Color_LostFocus(object sender, RoutedEventArgs e)
    {
        // Couleur invalide → on revient à la couleur par défaut plutôt que de garder une valeur cassée
        if (_selected != null && _selected.Color != null && IconFactory.ParseColor(_selected.Color) == null)
            _selected.Color = null;
    }

    // ---------------- Portée ----------------

    private void UpdateScopeSummary()
    {
        if (_selected == null) return;
        if (_selected.Scope == ShortcutScope.Global)
        {
            ScopeSummary.Text = "Toutes les roues (global)";
            return;
        }
        var names = _selected.AppIds.Select(id => _cfg.Apps.FirstOrDefault(a => a.Id == id)?.Name).Where(n => n != null).ToList();
        ScopeSummary.Text = names.Count == 1 ? $"Seulement {names[0]}" : string.Join(", ", names);
    }

    private void ChangeScope_Click(object sender, RoutedEventArgs e)
    {
        var sc = _selected;
        if (sc == null || InFolder) return;
        var dlg = new ScopeDialog(_cfg.Apps, Ctx.App, $"Où doit apparaître « {sc.Label} » ?", "Appliquer", sc.Scope, sc.AppIds) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        sc.Scope = dlg.Scope;
        sc.AppIds = new ObservableCollection<string>(dlg.AppIds);
        WheelLogic.Sync(_cfg);
        if (!Ctx.Layout.AllIds().Contains(sc.Id))
        {
            CanvasHint.Text = $"« {sc.Label} » n'apparaît plus dans cette roue.";
            Select(null);
        }
        else UpdateScopeSummary();
        RebuildRings();
        DrawWheel();
        RefreshWheelListLabels();
    }

    // ---------------- Type (action / dossier) ----------------

    private bool _settingKind;

    private void Kind_Checked(object sender, RoutedEventArgs e)
    {
        if (_settingKind || _selected == null) return;
        var kind = KindFolder.IsChecked == true ? ShortcutKind.Folder : ShortcutKind.Action;
        if (kind == _selected.Kind) return;
        if (kind == ShortcutKind.Action && _selected.Folder.Items.Count > 0 &&
            !Dialog.Confirm(this, "Transformer le dossier en action ?",
                $"Il contient {_selected.Folder.Items.Count} raccourci(s). Ils seront gardés mais invisibles tant que la case lance des actions.", "Transformer", DialogKind.Warning))
        {
            _settingKind = true; KindFolder.IsChecked = true; _settingKind = false;
            return;
        }
        _selected.Kind = kind;
        if (kind == ShortcutKind.Action && _selected.Actions.Count == 0) _selected.Actions.Add(new ActionStep());
        WheelLogic.Sync(_cfg);
        UpdateKindSections();
        DrawWheel();
    }

    private void UpdateKindSections()
    {
        bool folder = _selected?.IsFolder == true;
        FolderSection.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
        ActionsSection.Visibility = folder ? Visibility.Collapsed : Visibility.Visible;
        TestButton.IsEnabled = !folder;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.IsFolder == true) EnterFolder(_selected);
    }

    // =====================================================================
    //  Réglages généraux, enregistrement
    // =====================================================================

    private bool _loadingGeneral;
    private bool _closeWithoutAsking;

    private void LoadGeneral()
    {
        _loadingGeneral = true;
        HoldSlider.Value = _cfg.HoldThresholdMs;
        FolderSlider.Value = _cfg.FolderHoverMs;
        CenterSlider.Value = _cfg.CenterRadius;
        try { StartupCheck.IsChecked = ConfigService.StartWithWindows; } catch { }
        UpdateTriggerBox();
        _loadingGeneral = false;
        UpdateSliderLabels();
    }

    private void Sliders_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsInitialized || HoldLabel == null || _loadingGeneral || _cfg == null) return;
        _cfg.HoldThresholdMs = (int)HoldSlider.Value;
        _cfg.FolderHoverMs = (int)FolderSlider.Value;
        if (Math.Abs(_cfg.CenterRadius - CenterSlider.Value) > 0.1)
        {
            _cfg.CenterRadius = CenterSlider.Value;
            RequestRedraw();
        }
        UpdateSliderLabels();
    }

    private void UpdateSliderLabels()
    {
        HoldLabel.Text = $"Maintien avant exécution : {(int)HoldSlider.Value} ms";
        FolderLabel.Text = $"Ouverture des dossiers : {(int)FolderSlider.Value} ms";
        CenterLabel.Text = $"Taille du centre : {(int)CenterSlider.Value} px";
    }

    private void AppField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_path.Count > 0 && _path[0].App != null)
        {
            BuildBreadcrumb();
            RequestRedraw();
        }
    }

    /// <summary>Vérifie toutes les étapes « Touches » (y compris dans les dossiers).</summary>
    private static IEnumerable<string> Validate(IEnumerable<Shortcut> list, string prefix)
    {
        foreach (var s in list)
        {
            var name = prefix + s.Label;
            if (s.Kind == ShortcutKind.Action && s.Actions.Any(a => a.Type == ActionType.Click && !a.TryGetPoint(out _, out _)))
                yield return $"{name} : position de clic invalide (format x,y)";
            foreach (var step in s.Actions.Where(a => a.Type == ActionType.Keys))
            {
                string? err = null;
                try { if (KeyParser.Parse(step.Value).Count == 0 && s.Kind == ShortcutKind.Action) err = "aucune touche définie"; }
                catch (FormatException ex) { err = ex.Message; }
                if (err != null) yield return $"{name} : {err}";
            }
            if (s.Kind == ShortcutKind.Folder)
                foreach (var e in Validate(s.Folder.Items, name + " › ")) yield return e;
        }
    }

    private bool Commit()
    {
        var errors = WheelLogic.ProcessClashes(_cfg).Concat(Validate(_cfg.Shortcuts, "")).ToList();
        if (errors.Count > 0)
        {
            Dialog.Alert(this, DialogKind.Warning, "Impossible d'enregistrer",
                string.Join("\n", errors.Take(12).Select(x => "• " + x)));
            return false;
        }
        if (_trigger.VirtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 && !(_trigger.Ctrl || _trigger.Alt || _trigger.Win) &&
            !Dialog.Confirm(this, "Touche d'ouverture inhabituelle",
                "C'est une lettre ou un chiffre sans modificateur : tu ne pourras plus la taper normalement.", "Garder quand même", DialogKind.Warning))
            return false;

        _cfg.Trigger = CopyTrigger(_trigger);
        try { ConfigService.StartWithWindows = StartupCheck.IsChecked == true; } catch { /* registre inaccessible */ }
        WheelLogic.Sync(_cfg);
        try { App.Current.ApplyConfig(ConfigStore.DeepClone(_cfg)); }
        catch (Exception ex)
        {
            Dialog.Alert(this, DialogKind.Error, "Enregistrement impossible", ex.Message);
            return false;
        }
        _savedJson = ConfigStore.Serialize(_cfg);
        CanvasHint.Text = "Enregistré. Les changements sont actifs.";
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Commit();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _closeWithoutAsking = true;
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeWithoutAsking) return;
        _cfg.Trigger = CopyTrigger(_trigger);
        if (ConfigStore.Serialize(_cfg) == _savedJson) return;
        int answer = Dialog.Show(this, DialogKind.Question, "Enregistrer avant de fermer ?",
            "Tu as des modifications non enregistrées.",
            new DialogButton("Annuler", Cancel: true),
            new DialogButton("Ne pas enregistrer"),
            new DialogButton("Enregistrer", Primary: true));
        if (answer <= 0) e.Cancel = true;
        else if (answer == 2 && !Commit()) e.Cancel = true;
    }
}
