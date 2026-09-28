using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DeltaHarmonica.Core;

public sealed class WinInput
{
    private readonly object _gate = new();
    private readonly HashSet<ushort> _downScanCodes = [];
    private readonly HashSet<uint> _downMouse = [];

    public void PrepareModifiers(KeyPlan plan)
    {
        lock (_gate)
        {
            try
            {
                if (plan.Low) MouseDown(0x0002);
                if (plan.High) MouseDown(0x0008);
                if (plan.Sharp) MouseDown(0x0020);
            }
            catch { ReleaseAll(); throw; }
        }
    }

    public void PressKey(KeyPlan plan)
    {
        lock (_gate)
        {
            try { KeyDown(ToScanCode(plan.Key)); }
            catch { ReleaseAll(); throw; }
        }
    }

    public void Release(KeyPlan plan)
    {
        lock (_gate)
        {
            KeyUp(ToScanCode(plan.Key));
            if (plan.Sharp) MouseUp(0x0020);
            if (plan.High) MouseUp(0x0008);
            if (plan.Low) MouseUp(0x0002);
        }
    }

    public void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (var scanCode in _downScanCodes.ToArray()) TrySend(KeyInput(scanCode, true));
            foreach (var flag in _downMouse.ToArray()) TrySend(MouseInput(flag switch { 0x0002 => 0x0004u, 0x0008 => 0x0010u, _ => 0x0040u }));
            _downScanCodes.Clear();
            _downMouse.Clear();
        }
    }

    // Set 1 scan codes for the physical Z X C V B N M , keys.
    private static ushort ToScanCode(char key) => char.ToUpperInvariant(key) switch
    {
        'Z' => 0x2C, 'X' => 0x2D, 'C' => 0x2E, 'V' => 0x2F,
        'B' => 0x30, 'N' => 0x31, 'M' => 0x32, ',' => 0x33,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "不支持的演奏键。")
    };

    private void KeyDown(ushort scanCode) { Send(KeyInput(scanCode, false)); _downScanCodes.Add(scanCode); }
    private void KeyUp(ushort scanCode) { if (_downScanCodes.Contains(scanCode)) { Send(KeyInput(scanCode, true)); _downScanCodes.Remove(scanCode); } }
    private void MouseDown(uint flag) { Send(MouseInput(flag)); _downMouse.Add(flag); }
    private void MouseUp(uint flag)
    {
        if (_downMouse.Contains(flag))
        {
            Send(MouseInput(flag switch { 0x0002 => 0x0004u, 0x0008 => 0x0010u, _ => 0x0040u }));
            _downMouse.Remove(flag);
        }
    }

    private static INPUT KeyInput(ushort scanCode, bool up) => new()
    {
        Type = 1,
        Data = new InputUnion { Keyboard = new KEYBDINPUT { Scan = scanCode, Flags = 0x0008u | (up ? 0x0002u : 0) } }
    };
    private static INPUT MouseInput(uint flag) => new()
    {
        Type = 0,
        Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = flag } }
    };
    private static void Send(INPUT input)
    {
        if (SendInput(1, ref input, Marshal.SizeOf<INPUT>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "发送键鼠事件失败。");
    }
    private static void TrySend(INPUT input) { try { Send(input); } catch { /* closing safely */ } }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public nint ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, ref INPUT input, int size);
}
