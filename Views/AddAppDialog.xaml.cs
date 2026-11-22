using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuickWheel.Models;
using QuickWheel.Services;

namespace QuickWheel.Views;

public sealed class RunningAppVm
{
    public required RunningApp App { get; init; }
    public string Name => App.ProcessName;
    public string Title => App.Title;
    public ImageSource? Icon => App.ExePath != null ? IconFactory.ExeIcon(App.ExePath) : null;
    /// <summary>Roue qui a déjà ce processus (affiché en indication), null sinon.</summary>
    public string? Owner { get; init; }
    public string Sub => Owner != null ? $"A déjà sa roue : « {Owner} »" : Title;
}

public partial class AddAppDialog : Window
{
    private readonly IReadOnlyCollection<AppTarget> _existing;
    private readonly AppTarget? _into;
    private string? _exePath;

    /// <summary>Nouvelle roue (mode normal), ou la roue <c>into</c> complétée (mode « ajouter à cette roue »).</summary>
    public AppTarget? Result { get; private set; }

    /// <param name="into">Si fourni : on ajoute le processus choisi à cette roue au lieu d'en créer une.</param>
    public AddAppDialog(IReadOnlyCollection<AppTarget> existing, AppTarget? into = null)
    {
        InitializeComponent();
        _existing = existing;
        _into = into;
        if (into != null)
        {
            Title = HeadingText.Text = $"Ajouter une app à « {into.Name} »";
            IntroText.Text = "Choisis une app ouverte, ou écris le nom de son processus. Elle utilisera cette roue.";
            NamePanel.Visibility = Visibility.Collapsed;
            OkButton.Content = "Ajouter à cette roue";
        }
        Loaded += (_, _) => RunningList.ItemsSource = RunningApps.List()
            .Select(a => new RunningAppVm { App = a, Owner = existing.FirstOrDefault(x => x.Matches(a.ProcessName))?.Name })
            .ToList();
    }

    private void Running_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (RunningList.SelectedItem is not RunningAppVm vm) return;
        ProcessBox.Text = vm.Name;
        NameBox.Text = Pretty(vm);
        _exePath = vm.App.ExePath;
    }

    /// <summary>Nom lisible : description de l'exe si dispo (« Google Chrome »), sinon le processus.</summary>
    private static string Pretty(RunningAppVm vm)
    {
        try
        {
            if (vm.App.ExePath != null)
            {
                var desc = System.Diagnostics.FileVersionInfo.GetVersionInfo(vm.App.ExePath).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc) && desc.Length <= 40) return desc.Trim();
            }
        }
        catch { }
        return vm.Name;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_into != null) { AddToExisting(); return; }
        var app = new AppTarget
        {
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? ProcessBox.Text.Trim() : NameBox.Text.Trim(),
            ProcessNames = ProcessBox.Text.Trim(),
            ExePath = _exePath
        };
        if (!app.ProcessList().Any())
        {
            ShowError("Indique au moins un nom de processus, par exemple chrome.");
            return;
        }
        var clash = _existing.FirstOrDefault(a => app.ProcessList().Any(a.Matches));
        if (clash != null)
        {
            ShowError($"Ce processus a déjà sa roue : « {clash.Name} ».");
            return;
        }
        Result = app;
        DialogResult = true;
    }

    private void AddToExisting()
    {
        var wanted = new AppTarget { ProcessNames = ProcessBox.Text }.ProcessList().ToList();
        if (wanted.Count == 0) { ShowError("Indique au moins un nom de processus, par exemple chrome."); return; }
        foreach (var p in wanted)
        {
            var owner = _existing.FirstOrDefault(a => a.Matches(p));
            if (owner == _into) { ShowError($"« {p} » est déjà dans cette roue."); return; }
            if (owner != null) { ShowError($"« {p} » a déjà sa roue : « {owner.Name} ». Une app ne peut avoir qu'une roue."); return; }
        }
        var list = _into!.ProcessList().Concat(wanted).Distinct(StringComparer.OrdinalIgnoreCase);
        _into.ProcessNames = string.Join(", ", list);
        _into.ExePath ??= _exePath;
        Result = _into;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
