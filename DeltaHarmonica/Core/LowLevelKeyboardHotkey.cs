using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeltaHarmonica.Core;

// Observes physical key transitions on the current desktop. It never suppresses input.
internal sealed class LowLevelKeyboardHotkey : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint LlkfInjected = 0x10;
    private readonly HookProcedure _procedure;
    private nint _hook;

    public event Action<uint, bool>? KeyTransition;

    public LowLevelKeyboardHotkey() => _procedure = Callback;

    public void Start()
    {
        if (_hook != 0) return;
        using var process = Process.GetCurrentProcess();
        var module = GetModuleHandle(process.MainModule?.ModuleName);
        _hook = SetWindowsHookEx(WhKeyboardLl, _procedure, module, 0);
        if (_hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "低层键盘监听注册失败。");
    }

    public void Dispose()
    {
        if (_hook == 0) return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private nint Callback(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var kind = unchecked((uint)message.ToInt64());
            if (kind is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp)
            {
                var key = Marshal.PtrToStructure<KbdLlHookStruct>(data);
                if ((key.Flags & LlkfInjected) == 0)
                {
                    try { KeyTransition?.Invoke(key.VirtualKey, kind is WmKeyDown or WmSysKeyDown); }
                    catch { /* Never let managed exceptions cross the native hook callback. */ }
                }
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VirtualKey, ScanCode, Flags, Time;
        public nint ExtraInfo;
    }

    private delegate nint HookProcedure(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int hookType, HookProcedure procedure, nint module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
}
