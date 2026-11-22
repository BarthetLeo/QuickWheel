using System.Diagnostics;
using System.Runtime.InteropServices;
using static QuickWheel.Services.NativeMethods;

namespace QuickWheel.Services;

/// <summary>
/// Hook clavier bas niveau global. Le callback décide pour chaque touche si elle est
/// « avalée » (true) — elle n'arrive alors pas à l'application active.
/// Les touches injectées (par SendInput, y compris les nôtres) sont ignorées.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private readonly LowLevelKeyboardProc _proc; // garder une référence, sinon le GC la collecte
    private IntPtr _hook;

    /// <summary>(vk, isDown, isRepeat) → avaler la touche ?</summary>
    public Func<int, bool, bool, bool>? KeyHandler { get; set; }

    private readonly HashSet<int> _down = new();

    public KeyboardHook()
    {
        _proc = HookCallback;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
            throw new InvalidOperationException($"Impossible d'installer le hook clavier (erreur {Marshal.GetLastWin32Error()}).");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && KeyHandler != null)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if ((data.flags & LLKHF_INJECTED) == 0)
            {
                int msg = wParam.ToInt32();
                int vk = (int)data.vkCode;
                bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
                bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;
                if (isDown || isUp)
                {
                    bool repeat = isDown && !_down.Add(vk);
                    if (isUp) _down.Remove(vk);
                    try
                    {
                        if (KeyHandler(vk, isDown, repeat)) return (IntPtr)1;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex); // ne jamais laisser une exception remonter dans le hook
                    }
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
