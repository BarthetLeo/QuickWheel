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

/// <summary>Paramètres : liste des roues, import / export, réglages de la roue affichée (nom, icône, apps).</summary>
public partial class SettingsWindow
{
    // =====================================================================
    //  Liste des roues
    // =====================================================================

    private void RebuildWheelList(AppTarget? select)
    {
        var entries = new List<WheelEntry>
        {
            new()
            {
                Name = "Global", Sub = Count(_cfg.GlobalLayout),
                IconElement = IconFactory.Create("mdi:earth", 20, Palette.BrassBrush)
            }
        };
        foreach (var app in _cfg.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            entries.Add(new WheelEntry
            {
                App = app, Name = app.Name, Sub = Count(app.Layout),
                IconElement = IconFactory.Create(WheelIcon(app), 20, Palette.TextBrush)
            });
        }
        WheelList.ItemsSource = entries;
        WheelList.SelectedItem = entries.FirstOrDefault(e => e.App == select) ?? entries[0];
    }

    /// <summary>Affiche la roue de l'app correspondant à ce processus (la roue globale si aucune ne correspond).</summary>
    public void ShowWheelFor(string processName)
    {
        var app = _cfg.Apps.FirstOrDefault(a => a.Matches(processName));
        if (WheelList.ItemsSource is not List<WheelEntry> list) return;
        var entry = list.FirstOrDefault(e => e.App == app) ?? list[0];
        if (WheelList.SelectedItem == entry && _path.Count == 1) return;
        // Force le retour au premier niveau même si cette roue est déjà sélectionnée (on était dans un dossier)
        WheelList.SelectedItem = null;
        WheelList.SelectedItem = entry;
        WheelList.ScrollIntoView(entry);
    }

    private static string Count(WheelLayout l)
    {
        int n = l.AllIds().Count(), r = l.Rings.Count;
        if (n == 0) return "Vide";
        return $"{n} raccourci{(n > 1 ? "s" : "")} sur {r} anneau{(r > 1 ? "x" : "")}";
    }

    private void RefreshWheelListLabels()
    {
        var current = (WheelList.SelectedItem as WheelEntry)?.App;
        // Reconstruit seulement si un compteur ou un nom a changé
        if (WheelList.ItemsSource is List<WheelEntry> list &&
            list.Count == _cfg.Apps.Count + 1 &&
            list.All(e => e.Sub == Count(e.App?.Layout ?? _cfg.GlobalLayout) && (e.App == null || e.Name == e.App.Name)))
            return;
        _suppressWheelSelection = true;
        RebuildWheelList(current);
        _suppressWheelSelection = false;
    }

    private bool _suppressWheelSelection;

