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

/// <summary>Paramètres : étapes d'un raccourci, enregistrement de séquence, saisie de touches, test.</summary>
public partial class SettingsWindow
{
    // ---------------- Étapes ----------------

    private ActionStep? CurrentStep => StepsList.SelectedItem as ActionStep;

    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateStepEditor();

    private void UpdateStepEditor() =>
        StepEditor.Visibility = CurrentStep != null ? Visibility.Visible : Visibility.Collapsed;

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || (sender as FrameworkElement)?.Tag is not ActionType type) return;
        var step = new ActionStep { Type = type, Value = type == ActionType.Wait ? "200" : "" };
        int at = StepsList.SelectedIndex >= 0 ? StepsList.SelectedIndex + 1 : _selected.Actions.Count;
        _selected.Actions.Insert(at, step);
        StepsList.SelectedItem = step;
        if (type == ActionType.Keys) Dispatcher.BeginInvoke(() => KeyRecorder.Focus(), DispatcherPriority.Input);
        if (type == ActionType.Click) StartRecorder(pickPoint: true);
    }

    // ---------------- Enregistrement de séquence / choix d'un point ----------------

    private MacroRecorder? _recorder;
    private RecordingBadge? _badge;

    private void Record_Click(object sender, RoutedEventArgs e) => StartRecorder(pickPoint: false);

    private void ForgetTarget_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentStep != null) CurrentStep.Target = null;
    }

    private void PickPoint_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentStep?.Type == ActionType.Click) StartRecorder(pickPoint: true);
    }

    private static string RecordingText(MacroRecorder r) => r.PickPoint
        ? "Clique à l'endroit voulu · Échap pour annuler"
        : $"Enregistrement · {r.Steps.Count} étape{(r.Steps.Count > 1 ? "s" : "")} · Échap pour terminer";

    private async void StartRecorder(bool pickPoint)
    {
        if (_selected == null || _selected.IsFolder || _recorder != null) return;
        Keyboard.ClearFocus();
        App.Current.Suspended = true;
        Hide();
        await Task.Delay(150); // laisse la fenêtre disparaître avant le premier clic

        try { _recorder = new MacroRecorder(pickPoint); }
        catch (Exception ex)
        {
            App.Current.Suspended = false;
            Show();
            TestStatus.Text = ex.Message;
            return;
        }
        _badge = new RecordingBadge(RecordingText(_recorder));
        _badge.Show();
        _recorder.Changed += () => _badge?.SetText(RecordingText(_recorder!));
        _recorder.Finished += OnRecorderFinished;
    }

    private void OnRecorderFinished(MacroRecorder r)
    {
        _recorder = null;
        _badge?.Close();
        _badge = null;
        App.Current.Suspended = false;
        Show();
        Activate();
        if (_selected == null) return;

        if (r.PickPoint)
        {
            if (CurrentStep is { Type: ActionType.Click } step && r.PickedPoint is { } p)
            {
                step.Value = $"{p.x},{p.y}";
                step.Target = r.PickedTarget;
            }
            return;
        }

        if (r.Steps.Count == 0) { TestStatus.Text = "Rien n'a été enregistré."; return; }
        // Une étape vide (créée par défaut) est remplacée par l'enregistrement
        int at = StepsList.SelectedIndex >= 0 ? StepsList.SelectedIndex + 1 : _selected.Actions.Count;
        if (CurrentStep is { } cur && string.IsNullOrWhiteSpace(cur.Value))
        {
            at = StepsList.SelectedIndex;
            _selected.Actions.RemoveAt(at);
        }
        foreach (var s in r.Steps) _selected.Actions.Insert(at++, s);
        StepsList.SelectedItem = r.Steps[^1];
        TestStatus.Text = $"{r.Steps.Count} étape{(r.Steps.Count > 1 ? "s" : "")} enregistrée{(r.Steps.Count > 1 ? "s" : "")}. Teste avec le bouton ci-contre.";
    }

    private void StepUp_Click(object sender, RoutedEventArgs e) => MoveStep(-1);
    private void StepDown_Click(object sender, RoutedEventArgs e) => MoveStep(1);

    private void MoveStep(int delta)
    {
        if (_selected == null || CurrentStep == null) return;
        int i = StepsList.SelectedIndex, j = i + delta;
        if (j < 0 || j >= _selected.Actions.Count) return;
        _selected.Actions.Move(i, j);
        StepsList.SelectedIndex = j;
    }

    private void StepRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || CurrentStep == null) return;
        int i = StepsList.SelectedIndex;
        _selected.Actions.RemoveAt(i);
        if (_selected.Actions.Count > 0) StepsList.SelectedIndex = Math.Min(i, _selected.Actions.Count - 1);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentStep == null) return;
        var dlg = new OpenFileDialog { Filter = "Programmes|*.exe;*.bat;*.cmd;*.lnk|Tous les fichiers|*.*" };
        if (dlg.ShowDialog(this) == true) CurrentStep.Value = dlg.FileName;
    }

    // ---------------- Enregistreurs de touches ----------------

    private void Recorder_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => App.Current.Suspended = true;
    private void Recorder_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => App.Current.Suspended = false;

    private static (int vk, bool ctrl, bool alt, bool shift, bool win, bool modifierOnly) ReadKey(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key
        };
        var mods = Keyboard.Modifiers;
        bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        int vk = KeyInterop.VirtualKeyFromKey(key);
        bool modOnly = key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
        return (vk, mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Shift), win, modOnly);
    }

    private void KeyRecorder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (CurrentStep == null) return;
        var k = ReadKey(e);
        if (k.modifierOnly) return; // on attend la touche principale
        var combo = KeyParser.FormatCombo(k.ctrl, k.alt, k.shift, k.win, k.vk);
        CurrentStep.Value = AppendStep.IsChecked == true && !string.IsNullOrWhiteSpace(CurrentStep.Value)
            ? $"{CurrentStep.Value}, {combo}"
            : combo;
    }

    private void ClearKeys_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentStep != null) CurrentStep.Value = "";
    }

    private void TriggerBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var k = ReadKey(e);
        if (k.modifierOnly) return;
        if (k.vk == 0x1B && !(k.ctrl || k.alt || k.shift || k.win)) { Keyboard.ClearFocus(); return; }
        _trigger = new TriggerKey { VirtualKey = k.vk, Ctrl = k.ctrl, Alt = k.alt, Shift = k.shift, Win = k.win };
        UpdateTriggerBox();
    }

    private void UpdateTriggerBox() =>
        TriggerBox.Text = KeyParser.FormatCombo(_trigger.Ctrl, _trigger.Alt, _trigger.Shift, _trigger.Win, _trigger.VirtualKey, display: true);

    // ---------------- Test ----------------

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null || _selected.IsFolder) return;
        var errors = Validate(new[] { _selected }, "").ToList();
        if (errors.Count > 0) { TestStatus.Text = errors[0]; return; }

        var copy = _selected.CloneNew();
        TestButton.IsEnabled = false;
        try
        {
            for (int s = 3; s > 0; s--)
            {
                TestStatus.Text = $"Exécution dans {s}…";
                await Task.Delay(1000);
            }
            await ActionRunner.RunAsync(copy, ForegroundInfo.Capture());
            TestStatus.Text = "Exécuté.";
        }
        catch (Exception ex) { TestStatus.Text = "Échec : " + ex.Message; }
        finally { TestButton.IsEnabled = true; }
    }
}
