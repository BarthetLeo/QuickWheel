using System.Collections.ObjectModel;

namespace QuickWheel.Models;

/// <summary>Une roue éditable : sa disposition et l'endroit où chercher ses raccourcis.</summary>
public sealed class WheelContext
{
    public required string Title { get; init; }
    public required WheelLayout Layout { get; init; }
    /// <summary>App de la roue (null = roue globale ou dossier).</summary>
    public AppTarget? App { get; init; }
    /// <summary>Dossier dont c'est la sous-roue (null = roue de premier niveau).</summary>
    public Shortcut? Folder { get; init; }
    public required Func<string, Shortcut?> Resolve { get; init; }
}

/// <summary>Géométrie d'un anneau calculée à partir de la disposition.</summary>
public readonly record struct RingGeometry(int Index, double Inner, double Outer, int Count)
{
    public double Step => Count == 0 ? 360 : 360.0 / Count;
    /// <summary>Angle du milieu de la case i, en degrés, 0 = haut, sens horaire.</summary>
    public double SlotCenter(int i) => i * Step;
    public double SlotStart(int i) => SlotCenter(i) - Step / 2;
}

public readonly record struct DropTarget(int Ring, int Index, bool NewRing);

public static class WheelLogic
{
    public const double DefaultThickness = 70;

    // ---------------- Géométrie ----------------

    public static List<RingGeometry> Geometry(WheelLayout layout, double centerRadius, double gap)
    {
        var list = new List<RingGeometry>();
        double r = centerRadius + gap;
        for (int i = 0; i < layout.Rings.Count; i++)
        {
            var ring = layout.Rings[i];
            list.Add(new RingGeometry(i, r, r + ring.Thickness, ring.Items.Count));
            r += ring.Thickness + gap;
        }
        return list;
    }

    public static double OuterRadius(WheelLayout layout, double centerRadius, double gap) =>
        Geometry(layout, centerRadius, gap).Select(g => g.Outer).DefaultIfEmpty(centerRadius).Max();

    /// <summary>Angle en degrés (0 = haut, sens horaire, 0..360) d'un vecteur écran (y vers le bas).</summary>
    public static double AngleOf(double dx, double dy)
    {
        double a = Math.Atan2(dx, -dy) * 180 / Math.PI;
        return a < 0 ? a + 360 : a;
    }

    /// <summary>Case sous le point (survol exact). Null si centre, espace entre anneaux ou extérieur.</summary>
    public static (int ring, int index)? HitTest(WheelLayout layout, double centerRadius, double gap, double dx, double dy)
    {
        double dist = Math.Sqrt(dx * dx + dy * dy);
        foreach (var g in Geometry(layout, centerRadius, gap))
        {
            if (dist < g.Inner || dist > g.Outer || g.Count == 0) continue;
            return (g.Index, SlotAt(AngleOf(dx, dy), g.Count));
        }
        return null;
    }

    public static int SlotAt(double angle, int count)
    {
        if (count <= 0) return 0;
        double step = 360.0 / count;
        double a = (angle + step / 2) % 360;
        return Math.Min(count - 1, (int)(a / step));
    }

    /// <summary>
    /// Où déposer un raccourci glissé : anneau sous la souris (ou le plus proche), ou un nouvel anneau
    /// si on vise au-delà de l'anneau extérieur. <paramref name="draggedId"/> est exclu du calcul de position.
    /// </summary>
    public static DropTarget? DropAt(WheelLayout layout, double centerRadius, double gap, double dx, double dy, string? draggedId)
    {
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < centerRadius) return null;
        var geo = Geometry(layout, centerRadius, gap);
        double angle = AngleOf(dx, dy);

        double outer = geo.Count == 0 ? centerRadius : geo[^1].Outer;
        if (geo.Count == 0 || dist > outer + gap)
        {
            // Nouvel anneau extérieur — sauf si le raccourci glissé est déjà seul sur l'anneau extérieur
            if (geo.Count > 0 && draggedId != null && layout.Rings[^1].Items.Count == 1 && layout.Rings[^1].Items[0] == draggedId)
                return new DropTarget(geo.Count - 1, 0, false);
            return new DropTarget(geo.Count, 0, true);
        }

