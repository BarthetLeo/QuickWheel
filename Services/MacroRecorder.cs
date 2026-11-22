using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using QuickWheel.Models;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Services;

/// <summary>
/// Enregistre ce que fait l'utilisateur (clics, texte tapé, combos) en étapes de raccourci.
/// Le rythme n'est PAS enregistré : la séquence sera rejouée le plus vite possible.
/// Échap termine l'enregistrement. En mode <see cref="PickPoint"/>, le premier clic donne une position (et est avalé).
/// </summary>
public sealed class MacroRecorder : IDisposable
{
    private const int VK_ESCAPE = 0x1B, VK_BACK = 0x08, VK_RETURN = 0x0D, VK_TAB = 0x09,
                      VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_CAPITAL = 0x14,
                      VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_RMENU = 0xA5;

    private static readonly HashSet<int> ModifierKeys = new() { 0x10, 0x11, 0x12, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C, 0x14, 0x90, 0x91 };

    // Touches mortes courantes (AZERTY / QWERTY international) → diacritique combinant
    private static readonly Dictionary<char, char> DeadKeys = new()
    {
        ['^'] = '̂', ['¨'] = '̈', ['`'] = '̀', ['´'] = '́', ['\''] = '́', ['~'] = '̃', ['"'] = '̈',
    };

    private readonly LowLevelMouseProc _mouseProc;
    private readonly LowLevelKeyboardProc _keyProc;
    private IntPtr _mouseHook, _keyHook;
    private readonly bool _pickPoint;
    private bool _swallowNextUp;
    private char? _pendingDead;
    private (int x, int y, uint time)? _lastLeft;

    public List<ActionStep> Steps { get; } = new();

    /// <summary>Une étape a été ajoutée ou modifiée (pour mettre à jour le badge).</summary>
    public event Action? Changed;

    /// <summary>Fin de l'enregistrement (Échap, ou position choisie en mode PickPoint).</summary>
    public event Action<MacroRecorder>? Finished;

    public (int x, int y)? PickedPoint { get; private set; }

    /// <summary>Élément reconnu sous le point choisi (mode PickPoint).</summary>
    public ElementTarget? PickedTarget { get; private set; }

    // Reconnaissance des éléments cliqués, faite en arrière-plan pour ne pas ralentir le hook
    private readonly List<Task> _pending = new();

    private void CaptureElement(int x, int y, Action<ElementTarget?> apply) =>
        _pending.Add(Task.Run(() => ElementLocator.Capture(x, y)).ContinueWith(t => apply(t.IsCompletedSuccessfully ? t.Result : null)));
    public bool PickPoint => _pickPoint;

