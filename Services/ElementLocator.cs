using System.Diagnostics;
using Point = System.Windows.Point;
using Condition = System.Windows.Automation.Condition;
using System.Windows.Automation;
using QuickWheel.Models;

namespace QuickWheel.Services;

/// <summary>
/// Reconnaît l'élément d'interface sous un clic (UI Automation, l'API d'accessibilité de Windows)
/// et le retrouve plus tard, où qu'il soit à l'écran. À appeler hors du thread UI.
/// </summary>
public static class ElementLocator
{
    // Types de contrôle sur lesquels on clique « pour de vrai » : on remonte jusqu'à l'un d'eux
    private static readonly HashSet<int> Actionable = new[]
    {
        ControlType.Button, ControlType.SplitButton, ControlType.MenuItem, ControlType.TabItem, ControlType.ListItem,
        ControlType.TreeItem, ControlType.Hyperlink, ControlType.CheckBox, ControlType.RadioButton, ControlType.ComboBox,
        ControlType.Edit, ControlType.Document, ControlType.DataItem, ControlType.HeaderItem, ControlType.Slider,
    }.Select(c => c.Id).ToHashSet();

    private static readonly int OwnProcessId = Environment.ProcessId;

    // ---------------- Enregistrement ----------------

    /// <summary>Identité de l'élément au point (x, y) en pixels écran, ou null s'il n'est pas reconnaissable.</summary>
    public static ElementTarget? Capture(int x, int y)
    {
        try
        {
            var hit = AutomationElement.FromPoint(new Point(x, y));
            if (hit == null || hit.Current.ProcessId == OwnProcessId) return null;

            var el = PickMeaningful(hit);
            if (el == null) return null;
            var rect = el.Current.BoundingRectangle;
            if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1) return null;
            return Describe(el, Math.Clamp((x - rect.X) / rect.Width, 0, 1), Math.Clamp((y - rect.Y) / rect.Height, 0, 1));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    /// <summary>Identité de l'élément qui a le focus clavier (là où va le texte tapé), ou null.</summary>
    public static ElementTarget? CaptureFocused()
    {
        try
        {
            var el = AutomationElement.FocusedElement;
            if (el == null || el.Current.ProcessId == OwnProcessId) return null;
            var c = el.Current;
            if (string.IsNullOrWhiteSpace(c.Name) && string.IsNullOrWhiteSpace(c.AutomationId)) return null;
            return Describe(el, 0.5, 0.5);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    private static ElementTarget? Describe(AutomationElement el, double offsetX, double offsetY)
    {
        {
            var c = el.Current;
            string process;
            try { using var p = Process.GetProcessById(c.ProcessId); process = p.ProcessName; } catch { return null; }

            var target = new ElementTarget
            {
                Process = process,
                ControlTypeId = c.ControlType.Id,
                ControlTypeName = c.LocalizedControlType ?? "",
                Name = c.Name?.Trim() ?? "",
                AutomationId = c.AutomationId ?? "",
                OffsetX = offsetX,
                OffsetY = offsetY,
            };

            // Plusieurs éléments identiques dans la fenêtre (ex. plusieurs « OK ») : on retient le rang
            var window = TopWindow(el);
            if (window != null)
            {
                var all = window.FindAll(TreeScope.Descendants, MakeCondition(target));
                if (all.Count > 1)
                {
                    var id = el.GetRuntimeId();
                    for (int i = 0; i < all.Count; i++)
                        if (Automation.Compare(all[i].GetRuntimeId(), id)) { target.Index = i; break; }
                }
            }
            return target;
        }
    }

    /// <summary>L'élément lui-même, ou l'ancêtre proche qui est un vrai bouton / champ / onglet… et qui a un nom ou un identifiant.</summary>
    private static AutomationElement? PickMeaningful(AutomationElement hit)
    {
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? el = hit, fallback = null;
        for (int depth = 0; el != null && depth < 5; depth++)
        {
            var c = el.Current;
            if (c.ControlType == ControlType.Window || c.ControlType == ControlType.Pane && depth > 0) break;
            bool named = !string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.AutomationId);
            if (named && Actionable.Contains(c.ControlType.Id)) return el;
            if (named && fallback == null && depth == 0 && c.ControlType != ControlType.Pane && c.ControlType != ControlType.Custom)
                fallback = el;
            el = walker.GetParent(el);
        }
        return fallback;
    }

    private static AutomationElement? TopWindow(AutomationElement el)
    {
        var walker = TreeWalker.ControlViewWalker;
        var root = AutomationElement.RootElement;
        AutomationElement? cur = el, last = null;
        for (int i = 0; cur != null && i < 60; i++)
        {
            if (Automation.Compare(cur, root)) return last;
            last = cur;
            cur = walker.GetParent(cur);
        }
        return last;
    }

    private static Condition MakeCondition(ElementTarget t)
    {
        var parts = new List<Condition> { new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.LookupById(t.ControlTypeId)) };
        if (!string.IsNullOrEmpty(t.Name)) parts.Add(new PropertyCondition(AutomationElement.NameProperty, t.Name));
        if (!string.IsNullOrEmpty(t.AutomationId)) parts.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, t.AutomationId));
        return parts.Count == 1 ? parts[0] : new AndCondition(parts.ToArray());
    }

    // ---------------- Lecture ----------------

    /// <summary>
    /// Point écran où cliquer pour atteindre l'élément, ou null s'il reste introuvable après <paramref name="timeoutMs"/>.
    /// Réessaie tant que l'élément n'est pas apparu (fenêtre ou menu ouvert par l'étape précédente).
    /// </summary>
    public static (int x, int y)? Locate(ElementTarget target, int hintX, int hintY, int timeoutMs)
    {
        if (target.ControlTypeId == 0 || ControlType.LookupById(target.ControlTypeId) == null) return null;
        var sw = Stopwatch.StartNew();
        do
        {
            try
            {
                var el = FindAtHint(target, hintX, hintY) ?? FindInWindows(target);
                if (el != null)
                {
                    var r = el.Current.BoundingRectangle;
                    if (!r.IsEmpty && r.Width >= 1 && r.Height >= 1 && !el.Current.IsOffscreen)
                        return ((int)Math.Round(r.X + r.Width * target.OffsetX), (int)Math.Round(r.Y + r.Height * target.OffsetY));
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            if (sw.ElapsedMilliseconds + 60 >= timeoutMs) break;
            Thread.Sleep(40);
        } while (sw.ElapsedMilliseconds < timeoutMs);
        return null;
    }

    /// <summary>
    /// Donne le focus clavier à l'élément enregistré avant de taper (texte, touches).
    /// Renvoie false s'il reste introuvable : on tape alors là où est le focus, comme avant.
    /// </summary>
    public static bool Focus(ElementTarget target, int timeoutMs)
    {
        if (target.ControlTypeId == 0 || ControlType.LookupById(target.ControlTypeId) == null) return false;
        try
        {
            // Cas courant : l'étape précédente (un clic) a déjà mis le focus au bon endroit
            var focused = AutomationElement.FocusedElement;
            if (focused != null && Matches(focused, target)) return true;
        }
        catch { }

        var sw = Stopwatch.StartNew();
        do
        {
            try
            {
                var el = FindInWindows(target);
                if (el != null)
                {
                    el.SetFocus();
                    return true;
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex); }
            if (sw.ElapsedMilliseconds + 60 >= timeoutMs) break;
            Thread.Sleep(40);
        } while (sw.ElapsedMilliseconds < timeoutMs);
        return false;
    }

    private static bool Matches(AutomationElement el, ElementTarget t)
    {
        var c = el.Current;
        return c.ControlType.Id == t.ControlTypeId
               && (string.IsNullOrEmpty(t.Name) || (c.Name?.Trim() ?? "") == t.Name)
               && (string.IsNullOrEmpty(t.AutomationId) || c.AutomationId == t.AutomationId);
    }

    /// <summary>Raccourci rapide : l'élément est toujours à la position enregistrée (même écran, fenêtre pas bougée).</summary>
    private static AutomationElement? FindAtHint(ElementTarget t, int x, int y)
    {
        if (t.Index != 0) return null; // éléments en double : on passe par la recherche complète pour avoir le bon rang
        var el = AutomationElement.FromPoint(new Point(x, y));
        var walker = TreeWalker.ControlViewWalker;
        for (int depth = 0; el != null && depth < 5; depth++)
        {
            if (Matches(el, t)) return el;
            el = walker.GetParent(el);
        }
        return null;
    }

    /// <summary>
    /// Cherche dans les fenêtres de l'app, celle au premier plan d'abord. Les applis modernes (Calculatrice, Paramètres…)
    /// sont hébergées dans une fenêtre d'ApplicationFrameHost : on y cherche aussi, en filtrant sur le processus de l'app.
    /// </summary>
    private static HashSet<int> PidsOf(string processName)
    {
        var ids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(processName)) using (p) ids.Add(p.Id);
        return ids;
    }

    private static AutomationElement? FindInWindows(ElementTarget t)
    {
        var pids = PidsOf(t.Process);
        if (pids.Count == 0) return null;
        var hosts = PidsOf(ForegroundInfo.FrameHost);

        var windows = new List<AutomationElement>();
        void Add(AutomationElement w) { if (!windows.Any(x => Automation.Compare(x, w))) windows.Add(w); }

        var fg = NativeMethods.GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            NativeMethods.GetWindowThreadProcessId(fg, out var fgPid);
            if (pids.Contains((int)fgPid) || hosts.Contains((int)fgPid))
                try { Add(AutomationElement.FromHandle(fg)); } catch { }
        }
        foreach (int pid in pids.Concat(hosts))
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children,
                         new PropertyCondition(AutomationElement.ProcessIdProperty, pid)))
                Add(w);

        // L'élément doit appartenir au processus de l'app (évite un « OK » d'une autre fenêtre)
        var procCond = pids.Count == 1
            ? (Condition)new PropertyCondition(AutomationElement.ProcessIdProperty, pids.First())
            : new OrCondition(pids.Select(id => (Condition)new PropertyCondition(AutomationElement.ProcessIdProperty, id)).ToArray());
        var cond = new AndCondition(MakeCondition(t), procCond);

        foreach (var w in windows)
        {
            if (t.Index == 0)
            {
                var first = w.FindFirst(TreeScope.Descendants, cond);
                if (first != null) return first;
            }
            else
            {
                var all = w.FindAll(TreeScope.Descendants, cond);
                if (all.Count > t.Index) return all[t.Index];
            }
        }
        return null;
    }
}
