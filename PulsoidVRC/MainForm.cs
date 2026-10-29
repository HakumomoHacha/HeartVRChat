// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using PulsoidVRC.Core;

namespace PulsoidVRC;

public sealed partial class MainForm : Form
{
    private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox token = new() { UseSystemPasswordChar = true, Dock = DockStyle.Fill, PlaceholderText = "粘贴同一 Pulsoid 账号的 Access Token" };
    private readonly TextBox host = new() { Text = "127.0.0.1", Dock = DockStyle.Fill };
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 9000, Width = 95 };
    private readonly NumericUpDown interval = new() { Minimum = 1.5m, Maximum = 60, DecimalPlaces = 1, Increment = .5m, Value = 2, Width = 95 };
    private readonly TextBox template = new() { Text = "♥ 心率 {bpm} BPM", Dock = DockStyle.Fill, MaxLength = 160 };
    private readonly SoftToggle remember = new() { Text = "记住链接 / Token" };
    private readonly SoftToggle sound = new() { Text = "提示音" };
    private readonly HeartDisplay heartDisplay = new();
    private readonly Label dataState = new() { Text = "等待手机上传心率", AutoSize = true };
    private readonly Label connection = new() { Text = "尚未连接", AutoSize = true };
    private readonly Label preview = new() { Text = "预览：♥ 心率 72 BPM", AutoSize = false, AutoEllipsis = true, Dock = DockStyle.Fill, Height = 24 };
    private readonly Label sent = new() { Text = "尚未发送聊天消息", AutoSize = true };
    private readonly SoftButton start = new() { Text = "开始推送" };
    private readonly SoftButton stop = new() { Text = "停止推送", Enabled = false };
    private readonly SoftButton test = new() { Text = "测试消息" };
    private readonly SoftButton save = new() { Text = "保存设置" };
    private readonly SoftButton locate = new() { Text = "定位数字" };
    private string? webSelector, webSelectorUrlHash;
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Height = 75 };
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 400 };
    private readonly SettingsStore store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulsoidVRC"));
    private readonly SampleBuffer buffer = new();
    private CancellationTokenSource? cancellation;
    private Task? bridgeTask;
    private string lastStatus = "";
    private bool loading = true, closing, allowClose;
    private readonly bool smokeTest;

    public MainForm(bool smokeTest = false)
    {
        this.smokeTest = smokeTest;
        BuildInterface();

        var menu = new ContextMenuStrip();
        menu.Items.Add("显示窗口", null, (_, _) => RestoreWindow());
        menu.Items.Add("停止推送", null, (_, _) => cancellation?.Cancel());
        menu.Items.Add("退出", null, (_, _) => Close());
        tray = new NotifyIcon { Icon = Icon, Text = "心中的VRChat", ContextMenuStrip = menu, Visible = !smokeTest };
        tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && !smokeTest) Hide(); };
        start.Click += async (_, _) => await StartAsync();
        locate.Click += async (_, _) => await StartAsync(calibrate: true);
        stop.Click += (_, _) => { stop.Enabled = false; cancellation?.Cancel(); };
        save.Click += (_, _) => SaveSettings();
        test.Click += async (_, _) => await TestAsync();
        template.TextChanged += (_, _) => preview.Text = ChatText.Format(template.Text, 72);
        remember.CheckedChanged += (_, _) =>
        {
            if (loading || remember.Checked || smokeTest) return;
            try { store.Save(store.Load() with { ProtectedToken = null, ProtectedWebUrl = null }); Log("已删除本机保存的链接和 Token。"); }
            catch (Exception) { MessageBox.Show(this, "无法删除已保存的信息，请检查本机设置文件权限。", "保存失败"); }
        };
        displayTimer.Tick += (_, _) => RefreshHeart();
        displayTimer.Start();
        if (!smokeTest) LoadSettings();
        loading = false;
        FormClosing += OnClosing;
        if (smokeTest) Shown += (_, _) =>
        {
            var timer = new System.Windows.Forms.Timer { Interval = 900 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                using var bitmap = new Bitmap(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                bitmap.Save(Path.Combine(Environment.CurrentDirectory, "ui-smoke.png"));
                var layout = new List<string>();
                void Describe(Control control, int depth)
                {
                    layout.Add(new string(' ', depth * 2) + control.GetType().Name + " " + control.Bounds + " preferred=" + control.PreferredSize + (control is TableLayoutPanel table ? " rows=" + string.Join(',', table.GetRowHeights()) : ""));
                    foreach (Control child in control.Controls) Describe(child, depth + 1);
                }
                Describe(this, 0);
                File.WriteAllLines(Path.Combine(Environment.CurrentDirectory, "ui-layout.txt"), layout);
                // Offline visual checks: no network calls, credential reads or OSC sends.
                displayTimer.Stop();
                IEnumerable<Control> Descendants(Control parent)
                {
                    foreach (Control child in parent.Controls)
                    {
                        yield return child;
                        foreach (var nested in Descendants(child)) yield return nested;
                    }
                }
                var history = Descendants(this).OfType<SoftButton>().Single(x => x.Text.StartsWith("运行记录", StringComparison.Ordinal));
                history.PerformClick();
                ClientSize = MinimumSize;
                PerformLayout();
                using (var small = new Bitmap(Width, Height))
                {
                    DrawToBitmap(small, new Rectangle(0, 0, Width, Height));
                    small.Save(Path.Combine(Environment.CurrentDirectory, "ui-small.png"));
                }
                source.SelectedIndex = 1;
                if (locate.Enabled) throw new InvalidOperationException("API mode must disable web calibration.");
                source.SelectedIndex = 0;
                if (!locate.Enabled) throw new InvalidOperationException("Web mode must allow calibration.");
                history.PerformClick();
                ClientSize = new Size(840, 790);
                heartDisplay.HeartRate = 128;
                connection.Text = "样式预览";
                dataState.Text = "演示数值 · 未连接设备";
                using (var active = new Bitmap(Width, Height))
                {
                    DrawToBitmap(active, new Rectangle(0, 0, Width, Height));
                    active.Save(Path.Combine(Environment.CurrentDirectory, "ui-active.png"));
                }
                File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "ui-smoke-ok.txt"), "UI opened and rendered without network or saved credentials.");
                timer.Dispose();
                Close();
            };
            timer.Start();
        };
    }

    private void LoadSettings()
    {
        try
        {
            var value = store.Load();
            source.SelectedIndex = value.SourceMode == "api" ? 1 : 0;
            webSelector = value.WebSelector;
            webSelectorUrlHash = value.WebSelectorUrlHash;
            host.Text = value.Host;
            port.Value = Math.Clamp(value.Port, 1, 65535);
            interval.Value = Math.Clamp(value.Interval, 1.5m, 60);
            template.Text = value.Template;
            sound.Checked = value.Sound;
            var saved = source.SelectedIndex == 0 ? value.ProtectedWebUrl : value.ProtectedToken;
            if (saved != null) { token.Text = SettingsStore.Unprotect(saved); remember.Checked = true; }
        }
        catch (Exception) { Log("本机设置无法完整读取，请重新填写并保存。链接／Token 只能由原 Windows 用户解密。"); }
    }

    private Settings ReadSettings()
    {
        if (!IPAddress.TryParse(host.Text.Trim(), out _)) throw new ArgumentException("请输入有效 IP 地址。同一电脑使用 127.0.0.1。");
        ChatText.Validate(template.Text);
        return new Settings { WebSelector = webSelector, WebSelectorUrlHash = webSelectorUrlHash,
            SourceMode = source.SelectedIndex == 0 ? "web" : "api", Host = host.Text.Trim(), Port = (int)port.Value, Interval = interval.Value,
            Template = template.Text, Sound = sound.Checked,
            ProtectedToken = source.SelectedIndex == 1 && remember.Checked && token.Text.Trim().Length > 0 ? SettingsStore.Protect(token.Text.Trim()) : null,
            ProtectedWebUrl = source.SelectedIndex == 0 && remember.Checked && token.Text.Trim().Length > 0 ? SettingsStore.Protect(token.Text.Trim()) : null };
    }

    private void SaveSettings()
    {
        try { store.Save(ReadSettings()); Log("设置已保存。" + (remember.Checked ? "链接／Token 已使用 Windows 用户加密。" : "链接／Token 未保存。")); }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "请检查设置"); }
        catch (Exception) { MessageBox.Show(this, "保存失败，请检查本机设置目录权限。", "保存失败"); }
    }

    private static string UrlHash(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(new Uri(url).AbsoluteUri)));

    private async Task StartAsync(bool calibrate = false)
    {
        Settings settings;
        var accessToken = token.Text.Trim();
        try
        {
            settings = ReadSettings();
            if (settings.SourceMode == "web") BrowserReader.ValidateUrl(accessToken);
            else if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Any(char.IsWhiteSpace))
                throw new ArgumentException("请粘贴有效的 Pulsoid Access Token，不要包含 Bearer 前缀或空格。");
            store.Save(settings);
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "请检查设置"); return; }
        catch (Exception) { MessageBox.Show(this, "无法保存设置，请检查本机设置目录权限。", "保存失败"); return; }

        SetRunning(true);
        buffer.Clear();
        sent.Text = calibrate ? "正在定位数字，此操作不会发送聊天消息。" : "等待新的心率数据…";
        cancellation = new CancellationTokenSource();
        var current = cancellation;
        string? selected = null;
        bridgeTask = calibrate
            ? Task.Run(() => new BrowserReader().RunAsync(BrowserReader.ValidateUrl(accessToken), new SampleBuffer(), Status, current.Token,
                headless: false, onSelected: value => selected = value))
            : Task.Run(() => RunBridgeAsync(settings, accessToken, current.Token));
        try
        {
            await bridgeTask;
            if (calibrate && selected != null && !current.IsCancellationRequested)
            {
                webSelector = selected;
                webSelectorUrlHash = UrlHash(accessToken);
                store.Save(ReadSettings());
                Status("数字位置已保存，可直接开始后台推送");
            }
        }
        catch (OperationCanceledException) { Log("已停止推送。"); }
        catch (PulsoidAuthException ex) { Status(ex.Message); }
        catch (BrowserReaderException ex) { Status(ex.Message); }
        catch (Exception) { Status("推送已停止：请检查网络、OSC 地址和端口后重试。"); }
        finally
        {
            buffer.Clear();
            var wasCancelled = current.IsCancellationRequested;
            current.Dispose();
            cancellation = null;
            SetRunning(false);
            RefreshHeart();
            if (lastStatus.StartsWith("已连接", StringComparison.Ordinal) || wasCancelled)
                Status("已停止推送");
        }
    }

    private async Task RunBridgeAsync(Settings settings, string accessToken, CancellationToken ct)
    {
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var osc = new OscSender(IPAddress.Parse(settings.Host), settings.Port);
        var receiveTask = settings.SourceMode == "web"
            ? new BrowserReader().RunAsync(BrowserReader.ValidateUrl(accessToken), buffer, Status, session.Token,
                headless: true, selector: settings.WebSelectorUrlHash == UrlHash(accessToken) ? settings.WebSelector : null)
            : new PulsoidClient().RunAsync(accessToken, buffer, Status, _ => { }, session.Token);
        var sendTask = SendLoopAsync();
        await Task.WhenAny(receiveTask, sendTask);
        session.Cancel();
        await Task.WhenAll(receiveTask, sendTask);

        async Task SendLoopAsync()
        {
            var pump = new ChatPump(buffer, settings.Template, TimeSpan.FromSeconds((double)settings.Interval));
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
            while (await timer.WaitForNextTickAsync(session.Token))
            {
                var message = await pump.TickAsync((text, token) => osc.SendAsync(text, settings.Sound, token), session.Token);
                if (message != null) Post(() => sent.Text = $"{DateTime.Now:HH:mm:ss} 已发送 OSC：{message}（请在游戏内确认）");
            }
        }
    }

    private async Task TestAsync()
    {
        test.Enabled = false;
        start.Enabled = false;
        locate.Enabled = false;
        try
        {
            if (!IPAddress.TryParse(host.Text.Trim(), out var address)) throw new ArgumentException("VRChat 地址应为 IP；本机使用 127.0.0.1。");
            using var sender = new OscSender(address, (int)port.Value);
            await sender.SendAsync("♥ 测试消息：72 BPM（模拟值）", sound.Checked, CancellationToken.None);
            Log("测试消息已通过 UDP 发出；请在 VRChat 中确认显示。UDP 无法确认游戏已收到。");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "请检查设置"); }
        catch (Exception) { Log("测试发送失败，请检查 IP 地址、端口和网络。"); }
        finally { if (!IsDisposed && !closing) { test.Enabled = true; start.Enabled = true; locate.Enabled = source.SelectedIndex == 0; } }
    }

    private void SetRunning(bool running)
    {
        start.Enabled = test.Enabled = save.Enabled = !running;
        locate.Enabled = !running && source.SelectedIndex == 0;
        stop.Enabled = running;
        foreach (var control in new Control[] { source, token, host, port, interval, template, sound, remember }) control.Enabled = !running;
    }

    private void RefreshHeart()
    {
        var sample = buffer.Fresh(DateTimeOffset.UtcNow);
        heartDisplay.HeartRate = sample?.Bpm;
        dataState.Text = sample == null ? (cancellation == null ? "等待手机上传心率" : "暂无新鲜数据 · 暂停发送") : $"数据更新于 {sample.MeasuredAt.ToLocalTime():HH:mm:ss}";
    }

    private void Status(string message) => Post(() =>
    {
        if (lastStatus == message) return;
        lastStatus = message;
        connection.Text = message;
        Log(message);
    });
    private void Log(string message)
    {
        if (log.Lines.Length > 80) log.Lines = log.Lines.TakeLast(60).ToArray();
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
    private void Post(Action action)
    {
        if (IsDisposed || Disposing || closing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => { if (!IsDisposed && !closing) action(); })); }
            catch (InvalidOperationException) { }
        }
        else action();
    }
    private void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { MessageBox.Show("请在浏览器打开：" + url, "打开网页"); }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        cancellation?.Cancel();
        if (bridgeTask != null) { try { await bridgeTask; } catch (Exception) { } }
        displayTimer.Stop();
        displayTimer.Dispose();
        fadeTimer.Dispose();
        tray.Visible = false;
        tray.Dispose();
        allowClose = true;
        BeginInvoke(new Action(Close));
    }
}
