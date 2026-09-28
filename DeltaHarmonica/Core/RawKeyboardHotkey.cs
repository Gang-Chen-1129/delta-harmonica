using System.Runtime.InteropServices;

namespace DeltaHarmonica.Core;

// Receives physical keyboard events even when another window owns the foreground.
// This reads WM_INPUT from Windows; it never opens or reads another process.
internal static class RawKeyboardHotkey
{
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const uint RimTypeKeyboard = 1;
    private const ushort RiKeyBreak = 1;

    public static bool Register(nint window)
    {
        var device = new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevInputSink, Target = window };
        return RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    public static void Unregister()
    {
        var device = new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevRemove };
        RegisterRawInputDevices(ref device, 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    public static bool TryRead(nint rawInput, out ushort virtualKey, out bool released)
    {
        virtualKey = 0;
        released = false;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        if (GetRawInputData(rawInput, RidInput, nint.Zero, ref size, headerSize) != 0 ||
            size < headerSize + Marshal.SizeOf<RawKeyboard>() || size > 128)
            return false;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var read = GetRawInputData(rawInput, RidInput, buffer, ref size, headerSize);
            if (read == uint.MaxValue || read < headerSize + Marshal.SizeOf<RawKeyboard>()) return false;
            if (Marshal.PtrToStructure<RawInputHeader>(buffer).Type != RimTypeKeyboard) return false;
            var keyboard = Marshal.PtrToStructure<RawKeyboard>(nint.Add(buffer, (int)headerSize));
            virtualKey = keyboard.VirtualKey;
            released = (keyboard.Flags & RiKeyBreak) != 0;
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(ref RawInputDevice devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size, uint headerSize);
}