    public MacroRecorder(bool pickPoint)
    {
        _pickPoint = pickPoint;
        _mouseProc = MouseCallback;
        _keyProc = KeyCallback;
        using var module = Process.GetCurrentProcess().MainModule;
        var h = GetModuleHandle(module?.ModuleName);
        _mouseHook = SetWindowsHookExMouse(WH_MOUSE_LL, _mouseProc, h, 0);
        _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, h, 0);
        if (_mouseHook == IntPtr.Zero || _keyHook == IntPtr.Zero)
        {
            Dispose();
            throw new InvalidOperationException("Impossible d'installer les hooks d'enregistrement.");
        }
    }

    // ---------------- Souris ----------------

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var d = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            if ((d.flags & LLMHF_INJECTED) == 0)
            {
                try
                {
                    if (OnMouse(msg, d)) return (IntPtr)1;
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool OnMouse(int msg, MSLLHOOKSTRUCT d)
    {
        bool isUp = msg is WM_LBUTTONUP or WM_RBUTTONUP or WM_MBUTTONUP;
        if (isUp && _swallowNextUp)
        {
            _swallowNextUp = false;
            if (_pickPoint) Finish();
            return true;
        }

        ClickButton? button = msg switch
        {
            WM_LBUTTONDOWN => ClickButton.Left,
            WM_RBUTTONDOWN => ClickButton.Right,
            WM_MBUTTONDOWN => ClickButton.Middle,
            _ => null
        };
        if (button == null) return false;

        if (_pickPoint)
        {
            PickedPoint = (d.pt.X, d.pt.Y);
            CaptureElement(d.pt.X, d.pt.Y, t => PickedTarget = t);
            _swallowNextUp = true; // le clic ne doit pas atteindre la fenêtre en dessous
            return true;
        }

        FlushDeadKey();
        // Deux clics gauches rapprochés au même endroit → un double-clic
        if (button == ClickButton.Left && _lastLeft is { } last && Steps.Count > 0
            && Steps[^1] is { Type: ActionType.Click, Button: ClickButton.Left } prev
            && d.time - last.time <= GetDoubleClickTime()
            && Math.Abs(d.pt.X - last.x) <= 4 && Math.Abs(d.pt.Y - last.y) <= 4)
        {
            prev.Button = ClickButton.Double;
            _lastLeft = null;
        }
        else
        {
            var step = new ActionStep { Type = ActionType.Click, Value = $"{d.pt.X},{d.pt.Y}", Button = button.Value };
            Steps.Add(step);
            CaptureElement(d.pt.X, d.pt.Y, t => step.Target = t);
            _lastLeft = button == ClickButton.Left ? (d.pt.X, d.pt.Y, d.time) : null;
        }
        Changed?.Invoke();
        return false; // le clic passe : l'utilisateur voit le résultat de ce qu'il enregistre
    }

    // ---------------- Clavier ----------------

    private IntPtr KeyCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var d = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            if ((d.flags & LLKHF_INJECTED) == 0 && msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                try
                {
                    if (OnKeyDown((int)d.vkCode, (int)d.scanCode)) return (IntPtr)1;
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }
            else if ((d.flags & LLKHF_INJECTED) == 0 && msg is WM_KEYUP or WM_SYSKEYUP && d.vkCode == VK_ESCAPE)
                return (IntPtr)1;
        }
        return CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    private static bool Down(int vk) => KeyboardHook.IsKeyDown(vk);

    private bool OnKeyDown(int vk, int scan)
    {
        if (vk == VK_ESCAPE)
        {
            Finish();
            return true;
        }
        if (_pickPoint || ModifierKeys.Contains(vk)) return false;

        bool altGr = Down(VK_RMENU);
        bool ctrl = Down(VK_CONTROL) && !altGr;
        bool alt = Down(VK_MENU) && !altGr;
        bool shift = Down(VK_SHIFT);
        bool win = Down(VK_LWIN) || Down(VK_RWIN);

        if (!ctrl && !alt && !win)
        {
            if (vk == VK_RETURN) { AppendText("\n"); return false; }
            if (vk == VK_TAB && !shift) { AppendText("\t"); return false; }
            if (vk == VK_BACK && Steps.Count > 0 && Steps[^1] is { Type: ActionType.Text } t && t.Value.Length > 0 && _pendingDead == null)
            {
                // Corrige le texte enregistré plutôt que de rejouer la faute puis l'effacement
                t.Value = t.Value[..^1];
                if (t.Value.Length == 0) Steps.RemoveAt(Steps.Count - 1);
                Changed?.Invoke();
                return false;
            }

            var ch = Translate(vk, scan, shift, altGr);
            if (ch != null) { AppendText(ch); return false; }
        }

        FlushDeadKey();
        var combo = KeyParser.FormatCombo(ctrl, alt, shift, win, vk);
        if (Steps.Count > 0 && Steps[^1].Type == ActionType.Keys) Steps[^1].Value += ", " + combo;
        else AddFocusedStep(new ActionStep { Type = ActionType.Keys, Value = combo });
        Changed?.Invoke();
        return false;
    }

    /// <summary>Caractère produit par la touche avec la disposition clavier de l'app active, ou null.</summary>
    private string? Translate(int vk, int scan, bool shift, bool altGr)
    {
        var state = new byte[256];
        if (shift) state[VK_SHIFT] = 0x80;
        if (altGr) { state[VK_CONTROL] = 0x80; state[VK_MENU] = 0x80; }
        if ((GetKeyStateToggled(VK_CAPITAL))) state[VK_CAPITAL] = 0x01;

        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var layout = GetKeyboardLayout(thread);
        var sb = new StringBuilder(8);
        // Bit 2 : ne pas modifier l'état clavier du système (sinon les touches mortes de l'utilisateur sont cassées)
        int n = ToUnicodeEx((uint)vk, (uint)scan, state, sb, sb.Capacity, 0x4, layout);

        if (n < 0)
        {
            // Touche morte : on attend la lettre suivante
            if (sb.Length > 0) { FlushDeadKey(); _pendingDead = sb[0]; }
            return null;
        }
        if (n == 0) return null;
        var text = sb.ToString(0, n);
        if (text.Any(char.IsControl)) return null;

        if (_pendingDead is { } dead)
        {
            _pendingDead = null;
            if (text == " ") return dead.ToString();
            if (DeadKeys.TryGetValue(dead, out var mark))
            {
                var composed = (text + mark).Normalize(NormalizationForm.FormC);
                return composed.Length == 1 ? composed : dead + text;
            }
            return dead + text;
        }
        return text;
    }

    private static bool GetKeyStateToggled(int vk) => (GetKeyState(vk) & 1) != 0;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private void FlushDeadKey()
    {
        if (_pendingDead is { } dead)
        {
            _pendingDead = null;
            AppendText(dead.ToString(), notify: false);
        }
    }

    private void AppendText(string s, bool notify = true)
    {
        if (_pendingDead != null && s is "\n" or "\t") FlushDeadKey();
        if (Steps.Count > 0 && Steps[^1].Type == ActionType.Text) Steps[^1].Value += s;
        else AddFocusedStep(new ActionStep { Type = ActionType.Text, Value = s });
        _lastLeft = null;
        if (notify) Changed?.Invoke();
    }

    /// <summary>Nouvelle étape clavier : on retient aussi le champ qui a le focus, pour y retaper au bon endroit.</summary>
    private void AddFocusedStep(ActionStep step)
    {
        Steps.Add(step);
        _pending.Add(Task.Run(ElementLocator.CaptureFocused).ContinueWith(t => { if (t.IsCompletedSuccessfully) step.Target = t.Result; }));
    }

    // ---------------- Fin ----------------

    private bool _finished;

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        FlushDeadKey();
        // Laisse le hook rendre la main avant de le désinstaller
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        dispatcher.BeginInvoke(async () =>
        {
            Dispose();
            // Attend la fin de la reconnaissance des éléments (3 s max)
            try { await Task.WhenAny(Task.WhenAll(_pending.ToArray()), Task.Delay(3000)); } catch { }
            Finished?.Invoke(this);
        });
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        if (_keyHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
    }
}
