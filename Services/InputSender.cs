using System.Runtime.InteropServices;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Services;

/// <summary>Simulation clavier via SendInput.</summary>
public static class InputSender
{
    private static readonly int[] PhysicalModifiers = { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C };
    private const ushort VK_DUMMY = 0xE8; // touche non attribuée : empêche Alt/Win seuls d'ouvrir un menu

    private static INPUT Key(int vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = (ushort)vk,
                wScan = (ushort)MapVirtualKey((uint)vk, 0),
                dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (KeyParser.IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0)
            }
        }
    };

    private static INPUT Unicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) } }
    };

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0) return;
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    /// <summary>Relâche les modificateurs encore physiquement enfoncés pour ne pas polluer le combo.
    /// Renvoie true si quelque chose a été relâché.</summary>
    public static bool ReleaseHeldModifiers()
    {
        var inputs = new List<INPUT>();
        bool altOrWin = false;
        foreach (var vk in PhysicalModifiers)
        {
            if (KeyboardHook.IsKeyDown(vk))
            {
                if (vk is 0xA4 or 0xA5 or 0x5B or 0x5C) altOrWin = true;
                inputs.Add(Key(vk, true));
            }
        }
        if (altOrWin)
        {
            inputs.Insert(0, Key(VK_DUMMY, false));
            inputs.Insert(1, Key(VK_DUMMY, true));
        }
        Send(inputs);
        return inputs.Count > 0;
    }

    /// <summary>Envoie un « tapotement » de la touche factice (utile quand on avale une touche pressée avec Alt/Win).</summary>
    public static void TapDummy() => Send(new List<INPUT> { Key(VK_DUMMY, false), Key(VK_DUMMY, true) });

    public static async Task SendKeysAsync(string sequence)
    {
        var steps = KeyParser.Parse(sequence);
        if (ReleaseHeldModifiers()) await Task.Delay(10);
        foreach (var step in steps)
        {
            if (step.WaitMs > 0)
            {
                await Task.Delay(step.WaitMs);
                continue;
            }
            var inputs = new List<INPUT>();
            foreach (var vk in step.Keys) inputs.Add(Key(vk, false));
            foreach (var vk in step.Keys.Reverse()) inputs.Add(Key(vk, true));
            Send(inputs);
            // Écart minimal entre deux combos d'une séquence : certaines applis perdent les touches sinon
            await Task.Delay(5);
        }
    }

    public static async Task SendTextAsync(string text)
    {
        if (ReleaseHeldModifiers()) await Task.Delay(10);
        var inputs = new List<INPUT>();
        foreach (var c in text)
        {
            switch (c)
            {
                case '\r': continue;
                case '\n': inputs.Add(Key(0x0D, false)); inputs.Add(Key(0x0D, true)); break;
                case '\t': inputs.Add(Key(0x09, false)); inputs.Add(Key(0x09, true)); break;
                default: inputs.Add(Unicode(c, false)); inputs.Add(Unicode(c, true)); break;
            }
            // Par paquets pour ne pas saturer la file d'entrée des applis lentes
            if (inputs.Count >= 128) { Send(inputs); inputs.Clear(); await Task.Delay(2); }
        }
        Send(inputs);
    }

    // ---------------- Souris ----------------

    /// <summary>Déplacement absolu (pixels physiques, bureau virtuel multi-écrans) mis en file avec les autres entrées,
    /// pour que les clics s'appliquent toujours à la bonne position, même enchaînés très vite.</summary>
    private static INPUT MoveTo(int x, int y)
    {
        int left = GetSystemMetrics(SM_XVIRTUALSCREEN), top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN)), h = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        return Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            (int)Math.Round((x - left) * 65535.0 / (w - 1)), (int)Math.Round((y - top) * 65535.0 / (h - 1)));
    }

    private static INPUT Mouse(uint flags, int dx = 0, int dy = 0) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } }
    };

    public static void Click(int x, int y, Models.ClickButton button)
    {
        var inputs = new List<INPUT> { MoveTo(x, y) };
        (uint down, uint up) = button switch
        {
            Models.ClickButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            Models.ClickButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP)
        };
        int count = button == Models.ClickButton.Double ? 2 : 1;
        for (int i = 0; i < count; i++) { inputs.Add(Mouse(down)); inputs.Add(Mouse(up)); }
        Send(inputs);
    }

    public static void MoveCursor(int x, int y) => Send(new List<INPUT> { MoveTo(x, y) });
}
