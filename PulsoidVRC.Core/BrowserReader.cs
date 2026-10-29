// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PulsoidVRC.Core;

public sealed class BrowserReaderException(string message) : Exception(message);

public sealed class BrowserReader
{
    public static string Script { get; } = ReadScript();
    private static string ReadScript()
    {
        using var stream = typeof(BrowserReader).Assembly.GetManifestResourceStream("PulsoidVRC.Core.WebReader.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var url) || url.Scheme != "https" ||
            !(url.Host.Equals("pulsoid.net", StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith(".pulsoid.net", StringComparison.OrdinalIgnoreCase)) ||
            url.UserInfo.Length != 0)
            throw new ArgumentException("请粘贴 https:// 开头的 Pulsoid 心率组件链接（pulsoid.net 或其子域名）。");
        return url;
    }

    public static string FindBrowser()
    {
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            foreach (var relative in new[] { "Microsoft/Edge/Application/msedge.exe", "Google/Chrome/Application/chrome.exe" })
            {
                var path = Path.Combine(root, relative);
                if (File.Exists(path)) return path;
            }
        throw new BrowserReaderException("未找到 Edge 或 Chrome，请先安装其中一个浏览器。");
    }

    public async Task RunAsync(Uri page, SampleBuffer buffer, Action<string> status, CancellationToken ct,
        bool headless = true, string? selector = null, Action<string>? onSelected = null)
    {
        // This is a dedicated, disposable browser profile. Existing browser sessions are never attached.
        var profile = Path.Combine(Path.GetTempPath(), "PulsoidVRC-Browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        Process? process = null;
        CdpConnection? cdp = null;
        try
        {
            var info = new ProcessStartInfo(FindBrowser()) { UseShellExecute = false };
            if (headless) { info.ArgumentList.Add("--headless=new"); info.CreateNoWindow = true; info.WindowStyle = ProcessWindowStyle.Hidden; }
            foreach (var arg in new[] { "--user-data-dir=" + profile, "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--no-first-run", "--no-default-browser-check", "--disable-background-mode", "--disable-background-timer-throttling", "--disable-renderer-backgrounding", "--disable-backgrounding-occluded-windows", "--new-window", "about:blank" }) info.ArgumentList.Add(arg);
            status(headless ? "正在后台加载心率网页…" : "正在打开数字定位窗口…");
            process = Process.Start(info) ?? throw new BrowserReaderException("无法启动浏览器。");
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
            startup.CancelAfter(TimeSpan.FromSeconds(25));
            var portFile = Path.Combine(profile, "DevToolsActivePort");
            int debugPort = 0;
            while (debugPort == 0)
            {
                startup.Token.ThrowIfCancellationRequested();
                try
                {
                    if (File.Exists(portFile))
                    {
                        var lines = await File.ReadAllLinesAsync(portFile, startup.Token);
                        if (lines.Length > 0 && int.TryParse(lines[0], out var parsed) && parsed is > 0 and <= 65535) debugPort = parsed;
                    }
                }
                catch (IOException) { }
                if (debugPort == 0) await Task.Delay(150, startup.Token);
            }
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
            string? endpoint = null;
            while (endpoint == null)
            {
                using var targets = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{debugPort}/json/list", startup.Token));
                endpoint = targets.RootElement.EnumerateArray()
                    .Where(x => x.GetProperty("type").GetString() == "page")
                    .Select(x => x.TryGetProperty("webSocketDebuggerUrl", out var ws) ? ws.GetString() : null).FirstOrDefault(x => x != null);
                if (endpoint == null) await Task.Delay(150, startup.Token);
            }
            cdp = new CdpConnection();
            await cdp.ConnectAsync(new Uri(endpoint), startup.Token);
            await cdp.CallAsync("Page.navigate", new { url = page.AbsoluteUri }, startup.Token);
            long lastRevision = -1;
            string? lastInstance = null;
            while (!ct.IsCancellationRequested)
            {
                var options = JsonSerializer.Serialize(new { automatic = headless, selector });
                var expression = "window.__pulsoidVrcOptions=" + options + ";\n" + Script + "\nJSON.stringify(window.__pulsoidVrcReader.read())";
                var result = await cdp.CallAsync("Runtime.evaluate", new { expression, returnByValue = true }, ct);
                if (!result.TryGetProperty("result", out var evaluated) || !evaluated.TryGetProperty("value", out var raw) || raw.ValueKind != JsonValueKind.String)
                { buffer.Clear(); lastRevision = -1; status("网页加载中，等待心率组件…"); }
                else
                {
                    using var value = JsonDocument.Parse(raw.GetString()!);
                    var reading = value.RootElement;
                    var instance = reading.GetProperty("instance").GetString();
                    if (instance != lastInstance) { buffer.Clear(); lastRevision = -1; lastInstance = instance; }
                    var kind = reading.GetProperty("kind").GetString();
                    if (kind != "value")
                    {
                        buffer.Clear(); lastRevision = -1;
                        status(kind switch {
                            "select" => "请在定位窗口中点击心率数字",
                            "ambiguous" => "网页包含多个数字，请停止后点击「定位数字」",
                            _ => headless ? "后台未找到有效心率；检查链接或先「定位数字」" : "网页数字已失效，请重新选择"
                        });
                    }
                    else if (reading.GetProperty("ageMs").GetDouble() > 15000)
                    { buffer.Clear(); status("网页数字 15 秒未变化，暂停发送"); }
                    else
                    {
                        if (onSelected != null && reading.TryGetProperty("selector", out var picked) && picked.GetString() is { Length: > 0 } chosen)
                        {
                            onSelected(chosen);
                            status("数字定位完成，窗口将关闭");
                            break;
                        }
                        var revision = reading.GetProperty("revision").GetInt64();
                        if (revision != lastRevision)
                        {
                            var heartRate = reading.GetProperty("bpm").GetInt32();
                            if (heartRate is >= 1 and <= 300)
                            {
                                var now = DateTimeOffset.UtcNow;
                                buffer.Set(new HeartSample(heartRate, now.AddMilliseconds(-reading.GetProperty("ageMs").GetDouble())), now);
                            }
                            lastRevision = revision;
                        }
                        status("已连接 · 正在后台读取网页心率");
                    }
                }
                await Task.Delay(350, ct);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BrowserReaderException("浏览器启动或读取超时，请重试；若浏览器被管理策略限制，请检查远程调试设置。"); }
        catch (Exception ex) when (ex is WebSocketException or HttpRequestException or IOException)
        { throw new BrowserReaderException("后台浏览器连接已断开，请重新开始推送。"); }
        finally
        {
            buffer.Clear();
            if (cdp != null)
            {
                try { using var close = new CancellationTokenSource(TimeSpan.FromSeconds(2)); await cdp.CallAsync("Browser.close", new { }, close.Token); } catch (Exception) { }
                cdp.Dispose();
            }
            if (process != null)
            {
                try
                {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    try { await process.WaitForExitAsync(stop.Token); }
                    catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); }
                }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            // Only remove this run's freshly-created temporary browser profile.
            var fullProfile = Path.GetFullPath(profile);
            var tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (string.Equals(Path.GetDirectoryName(fullProfile), tempRoot, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullProfile).StartsWith("PulsoidVRC-Browser-", StringComparison.Ordinal) &&
                Guid.TryParseExact(Path.GetFileName(fullProfile)["PulsoidVRC-Browser-".Length..], "N", out _))
            {
                try { Directory.Delete(fullProfile, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
}

public sealed class CdpConnection : IDisposable
{
    private readonly ClientWebSocket socket = new();
    public CdpConnection() => socket.Options.Proxy = null;
    private int nextId;
    public Task ConnectAsync(Uri uri, CancellationToken ct) => socket.ConnectAsync(uri, ct);
    public async Task<JsonElement> CallAsync(string method, object parameters, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var id = ++nextId;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        var chunk = new byte[8192];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), timeout.Token);
                if (frame.MessageType == WebSocketMessageType.Close) throw new IOException("Browser closed.");
                message.Write(chunk, 0, frame.Count);
                if (message.Length > 1024 * 1024) throw new IOException("Browser response too large.");
            } while (!frame.EndOfMessage);
            using var doc = JsonDocument.Parse(message.ToArray());
            if (!doc.RootElement.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
            if (doc.RootElement.TryGetProperty("error", out _)) throw new IOException("Browser command failed.");
            return doc.RootElement.GetProperty("result").Clone();
        }
    }
    public void Dispose() => socket.Dispose();
}