    private void WheelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWheelSelection || WheelList.SelectedItem is not WheelEntry entry) return;
        _path.Clear();
        _path.Add(entry.App == null ? WheelLogic.GlobalContext(_cfg) : WheelLogic.AppContext(_cfg, entry.App));
        Select(null);
        OnContextChanged();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddAppDialog(_cfg.Apps) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        _cfg.Apps.Add(dlg.Result);
        WheelLogic.Sync(_cfg); // la nouvelle roue reçoit les raccourcis globaux
        RebuildWheelList(dlg.Result);
    }

    /// <summary>Ajoute le processus d'une autre app à la roue affichée.</summary>
    private void AddAppToWheel_Click(object sender, RoutedEventArgs e)
    {
        var app = _path.FirstOrDefault()?.App;
        if (app == null) return;
        var dlg = new AddAppDialog(_cfg.Apps, into: app) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        RefreshWheelListLabels();
        CheckProcessClash();
    }

    private void ProcessField_Changed(object sender, TextChangedEventArgs e) => CheckProcessClash();

    /// <summary>Signale tout de suite un processus qui a déjà sa roue (une app = une seule roue).</summary>
    private void CheckProcessClash()
    {
        var app = _path.FirstOrDefault()?.App;
        var clash = app?.ProcessList().Select(p => (p, owner: WheelLogic.OwnerOf(_cfg, p, except: app))).FirstOrDefault(x => x.owner != null);
        if (clash?.owner is { } owner)
        {
            ProcessClash.Text = $"« {clash.Value.p} » a déjà sa roue : « {owner.Name} ». Une app ne peut avoir qu'une roue.";
            ProcessClash.Visibility = Visibility.Visible;
        }
        else ProcessClash.Visibility = Visibility.Collapsed;
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        var app = _path.FirstOrDefault()?.App;
        if (app == null) return;
        int own = _cfg.Shortcuts.Count(s => s.Scope == ShortcutScope.Apps && s.AppIds.Count == 1 && s.AppIds[0] == app.Id);
        var msg = own > 0 ? $"Ses {own} raccourci(s) propres seront supprimés. Les raccourcis partagés avec d'autres apps sont gardés." : "";
        if (!Dialog.Confirm(this, $"Supprimer la roue « {app.Name} » ?", msg, "Supprimer", DialogKind.Danger)) return;

        foreach (var s in _cfg.Shortcuts.ToList())
        {
            if (s.Scope != ShortcutScope.Apps || !s.AppIds.Contains(app.Id)) continue;
            if (s.AppIds.Count == 1) _cfg.Shortcuts.Remove(s);
            else s.AppIds.Remove(app.Id);
        }
        _cfg.Apps.Remove(app);
        WheelLogic.Sync(_cfg);
        RebuildWheelList(null);
    }

    // ---------------- Import / export ----------------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var app = _path.FirstOrDefault()?.App;
        var dlg = new SaveFileDialog
        {
            Filter = "Roue QuickWheel|*.qwheel.json|JSON|*.json",
            FileName = app != null ? Safe(app.Name) + ".qwheel.json" : "QuickWheel-sauvegarde.qwheel.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        // Une app → ses raccourcis et sa disposition ; la roue globale → toute la configuration
        try { File.WriteAllText(dlg.FileName, app != null ? ConfigStore.ExportApp(_cfg, app) : ConfigStore.Serialize(_cfg)); }
        catch (Exception ex) { Dialog.Alert(this, DialogKind.Error, "Export impossible", ex.Message); return; }
        CanvasHint.Text = app != null ? $"Roue « {app.Name} » exportée." : "Configuration complète exportée.";
    }

    private static string Safe(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Roue QuickWheel|*.qwheel.json;*.json|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var text = File.ReadAllText(dlg.FileName);
            var node = JsonNode.Parse(text);
            if (node?["App"] != null)
            {
                var app = ConfigStore.ImportApp(_cfg, text);
                RebuildWheelList(app);
            }
            else
            {
                if (!Dialog.Confirm(this, "Remplacer toute la configuration ?",
                        "Ce fichier contient une configuration complète. Elle remplacera toutes tes roues actuelles.", "Remplacer", DialogKind.Warning)) return;
                _cfg = ConfigStore.Deserialize(text);
                ConfigStore.DropMissingImages(_cfg);
                _trigger = CopyTrigger(_cfg.Trigger);
                LoadGeneral();
                RebuildWheelList(null);
            }
        }
        catch (Exception ex)
        {
            Dialog.Alert(this, DialogKind.Error, "Import impossible", ex.Message);
        }
    }

    // ---------------- Réglages de la roue (panneau de droite sans case choisie) ----------------

    /// <summary>Sans case choisie : à droite, les réglages de la roue affichée (nom, icône, apps). Dans un dossier : message vide.</summary>
    private void UpdateWheelInspector()
    {
        if (_path.Count == 0) return;
        bool showWheel = _selected == null && !InFolder;
        WheelInspector.Visibility = showWheel ? Visibility.Visible : Visibility.Collapsed;
        EmptyInspector.Visibility = _selected == null && InFolder ? Visibility.Visible : Visibility.Collapsed;
        if (!showWheel) return;

        var app = _path[0].App;
        WheelInspector.DataContext = app;
        bool isApp = app != null;
        WheelNameBox.Visibility = WheelAppSection.Visibility = RemoveAppButton.Visibility = isApp ? Visibility.Visible : Visibility.Collapsed;
        GlobalNameText.Visibility = GlobalHint.Visibility = isApp ? Visibility.Collapsed : Visibility.Visible;
        WheelIconButton.IsEnabled = isApp;
        WheelIconActions.Visibility = isApp ? Visibility.Visible : Visibility.Collapsed;
        WheelIconPreview.Content = IconFactory.Create(app != null ? WheelIcon(app) : "mdi:earth", 34, isApp ? Palette.TextBrush : Palette.BrassBrush);
        var layout = app?.Layout ?? _cfg.GlobalLayout;
        WheelStats.Text = Count(layout);
        ExportWheelButton.Content = isApp ? "Exporter cette roue…" : "Exporter la configuration…";
        CheckProcessClash();
    }

    /// <summary>Icône affichée pour une roue. Si l'exe enregistré n'existe plus (app mise à jour), on prend celui de l'app ouverte.</summary>
    private static string WheelIcon(AppTarget app)
    {
        if (app.Icon != null || app.ExePath == null || File.Exists(app.ExePath)) return app.IconRef;
        return RunningApps.FindExe(app.ProcessList()) is { } exe ? "exe:" + exe : "mdi:application-outline";
    }

    private void PickWheelIcon_Click(object sender, RoutedEventArgs e)
    {
        var app = _path.FirstOrDefault()?.App;
        if (app == null) return;
        var dlg = new IconPickerWindow(app.Icon ?? "mdi:application-outline") { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;
        app.Icon = dlg.Result;
        WheelIconsChanged(app);
    }

    private void ImportWheelImage_Click(object sender, RoutedEventArgs e)
    {
        var app = _path.FirstOrDefault()?.App;
        if (app == null) return;
        var dlg = new OpenFileDialog { Title = "Image pour la roue", Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif" };
        if (dlg.ShowDialog(this) != true) return;
        try { app.Icon = "img:" + ConfigService.ImportImage(dlg.FileName); }
        catch (Exception ex) { Dialog.Alert(this, DialogKind.Error, "Image impossible à importer", ex.Message); return; }
        WheelIconsChanged(app);
    }

    /// <summary>Icône du programme : celle de l'exe connu, sinon d'une instance ouverte, sinon on demande le .exe.</summary>
    private void ResetWheelIcon_Click(object sender, RoutedEventArgs e)
    {
        var app = _path.FirstOrDefault()?.App;
        if (app == null) return;
        var exe = app.ExePath is { } known && File.Exists(known) ? known : RunningApps.FindExe(app.ProcessList());
        if (exe == null)
        {
            var dlg = new OpenFileDialog { Title = $"Programme de « {app.Name} »", Filter = "Programmes|*.exe" };
            if (dlg.ShowDialog(this) != true) return;
            exe = dlg.FileName;
        }
        app.ExePath = exe;
        app.Icon = null;
        WheelIconsChanged(app);
    }

    private void WheelIconsChanged(AppTarget app)
    {
        _suppressWheelSelection = true;
        RebuildWheelList(app);
        _suppressWheelSelection = false;
        UpdateWheelInspector();
    }
}