        // Anneau sous la souris ou le plus proche (pour les espaces entre anneaux)
        var g = geo.OrderBy(r => dist < r.Inner ? r.Inner - dist : dist > r.Outer ? dist - r.Outer : 0).First();
        var items = layout.Rings[g.Index].Items.Where(i => i != draggedId).ToList();
        int slots = items.Count + 1;
        return new DropTarget(g.Index, SlotAt(angle, slots), false);
    }

    // ---------------- Modifications ----------------

    /// <summary>Déplace (ou insère) un raccourci. Les anneaux vides sont supprimés.</summary>
    public static void Move(WheelLayout layout, string id, DropTarget target)
    {
        int ringIdx = target.Ring;
        int sourceRing = -1;
        for (int r = 0; r < layout.Rings.Count; r++)
            if (layout.Rings[r].Items.Contains(id)) { sourceRing = r; break; }

        if (target.NewRing || ringIdx >= layout.Rings.Count)
        {
            double t = layout.Rings.Count > 0 ? layout.Rings[^1].Thickness : DefaultThickness;
            layout.Rings.Add(new Ring { Thickness = t });
            ringIdx = layout.Rings.Count - 1;
        }

        var ring = layout.Rings[ringIdx];
        if (sourceRing >= 0) layout.Rings[sourceRing].Items.Remove(id);
        ring.Items.Insert(Math.Clamp(target.Index, 0, ring.Items.Count), id);
        RemoveEmptyRings(layout);
    }

    public static void RemoveEmptyRings(WheelLayout layout)
    {
        for (int i = layout.Rings.Count - 1; i >= 0; i--)
            if (layout.Rings[i].Items.Count == 0) layout.Rings.RemoveAt(i);
    }

    /// <summary>Ajoute à la fin de l'anneau extérieur (le crée s'il n'existe pas).</summary>
    public static void Append(WheelLayout layout, string id)
    {
        if (layout.AllIds().Contains(id)) return;
        if (layout.Rings.Count == 0) layout.Rings.Add(new Ring { Thickness = DefaultThickness });
        layout.Rings[^1].Items.Add(id);
    }

    // ---------------- Cohérence ----------------

    public static bool BelongsTo(Shortcut s, AppTarget? app) =>
        s.Scope == ShortcutScope.Global || (app != null && s.AppIds.Contains(app.Id));

    /// <summary>
    /// Garantit que chaque roue contient exactement les raccourcis de sa portée :
    /// ajoute les manquants sur l'anneau extérieur, retire ceux qui n'y ont plus leur place.
    /// </summary>
    public static void Sync(AppConfig cfg)
    {
        var appIds = cfg.Apps.Select(a => a.Id).ToHashSet();
        foreach (var s in cfg.Shortcuts)
        {
            foreach (var dead in s.AppIds.Where(id => !appIds.Contains(id)).ToList()) s.AppIds.Remove(dead);
            if (s.Scope == ShortcutScope.Apps && s.AppIds.Count == 0) s.Scope = ShortcutScope.Global;
            s.RefreshScopeText();
        }

        SyncLayout(cfg.GlobalLayout, cfg.Shortcuts.Where(s => s.Scope == ShortcutScope.Global));
        foreach (var app in cfg.Apps)
            SyncLayout(app.Layout, cfg.Shortcuts.Where(s => BelongsTo(s, app)));
        foreach (var s in cfg.Shortcuts) SyncFolder(s);
    }

    private static void SyncFolder(Shortcut s)
    {
        if (s.Kind != ShortcutKind.Folder) return;
        SyncLayout(s.Folder.Layout, s.Folder.Items);
        foreach (var c in s.Folder.Items) SyncFolder(c);
    }

    private static void SyncLayout(WheelLayout layout, IEnumerable<Shortcut> expected)
    {
        var want = expected.Select(s => s.Id).ToList();
        var set = want.ToHashSet();
        var seen = new HashSet<string>();
        foreach (var ring in layout.Rings)
            for (int i = ring.Items.Count - 1; i >= 0; i--)
                if (!set.Contains(ring.Items[i]) || !seen.Add(ring.Items[i])) ring.Items.RemoveAt(i);
        RemoveEmptyRings(layout);
        foreach (var id in want) Append(layout, id);
    }


    // ---------------- Contextes ----------------

    public static WheelContext GlobalContext(AppConfig cfg) => new()
    {
        Title = "Global", Layout = cfg.GlobalLayout, Resolve = id => cfg.Shortcuts.FirstOrDefault(s => s.Id == id)
    };

    public static WheelContext AppContext(AppConfig cfg, AppTarget app) => new()
    {
        Title = app.Name, Layout = app.Layout, App = app, Resolve = id => cfg.Shortcuts.FirstOrDefault(s => s.Id == id)
    };

    public static WheelContext FolderContext(Shortcut folder, AppTarget? app) => new()
    {
        Title = folder.Label, Layout = folder.Folder.Layout, App = app, Folder = folder,
        Resolve = id => folder.Folder.Items.FirstOrDefault(s => s.Id == id)
    };

    /// <summary>Roue qui possède déjà ce processus (hors <paramref name="except"/>), ou null. Un processus n'a qu'une roue.</summary>
    public static AppTarget? OwnerOf(AppConfig cfg, string process, AppTarget? except = null) =>
        cfg.Apps.FirstOrDefault(a => a != except && a.Matches(process));

    /// <summary>Processus présents dans plusieurs roues, sous forme de messages lisibles.</summary>
    public static IEnumerable<string> ProcessClashes(AppConfig cfg)
    {
        var seen = new Dictionary<string, AppTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in cfg.Apps)
            foreach (var p in app.ProcessList().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (seen.TryGetValue(p, out var other)) yield return $"« {p} » est dans deux roues : « {other.Name} » et « {app.Name} »";
                else seen[p] = app;
            }
    }

    /// <summary>Roue à afficher pour un processus au premier plan.</summary>
    public static WheelContext ForProcess(AppConfig cfg, string processName)
    {
        var app = cfg.Apps.FirstOrDefault(a => a.Matches(processName));
        return app != null ? AppContext(cfg, app) : GlobalContext(cfg);
    }

    // ---------------- Valeurs par défaut ----------------

    public static Shortcut Make(string label, string icon, ActionType type, string value, string? color = null)
    {
        var s = new Shortcut { Label = label, Icon = icon, Color = color };
        s.Actions.Add(new ActionStep { Type = type, Value = value });
        return s;
    }

    /// <summary>Configuration du premier lancement : seulement la roue globale et ses raccourcis de base.</summary>
    public static AppConfig CreateDefault()
    {
        var c = new AppConfig();
        c.Shortcuts.Add(Make("Copier", "mdi:content-copy", ActionType.Keys, "Ctrl+C"));
        c.Shortcuts.Add(Make("Coller", "mdi:content-paste", ActionType.Keys, "Ctrl+V"));
        c.Shortcuts.Add(Make("Annuler", "mdi:undo", ActionType.Keys, "Ctrl+Z"));

        var capture = Make("Capture", "mdi:monitor-screenshot", ActionType.Keys, "Win+Shift+S");
        var explorer = Make("Explorateur", "mdi:folder-outline", ActionType.Launch, "explorer.exe");
        var desktop = Make("Bureau", "mdi:monitor", ActionType.Keys, "Win+D");

        var tools = new Shortcut { Label = "Outils", Icon = "mdi:toolbox-outline", Kind = ShortcutKind.Folder };
        tools.Folder.Items.Add(Make("Calculatrice", "mdi:calculator", ActionType.Launch, "calc.exe"));
        tools.Folder.Items.Add(Make("Gestionnaire", "mdi:chart-box-outline", ActionType.Keys, "Ctrl+Shift+Escape"));
        tools.Folder.Items.Add(Make("Paramètres", "mdi:cog-outline", ActionType.Launch, "ms-settings:"));
        var date = new Shortcut { Label = "Date du jour", Icon = "mdi:calendar-today" };
        date.Actions.Add(new ActionStep { Type = ActionType.Command, Value = "Set-Clipboard (Get-Date -Format 'dd/MM/yyyy')" });
        date.Actions.Add(new ActionStep { Type = ActionType.Wait, Value = "150" });
        date.Actions.Add(new ActionStep { Type = ActionType.Keys, Value = "Ctrl+V" });
        tools.Folder.Items.Add(date);

        foreach (var s in new[] { capture, explorer, desktop, tools }) c.Shortcuts.Add(s);

        Sync(c);

        // Disposition de départ : les essentiels collés au centre, le reste autour.
        // Aucune roue d'app : chacun ajoute les siennes.
        var ordered = c.GlobalLayout.AllIds().ToList();
        c.GlobalLayout.Rings.Clear();
        c.GlobalLayout.Rings.Add(new Ring { Thickness = 64, Items = new ObservableCollection<string>(ordered.Take(3)) });
        if (ordered.Count > 3) c.GlobalLayout.Rings.Add(new Ring { Thickness = 72, Items = new ObservableCollection<string>(ordered.Skip(3)) });
        return c;
    }
}
