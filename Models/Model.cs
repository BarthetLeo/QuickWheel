using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace QuickWheel.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ======================= Actions =======================

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Keys,      // combo / séquence : "Ctrl+Shift+S" ou "Ctrl+K, Ctrl+C"
    Launch,    // programme, fichier, dossier ou URL
    Text,      // texte à taper
    Command,   // commande PowerShell
    Wait,      // pause (Value = millisecondes)
    Click      // clic souris à une position écran (Value = "x,y" en pixels physiques, Arguments = bouton)
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ClickButton { Left, Right, Middle, Double }

/// <summary>
/// Identité d'un élément d'interface cliqué (bouton, champ, onglet…), reconnu via l'accessibilité Windows.
/// Permet de retrouver le même élément même si la fenêtre a bougé ou si l'écran est différent.
/// </summary>
public class ElementTarget
{
    /// <summary>Nom du processus de l'app (sans .exe).</summary>
    public string Process { get; set; } = "";
    /// <summary>Identifiant UI Automation du type de contrôle (bouton, champ…).</summary>
    public int ControlTypeId { get; set; }
    /// <summary>Type de contrôle lisible, pour l'affichage (« bouton », « onglet »…).</summary>
    public string ControlTypeName { get; set; } = "";
    public string Name { get; set; } = "";
    public string AutomationId { get; set; } = "";
    /// <summary>Rang parmi les éléments identiques de la fenêtre (0 = premier).</summary>
    public int Index { get; set; }
    /// <summary>Position du clic dans l'élément, en proportion de sa taille (0..1).</summary>
    public double OffsetX { get; set; } = 0.5;
    public double OffsetY { get; set; } = 0.5;

    public ElementTarget Clone() => (ElementTarget)MemberwiseClone();

    public string Describe()
    {
        var what = string.IsNullOrWhiteSpace(ControlTypeName) ? "élément" : ControlTypeName;
        var label = !string.IsNullOrWhiteSpace(Name) ? $" « {Shorten(Name, 32)} »" : $" #{AutomationId}";
        return $"{what}{label} ({Process})";
    }

    private static string Shorten(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}

/// <summary>Une étape d'un raccourci. Un raccourci = une ou plusieurs étapes enchaînées (macro).</summary>
public class ActionStep : Observable
{
    private ActionType _type = ActionType.Keys;
    private string _value = "";
    private string _arguments = "";
    private bool _showWindow;

    public ActionType Type { get => _type; set { if (Set(ref _type, value)) { Notify(nameof(Summary)); Notify(nameof(ShowTargetInfo)); Notify(nameof(TargetText)); } } }
    public string Value { get => _value; set { if (Set(ref _value, value)) Notify(nameof(Summary)); } }
    public string Arguments { get => _arguments; set => Set(ref _arguments, value); }
    public bool ShowWindow { get => _showWindow; set => Set(ref _showWindow, value); }

    [JsonIgnore]
    public string Summary => Type switch
    {
        ActionType.Keys => (string.IsNullOrWhiteSpace(Value) ? "Touches (vide)" : Value) + InTarget,
        ActionType.Launch => string.IsNullOrWhiteSpace(Value) ? "Lancer (vide)" : "Ouvrir " + Value,
        ActionType.Text => "Taper « " + Shorten(Value.Replace("\n", " ⏎ "), 40) + " »" + InTarget,
        ActionType.Command => "PowerShell : " + Shorten(Value, 40),
        ActionType.Wait => $"Pause {Value} ms",
        ActionType.Click => Target != null ? $"{ButtonText} sur {Target.Describe()}"
                          : TryGetPoint(out var x, out var y) ? $"{ButtonText} en ({x}, {y})" : "Clic (position à choisir)",
        _ => Value
    };

    private ElementTarget? _target;

    /// <summary>Élément reconnu sous le clic (null = clic à la position <see cref="Value"/> uniquement).</summary>
    public ElementTarget? Target
    {
        get => _target;
        set { if (Set(ref _target, value)) { Notify(nameof(Summary)); Notify(nameof(TargetText)); Notify(nameof(HasTarget)); Notify(nameof(ShowTargetInfo)); } }
    }

    [JsonIgnore] public bool HasTarget => Target != null;

    private string InTarget => Target != null ? " dans " + Target.Describe() : "";

