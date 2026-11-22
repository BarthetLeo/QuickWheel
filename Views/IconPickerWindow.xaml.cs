using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using QuickWheel.Models;
using QuickWheel.Services;

namespace QuickWheel.Views;

public sealed class IconVm
{
    public required IconEntry Entry { get; init; }
    public string Name => Entry.Name;
    public Geometry? Geometry => IconFactory.MdiGeometry(Entry.Name);
}

public sealed record CategoryVm(string? Key, string Label);

public sealed class RunningVm
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public ImageSource? Icon => IconFactory.ExeIcon(Path);
}

public partial class IconPickerWindow : Window
{
    private const int PerRow = 11;
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(160) };

    /// <summary>Référence d'icône choisie ("mdi:…", "img:…", "exe:…", "txt:…").</summary>
    public string? Result { get; private set; }

    public IconPickerWindow(string? current)
    {
        InitializeComponent();

        var cats = new List<CategoryVm> { new(null, $"Toutes ({IconCatalog.All.Count})") };
        cats.AddRange(IconCatalog.Categories().Select(c => new CategoryVm(c, IconCatalog.CategoryName(c))));
        CategoryBox.ItemsSource = cats;
        CategoryBox.SelectedIndex = 0;

        _debounce.Tick += (_, _) => { _debounce.Stop(); Refresh(); };

        Result = current;
        if (current != null)
        {
            if (current.StartsWith("img:")) { ImagePath.Text = current[4..]; Tabs.SelectedIndex = 1; }
            else if (current.StartsWith("exe:")) { ExePath.Text = current[4..]; Tabs.SelectedIndex = 2; }
            else if (current.StartsWith("txt:")) { TextIcon.Text = current[4..]; Tabs.SelectedIndex = 3; }
        }
        UpdatePreview();
        Loaded += (_, _) =>
        {
            Refresh();
            SearchBox.Focus();
            RunningList.ItemsSource = RunningApps.List()
                .Where(a => a.ExePath != null)
                .Select(a => new RunningVm { Name = a.ProcessName, Path = a.ExePath! })
                .ToList();
        };
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) { _debounce.Stop(); _debounce.Start(); }

    private void Category_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Refresh(); }

    private void Refresh()
    {
        var cat = (CategoryBox.SelectedItem as CategoryVm)?.Key;
        var found = IconCatalog.Search(SearchBox.Text, cat);
        CountText.Text = found.Count == 0
            ? "Aucune icône. Essaie un mot anglais (home, folder, sword…) ou un mot simple en français."
            : found.Count == 1 ? "1 icône" : $"{found.Count} icônes";
        IconRows.ItemsSource = found
            .Select(e => new IconVm { Entry = e })
            .Chunk(PerRow)
            .ToList();
    }

    private void Icon_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is IconVm vm)
        {
            Result = vm.Entry.Ref;
            UpdatePreview();
        }
    }

    private void Icon_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        Icon_Click(sender, e);
        Ok_Click(sender, e);
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp;*.gif|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) == true) ImagePath.Text = dlg.FileName;
    }

    private void ImagePath_Changed(object sender, TextChangedEventArgs e)
    {
        if (File.Exists(ImagePath.Text)) { Result = "img:" + ImagePath.Text; UpdatePreview(); }
    }

    private void BrowseExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Programmes|*.exe;*.lnk|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) == true) ExePath.Text = dlg.FileName;
    }

    private void ExePath_Changed(object sender, TextChangedEventArgs e)
    {
        if (File.Exists(ExePath.Text)) { Result = "exe:" + ExePath.Text; UpdatePreview(); }
    }

    private void Running_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (RunningList.SelectedItem is RunningVm vm) ExePath.Text = vm.Path;
    }

    private void TextIcon_Changed(object sender, TextChangedEventArgs e)
    {
        if (TextIcon.Text.Length > 0) { Result = "txt:" + TextIcon.Text; UpdatePreview(); }
    }

    private void UpdatePreview()
    {
        Preview.Content = IconFactory.Create(Result ?? "txt:", 26, Palette.TextBrush);
        PreviewName.Text = Result switch
        {
            null => "Aucune icône choisie",
            var r when r.StartsWith("mdi:") => r[4..],
            var r when r.StartsWith("img:") || r.StartsWith("exe:") => System.IO.Path.GetFileName(r[4..]),
            var r => r[4..]
        };
        OkButton.IsEnabled = Result != null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Result == null) return;
        // Copie dans %APPDATA%\QuickWheel\icons (une seule fois) : l'icône survit si l'original est déplacé
        if (Result.StartsWith("img:") && !Result[4..].StartsWith(ConfigService.IconsFolder, StringComparison.OrdinalIgnoreCase))
        {
            try { Result = "img:" + ConfigService.ImportImage(Result[4..]); }
            catch { /* on garde le chemin d'origine */ }
        }
        DialogResult = true;
    }
}
