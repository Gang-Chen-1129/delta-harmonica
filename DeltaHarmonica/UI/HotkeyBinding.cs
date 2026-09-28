using System.Runtime.InteropServices;

namespace DeltaHarmonica.UI;

internal readonly record struct HotkeyBinding(Keys Key, uint Modifiers)
{
    private const uint Alt = 0x0001;
    private const uint Control = 0x0002;
    private const uint Shift = 0x0004;
    public static HotkeyBinding Default => new(Keys.F8, 0);
    public uint VirtualKey => (uint)Key;
    public string Code => Prefix + Key;
    public string DisplayName => Prefix + KeyName(Key);

    private string Prefix =>
        ((Modifiers & Control) != 0 ? "Ctrl+" : "") +
        ((Modifiers & Alt) != 0 ? "Alt+" : "") +
        ((Modifiers & Shift) != 0 ? "Shift+" : "");

    public static bool TryCreate(Keys keyData, out HotkeyBinding binding)
    {
        var key = keyData & Keys.KeyCode;
        if (!IsUsable(key)) { binding = Default; return false; }
        var modifiers = 0u;
        if ((keyData & Keys.Control) != 0) modifiers |= Control;
        if ((keyData & Keys.Alt) != 0) modifiers |= Alt;
        if ((keyData & Keys.Shift) != 0) modifiers |= Shift;
        binding = new(key, modifiers);
        return true;
    }

    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var modifiers = 0u;
        foreach (var part in parts[..^1])
        {
            var flag = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Control,
                "alt" => Alt,
                "shift" => Shift,
                _ => 0u
            };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        var keyText = parts[^1];
        if (keyText.Length == 1 && keyText[0] is >= '0' and <= '9') keyText = "D" + keyText;
        if (!Enum.TryParse<Keys>(keyText, true, out var key) || !IsUsable(key)) return false;
        binding = new(key, modifiers);
        return true;
    }

    public bool MatchesCurrentModifiers()
    {
        return Down(0x11) == ((Modifiers & Control) != 0) &&
               Down(0x12) == ((Modifiers & Alt) != 0) &&
               Down(0x10) == ((Modifiers & Shift) != 0) &&
               !Down(0x5B) && !Down(0x5C);
    }

    public bool IsKeyDown() => Down((int)Key);

    private static bool Down(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static bool IsUsable(Keys key)
    {
        var value = (uint)key;
        return value is >= 0x08 and <= 0xFE && key is not (
            Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or
            Keys.Packet or Keys.ProcessKey);
    }

    private static string KeyName(Keys key)
    {
        if (key is >= Keys.D0 and <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return "小键盘 " + ((int)key - (int)Keys.NumPad0);
        return key switch
        {
            Keys.Space => "空格",
            Keys.Return => "回车",
            Keys.Tab => "Tab",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            _ => key.ToString()
        };
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}

internal sealed class HotkeyCaptureDialog : Form
{
    private readonly Label _prompt;
    public HotkeyBinding Binding { get; private set; }

    public HotkeyCaptureDialog(HotkeyBinding current)
    {
        Binding = current;
        Text = "设置全局热键";
        ClientSize = new Size(420, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Theme.Canvas;
        _prompt = new Label
        {
            Text = "请按下要用的按键，也可按住 Ctrl、Alt 或 Shift 再按主键。",
            ForeColor = Theme.Ink,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(25, 25),
            Size = new Size(370, 65),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var currentLabel = new Label
        {
            Text = $"当前：{current.DisplayName}    ·    Esc 取消",
            ForeColor = Theme.Muted,
            Location = new Point(25, 101),
            Size = new Size(370, 26),
            TextAlign = ContentAlignment.MiddleCenter
        };
        var cancel = new ModernButton { Text = "取消", Size = new Size(100, 39), Location = new Point(160, 140) };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.AddRange([_prompt, currentLabel, cancel]);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            else CaptureKey(e.KeyData);
            e.SuppressKeyPress = true;
        };
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        if (CaptureKey(keyData)) return true;
        return base.ProcessDialogKey(keyData);
    }

    private bool CaptureKey(Keys keyData)
    {
        if (!HotkeyBinding.TryCreate(keyData, out var binding))
        {
            _prompt.Text = "继续按下一个主键（单独的修饰键不能使用）";
            return false;
        }
        Binding = binding;
        DialogResult = DialogResult.OK;
        Close();
        return true;
    }
}
