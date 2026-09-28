using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualBasic.FileIO;
using DeltaHarmonica.Core;

namespace DeltaHarmonica.UI;

public sealed class MainForm : Form
{
    private const int HotkeyId = 0xD311;
    private const int WmHotkey = 0x0312;
    private const int WmInput = 0x00FF;
    private static readonly TimeSpan ButtonStartDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan HotkeyStartDelay = TimeSpan.FromMilliseconds(6500);
    private readonly string _root = AppContext.BaseDirectory;
    private readonly string _songsDir;
    private readonly string _settingsPath;
    private readonly AppSettings _settings;
    private readonly PlaybackEngine _player = new();
    private readonly LowLevelKeyboardHotkey _lowLevelKeyboard = new();
    private readonly System.Windows.Forms.Timer _hotkeyPoll = new() { Interval = 15 };
    private readonly ToastForm _toast = new();
    private readonly ListBox _songList = new();
    private readonly ListBox _categoryList = new();
    private readonly Label _libraryCount = new();
    private readonly TextBox _songSearch = new();
    private readonly ToolTip _songToolTip = new();
    private readonly SplitContainer _librarySplit = new();
    private readonly DataGridView _grid = new();
    private readonly Label _title = new();
    private readonly Label _status = new();
    private readonly Label _noteCount = new();
    private readonly TextBox _midiPath = new();
    private readonly Label _convertStatus = new();
    private readonly ModernButton _convertButton = new();
    private readonly CheckBox _trimMidiIntro = new();
    private readonly NumericUpDown _baseMidi = new();
    private readonly NumericUpDown _transpose = new();
    private readonly NumericUpDown _speed = new();
    private readonly ModernButton _hotkey = new();
    private readonly Panel _pageHost = new();
    private readonly List<Control> _pages = [];
    private readonly List<NavButton> _navButtons = [];
    private Score? _current;
    private string? _currentPath;
    private bool _dirty;
    private bool _loading;
    private bool _hotkeyRegistered;
    private bool _rawKeyboardRegistered;
    private bool _lowLevelKeyboardRegistered;
    private bool _rawHotkeyDown;
    private bool _lowLevelHotkeyDown;
    private bool _pollHotkeyDown;
    private bool _suppressHotkeyUntilReleased;
    private long _lastHotkeyTrigger;
    private HotkeyBinding _chosenHotkey = HotkeyBinding.Default;
    private CancellationTokenSource? _countdown;

    public MainForm()
    {
        _songsDir = Path.Combine(_root, "songs");
        _settingsPath = Path.Combine(_root, "settings.json");
        Directory.CreateDirectory(_songsDir);
        _settings = AppSettings.Load(_settingsPath);
        if (!HotkeyBinding.TryParse(_settings.Hotkey, out _chosenHotkey))
            _settings.Hotkey = _chosenHotkey.Code;
        Text = "三角洲口琴 · 曲谱与演奏";
        MinimumSize = new Size(980, 640);
        Size = new Size(1080, Math.Min(740, Math.Max(640, Screen.PrimaryScreen?.WorkingArea.Height - 16 ?? 740)));
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = Theme.Canvas;
        BuildUi();
        _hotkeyPoll.Tick += (_, _) => PollChosenHotkey();
        _lowLevelKeyboard.KeyTransition += OnLowLevelKeyTransition;
        _player.Progress += (i, total) => SafeUi(() => SetStatus($"播放中 · {i}/{total}"));
        _player.Finished += message => SafeUi(() =>
        {
            SetStatus(message);
            if (message.StartsWith("播放失败：", StringComparison.Ordinal))
                _toast.ShowMessage("演奏输入失败", message, ToastTone.Error);
        });
        Shown += (_, _) =>
        {
            _librarySplit.Panel1MinSize = 240;
            _librarySplit.Panel2MinSize = 600;
            _librarySplit.SplitterDistance = Math.Clamp(_settings.LibraryWidth, 240,
                Math.Max(240, _librarySplit.ClientSize.Width - _librarySplit.Panel2MinSize - _librarySplit.SplitterWidth));
            RefreshSongs();
            RegisterChosenHotkey();
        };
        FormClosing += (_, e) =>
        {
            if (!PromptSaveIfDirty()) { e.Cancel = true; return; }
            _countdown?.Cancel();
            _player.StopAsync().GetAwaiter().GetResult();
            UnregisterChosenHotkey();
            _lowLevelKeyboard.Dispose();
            _hotkeyPoll.Dispose();
            _toast.Close();
            _toast.Dispose();
            _songToolTip.Dispose();
        };
    }