    /// <summary>Encadré « élément reconnu » : toujours pour un clic, et pour le texte / les touches quand un champ a été reconnu.</summary>
    [JsonIgnore] public bool ShowTargetInfo => Type == ActionType.Click || (Target != null && Type is ActionType.Text or ActionType.Keys);

    [JsonIgnore]
    public string TargetText => (Type, Target) switch
    {
        (ActionType.Click, null) => "Aucun élément reconnu : clic à la position exacte.",
        (ActionType.Click, _) => $"Élément reconnu : {Target!.Describe()}. S'il est introuvable, clic à la position (x,y).",
        (_, null) => "",
        _ => $"Tapé dans : {Target!.Describe()}. QuickWheel y remet le focus avant de taper ; s'il ne le trouve pas, il tape là où est le curseur.",
    };

    private ClickButton _button = ClickButton.Left;

    /// <summary>Bouton d'une étape « Clic ».</summary>
    public ClickButton Button { get => _button; set { if (Set(ref _button, value)) Notify(nameof(Summary)); } }

    [JsonIgnore]
    public string ButtonText => Button switch
    {
        ClickButton.Right => "Clic droit",
        ClickButton.Middle => "Clic milieu",
        ClickButton.Double => "Double-clic",
        _ => "Clic"
    };

    public bool TryGetPoint(out int x, out int y)
    {
        x = y = 0;
        var parts = Value.Split(',', ';');
        return parts.Length == 2 && int.TryParse(parts[0].Trim(), out x) && int.TryParse(parts[1].Trim(), out y);
    }

