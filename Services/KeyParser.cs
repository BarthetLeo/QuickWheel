using System.Text;
using System.Windows.Input;

namespace QuickWheel.Services;

/// <summary>
/// Conversion texte ⇄ touches. Format : "Ctrl+Shift+S" pour un combo,
/// "Ctrl+K, Ctrl+C" pour une séquence (étapes séparées par des virgules).
/// Une étape "wait 200" (ou "{200}") fait une pause de 200 ms.
/// </summary>
public static class KeyParser
{
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B;

    private static readonly Dictionary<string, int> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VK_CONTROL, ["control"] = VK_CONTROL, ["ctl"] = VK_CONTROL,
        ["shift"] = VK_SHIFT, ["maj"] = VK_SHIFT,
        ["alt"] = VK_MENU,
        ["win"] = VK_LWIN, ["windows"] = VK_LWIN, ["meta"] = VK_LWIN,
        ["enter"] = 0x0D, ["return"] = 0x0D, ["entrée"] = 0x0D,
        ["tab"] = 0x09, ["esc"] = 0x1B, ["escape"] = 0x1B, ["echap"] = 0x1B,
        ["space"] = 0x20, ["espace"] = 0x20,
        ["backspace"] = 0x08, ["back"] = 0x08,
        ["delete"] = 0x2E, ["del"] = 0x2E, ["suppr"] = 0x2E,
        ["insert"] = 0x2D, ["ins"] = 0x2D,
        ["home"] = 0x24, ["end"] = 0x23, ["fin"] = 0x23,
        ["pageup"] = 0x21, ["pgup"] = 0x21, ["pagedown"] = 0x22, ["pgdn"] = 0x22,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["haut"] = 0x26, ["bas"] = 0x28, ["gauche"] = 0x25, ["droite"] = 0x27,
        ["printscreen"] = 0x2C, ["prtsc"] = 0x2C, ["pause"] = 0x13, ["capslock"] = 0x14,
        ["apps"] = 0x5D, ["contextmenu"] = 0x5D,
        ["volumeup"] = 0xAF, ["volumedown"] = 0xAE, ["mute"] = 0xAD,
        ["playpause"] = 0xB3, ["nexttrack"] = 0xB0, ["prevtrack"] = 0xB1, ["stop"] = 0xB2,
    };

    private static readonly HashSet<int> Extended = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E,
        0x5B, 0x5C, 0x5D, 0x6F, 0x90, 0xA3, 0xA5,
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3
    };

    public static bool IsExtended(int vk) => Extended.Contains(vk);

    public static bool IsModifier(int vk) => vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_LWIN
        or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    /// <summary>Une étape : soit un combo (liste de vk), soit une pause (WaitMs > 0).</summary>
    public record Step(IReadOnlyList<int> Keys, int WaitMs);

    public static List<Step> Parse(string text)
    {
        var steps = new List<Step>();
        foreach (var rawStep in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            bool isWait = rawStep.StartsWith("{") || rawStep.StartsWith("wait", StringComparison.OrdinalIgnoreCase);
            var s = rawStep.Trim('{', '}', ' ');
            if (s.StartsWith("wait", StringComparison.OrdinalIgnoreCase)) s = s[4..].Trim();
            if (isWait && int.TryParse(s, out var ms))
            {
                steps.Add(new Step(Array.Empty<int>(), ms));
                continue;
            }

            var keys = new List<int>();
            // "Ctrl++" → Ctrl et la touche "+"
            var tokens = rawStep.EndsWith("++") ? rawStep[..^2].Split('+').Append("+") : rawStep.Split('+');
            foreach (var t in tokens.Select(t => t.Trim()).Where(t => t.Length > 0))
            {
                var vk = ParseKey(t) ?? throw new FormatException($"Touche inconnue : « {t} »");
                keys.Add(vk);
            }
            if (keys.Count > 0) steps.Add(new Step(keys, 0));
        }
        return steps;
    }

    public static int? ParseKey(string token)
    {
        if (Names.TryGetValue(token, out var vk)) return vk;

        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
            if (c == '+') return 0xBB; // OemPlus
        }

        if (token.Length is 2 or 3 && (token[0] is 'F' or 'f') && int.TryParse(token[1..], out var f) && f is >= 1 and <= 24)
            return 0x70 + f - 1;

        if (token.StartsWith("numpad", StringComparison.OrdinalIgnoreCase) && int.TryParse(token[6..], out var n) && n is >= 0 and <= 9)
            return 0x60 + n;

        if (token.StartsWith("vk:", StringComparison.OrdinalIgnoreCase) || token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var hex = token.StartsWith("vk:", StringComparison.OrdinalIgnoreCase) ? token[3..] : token;
            hex = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
            if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var raw)) return raw;
        }

        // Repli : noms de l'énumération WPF (Oem7, OemComma, Multiply, ...)
        if (Enum.TryParse<Key>(token, true, out var key) && key != Key.None)
        {
            var v = KeyInterop.VirtualKeyFromKey(key);
            if (v != 0) return v;
        }
        return null;
    }

    public static string FormatKey(int vk)
    {
        switch (vk)
        {
            case VK_CONTROL or 0xA2 or 0xA3: return "Ctrl";
            case VK_SHIFT or 0xA0 or 0xA1: return "Shift";
            case VK_MENU or 0xA4 or 0xA5: return "Alt";
            case VK_LWIN or 0x5C: return "Win";
        }
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x70 + 1);
        if (vk is >= 0x60 and <= 0x69) return "Numpad" + (vk - 0x60);
        var named = Names.FirstOrDefault(kv => kv.Value == vk && kv.Key.All(ch => ch < 128));
        if (named.Key != null) return char.ToUpper(named.Key[0]) + named.Key[1..];
        var k = KeyInterop.KeyFromVirtualKey(vk);
        return k != Key.None ? k.ToString() : $"vk:0x{vk:X2}";
    }

    /// <summary>Nom lisible pour l'utilisateur, tel qu'imprimé sur son clavier (ex : « ² »).</summary>
    public static string DisplayName(int vk)
    {
        uint scan = NativeMethods.MapVirtualKey((uint)vk, 0);
        if (scan != 0)
        {
            var sb = new StringBuilder(64);
            int lParam = (int)(scan << 16);
            if (IsExtended(vk)) lParam |= 1 << 24;
            if (NativeMethods.GetKeyNameText(lParam, sb, sb.Capacity) > 0) return sb.ToString();
        }
        return FormatKey(vk);
    }

    public static string FormatCombo(bool ctrl, bool alt, bool shift, bool win, int vk, bool display = false)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        if (vk != 0 && !IsModifier(vk)) parts.Add(display ? DisplayName(vk) : FormatKey(vk));
        return string.Join("+", parts);
    }
}
