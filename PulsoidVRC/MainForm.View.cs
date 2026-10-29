// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PulsoidVRC;

public sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 16 };

    private void BuildInterface()
    {
        Text = "心中的VRChat";
        Font = new Font("Microsoft YaHei UI", 10);
        ForeColor = Palette.Ink;
        BackColor = Color.FromArgb(238, 244, 255);
        ClientSize = new Size(840, 790);
        MinimumSize = new Size(770, 690);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        Icon = SystemIcons.Application;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        var surface = new GradientSurface { Dock = DockStyle.Fill };
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Transparent, Margin = Padding.Empty };
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, Padding = new Padding(28, 0, 28, 22), BackColor = Color.Transparent
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(page);
        shell.Controls.Add(scroll, 0, 1);
        surface.Controls.Add(shell);
        Controls.Add(surface);
        void Add(Control control, int gap = 16)
        {
            control.Margin = new Padding(0, 0, 0, gap);
            var row = page.RowCount++;
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Controls.Add(control, 0, row);
        }

        var header = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 0), BackColor = Color.Transparent, Margin = Padding.Empty };
        var title = new Label
        {
            Text = "心中的VRChat", AutoSize = true, Location = new Point(28, 25),
            Font = new Font("Microsoft YaHei UI", 23, FontStyle.Bold), ForeColor = Palette.Ink, BackColor = Color.Transparent
        };
        var close = new SoftButton { Text = "×", Quiet = true, Width = 36, Height = 34, Font = new Font("Segoe UI", 18), AccessibleName = "关闭" };
        var minimize = new SoftButton { Text = "−", Quiet = true, Width = 36, Height = 34, Font = new Font("Segoe UI", 18), AccessibleName = "最小化" };
        var windowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 84, FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent, Padding = new Padding(0, 2, 0, 0)
        };
        windowButtons.Controls.AddRange([minimize, close]);
        close.Click += (_, _) => Close();
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        header.MouseDown += DragWindow;
        title.MouseDown += DragWindow;
        header.Controls.Add(title);
        header.Controls.Add(windowButtons);
        shell.Controls.Add(header, 0, 0);

        var heartCard = new GlassPanel { Height = 138, Dock = DockStyle.Fill, Padding = new Padding(22, 16, 22, 16), GlassAlpha = 165 };
        var heartLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Color.Transparent };
        heartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        heartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
        heartDisplay.Dock = DockStyle.Fill;
        heartDisplay.Margin = Padding.Empty;
        heartLayout.Controls.Add(heartDisplay, 0, 0);
        var heartInfo = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            BackColor = Color.Transparent, Padding = new Padding(12, 20, 0, 0)
        };
        connection.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        connection.ForeColor = Palette.Ink;
        connection.BackColor = Color.Transparent;
        connection.MaximumSize = new Size(340, 0);
        connection.Margin = new Padding(0, 0, 0, 9);
        dataState.Font = new Font(Font.FontFamily, 9);
        dataState.ForeColor = Palette.Muted;
        dataState.BackColor = Color.Transparent;
        dataState.Margin = Padding.Empty;
        heartInfo.Controls.AddRange([connection, dataState]);
        heartLayout.Controls.Add(heartInfo, 1, 0);
        heartCard.Controls.Add(heartLayout);
        Add(heartCard, 18);

        var settingsCard = new GlassPanel { Height = 326, Dock = DockStyle.Fill, Padding = new Padding(22, 18, 22, 18), GlassAlpha = 170 };
        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void SettingRow(Control c, int height)
        {
            var row = settings.RowCount++;
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            c.Dock = DockStyle.Fill;
            c.Margin = Padding.Empty;
            settings.Controls.Add(c, 0, row);
        }
        var firstRow = Columns(36, 64);
        source.Items.AddRange(["网页读取", "官方 API"]);
        source.SelectedIndex = 0;
        source.FlatStyle = FlatStyle.Flat;
        source.DrawMode = DrawMode.OwnerDrawFixed;
        source.ItemHeight = 24;
        source.BackColor = Color.White;
        source.ForeColor = Palette.Ink;
        source.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, source.Items[e.Index]?.ToString() ?? "", source.Font,
                new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), Palette.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
        firstRow.Controls.Add(Field("数据来源", source), 0, 0);
        firstRow.Controls.Add(Field("链接 / Token", token), 1, 0);
        SettingRow(firstRow, 72);

        var tokenOptions = new FlowLayoutPanel { BackColor = Color.Transparent, FlowDirection = FlowDirection.LeftToRight };
        var reveal = new SoftToggle { Text = "显示", Width = 82 };
        remember.Width = 166;
        remember.Margin = new Padding(0, 2, 14, 0);
        reveal.Margin = new Padding(0, 2, 18, 0);
        reveal.CheckedChanged += (_, _) => token.UseSystemPasswordChar = !reveal.Checked;
        var tokenLink = new LinkLabel
        {
            Text = "打开 Pulsoid ↗", AutoSize = true, LinkColor = Color.FromArgb(111, 145, 198),
            ActiveLinkColor = Palette.Pink, LinkBehavior = LinkBehavior.HoverUnderline,
            BackColor = Color.Transparent, Margin = new Padding(0, 6, 0, 0), Font = new Font(Font.FontFamily, 9)
        };
        tokenLink.LinkClicked += (_, _) => OpenUrl(source.SelectedIndex == 0 ? "https://pulsoid.net/" : "https://pulsoid.net/ui/keys");
        void UpdateSource()
        {
            var web = source.SelectedIndex == 0;
            token.PlaceholderText = web ? "粘贴 Pulsoid 组件链接" : "粘贴 Pulsoid Access Token";
            tokenLink.Text = web ? "打开 Pulsoid ↗" : "获取 Token ↗";
            locate.Enabled = web && cancellation == null;
        }
        source.SelectedIndexChanged += (_, _) => { if (!loading) token.Clear(); UpdateSource(); };
        UpdateSource();
        tokenOptions.Controls.AddRange([remember, reveal, tokenLink]);
        SettingRow(tokenOptions, 42);

        var network = Columns(42, 22, 36);
        network.Controls.Add(Field("VRChat 地址", host), 0, 0);
        network.Controls.Add(Field("端口", port), 1, 0);
        network.Controls.Add(Field("推送间隔 · 秒", interval), 2, 0);
        SettingRow(network, 76);
        SettingRow(Field("聊天内容", template), 68);

        var previewRow = Columns(77, 23);
        preview.Text = "♥ 心率 72 BPM";
        preview.BackColor = Color.Transparent;
        preview.ForeColor = Color.FromArgb(140, 125, 168);
        preview.Font = new Font(Font.FontFamily, 9);
        preview.Dock = DockStyle.Fill;
        preview.TextAlign = ContentAlignment.MiddleLeft;
        preview.Margin = Padding.Empty;
        sound.Width = 120;
        sound.Margin = Padding.Empty;
        previewRow.Controls.Add(preview, 0, 0);
        previewRow.Controls.Add(sound, 1, 0);
        SettingRow(previewRow, 30);
        settingsCard.Controls.Add(settings);
        Add(settingsCard, 20);

        var buttons = new FlowLayoutPanel { Height = 46, Dock = DockStyle.Fill, BackColor = Color.Transparent, WrapContents = false };
        start.Primary = true;
        start.Width = 140;
        stop.Width = 108;
        test.Width = 132;
        locate.Width = 108;
        save.Width = 108;
        foreach (var button in new[] { start, stop, test, locate, save })
        {
            button.Height = 44;
            button.Margin = new Padding(0, 0, 10, 0);
            buttons.Controls.Add(button);
        }
        Add(buttons, 12);
        sent.AutoSize = false;
        sent.AutoEllipsis = true;
        sent.Dock = DockStyle.Fill;
        sent.Height = 22;
        sent.BackColor = Color.Transparent;
        sent.Font = new Font(Font.FontFamily, 9);
        sent.ForeColor = Palette.Muted;
        Add(sent, 5);

        var showLog = new SoftButton { Text = "运行记录  ﹀", Quiet = true, Width = 112, Height = 30, Font = new Font(Font.FontFamily, 9) };
        var logCard = new GlassPanel { Height = 100, Dock = DockStyle.Fill, Padding = new Padding(14), Visible = false };
        log.BorderStyle = BorderStyle.None;
        log.BackColor = Color.FromArgb(249, 250, 255);
        log.ForeColor = Palette.Muted;
        log.Font = new Font(Font.FontFamily, 9);
        logCard.Controls.Add(log);
        showLog.Click += (_, _) => { logCard.Visible = !logCard.Visible; showLog.Text = logCard.Visible ? "运行记录  ﹁" : "运行记录  ﹀"; };
        Add(showLog, 4);
        Add(logCard, 0);

        if (!smokeTest && Palette.MotionEnabled)
        {
            Opacity = 0;
            var clock = new Stopwatch();
            Shown += (_, _) => { clock.Start(); fadeTimer.Start(); };
            fadeTimer.Tick += (_, _) =>
            {
                var t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 180);
                Opacity = 1 - Math.Pow(1 - t, 3);
                if (t >= 1 || closing) fadeTimer.Stop();
            };
        }
    }

    private static TableLayoutPanel Columns(params float[] widths)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = widths.Length, RowCount = 1, BackColor = Color.Transparent };
        foreach (var width in widths) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    private Control Field(string title, Control input)
    {
        var cell = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 0, 14, 0) };
        var caption = new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = Palette.Muted, BackColor = Color.Transparent, Font = new Font(Font.FontFamily, 9) };
        var frame = new GlassPanel { Dock = DockStyle.Top, Height = 38, Radius = 9, GlassAlpha = 240, Padding = new Padding(12, 8, 12, 6) };
        input.ForeColor = Palette.Ink;
        input.BackColor = Color.White;
        input.Dock = DockStyle.Fill;
        if (input is TextBox textBox) textBox.BorderStyle = BorderStyle.None;
        if (input is NumericUpDown number) number.BorderStyle = BorderStyle.None;
        if (input is ComboBox) frame.Padding = new Padding(6, 4, 6, 4);
        frame.Controls.Add(input);
        cell.Controls.Add(frame);
        cell.Controls.Add(caption);
        return cell;
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width < 1 || Height < 1 || FormBorderStyle != FormBorderStyle.None) return;
        using var round = Palette.Round(new RectangleF(0, 0, Width, Height), 18 * DeviceDpi / 96f);
        var old = Region;
        Region = new Region(round);
        old?.Dispose();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84 || (int)m.Result != 1 || WindowState != FormWindowState.Normal) return;
        var packed = m.LParam.ToInt64();
        var p = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
        var edge = Math.Max(5, 6 * DeviceDpi / 96);
        var left = p.X < edge; var right = p.X >= Width - edge;
        var top = p.Y < edge; var bottom = p.Y >= Height - edge;
        m.Result = (IntPtr)(top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1);
    }
}
