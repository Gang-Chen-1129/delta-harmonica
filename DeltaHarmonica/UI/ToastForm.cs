using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DeltaHarmonica.UI;

internal enum ToastTone { Success, Neutral, Error }

// A separate, non-activating window so feedback remains visible while MainForm is behind another app.
internal sealed class ToastForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _watch = new();
    private string _heading = "";
    private string _detail = "";
    private string _symbol = "▶";
    private Color _accent = Theme.Accent;

    public ToastForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(382, 94);
        Font = new Font("Microsoft YaHei UI", 9);
        BackColor = Theme.Navy;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        _timer.Tick += (_, _) => Animate();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00080000 | 0x00000080 | 0x00000020; // no activate, layered, tool, click-through
            return parameters;
        }
    }

    public void ShowMessage(string heading, string detail, ToastTone tone)
    {
        _heading = heading;
        _detail = detail;
        (_accent, _symbol) = tone switch
        {
            ToastTone.Success => (Color.FromArgb(75, 142, 255), "▶"),
            ToastTone.Error => (Color.FromArgb(250, 106, 106), "!"),
            _ => (Color.FromArgb(151, 167, 190), "■")
        };
        var foreground = GetForegroundWindow();
        var area = (foreground != 0 ? Screen.FromHandle(foreground) : Screen.PrimaryScreen)?.WorkingArea
            ?? Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Top + 24);
        Opacity = 0.02;
        if (!Visible) Show();
        _watch.Restart();
        _timer.Start();
        Invalidate();
    }

    private void Animate()
    {
        var elapsed = _watch.Elapsed.TotalMilliseconds;
        if (elapsed < 180) Opacity = Math.Max(0.02, 0.98 * elapsed / 180);
        else if (elapsed < 2600) Opacity = 0.98;
        else if (elapsed < 2950) Opacity = Math.Max(0.02, 0.98 * (2950 - elapsed) / 350);
        else { _timer.Stop(); Hide(); }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 32 || Height < 32) return;
        using var outline = Theme.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), 15);
        var previous = Region;
        Region = new Region(outline);
        previous?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Theme.Navy);
        using var accent = new SolidBrush(_accent);
        using var iconBackground = new SolidBrush(Color.FromArgb(39, 62, 91));
        using var border = new Pen(Color.FromArgb(66, 88, 119));
        using var iconFont = new Font(Font.FontFamily, 16, FontStyle.Bold);
        using var headingFont = new Font(Font.FontFamily, 12, FontStyle.Bold);
        e.Graphics.FillRectangle(accent, 0, 0, 5, Height);
        e.Graphics.FillEllipse(iconBackground, 24, 24, 46, 46);
        e.Graphics.DrawEllipse(border, 24, 24, 46, 46);
        TextRenderer.DrawText(e.Graphics, _symbol, iconFont,
            new Rectangle(24, 25, 46, 44), _accent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(e.Graphics, _heading, headingFont,
            new Rectangle(88, 20, Width - 110, 28), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, _detail, Font,
            new Rectangle(89, 52, Width - 112, 22), Color.FromArgb(187, 201, 219),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
