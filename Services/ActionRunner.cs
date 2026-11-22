using System.Diagnostics;
using QuickWheel.Models;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Services;

public static class ActionRunner
{
    /// <summary>Un raccourci à la fois : le suivant attend, sinon leurs touches et clics s'entremêlent.</summary>
    private static readonly SemaphoreSlim Running = new(1, 1);

    /// <summary>Un raccourci est en cours : le prochain devra attendre.</summary>
    public static bool IsBusy => Running.CurrentCount == 0;

    /// <summary>Exécute toutes les étapes du raccourci, dans l'ordre.</summary>
    public static async Task RunAsync(Shortcut shortcut, ForegroundInfo? target)
    {
        await Running.WaitAsync();
        try { await RunCoreAsync(shortcut, target); }
        finally { Running.Release(); }
    }

    private static async Task RunCoreAsync(Shortcut shortcut, ForegroundInfo? target)
    {
        // Laisse le menu disparaître et rend la main à la fenêtre cible
        await Task.Delay(40);
        if (target != null && target.Handle != IntPtr.Zero)
            SetForegroundWindow(target.Handle);

        // Les étapes s'enchaînent sans attendre : seule une étape « Pause » ralentit volontairement la séquence.
        var steps = shortcut.Actions.ToList();
        bool clicks = steps.Any(s => s.Type == ActionType.Click);
        GetCursorPos(out var cursor);

        foreach (var step in steps)
            await RunStepAsync(step);

        // Remet la souris là où elle était avant la séquence
        if (clicks) InputSender.MoveCursor(cursor.X, cursor.Y);
    }

    /// <summary>Temps max pour qu'un élément apparaisse (ex. fenêtre ouverte par l'étape précédente).</summary>
    private const int LocateTimeoutMs = 1200;

    /// <summary>Remet le focus sur le champ où le texte / les touches avaient été tapés à l'enregistrement.</summary>
    private static async Task FocusTargetAsync(ActionStep step)
    {
        if (step.Target is not { } target) return;
        if (await Task.Run(() => ElementLocator.Focus(target, LocateTimeoutMs))) await Task.Delay(10);
    }

    public static async Task RunStepAsync(ActionStep step)
    {
        switch (step.Type)
        {
            case ActionType.Keys:
                await FocusTargetAsync(step);
                await InputSender.SendKeysAsync(step.Value);
                break;

            case ActionType.Text:
                await FocusTargetAsync(step);
                await InputSender.SendTextAsync(step.Value);
                break;

            case ActionType.Click:
                if (!step.TryGetPoint(out var x, out var y)) throw new InvalidOperationException("position du clic non définie");
                if (step.Target is { } target)
                {
                    // Retrouve le même bouton / champ où qu'il soit ; sinon, clic à la position enregistrée
                    var found = await Task.Run(() => ElementLocator.Locate(target, x, y, LocateTimeoutMs));
                    if (found is { } p) (x, y) = p;
                }
                InputSender.Click(x, y, step.Button);
                // Laisse la fenêtre cliquée prendre le focus avant la suite (texte, touches)
                await Task.Delay(15);
                break;

            case ActionType.Wait:
                if (int.TryParse(step.Value, out var ms)) await Task.Delay(Math.Clamp(ms, 0, 60_000));
                break;

            case ActionType.Launch:
                Process.Start(new ProcessStartInfo
                {
                    FileName = Environment.ExpandEnvironmentVariables(step.Value.Trim().Trim('"')),
                    Arguments = Environment.ExpandEnvironmentVariables(step.Arguments),
                    UseShellExecute = true // exe, fichiers, dossiers, URL, ms-settings:
                });
                await Task.Delay(50);
                break;

            case ActionType.Command:
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    CreateNoWindow = !step.ShowWindow,
                };
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                if (!step.ShowWindow) { psi.ArgumentList.Add("-WindowStyle"); psi.ArgumentList.Add("Hidden"); }
                psi.ArgumentList.Add("-Command");
                psi.ArgumentList.Add(step.Value);
                using (var p = Process.Start(psi))
                using (var timeout = new CancellationTokenSource(10_000))
                {
                    // Commande cachée : on attend la fin (max 10 s) pour que la macro reste ordonnée
                    if (p != null && !step.ShowWindow) await p.WaitForExitAsync(timeout.Token).ContinueWith(_ => { });
                }
                break;
        }
    }
}