    private static string Shorten(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    public ActionStep Clone() => new() { Type = Type, Value = Value, Arguments = Arguments, ShowWindow = ShowWindow, Button = Button, Target = Target?.Clone() };
}

// ======================= Raccourcis =======================

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShortcutScope
{
    Global,   // dans toutes les roues
    Apps      // seulement dans les apps listées dans AppIds
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ShortcutKind
{
    Action,   // exécute ses étapes
    Folder    // ouvre une sous-roue
}

public class Shortcut : Observable
{
    private string _label = "Nouveau raccourci";
    private string _icon = "mdi:star-outline";
    private string? _color;
    private string? _iconColor;
    private ShortcutScope _scope = ShortcutScope.Global;
    private ShortcutKind _kind = ShortcutKind.Action;

    public string Id { get; set; } = NewId();

    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>
    /// Référence d'icône : "mdi:nom" (Material Design), "img:chemin" (PNG/JPG/ICO),
    /// "exe:chemin" (icône d'un programme), "txt:★" (caractère / emoji).
    /// </summary>
    public string Icon { get => _icon; set => Set(ref _icon, value); }

    /// <summary>Couleur de fond de la case (#RRGGBB), null = couleur par défaut.</summary>
    public string? Color { get => _color; set => Set(ref _color, value); }

    /// <summary>Couleur de l'icône (#RRGGBB), null = auto.</summary>
    public string? IconColor { get => _iconColor; set => Set(ref _iconColor, value); }

    public ShortcutScope Scope { get => _scope; set { if (Set(ref _scope, value)) Notify(nameof(ScopeText)); } }

    /// <summary>Apps concernées quand <see cref="Scope"/> = Apps (raccourci partagé entre ces apps).</summary>
    public ObservableCollection<string> AppIds { get; set; } = new();

    public ShortcutKind Kind { get => _kind; set { if (Set(ref _kind, value)) Notify(nameof(IsFolder)); } }

    [JsonIgnore] public bool IsFolder => Kind == ShortcutKind.Folder;

    public ObservableCollection<ActionStep> Actions { get; set; } = new();

    /// <summary>Contenu du sous-menu quand <see cref="Kind"/> = Folder.</summary>
    public FolderContent Folder { get; set; } = new();

    [JsonIgnore]
    public string ScopeText => Scope == ShortcutScope.Global ? "Global" : AppIds.Count == 1 ? "Cette app" : $"{AppIds.Count} apps";

    public void RefreshScopeText() => Notify(nameof(ScopeText));

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Copie profonde avec de nouveaux identifiants.</summary>
    public Shortcut CloneNew()
    {
        var c = new Shortcut
        {
            Label = Label, Icon = Icon, Color = Color, IconColor = IconColor,
            Scope = Scope, AppIds = new ObservableCollection<string>(AppIds), Kind = Kind,
            Actions = new ObservableCollection<ActionStep>(Actions.Select(a => a.Clone())),
        };
        var map = new Dictionary<string, string>();
        foreach (var child in Folder.Items)
        {
            var cc = child.CloneNew();
            map[child.Id] = cc.Id;
            c.Folder.Items.Add(cc);
        }
        c.Folder.Layout = Folder.Layout.CloneWith(id => map.TryGetValue(id, out var n) ? n : id);
        return c;
    }
}

public class FolderContent
{
    /// <summary>Raccourcis propres au dossier (pas de portée : ils suivent le dossier).</summary>
    public ObservableCollection<Shortcut> Items { get; set; } = new();
    public WheelLayout Layout { get; set; } = new();
}

// ======================= Disposition de la roue =======================

public class Ring : Observable
{
    private double _thickness = 70;

    /// <summary>Épaisseur de l'anneau en pixels.</summary>
    public double Thickness { get => _thickness; set => Set(ref _thickness, Math.Clamp(value, 36, 160)); }

    /// <summary>Identifiants des raccourcis, dans l'ordre horaire en partant du haut. Cases égales.</summary>
    public ObservableCollection<string> Items { get; set; } = new();
}

public class WheelLayout
{
    /// <summary>Anneaux du centre vers l'extérieur.</summary>
    public ObservableCollection<Ring> Rings { get; set; } = new();

    public IEnumerable<string> AllIds() => Rings.SelectMany(r => r.Items);

    public WheelLayout CloneWith(Func<string, string>? mapId = null)
    {
        var l = new WheelLayout();
        foreach (var r in Rings)
            l.Rings.Add(new Ring { Thickness = r.Thickness, Items = new ObservableCollection<string>(r.Items.Select(i => mapId?.Invoke(i) ?? i)) });
        return l;
    }
}

// ======================= Apps =======================

public class AppTarget : Observable
{
    private string _name = "Nouvelle app";
    private string _processNames = "";
    private string? _exePath;
    private string? _icon;

    public string Id { get; set; } = Shortcut.NewId();
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Icône choisie pour la roue (même format que <see cref="Shortcut.Icon"/>). Null = icône de l'exe.</summary>
    public string? Icon { get => _icon; set => Set(ref _icon, value); }

    /// <summary>Icône à afficher : la personnalisée, sinon celle de l'exe, sinon une icône générique.</summary>
    public string IconRef => Icon ?? (ExePath != null ? "exe:" + ExePath : "mdi:application-outline");

    /// <summary>Noms de processus séparés par des virgules, sans .exe (ex : "chrome, msedge").</summary>
    public string ProcessNames { get => _processNames; set => Set(ref _processNames, value); }

    /// <summary>Chemin de l'exe, pour afficher son icône dans la liste.</summary>
    public string? ExePath { get => _exePath; set => Set(ref _exePath, value); }

    public WheelLayout Layout { get; set; } = new();

    public IEnumerable<string> ProcessList() =>
        ProcessNames.Split(',', ';')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p);

    public bool Matches(string processName) =>
        ProcessList().Any(p => string.Equals(p, processName, StringComparison.OrdinalIgnoreCase));
}

// ======================= Configuration =======================

public class TriggerKey
{
    /// <summary>Code de touche virtuelle Windows. 0xDE = « ² » sur clavier AZERTY.</summary>
    public int VirtualKey { get; set; } = 0xDE;
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
}

public class AppConfig
{
    public int Version { get; set; } = 2;

    public TriggerKey Trigger { get; set; } = new();

    /// <summary>Maintenir la touche plus longtemps que ce délai puis relâcher = exécute l'élément survolé.</summary>
    public int HoldThresholdMs { get; set; } = 250;

    /// <summary>Temps de survol d'un dossier avant d'ouvrir sa sous-roue.</summary>
    public int FolderHoverMs { get; set; } = 350;

    /// <summary>Rayon du disque central (zone neutre / retour).</summary>
    public double CenterRadius { get; set; } = 46;

    /// <summary>Espace entre deux anneaux.</summary>
    public double RingGap { get; set; } = 4;

    /// <summary>Bibliothèque des raccourcis de premier niveau (globaux ou par apps).</summary>
    public ObservableCollection<Shortcut> Shortcuts { get; set; } = new();

    /// <summary>Roue affichée quand aucune app ne correspond (raccourcis globaux uniquement).</summary>
    public WheelLayout GlobalLayout { get; set; } = new();

    public ObservableCollection<AppTarget> Apps { get; set; } = new();
}