    private void BuildUi()
    {
        var heading = new Panel { Dock = DockStyle.Top, Height = 75, BackColor = Theme.Navy };
        heading.Controls.Add(new Label
        {
            Text = "三角洲口琴", ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 21, FontStyle.Bold),
            Location = new Point(29, 10), AutoSize = true
        });
        heading.Controls.Add(new Label
        {
            Text = "曲谱管理  /  MIDI 导入  /  键鼠演奏", ForeColor = Theme.SidebarMuted,
            Font = new Font(Font.FontFamily, 9), Location = new Point(31, 49), AutoSize = true
        });
        var badge = new Label
        {
            Text = "本机处理  ·  便携版", ForeColor = Color.FromArgb(205, 220, 240),
            Font = new Font(Font.FontFamily, 9), TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(168, 34), Location = new Point(Width - 207, 20),
            BackColor = Color.FromArgb(34, 55, 83)
        };
        heading.Controls.Add(badge);
        heading.Resize += (_, _) => badge.Left = heading.ClientSize.Width - badge.Width - 29;

        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Padding = new Padding(16, 0, 16, 14) };
        var libraryHeader = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Sidebar };
        libraryHeader.Controls.Add(new Label
        {
            Text = "曲谱库", ForeColor = Color.White, Font = new Font(Font.FontFamily, 13, FontStyle.Bold),
            Location = new Point(6, 10), AutoSize = true
        });
        _libraryCount.Text = "0 首曲目";
        _libraryCount.ForeColor = Theme.SidebarMuted;
        _libraryCount.Font = new Font(Font.FontFamily, 8.5f);
        _libraryCount.Location = new Point(7, 38);
        _libraryCount.AutoSize = true;
        libraryHeader.Controls.Add(_libraryCount);

        var categorySection = new Panel { Dock = DockStyle.Top, Height = 148, BackColor = Theme.Sidebar, Padding = new Padding(0, 0, 0, 8) };
        var categoryToolbar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Theme.Sidebar };
        var categoryHeading = new Label
        {
            Text = "分类", ForeColor = Theme.SidebarMuted, Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Dock = DockStyle.Fill, Padding = new Padding(7, 6, 0, 0)
        };
        var newCategory = new ModernButton { Text = "＋ 新建", Dock = DockStyle.Right, Width = 85, Height = 31, OnDark = true,
            Font = new Font(Font.FontFamily, 8.5f) };
        newCategory.Click += (_, _) => CreateCategory();
        categoryToolbar.Controls.Add(categoryHeading);
        categoryToolbar.Controls.Add(newCategory);
        _categoryList.Dock = DockStyle.Fill;
        _categoryList.BorderStyle = BorderStyle.None;
        _categoryList.BackColor = Theme.Sidebar;
        _categoryList.ForeColor = Color.White;
        _categoryList.DrawMode = DrawMode.OwnerDrawFixed;
        _categoryList.ItemHeight = 36;
        _categoryList.IntegralHeight = false;
        _categoryList.DrawItem += DrawCategoryItem;
        _categoryList.SelectedIndexChanged += (_, _) => { if (!_loading) RefreshSongs(keepCurrentOnly: true); };
        categorySection.Controls.Add(_categoryList);
        categorySection.Controls.Add(categoryToolbar);

        var searchSection = new Panel { Dock = DockStyle.Top, Height = 68, BackColor = Theme.Sidebar, Padding = new Padding(0, 4, 0, 8) };
        var searchLabel = new Label
        {
            Text = "搜索歌名", ForeColor = Theme.SidebarMuted, Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Dock = DockStyle.Top, Height = 20, Padding = new Padding(7, 0, 0, 0)
        };
        var searchBox = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 57, 88), Padding = new Padding(10, 6, 8, 4) };
        var searchIcon = new Label { Text = "⌕", ForeColor = Theme.SidebarMuted, Dock = DockStyle.Left, Width = 25,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Font.FontFamily, 14) };
        _songSearch.Dock = DockStyle.Fill;
        _songSearch.PlaceholderText = "输入歌曲名查找";
        _songSearch.BorderStyle = BorderStyle.None;
        _songSearch.BackColor = searchBox.BackColor;
        _songSearch.ForeColor = Color.White;
        _songSearch.TextChanged += (_, _) => { if (!_loading) RefreshSongs(keepCurrentOnly: true); };
        searchBox.Controls.Add(_songSearch);
        searchBox.Controls.Add(searchIcon);
        searchSection.Controls.Add(searchBox);
        searchSection.Controls.Add(searchLabel);
        var sideActions = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 168, BackColor = Theme.Sidebar,
            ColumnCount = 2, RowCount = 4, Padding = new Padding(0, 4, 0, 0)
        };
        sideActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        sideActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        sideActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        sideActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        sideActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        sideActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        var create = ActionButton("＋  新建曲谱", (_, _) => NewScore(), true);
        create.Dock = DockStyle.Fill;
        sideActions.Controls.Add(create, 0, 0);
        sideActions.SetColumnSpan(create, 2);
        var refresh = ActionButton("刷新", (_, _) => RefreshSongs(), onDark: true);
        var open = ActionButton("打开目录", (_, _) => Process.Start(new ProcessStartInfo(_songsDir) { UseShellExecute = true }), onDark: true);
        refresh.Dock = DockStyle.Fill;
        open.Dock = DockStyle.Fill;
        sideActions.Controls.Add(refresh, 0, 1);
        sideActions.Controls.Add(open, 1, 1);
        var moveCategory = ActionButton("移动到分类", (sender, _) => ShowMoveCategoryMenu((Control)sender!), onDark: true);
        moveCategory.Dock = DockStyle.Fill;
        sideActions.Controls.Add(moveCategory, 0, 2);
        sideActions.SetColumnSpan(moveCategory, 2);
        var deleteSong = ActionButton("删除所选曲目", async (_, _) => await DeleteSelectedSongAsync(), onDark: true);
        deleteSong.Dock = DockStyle.Fill;
        sideActions.Controls.Add(deleteSong, 0, 3);
        sideActions.SetColumnSpan(deleteSong, 2);
        _songList.Dock = DockStyle.Fill;
        _songList.BorderStyle = BorderStyle.None;
        _songList.BackColor = Theme.Sidebar;
        _songList.ForeColor = Color.White;
        _songList.DrawMode = DrawMode.OwnerDrawFixed;
        _songList.ItemHeight = 48;
        _songList.IntegralHeight = false;
        _songList.DrawItem += DrawSongItem;
        _songList.SelectedIndexChanged += (_, _) => LoadSelectedSong();
        _songList.MouseMove += ShowSongToolTip;
        sidebar.Controls.Add(_songList);
        sidebar.Controls.Add(sideActions);
        sidebar.Controls.Add(searchSection);
        sidebar.Controls.Add(categorySection);
        sidebar.Controls.Add(libraryHeader);

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
        var nav = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Surface };
        var navFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, Width = 470, Height = 57, WrapContents = false,
            Padding = new Padding(18, 0, 0, 0), BackColor = Theme.Surface
        };
        var captions = new[] { "曲谱与演奏", "导入 MIDI", "设置" };
        for (var i = 0; i < captions.Length; i++)
        {
            var index = i;
            var button = new NavButton { Text = captions[i], Font = new Font(Font.FontFamily, 10, FontStyle.Bold), Margin = Padding.Empty };
            button.Click += (_, _) => SelectPage(index);
            navFlow.Controls.Add(button);
            _navButtons.Add(button);
        }
        nav.Controls.Add(navFlow);
        nav.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Line });
        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = Theme.Canvas;
        _pages.AddRange([BuildScorePage(), BuildConvertPage(), BuildSettingsPage()]);
        foreach (var page in _pages) { page.Dock = DockStyle.Fill; _pageHost.Controls.Add(page); }
        content.Controls.Add(_pageHost);
        content.Controls.Add(nav);

        _librarySplit.Dock = DockStyle.Fill;
        _librarySplit.Orientation = Orientation.Vertical;
        _librarySplit.SplitterWidth = 7;
        _librarySplit.Panel1.Controls.Add(sidebar);
        _librarySplit.Panel2.Controls.Add(content);
        _librarySplit.SplitterMoved += (_, _) => { _settings.LibraryWidth = _librarySplit.SplitterDistance; _settings.Save(_settingsPath); _songList.Invalidate(); };
        Controls.Add(_librarySplit);
        Controls.Add(heading);
        SelectPage(0);
    }

    private void DrawSongItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        using (var background = new SolidBrush(Theme.Sidebar)) e.Graphics.FillRectangle(background, e.Bounds);
        var selected = (e.State & DrawItemState.Selected) != 0;
        var bounds = new Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 3, e.Bounds.Width - 5, e.Bounds.Height - 6);
        if (selected)
        {
            using var path = Theme.RoundRect(bounds, 8);
            using var brush = new SolidBrush(Color.FromArgb(42, 78, 130));
            e.Graphics.FillPath(brush, path);
        }
        var item = (SongItem)_songList.Items[e.Index];
        var text = item.ToString();
        var category = CategoryNameForPath(item.Path);
        var color = selected ? Color.White : Color.FromArgb(214, 225, 240);
        using var iconFont = new Font(Font.FontFamily, 12);
        TextRenderer.DrawText(e.Graphics, "♪", iconFont,
            new Rectangle(bounds.Left + 13, bounds.Top + 1, 23, bounds.Height - 2),
            selected ? Color.FromArgb(119, 175, 255) : Theme.SidebarMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(e.Graphics, text, Font,
            new Rectangle(bounds.Left + 43, bounds.Top + 3, bounds.Width - 54, 23),
            color, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var categoryFont = new Font(Font.FontFamily, 8);
        TextRenderer.DrawText(e.Graphics, category, categoryFont,
            new Rectangle(bounds.Left + 43, bounds.Top + 26, bounds.Width - 54, 16),
            Theme.SidebarMuted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }

    private void DrawCategoryItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        using (var background = new SolidBrush(Theme.Sidebar)) e.Graphics.FillRectangle(background, e.Bounds);
        var category = (CategoryItem)_categoryList.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) != 0;
        var row = new Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 2, e.Bounds.Width - 5, e.Bounds.Height - 4);
        if (selected)
        {
            using var path = Theme.RoundRect(row, 8);
            using var fill = new SolidBrush(Color.FromArgb(43, 77, 124));
            e.Graphics.FillPath(fill, path);
            using var accent = new SolidBrush(Color.FromArgb(101, 165, 255));
            e.Graphics.FillRectangle(accent, row.Left, row.Top + 7, 3, row.Height - 14);
        }
        var symbol = category.DirectoryPath is null ? "▦" :
            string.Equals(category.DirectoryPath, _songsDir, StringComparison.OrdinalIgnoreCase) ? "◫" : "♫";
        var foreground = selected ? Color.White : Color.FromArgb(215, 226, 241);
        TextRenderer.DrawText(e.Graphics, symbol, Font,
            new Rectangle(row.Left + 10, row.Top, 24, row.Height), selected ? Color.FromArgb(145, 192, 255) : Theme.SidebarMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
        TextRenderer.DrawText(e.Graphics, category.Name, Font,
            new Rectangle(row.Left + 40, row.Top, Math.Max(30, row.Width - 91), row.Height), foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var countFont = new Font(Font.FontFamily, 8);
        TextRenderer.DrawText(e.Graphics, category.Count.ToString(), countFont,
            new Rectangle(row.Right - 44, row.Top, 36, row.Height), Theme.SidebarMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine);
    }

    private void SelectPage(int index)
    {
        for (var i = 0; i < _pages.Count; i++)
        {
            _pages[i].Visible = i == index;
            _navButtons[i].Active = i == index;
            _navButtons[i].Invalidate();
        }
        _pages[index].BringToFront();
    }

    private Panel BuildScorePage()
    {
        var page = Page();
        var summary = new SurfacePanel { Dock = DockStyle.Top, Height = 102 };
        _title.Text = "请选择曲目";
        _title.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
        _title.ForeColor = Theme.Ink;
        _title.Location = new Point(21, 16);
        _title.Size = new Size(510, 38);
        _title.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _title.AutoEllipsis = true;
        _noteCount.Text = "";
        _noteCount.ForeColor = Theme.Muted;
        _noteCount.Location = new Point(23, 59);
        _noteCount.Size = new Size(320, 27);
        _status.Text = "就绪";
        _status.ForeColor = Theme.Accent;
        _status.TextAlign = ContentAlignment.MiddleRight;
        _status.Location = new Point(550, 30);
        _status.Size = new Size(250, 34);
        summary.Controls.AddRange([_title, _noteCount, _status]);
        summary.Resize += (_, _) =>
        {
            _status.Left = Math.Max(350, summary.ClientSize.Width - _status.Width - 21);
            _title.Width = Math.Max(250, _status.Left - _title.Left - 18);
        };

        var actionCard = new SurfacePanel { Dock = DockStyle.Top, Height = 68 };
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(13, 12, 0, 8), WrapContents = false, BackColor = Theme.Surface };
        tools.Controls.Add(ActionButton("▶  开始播放", async (_, _) => await StartPlaybackAsync(ButtonStartDelay), true));
        tools.Controls.Add(ActionButton("■  停止", async (_, _) => await StopPlaybackAsync()));
        tools.Controls.Add(ActionButton("保存曲谱", (_, _) => SaveScore()));
        tools.Controls.Add(ActionButton("＋  添加音符", (_, _) => AddNote()));
        tools.Controls.Add(ActionButton("删除选中", (_, _) => DeleteSelectedNotes()));
        actionCard.Controls.Add(tools);

        var gridCard = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(13, 0, 13, 14) };
        var gridHeading = new Panel { Dock = DockStyle.Top, Height = 53, BackColor = Theme.Surface };
        gridHeading.Controls.Add(new Label
        {
            Text = "音符列表", Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            ForeColor = Theme.Ink, Location = new Point(9, 16), AutoSize = true
        });
        gridHeading.Controls.Add(new Label
        {
            Text = "双击单元格可编辑", Font = new Font(Font.FontFamily, 8.5f),
            ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleRight,
            Size = new Size(180, 30), Location = new Point(490, 11)
        });
        gridHeading.Resize += (_, _) => gridHeading.Controls[1].Left = gridHeading.ClientSize.Width - gridHeading.Controls[1].Width - 11;

        _grid.Dock = DockStyle.Fill;
        _grid.BackgroundColor = Theme.Surface;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Theme.Line;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersHeight = 39;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 243, 249);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Muted;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(238, 243, 249);
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.Muted;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font.FontFamily, 9, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 0, 0);
        _grid.DefaultCellStyle.BackColor = Theme.Surface;
        _grid.DefaultCellStyle.ForeColor = Theme.Ink;
        _grid.DefaultCellStyle.SelectionBackColor = Theme.AccentWash;
        _grid.DefaultCellStyle.SelectionForeColor = Theme.Ink;
        _grid.DefaultCellStyle.Padding = new Padding(10, 0, 0, 0);
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
        _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = Theme.AccentWash;
        _grid.RowTemplate.Height = 38;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Start", HeaderText = "开始时间（毫秒）", FillWeight = 28 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Midi", HeaderText = "音高（MIDI）", FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Duration", HeaderText = "持续时间（毫秒）", FillWeight = 28 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Keys", HeaderText = "演奏按键", ReadOnly = true, FillWeight = 35 });
        _grid.CellEndEdit += (_, e) => { _dirty = true; UpdateKeyPreview(e.RowIndex); _title.Text = (_current?.Title ?? "未命名曲目") + " *"; };
        gridCard.Controls.Add(_grid);
        gridCard.Controls.Add(gridHeading);
        page.Controls.Add(gridCard);
        page.Controls.Add(Spacer(12));
        page.Controls.Add(actionCard);
        page.Controls.Add(Spacer(12));
        page.Controls.Add(summary);
        return page;
    }

    private Panel BuildConvertPage()
    {
        var page = Page();
        page.AutoScroll = true;
        page.AutoScrollMinSize = new Size(0, 550);
        var intro = Intro("导入 MIDI 曲谱", "选择 MIDI 文件和旋律音轨，直接生成可演奏的 TXT 曲谱。");

        var fileCard = new SurfacePanel { Dock = DockStyle.Top, Height = 140 };
        var fileLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(21, 15, 21, 17), ColumnCount = 2, RowCount = 3 };
        fileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        fileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        fileLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        fileLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fileLayout.Controls.Add(SectionTitle("01  选择 MIDI 文件"), 0, 0);
        fileLayout.SetColumnSpan(fileLayout.GetControlFromPosition(0, 0)!, 2);
        var fileHint = Hint("支持 .mid 和 .midi；多音轨文件可手动选择主旋律音轨。");
        fileLayout.Controls.Add(fileHint, 0, 1);
        fileLayout.SetColumnSpan(fileHint, 2);
        _midiPath.Dock = DockStyle.Fill;
        _midiPath.BorderStyle = BorderStyle.FixedSingle;
        _midiPath.Margin = new Padding(0, 3, 9, 3);
        _midiPath.BackColor = Color.FromArgb(250, 252, 255);
        fileLayout.Controls.Add(_midiPath, 0, 2);
        var browse = ActionButton("选择文件", (_, _) => BrowseInput());
        browse.Dock = DockStyle.Fill;
        fileLayout.Controls.Add(browse, 1, 2);
        fileCard.Controls.Add(fileLayout);

        var optionsCard = new SurfacePanel { Dock = DockStyle.Top, Height = 113 };
        var optionsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(21, 15, 21, 14), ColumnCount = 1, RowCount = 3 };
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        optionsLayout.Controls.Add(SectionTitle("02  导入设置"), 0, 0);
        _trimMidiIntro.Text = "跳过开头空白，导入后从第一个音符开始播放";
        _trimMidiIntro.Checked = true;
        _trimMidiIntro.AutoSize = true;
        _trimMidiIntro.BackColor = Theme.Surface;
        optionsLayout.Controls.Add(_trimMidiIntro, 0, 1);
        optionsLayout.Controls.Add(Hint("保留 MIDI 的音高与节奏；超出口琴键位的音按八度折回。"), 0, 2);
        optionsCard.Controls.Add(optionsLayout);

        var progressCard = new SurfacePanel { Dock = DockStyle.Top, Height = 105 };
        var progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(21, 16, 21, 18), ColumnCount = 1, RowCount = 2 };
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        progressLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Theme.Surface };
        _convertButton.Text = "导入 MIDI";
        StyleButton(_convertButton, true);
        _convertButton.Click += async (_, _) => await ImportMidiAsync();
        buttons.Controls.Add(_convertButton);
        progressLayout.Controls.Add(buttons, 0, 0);
        _convertStatus.Text = "等待选择 MIDI 文件";
        _convertStatus.ForeColor = Theme.Muted;
        _convertStatus.Dock = DockStyle.Fill;
        _convertStatus.TextAlign = ContentAlignment.MiddleLeft;
        progressLayout.Controls.Add(_convertStatus, 0, 1);
        progressCard.Controls.Add(progressLayout);

        page.Controls.Add(progressCard);
        page.Controls.Add(Spacer(10));
        page.Controls.Add(optionsCard);
        page.Controls.Add(Spacer(10));
        page.Controls.Add(fileCard);
        page.Controls.Add(intro);
        return page;
    }

    private Panel BuildSettingsPage()
    {
        var page = Page();
        page.AutoScroll = true;
        page.AutoScrollMinSize = new Size(0, 465);
        var intro = Intro("演奏设置", "根据口琴实际音高调整映射；点击热键按钮后直接按下想用的按键。");
        var card = new SurfacePanel { Dock = DockStyle.Top, Height = 338 };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 17, 24, 16), ColumnCount = 2, RowCount = 5 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        ConfigureNumber(_baseMidi, 21, 108, _settings.BaseMidi, 1, 0);
        ConfigureNumber(_transpose, -24, 24, _settings.Transpose, 1, 0);
        ConfigureNumber(_speed, 0.25m, 2m, (decimal)Math.Clamp(_settings.Speed, 0.25, 2), 0.05m, 2);
        _hotkey.Text = $"{_chosenHotkey.DisplayName}  ·  点击修改";
        _hotkey.Width = 230;
        StyleButton(_hotkey, false);
        AddSetting(panel, 0, "Z 键音高（MIDI）", "校准口琴的起始音", _baseMidi);
        AddSetting(panel, 1, "播放移调（半音）", "整体升高或降低曲谱音高", _transpose);
        AddSetting(panel, 2, "播放速度", "0.25 至 2 倍速", _speed);
        AddSetting(panel, 3, "全局启停热键", "约 6.5 秒后开始；再按可取消或停止", _hotkey);
        var note = Hint("默认 Z = MIDI 60。鼠标左键按住降八度，右键按住升八度，中键按住升半音。");
        note.Dock = DockStyle.Fill;
        note.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(note, 0, 4);
        panel.SetColumnSpan(note, 2);
        _baseMidi.ValueChanged += (_, _) => SaveSettings();
        _transpose.ValueChanged += (_, _) => SaveSettings();
        _speed.ValueChanged += (_, _) => SaveSettings();
        _hotkey.Click += (_, _) => CaptureHotkey();
        card.Controls.Add(panel);
        page.Controls.Add(card);
        page.Controls.Add(intro);
        return page;
    }

    private void AddSetting(TableLayoutPanel panel, int row, string label, string description, Control control)
    {
        var text = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        text.Controls.Add(new Label { Text = label, ForeColor = Theme.Ink, Font = new Font(Font, FontStyle.Bold), Location = new Point(1, 6), AutoSize = true });
        text.Controls.Add(new Label { Text = description, ForeColor = Theme.Muted, Font = new Font(Font.FontFamily, 8.5f), Location = new Point(1, 31), AutoSize = true });
        panel.Controls.Add(text, 0, row);
        control.Anchor = AnchorStyles.Left;
        panel.Controls.Add(control, 1, row);
    }

    private static void ConfigureNumber(NumericUpDown control, decimal min, decimal max, decimal value, decimal step, int decimals)
    {
        control.Minimum = min; control.Maximum = max; control.Value = Math.Clamp(value, min, max);
        control.Increment = step; control.DecimalPlaces = decimals; control.Width = 145; control.Height = 32;
    }

    private static Panel Page() => new() { BackColor = Theme.Canvas, Padding = new Padding(23, 20, 23, 22) };
    private static Panel Spacer(int height) => new() { Dock = DockStyle.Top, Height = height, BackColor = Theme.Canvas };
    private Panel Intro(string title, string description)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 75, BackColor = Theme.Canvas };
        panel.Controls.Add(new Label { Text = title, ForeColor = Theme.Ink, Font = new Font(Font.FontFamily, 19, FontStyle.Bold), Location = new Point(1, 3), AutoSize = true });
        panel.Controls.Add(new Label { Text = description, ForeColor = Theme.Muted, Font = new Font(Font.FontFamily, 9), Location = new Point(2, 45), AutoSize = true });
        return panel;
    }
    private Label SectionTitle(string text) => new() { Text = text, ForeColor = Theme.Ink, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), AutoSize = true, Anchor = AnchorStyles.Left };
    private Label Hint(string text) => new() { Text = text, ForeColor = Theme.Muted, Font = new Font(Font.FontFamily, 9), AutoSize = true, Anchor = AnchorStyles.Left };
    private static ModernButton ActionButton(string text, EventHandler handler, bool primary = false, bool onDark = false)
    {
        var button = new ModernButton { Text = text, Width = Math.Max(82, text.Length * 12 + 28), Height = 40, Margin = new Padding(3) };
        StyleButton(button, primary, onDark);
        button.Click += handler;
        return button;
    }
    private static void StyleButton(ModernButton button, bool primary, bool onDark = false)
    {
        button.Primary = primary;
        button.OnDark = onDark;
        button.Width = Math.Max(96, button.Text.Length * 12 + 35);
        button.Height = 40;
        button.Invalidate();
    }

    private void RefreshCategories(string? selectDirectory = null)
    {
        var selected = selectDirectory ?? (_categoryList.SelectedItem as CategoryItem)?.DirectoryPath;
        _loading = true;
        _categoryList.Items.Clear();
        var directories = Directory.GetDirectories(_songsDir).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var rootCount = Directory.GetFiles(_songsDir, "*.txt").Length;
        var counts = directories.Select(directory => (Directory: directory, Count: Directory.GetFiles(directory, "*.txt").Length)).ToList();
        _categoryList.Items.Add(new CategoryItem("全部曲目", null, rootCount + counts.Sum(entry => entry.Count)));
        _categoryList.Items.Add(new CategoryItem("未分类", _songsDir, rootCount));
        foreach (var entry in counts)
            _categoryList.Items.Add(new CategoryItem(Path.GetFileName(entry.Directory), entry.Directory, entry.Count));
        var index = _categoryList.Items.Cast<CategoryItem>().ToList()
            .FindIndex(item => string.Equals(item.DirectoryPath, selected, StringComparison.OrdinalIgnoreCase));
        _categoryList.SelectedIndex = index >= 0 ? index : 0;
        _loading = false;
    }

    private void RefreshSongs(string? selectPath = null, bool keepCurrentOnly = false)
    {
        RefreshCategories();
        var selected = selectPath ?? _currentPath;
        var directory = (_categoryList.SelectedItem as CategoryItem)?.DirectoryPath;
        var directories = directory is null
            ? new[] { _songsDir }.Concat(Directory.GetDirectories(_songsDir))
            : [directory];
        var search = _songSearch.Text.Trim();
        _loading = true;
        _songList.Items.Clear();
        foreach (var path in directories.SelectMany(folder => Directory.GetFiles(folder, "*.txt"))
                     .Where(path => Path.GetFileNameWithoutExtension(path).Contains(search, StringComparison.CurrentCultureIgnoreCase))
                     .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            _songList.Items.Add(new SongItem(path));
        _libraryCount.Text = $"{_songList.Items.Count} 首曲目";
        var index = _songList.Items.Cast<SongItem>().ToList()
            .FindIndex(i => string.Equals(i.Path, selected, StringComparison.OrdinalIgnoreCase));
        _songList.SelectedIndex = index >= 0 ? index : keepCurrentOnly ? -1 : (_songList.Items.Count > 0 ? 0 : -1);
        _loading = false;
        if (_songList.SelectedItem is SongItem) LoadSelectedSong();
    }

    private string CategoryNameForPath(string path) =>
        string.Equals(Path.GetDirectoryName(path), _songsDir, StringComparison.OrdinalIgnoreCase)
            ? "未分类" : Path.GetFileName(Path.GetDirectoryName(path)) ?? "未分类";

    private void ShowSongToolTip(object? sender, MouseEventArgs e)
    {
        var index = _songList.IndexFromPoint(e.Location);
        var text = index >= 0 && _songList.Items[index] is SongItem item
            ? $"{item}  ·  {CategoryNameForPath(item.Path)}" : "";
        if (_songToolTip.GetToolTip(_songList) != text) _songToolTip.SetToolTip(_songList, text);
    }

    private void CreateCategory()
    {
        using var dialog = new CategoryDialog(_songsDir, Font);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var name = dialog.CategoryName;
        var directory = Path.Combine(_songsDir, name);
        try
        {
            Directory.CreateDirectory(directory);
            RefreshCategories(directory);
            RefreshSongs(keepCurrentOnly: true);
            SetStatus($"已创建分类：{name}");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "新建分类失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowMoveCategoryMenu(Control anchor)
    {
        if (_songList.SelectedItem is not SongItem item)
        {
            MessageBox.Show(this, "请先选择要分类的曲目。", "移动到分类");
            return;
        }
        RefreshCategories();
        var menu = new ContextMenuStrip();
        foreach (var category in _categoryList.Items.Cast<CategoryItem>().Where(c => c.DirectoryPath is not null))
        {
            var destination = category.DirectoryPath!;
            var option = menu.Items.Add(category.Name);
            option.Enabled = !string.Equals(Path.GetDirectoryName(item.Path), destination, StringComparison.OrdinalIgnoreCase);
            option.Click += (_, _) => BeginInvoke((Action)(() => MoveSongToCategory(item, destination)));
        }
        menu.Closed += (_, _) => BeginInvoke((Action)menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private void MoveSongToCategory(SongItem item, string destinationDirectory)
    {
        if (string.Equals(item.Path, _currentPath, StringComparison.OrdinalIgnoreCase) && !PromptSaveIfDirty()) return;
        var stem = Path.GetFileNameWithoutExtension(item.Path);
        var destination = Path.Combine(destinationDirectory, stem + ".txt");
        for (var n = 2; new[] { ".txt", ".mid", ".midi" }.Any(ext => File.Exists(Path.ChangeExtension(destination, ext))); n++)
            destination = Path.Combine(destinationDirectory, $"{stem} ({n}).txt");
        var moved = new List<(string Source, string Target)>();
        try
        {
            foreach (var extension in new[] { ".txt", ".mid", ".midi" })
            {
                var source = Path.ChangeExtension(item.Path, extension);
                if (!File.Exists(source)) continue;
                var target = Path.ChangeExtension(destination, extension);
                File.Move(source, target);
                moved.Add((source, target));
            }
            if (string.Equals(_currentPath, item.Path, StringComparison.OrdinalIgnoreCase)) _currentPath = destination;
            RefreshCategories(destinationDirectory);
            _songSearch.Clear();
            RefreshSongs(destination);
            SetStatus($"已移至分类：{Path.GetFileName(destinationDirectory)}");
        }
        catch (Exception ex)
        {
            foreach (var (source, target) in moved.AsEnumerable().Reverse())
                try { File.Move(target, source); } catch { /* Keep the remaining file in the destination. */ }
            RefreshSongs(item.Path);
            MessageBox.Show(this, ex.Message, "移动曲目失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task DeleteSelectedSongAsync()
    {
        if (_songList.SelectedItem is not SongItem item)
        {
            MessageBox.Show(this, "请先在曲谱库中选择要删除的曲目。", "删除曲目");
            return;
        }

        var midiPaths = new[] { ".mid", ".midi" }
            .Select(extension => Path.ChangeExtension(item.Path, extension))
            .Where(File.Exists).ToList();
        var deletingCurrent = string.Equals(item.Path, _currentPath, StringComparison.OrdinalIgnoreCase);
        var message = $"将以下文件移到 Windows 回收站：\n\n{Path.GetFileName(item.Path)}";
        foreach (var midiPath in midiPaths) message += $"\n{Path.GetFileName(midiPath)}";
        if (deletingCurrent && _dirty) message += "\n\n当前未保存的修改也会丢失。";
        message += "\n\n确定删除这首曲目吗？";
        if (MessageBox.Show(this, message, "删除曲目", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        await StopPlaybackAsync();
        try
        {
            foreach (var midiPath in midiPaths)
                FileSystem.DeleteFile(midiPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            if (deletingCurrent)
            {
                _current = null;
                _currentPath = null;
                _dirty = false;
            }
            RefreshSongs(keepCurrentOnly: !deletingCurrent);
            if (deletingCurrent && _songList.Items.Count == 0)
            {
                _grid.Rows.Clear();
                _title.Text = "请选择曲目";
                _noteCount.Text = "";
            }
            SetStatus($"已删除曲目：{Path.GetFileNameWithoutExtension(item.Path)}");
        }
        catch (Exception ex)
        {
            RefreshSongs(item.Path);
            MessageBox.Show(this, ex.Message, "删除曲目失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSelectedSong()
    {
        if (_loading || _songList.SelectedItem is not SongItem item || item.Path == _currentPath) return;
        if (!PromptSaveIfDirty())
        {
            _loading = true;
            _songList.SelectedIndex = _songList.Items.Cast<SongItem>().ToList()
                .FindIndex(song => string.Equals(song.Path, _currentPath, StringComparison.OrdinalIgnoreCase));
            _loading = false;
            return;
        }
        try
        {
            _current = ScoreFile.Load(item.Path);
            _currentPath = item.Path;
            _dirty = false;
            FillGrid(_current);
            SetStatus("曲谱已加载");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "读取曲谱失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void FillGrid(Score score)
    {
        _grid.Rows.Clear();
        foreach (var n in score.Notes)
        {
            var row = _grid.Rows.Add(n.StartMs, n.Midi, n.DurationMs);
            UpdateKeyPreview(row);
        }
        _title.Text = score.Title;
        _noteCount.Text = $"{score.Notes.Count} 个音符 · {(score.Notes.Max(n => n.EndMs) / 1000.0):F1} 秒";
    }

    private void UpdateKeyPreview(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _grid.Rows.Count) return;
        var row = _grid.Rows[rowIndex];
        if (!int.TryParse(Convert.ToString(row.Cells["Midi"].Value), out var midi)) { row.Cells["Keys"].Value = "无效音高"; return; }
        row.Cells["Keys"].Value = NoteMapper.TryMap(midi + (int)_transpose.Value, (int)_baseMidi.Value, out var plan) ? plan.ToString() : "超出演奏范围";
    }

    private Score ReadGrid()
    {
        var score = new Score { Title = _current?.Title ?? "未命名曲目" };
        foreach (DataGridViewRow row in _grid.Rows)
        {
            var line = row.Index + 1;
            if (!int.TryParse(Convert.ToString(row.Cells["Start"].Value), out var start)
                || !int.TryParse(Convert.ToString(row.Cells["Midi"].Value), out var midi)
                || !int.TryParse(Convert.ToString(row.Cells["Duration"].Value), out var duration))
                throw new InvalidDataException($"第 {line} 行有无效数字。");
            score.Notes.Add(new NoteEvent(start, midi, duration));
        }
        score.Notes.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        score.Validate();
        return score;
    }

    private bool SaveScore()
    {
        try
        {
            var score = ReadGrid();
            var path = _currentPath;
            if (path is null)
            {
                using var dialog = new SaveFileDialog { InitialDirectory = _songsDir, Filter = "曲谱 (*.txt)|*.txt", FileName = score.Title + ".txt" };
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                path = dialog.FileName;
            }
            ScoreFile.Save(path, score);
            _current = score;
            _currentPath = path;
            _dirty = false;
            _title.Text = score.Title;
            SetStatus("已保存到 " + path);
            RefreshSongs(path);
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }

    private bool PromptSaveIfDirty()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this, "当前曲谱有未保存修改。是否保存？", "保存曲谱", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer switch { DialogResult.Yes => SaveScore(), DialogResult.No => true, _ => false };
    }

    private void NewScore()
    {
        if (!PromptSaveIfDirty()) return;
        _current = new Score { Title = "新曲谱" };
        _currentPath = null;
        _grid.Rows.Clear();
        _title.Text = "新曲谱";
        _noteCount.Text = "0 个音符";
        _dirty = true;
        SelectPage(0);
    }

    private void AddNote()
    {
        var start = _grid.Rows.Count == 0 ? 0 : int.TryParse(Convert.ToString(_grid.Rows[^1].Cells["Start"].Value), out var value) ? value + 300 : 0;
        var row = _grid.Rows.Add(start, (int)_baseMidi.Value, 250);
        UpdateKeyPreview(row);
        _dirty = true;
    }

    private void DeleteSelectedNotes()
    {
        foreach (DataGridViewRow row in _grid.SelectedRows.Cast<DataGridViewRow>().OrderByDescending(r => r.Index)) _grid.Rows.Remove(row);
        _dirty = true;
    }

    private async Task StartPlaybackAsync(TimeSpan startDelay, bool fromHotkey = false)
    {
        if (_player.IsPlaying) { await StopPlaybackAsync(); return; }
        CancellationTokenSource? countdownToken = null;
        try
        {
            var score = ReadGrid();
            if (fromHotkey) _toast.ShowMessage("自动播放已触发", $"约 {startDelay.TotalSeconds:0.#} 秒后开始演奏", ToastTone.Success);
            if (startDelay > TimeSpan.Zero)
            {
                _countdown?.Cancel();
                countdownToken = new CancellationTokenSource();
                _countdown = countdownToken;
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < startDelay)
                {
                    var remaining = startDelay - watch.Elapsed;
                    SetStatus($"{remaining.TotalSeconds:0.0} 秒后开始，请切换到目标窗口…");
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500, remaining.TotalMilliseconds)), countdownToken.Token);
                }
                countdownToken.Token.ThrowIfCancellationRequested();
            }
            _player.Start(score, (int)_baseMidi.Value, (int)_transpose.Value, (double)_speed.Value);
            SetStatus("播放中");
            if (fromHotkey) _toast.ShowMessage("正在演奏", score.Title, ToastTone.Success);
        }
        catch (OperationCanceledException) { SetStatus("已取消"); }
        catch (Exception ex)
        {
            if (fromHotkey) _toast.ShowMessage("无法播放", ex.Message, ToastTone.Error);
            else MessageBox.Show(this, ex.Message, "无法播放", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("无法播放");
        }
        finally
        {
            if (ReferenceEquals(_countdown, countdownToken)) _countdown = null;
            countdownToken?.Dispose();
        }
    }

    private async Task StopPlaybackAsync()
    {
        _countdown?.Cancel();
        await _player.StopAsync();
        SetStatus("已停止");
    }

    private void BrowseInput()
    {
        using var dialog = new OpenFileDialog { Filter = "MIDI 文件 (*.mid;*.midi)|*.mid;*.midi" };
        if (dialog.ShowDialog(this) == DialogResult.OK) _midiPath.Text = dialog.FileName;
    }

    private async Task ImportMidiAsync()
    {
        var inputPath = _midiPath.Text.Trim();
        if (!File.Exists(inputPath) || Path.GetExtension(inputPath).ToLowerInvariant() is not (".mid" or ".midi"))
        { MessageBox.Show(this, "请选择有效的 MIDI 文件。", "提示"); return; }
        _convertButton.Enabled = false;
        _convertStatus.Text = "正在读取 MIDI…";
        try
        {
            var tracks = await Task.Run(() => MidiImporter.ReadTracks(inputPath, 0, 127));
            var chosen = ChooseMidiTrack(tracks);
            if (chosen is null) { _convertStatus.Text = "已取消导入"; return; }
            var score = MidiSongConverter.ToPlayableScore(chosen.Score, (int)_baseMidi.Value, _trimMidiIntro.Checked);
            if (tracks.Count == 1) score.Title = Path.GetFileNameWithoutExtension(inputPath);
            var path = UniqueSongPath(score.Title);
            ScoreFile.Save(path, score);
            File.Copy(inputPath, Path.ChangeExtension(path, ".mid"));
            _convertStatus.Text = $"已导入：{Path.GetFileNameWithoutExtension(path)}，共 {score.Notes.Count} 个音符";
            RefreshSongs(path);
            SelectPage(0);
        }
        catch (Exception ex) { _convertStatus.Text = "导入失败"; MessageBox.Show(this, ex.GetBaseException().Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _convertButton.Enabled = true; }
    }

    private MidiTrackScore? ChooseMidiTrack(IReadOnlyList<MidiTrackScore> tracks)
    {
        if (tracks.Count == 1) return tracks[0];
        using var dialog = new Form
        {
            Text = "选择主旋律音轨", Size = new Size(440, 350), MinimumSize = new Size(360, 270),
            StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = Theme.Canvas
        };
        var list = new ListBox { Dock = DockStyle.Fill, Font = Font, IntegralHeight = false };
        foreach (var track in tracks) list.Items.Add($"{track.Name}  ·  {track.Score.Notes.Count} 个音符");
        list.SelectedIndex = 0;
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft };
        var confirm = new Button { Text = "导入音轨", Width = 100, Height = 32, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "取消", Width = 80, Height = 32, DialogResult = DialogResult.Cancel };
        footer.Controls.Add(confirm);
        footer.Controls.Add(cancel);
        dialog.Controls.Add(list);
        dialog.Controls.Add(footer);
        dialog.AcceptButton = confirm;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0 ? tracks[list.SelectedIndex] : null;
    }

    private string UniqueSongPath(string title)
    {
        var stem = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (stem.Length == 0) stem = "转换曲谱";
        var path = Path.Combine(_songsDir, stem + ".txt");
        for (var n = 2; File.Exists(path) || File.Exists(Path.ChangeExtension(path, ".mid")); n++)
            path = Path.Combine(_songsDir, $"{stem} ({n}).txt");
        return path;
    }

    private void SaveSettings()
    {
        _settings.BaseMidi = (int)_baseMidi.Value;
        _settings.Transpose = (int)_transpose.Value;
        _settings.Speed = (double)_speed.Value;
        _settings.Save(_settingsPath);
        for (var i = 0; i < _grid.Rows.Count; i++) UpdateKeyPreview(i);
    }

    private void CaptureHotkey()
    {
        // Stop listening while the capture dialog is open, so the chosen key cannot start playback.
        UnregisterChosenHotkey();
        try
        {
            using var dialog = new HotkeyCaptureDialog(_chosenHotkey);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _chosenHotkey = dialog.Binding;
            _settings.Hotkey = _chosenHotkey.Code;
            _hotkey.Text = $"{_chosenHotkey.DisplayName}  ·  点击修改";
            SaveSettings();
            SetStatus($"已设置热键 {_chosenHotkey.DisplayName}");
        }
        finally
        {
            if (!IsDisposed) RegisterChosenHotkey();
        }
    }

    private void RegisterChosenHotkey()
    {
        UnregisterChosenHotkey();
        _pollHotkeyDown = _chosenHotkey.IsKeyDown();
        _suppressHotkeyUntilReleased = _pollHotkeyDown;
        _rawKeyboardRegistered = RawKeyboardHotkey.Register(Handle);
        try { _lowLevelKeyboard.Start(); _lowLevelKeyboardRegistered = true; }
        catch { _lowLevelKeyboardRegistered = false; }
        _hotkeyRegistered = RegisterHotKey(Handle, HotkeyId,
            _chosenHotkey.Modifiers | 0x4000, _chosenHotkey.VirtualKey);
        _hotkeyPoll.Start();
        if (!_hotkeyRegistered && !_rawKeyboardRegistered && !_lowLevelKeyboardRegistered)
            SetStatus($"热键 {_chosenHotkey.DisplayName} 的消息监听不可用；按键状态轮询已启用。");
        else if (!_hotkeyRegistered)
            SetStatus($"热键 {_chosenHotkey.DisplayName} 被其他程序占用；后台键盘监听仍已启用。");
        else if (!_rawKeyboardRegistered && !_lowLevelKeyboardRegistered)
            SetStatus("后台键盘监听未启用；普通热键与按键状态轮询仍可使用。");
    }

    private void UnregisterChosenHotkey()
    {
        _hotkeyPoll.Stop();
        if (_hotkeyRegistered && IsHandleCreated) UnregisterHotKey(Handle, HotkeyId);
        if (_rawKeyboardRegistered) RawKeyboardHotkey.Unregister();
        _lowLevelKeyboard.Dispose();
        _hotkeyRegistered = _rawKeyboardRegistered = _lowLevelKeyboardRegistered = false;
        _rawHotkeyDown = _lowLevelHotkeyDown = _pollHotkeyDown = false;
        _suppressHotkeyUntilReleased = false;
    }

    private void OnLowLevelKeyTransition(uint key, bool down)
    {
        if (key != _chosenHotkey.VirtualKey) return;
        if (!down) { _lowLevelHotkeyDown = false; return; }
        if (_lowLevelHotkeyDown) return;
        _lowLevelHotkeyDown = true;
        if (_chosenHotkey.MatchesCurrentModifiers() && IsHandleCreated && !IsDisposed)
            BeginInvoke((Action)ToggleFromHotkey);
    }

    private void PollChosenHotkey()
    {
        var down = _chosenHotkey.IsKeyDown();
        if (!down) _suppressHotkeyUntilReleased = false;
        if (down && !_pollHotkeyDown && !_suppressHotkeyUntilReleased && _chosenHotkey.MatchesCurrentModifiers())
            ToggleFromHotkey();
        _pollHotkeyDown = down;
    }

    private void ToggleFromHotkey()
    {
        if (_suppressHotkeyUntilReleased) return;
        // The same physical press can produce both WM_INPUT and WM_HOTKEY.
        var now = Stopwatch.GetTimestamp();
        if (_lastHotkeyTrigger != 0 && Stopwatch.GetElapsedTime(_lastHotkeyTrigger, now) < TimeSpan.FromMilliseconds(250)) return;
        _lastHotkeyTrigger = now;
        if (_player.IsPlaying || _countdown is not null)
        {
            var counting = !_player.IsPlaying && _countdown is not null;
            _toast.ShowMessage(counting ? "已取消播放" : "已停止演奏",
                counting ? "倒计时已取消" : "演奏按键已释放", ToastTone.Neutral);
            _ = StopPlaybackAsync();
        }
        else _ = StartPlaybackAsync(HotkeyStartDelay, true);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == HotkeyId)
        {
            ToggleFromHotkey();
            return;
        }
        if (m.Msg == WmInput && _rawKeyboardRegistered &&
            RawKeyboardHotkey.TryRead(m.LParam, out var key, out var released) && key == _chosenHotkey.VirtualKey)
        {
            if (released) _rawHotkeyDown = false;
            else if (!_rawHotkeyDown)
            {
                _rawHotkeyDown = true;
                if (_chosenHotkey.MatchesCurrentModifiers()) ToggleFromHotkey();
            }
        }
        base.WndProc(ref m);
    }

    private void SetStatus(string text) => _status.Text = text;
    private void SafeUi(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
    private sealed record SongItem(string Path)
    {
        public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
    }
    private sealed record CategoryItem(string Name, string? DirectoryPath, int Count)
    {
        public override string ToString() => Name;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint window, int id);
}
