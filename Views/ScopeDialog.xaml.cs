using System.Windows;
using QuickWheel.Models;

namespace QuickWheel.Views;

public sealed class AppChoice : Observable
{
    private bool _isChecked;
    public required AppTarget App { get; init; }
    public string Name => App.Name;
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
}

/// <summary>Choix de la portée d'un raccourci : global, cette app, ou plusieurs apps.</summary>
public partial class ScopeDialog : Window
{
    private readonly List<AppChoice> _choices;
    private readonly AppTarget? _current;

    public ShortcutScope Scope { get; private set; } = ShortcutScope.Global;
    public List<string> AppIds { get; private set; } = new();

    /// <param name="current">App de la roue en cours d'édition (null = roue globale).</param>
    public ScopeDialog(IEnumerable<AppTarget> apps, AppTarget? current, string heading, string okText,
                       ShortcutScope? scope = null, IEnumerable<string>? appIds = null)
    {
        InitializeComponent();
        _current = current;
        Heading.Text = heading;
        OkButton.Content = okText;

        var selected = appIds?.ToHashSet() ?? new HashSet<string>();
        if (current != null && appIds == null) selected.Add(current.Id);
        _choices = apps.Select(a => new AppChoice { App = a, IsChecked = selected.Contains(a.Id) }).ToList();
        AppsList.ItemsSource = _choices;

        if (current != null) ThisAppText.Text = $"Cette app ({current.Name})";
        else ThisAppRadio.Visibility = Visibility.Collapsed;
        if (_choices.Count == 0) AppsRadio.IsEnabled = false;

        // Sélection initiale
        if (scope == ShortcutScope.Global || (scope == null && current == null)) GlobalRadio.IsChecked = true;
        else if (current != null && (scope == null || (selected.Count == 1 && selected.Contains(current.Id)))) ThisAppRadio.IsChecked = true;
        else AppsRadio.IsChecked = true;
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        AppsPanel.Visibility = AppsRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalRadio.IsChecked == true)
        {
            Scope = ShortcutScope.Global;
            AppIds = new();
        }
        else if (ThisAppRadio.IsChecked == true && _current != null)
        {
            Scope = ShortcutScope.Apps;
            AppIds = new() { _current.Id };
        }
        else
        {
            AppIds = _choices.Where(c => c.IsChecked).Select(c => c.App.Id).ToList();
            if (AppIds.Count == 0)
            {
                ErrorText.Text = "Coche au moins une app, ou choisis « Global ».";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            Scope = ShortcutScope.Apps;
        }
        DialogResult = true;
    }
}
