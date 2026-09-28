using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace DeltaHarmonica.UI;

internal static class Theme
{
    public static readonly Color Canvas = Color.FromArgb(244, 247, 251);
    public static readonly Color Surface = Color.White;
    public static readonly Color Ink = Color.FromArgb(29, 43, 64);
    public static readonly Color Muted = Color.FromArgb(109, 124, 146);
    public static readonly Color Accent = Color.FromArgb(47, 105, 221);
    public static readonly Color AccentHover = Color.FromArgb(32, 85, 191);
    public static readonly Color AccentWash = Color.FromArgb(234, 241, 255);
    public static readonly Color Line = Color.FromArgb(224, 231, 240);
    public static readonly Color Navy = Color.FromArgb(18, 32, 53);
    public static readonly Color Sidebar = Color.FromArgb(24, 42, 68);
    public static readonly Color SidebarMuted = Color.FromArgb(162, 180, 204);

    public static GraphicsPath RoundRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class SurfacePanel : Panel
{
    public SurfacePanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var path = Theme.RoundRect(bounds, 12);
        using var fill = new SolidBrush(Theme.Surface);
        using var pen = new Pen(Theme.Line);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Canvas);
}

internal sealed class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool OnDark { get; set; }

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Height = 40;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
        var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
        var fill = Primary
            ? (hovered ? Theme.AccentHover : Theme.Accent)
            : OnDark
                ? (hovered ? Color.FromArgb(43, 70, 108) : Color.FromArgb(34, 56, 87))
                : (hovered ? Theme.AccentWash : Color.FromArgb(246, 249, 253));
        if (!Enabled) fill = Color.FromArgb(233, 237, 243);
        using var path = Theme.RoundRect(bounds, 8);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (!Primary && !OnDark)
        {
            using var border = new Pen(Theme.Line);
            e.Graphics.DrawPath(border, path);
        }
        var foreground = !Enabled ? Theme.Muted : (Primary || OnDark ? Color.White : Theme.Ink);
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4));
    }
}

internal sealed class NavButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Active { get; set; }

    public NavButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Width = 142;
        Height = 56;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Surface);
        var hovered = ClientRectangle.Contains(PointToClient(Cursor.Position));
        var foreground = Active ? Theme.Accent : (hovered ? Theme.Ink : Theme.Muted);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Active)
        {
            using var brush = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(brush, new Rectangle(22, Height - 4, Width - 44, 3));
        }
    }
}
