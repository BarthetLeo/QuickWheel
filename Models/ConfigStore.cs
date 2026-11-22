using System.IO;
using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QuickWheel.Models;

/// <summary>Lecture / écriture JSON de la configuration, migration v1 → v2, import / export.</summary>
public static class ConfigStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // garde emojis et accents lisibles
    };

    public static string Serialize(AppConfig cfg) => JsonSerializer.Serialize(cfg, Options);

    public static AppConfig Deserialize(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new JsonException("Fichier vide");
        AppConfig cfg = node["Version"] is null && node["Profiles"] is not null
            ? MigrateV1(node)
            : node.Deserialize<AppConfig>(Options) ?? new AppConfig();
        Normalize(cfg);
        return cfg;
    }

    public static AppConfig DeepClone(AppConfig cfg) => Deserialize(Serialize(cfg));

    private static void Normalize(AppConfig cfg)
    {
        cfg.Trigger ??= new TriggerKey();
        cfg.GlobalLayout ??= new WheelLayout();
        cfg.Shortcuts ??= new();
        cfg.Apps ??= new();
        foreach (var a in cfg.Apps) a.Layout ??= new WheelLayout();
        Fix(cfg.Shortcuts);
        WheelLogic.Sync(cfg);

        static void Fix(IEnumerable<Shortcut> list)
        {
            foreach (var s in list)
            {
                s.Actions ??= new();
                s.AppIds ??= new();
                s.Folder ??= new FolderContent();
                s.Folder.Items ??= new();
                s.Folder.Layout ??= new WheelLayout();
                s.Icon ??= new Shortcut().Icon; // "Icon": null écrit à la main
                if (!s.Icon.Contains(':')) s.Icon = "txt:" + s.Icon;
                Fix(s.Folder.Items);
            }
        }
    }

    /// <summary>Convertit l'ancien format (profils avec listes d'éléments).</summary>
    private static AppConfig MigrateV1(JsonNode v1)
    {
        var cfg = new AppConfig();
        if (v1["Trigger"] is JsonNode t) cfg.Trigger = t.Deserialize<TriggerKey>(Options) ?? new TriggerKey();
        if (v1["HoldThresholdMs"]?.GetValue<int>() is int hold) cfg.HoldThresholdMs = hold;

        Shortcut Convert(JsonNode item)
        {
            var s = new Shortcut
            {
                Label = (string?)item["Label"] ?? "Raccourci",
                Icon = "txt:" + ((string?)item["Icon"] ?? "★"),
            };
            var type = Enum.TryParse<ActionType>((string?)item["Type"], out var at) ? at : ActionType.Keys;
            s.Actions.Add(new ActionStep
            {
                Type = type,
                Value = (string?)item["Value"] ?? "",
                Arguments = (string?)item["Arguments"] ?? "",
                ShowWindow = (bool?)item["ShowWindow"] ?? false
            });
            return s;
        }

        foreach (var item in v1["Global"]?["Items"]?.AsArray() ?? new JsonArray())
            if (item != null) cfg.Shortcuts.Add(Convert(item));

        foreach (var p in v1["Profiles"]?.AsArray() ?? new JsonArray())
        {
            if (p == null) continue;
            var app = new AppTarget { Name = (string?)p["Name"] ?? "App", ProcessNames = (string?)p["ProcessNames"] ?? "" };
            cfg.Apps.Add(app);
            foreach (var item in p["Items"]?.AsArray() ?? new JsonArray())
            {
                if (item == null) continue;
                var s = Convert(item);
                s.Scope = ShortcutScope.Apps;
                s.AppIds.Add(app.Id);
                cfg.Shortcuts.Add(s);
            }
        }
        return cfg;
    }

    // ---------------- Import / export d'une app ----------------

    public class AppExport
    {
        public int Format { get; set; } = 1;
        public AppTarget App { get; set; } = new();
        /// <summary>Raccourcis propres à l'app (les globaux ne sont pas exportés).</summary>
        public List<Shortcut> Shortcuts { get; set; } = new();
    }

    public static string ExportApp(AppConfig cfg, AppTarget app)
    {
        var export = new AppExport
        {
            App = app,
            Shortcuts = cfg.Shortcuts.Where(s => s.Scope == ShortcutScope.Apps && s.AppIds.Contains(app.Id)).ToList()
        };
        return JsonSerializer.Serialize(export, Options);
    }

    /// <summary>Importe une app exportée. Les identifiants sont régénérés pour éviter les collisions.</summary>
    private static bool MissingImage(string? icon) =>
        icon != null && icon.StartsWith("img:") && !File.Exists(icon[4..]);

    /// <summary>Remplace les images introuvables sur ce PC (fichier venu d'un autre PC) par l'icône par défaut.</summary>
    public static void DropMissingImages(AppConfig cfg)
    {
        string fallback = new Shortcut().Icon;
        foreach (var app in cfg.Apps)
            if (MissingImage(app.Icon)) app.Icon = null;
        void Walk(IEnumerable<Shortcut> list)
        {
            foreach (var s in list)
            {
                if (MissingImage(s.Icon)) s.Icon = fallback;
                Walk(s.Folder.Items);
            }
        }
        Walk(cfg.Shortcuts);
    }

    public static AppTarget ImportApp(AppConfig cfg, string json)
    {
        var export = JsonSerializer.Deserialize<AppExport>(json, Options) ?? throw new JsonException("Fichier invalide");
        var app = export.App;
        var clash = app.ProcessList().Select(p => (p, owner: WheelLogic.OwnerOf(cfg, p))).FirstOrDefault(x => x.owner != null);
        if (clash.owner != null)
            throw new InvalidOperationException($"« {clash.p} » a déjà sa roue : « {clash.owner.Name} ». Supprime-la ou retire ce processus avant d'importer.");
        app.Id = Shortcut.NewId();
        app.Layout ??= new WheelLayout();
        // Les images importées vivent dans le %APPDATA% de celui qui a exporté : absentes ici
        if (MissingImage(app.Icon)) app.Icon = null;

        var map = new Dictionary<string, string>();
        foreach (var s in export.Shortcuts)
        {
            var copy = s.CloneNew();
            map[s.Id] = copy.Id;
            copy.Scope = ShortcutScope.Apps;
            if (MissingImage(copy.Icon)) copy.Icon = new Shortcut().Icon;
            copy.AppIds = new ObservableCollection<string> { app.Id };
            cfg.Shortcuts.Add(copy);
        }
        // Garde la disposition exportée (les globaux absents seront ajoutés par Sync)
        app.Layout = app.Layout.CloneWith(id => map.TryGetValue(id, out var n) ? n : id);
        cfg.Apps.Add(app);
        Normalize(cfg);
        return app;
    }
}
