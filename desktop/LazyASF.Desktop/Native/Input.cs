using System.Runtime.InteropServices;

namespace LazyASF.Native;

/// <summary>Имитация мыши и клавиатуры через SendInput.</summary>
public static class Input
{
    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
        MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);

    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    static void Send(params INPUT[] inputs) => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

    static INPUT Mouse(uint flags, int dx = 0, int dy = 0, int data = 0) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = unchecked((uint)data), dwFlags = flags } }
    };

    static INPUT Key(ushort vk, bool up, bool unicode = false, char ch = '\0')
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (unicode) flags |= KEYEVENTF_UNICODE;
        else if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        return new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion { ki = new KEYBDINPUT { wVk = unicode ? (ushort)0 : vk, wScan = unicode ? ch : (ushort)0, dwFlags = flags } }
        };
    }

    static bool IsExtended(ushort vk) => vk is >= 0x21 and <= 0x2E || vk is >= 0xA6 and <= 0xB7 || vk is 0x5B or 0x5C;

    // ----- мышь -----
    public static void MoveBy(int dx, int dy) { if (dx != 0 || dy != 0) Send(Mouse(MOUSEEVENTF_MOVE, dx, dy)); }
    public static void MoveTo(int x, int y) => SetCursorPos(x, y);

    public static void Button(string button, bool down)
    {
        uint f = (button, down) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN,
            ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };
        if (f != 0) Send(Mouse(f));
    }

    public static void Click(string button = "left")
    {
        Button(button, true);
        Button(button, false);
    }

    /// <summary>delta: 120 = один «щелчок» колеса; положительное — вверх/вправо.</summary>
    public static void Wheel(int dy, int dx = 0)
    {
        if (dy != 0) Send(Mouse(MOUSEEVENTF_WHEEL, data: dy));
        if (dx != 0) Send(Mouse(MOUSEEVENTF_HWHEEL, data: dx));
    }

    // ----- клавиатура -----
    static readonly Dictionary<string, ushort> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0x11, ["control"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["win"] = 0x5B,
        ["enter"] = 0x0D, ["esc"] = 0x1B, ["escape"] = 0x1B, ["space"] = 0x20, ["tab"] = 0x09,
        ["backspace"] = 0x08, ["delete"] = 0x2E, ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["volup"] = 0xAF, ["voldown"] = 0xAE, ["mute"] = 0xAD,
        ["playpause"] = 0xB3, ["next"] = 0xB0, ["prev"] = 0xB1,
        ["browserback"] = 0xA6, ["browserforward"] = 0xA7, ["browserrefresh"] = 0xA8,
    };

    static ushort? Vk(string name)
    {
        if (Keys.TryGetValue(name, out var vk)) return vk;
        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (name.Length >= 2 && (name[0] == 'f' || name[0] == 'F') && int.TryParse(name[1..], out var n) && n is >= 1 and <= 24)
            return (ushort)(0x70 + n - 1);
        return null;
    }

    /// <summary>Нажать комбинацию вида "ctrl+shift+y", "f11", "space".</summary>
    public static bool Combo(string combo)
    {
        var vks = new List<ushort>();
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var vk = Vk(part);
            if (vk is null) { Log.Write($"Неизвестная клавиша: {part}"); return false; }
            vks.Add(vk.Value);
        }
        if (vks.Count == 0) return false;
        var seq = new List<INPUT>();
        foreach (var vk in vks) seq.Add(Key(vk, false));
        for (int i = vks.Count - 1; i >= 0; i--) seq.Add(Key(vks[i], true));
        Send(seq.ToArray());
        return true;
    }

    public static void Text(string text)
    {
        var seq = new List<INPUT>();
        foreach (var ch in text)
        {
            if (ch == '\n') { seq.Add(Key(0x0D, false)); seq.Add(Key(0x0D, true)); continue; }
            seq.Add(Key(0, false, true, ch));
            seq.Add(Key(0, true, true, ch));
        }
        if (seq.Count > 0) Send(seq.ToArray());
    }
}
