namespace DeltaHarmonica.UI;

internal sealed class CategoryDialog : Form
{
    private readonly string _songsDirectory;
    private readonly TextBox _name = new();
    private readonly Label _feedback = new();
    private readonly ModernButton _create = new();

    public string CategoryName { get; private set; } = "";

    public CategoryDialog(string songsDirectory, Font parentFont)
    {
        _songsDirectory = songsDirectory;
        Text = "创建曲目分类";
        ClientSize = new Size(446, 292);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = parentFont;
        BackColor = Theme.Canvas;

        var header = new Panel { Dock = DockStyle.Top, Height = 94, BackColor = Theme.Navy };
        var icon = new Label
        {
            Text = "♫", Location = new Point(22, 24), Size = new Size(44, 44),
            BackColor = Color.FromArgb(43, 74, 113), ForeColor = Color.FromArgb(143, 190, 255),
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(parentFont.FontFamily, 17, FontStyle.Bold)
        };
        var title = new Label
        {
            Text = "创建新分类", Location = new Point(78, 21), Size = new Size(330, 29),
            ForeColor = Color.White, Font = new Font(parentFont.FontFamily, 14, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = "让曲谱按歌手、风格或用途归档", Location = new Point(79, 55), Size = new Size(330, 22),
            ForeColor = Theme.SidebarMuted, Font = new Font(parentFont.FontFamily, 8.5f)
        };
        header.Controls.AddRange([icon, title, subtitle]);

        var label = new Label
        {
            Text = "分类名称", Location = new Point(26, 116), Size = new Size(390, 25),
            ForeColor = Theme.Ink, Font = new Font(parentFont, FontStyle.Bold)
        };
        var inputFrame = new Panel { Location = new Point(26, 146), Size = new Size(394, 45), BackColor = Theme.Surface };
        inputFrame.Paint += (_, e) =>
        {
            using var border = new Pen(_name.Focused ? Theme.Accent : Theme.Line, _name.Focused ? 2 : 1);
            e.Graphics.DrawRectangle(border, 1, 1, inputFrame.Width - 3, inputFrame.Height - 3);
        };
        _name.Location = new Point(13, 12);
        _name.Width = inputFrame.Width - 26;
        _name.BorderStyle = BorderStyle.None;
        _name.BackColor = Theme.Surface;
        _name.ForeColor = Theme.Ink;
        _name.PlaceholderText = "例如：周杰伦、练习曲、常听";
        _name.GotFocus += (_, _) => inputFrame.Invalidate();
        _name.LostFocus += (_, _) => inputFrame.Invalidate();
        _name.TextChanged += (_, _) => ValidateInput();
        inputFrame.Controls.Add(_name);

        _feedback.Location = new Point(28, 199);
        _feedback.Size = new Size(390, 37);
        _feedback.ForeColor = Theme.Muted;
        _feedback.Font = new Font(parentFont.FontFamily, 8.5f);
        _feedback.Text = "分类会保存在 songs 文件夹中，曲目可稍后移入。";

        var cancel = new ModernButton { Text = "取消", Location = new Point(204, 243), Size = new Size(100, 40) };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _create.Text = "创建分类";
        _create.Location = new Point(312, 243);
        _create.Size = new Size(108, 40);
        _create.Primary = true;
        _create.Enabled = false;
        _create.Click += (_, _) =>
        {
            var candidate = _name.Text.Trim();
            if (ValidationMessage(candidate) is not null) return;
            CategoryName = candidate;
            DialogResult = DialogResult.OK;
            Close();
        };
        AcceptButton = _create;
        CancelButton = cancel;
        Controls.AddRange([header, label, inputFrame, _feedback, cancel, _create]);
        Shown += (_, _) => _name.Focus();
    }

    private void ValidateInput()
    {
        var message = ValidationMessage(_name.Text.Trim());
        _feedback.Text = message ?? "创建后可通过“移动到分类”整理曲目。";
        _feedback.ForeColor = message is null ? Theme.Muted : Color.FromArgb(185, 67, 67);
        _create.Enabled = message is null;
    }

    private string? ValidationMessage(string name)
    {
        if (name.Length == 0) return "请输入分类名称。";
        if (name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny("<>:\"/\\|?*".ToCharArray()) >= 0 || name.Any(char.IsControl))
            return "名称包含 Windows 文件夹不允许的字符。";
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reserved.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase)) return "这个名称是 Windows 保留名称。";
        if (Directory.Exists(Path.Combine(_songsDirectory, name)) || File.Exists(Path.Combine(_songsDirectory, name)))
            return "这个分类名称已存在。";
        return null;
    }
}
