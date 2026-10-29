// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PulsoidVRC;
using PulsoidVRC.Core;

internal static class Program
{
    private static int passed;
    private static async Task Main()
    {
        await Test("v2 schema, malformed payloads and sensor range", () =>
        {
            var now = DateTimeOffset.UtcNow;
            Check(HeartProtocol.Parse(Payload(82, now)) == new HeartSample(82, now), "v2 sample mismatch");
            foreach (var json in new[] { "{", "[]", "null", "{}", "{\"heartRate\":7}", Payload(0, now), Payload(301, now), "{\"heartRate\":{\"bpm\":\"80\"},\"timestamp\":\"invalid\"}" })
                Check(HeartProtocol.Parse(json) == null, "accepted invalid payload");
            Check(HeartProtocol.IsPause("{\"ingestionPause\":{}}"), "missed ingestion pause");
            return Task.CompletedTask;
        });
        await Test("timestamp freshness, replay rejection and immediate disconnect invalidation", () =>
        {
            var b = new SampleBuffer(); var now = DateTimeOffset.UtcNow;
            Check(!b.Set(new(70, now.AddSeconds(-16)), now), "stale input accepted");
            Check(!b.Set(new(70, now.AddSeconds(40)), now), "future input accepted");
            Check(b.Set(new(80, now), now), "fresh rejected");
            Check(!b.Set(new(81, now), now), "replayed timestamp accepted");
            Check(!b.Set(new(81, now.AddSeconds(-1)), now), "out of order input accepted");
            Check(b.Fresh(now.AddSeconds(16)) == null, "stale sample still published");
            b.Clear(); Check(b.Fresh(now) == null, "disconnect left stale value");
            return Task.CompletedTask;
        });
        await Test("OSC UTF-8 byte alignment and True/False type tags", () =>
        {
            var text = "♥ 心率 88 BPM";
            var bytes = OscPacket.Chat(text);
            Check(bytes.Length % 4 == 0, "packet alignment");
            var decoded = DecodeOsc(bytes);
            Check(decoded == ("/chatbox/input", ",sTF", text), "OSC content differs");
            Check(DecodeOsc(OscPacket.Chat(text, true)).Tags == ",sTT", "notification flag");
            CheckThrows<ArgumentException>(() => OscPacket.Chat("bad\0text"));
            CheckThrows<ArgumentException>(() => OscPacket.Chat(new string('x', 145)));
            return Task.CompletedTask;
        });
        await Test("template placeholder and worst-case 144 character limit", () =>
        {
            ChatText.Validate("♥ {bpm} BPM");
            Check(ChatText.Format("{bpm}/{bpm}", 99) == "99/99", "replacement");
            ChatText.Validate(new string('a', 141) + "{bpm}");
            foreach (var invalid in new[] { "", "heart", "{bpm}\n", "{bpm}\0", "{other}{bpm}", new string('a', 142) + "{bpm}" })
                CheckThrows<ArgumentException>(() => ChatText.Validate(invalid));
            return Task.CompletedTask;
        });
        await Test("throttle coalesces rapid updates and sends newest sample", async () =>
        {
            var b = new SampleBuffer(); var pump = new ChatPump(b, "{bpm}", TimeSpan.FromMilliseconds(120));
            var messages = new List<string>();
            Task Send(string text, CancellationToken _) { messages.Add(text); return Task.CompletedTask; }
            Check(await pump.TickAsync(Send, default) == null, "sent without sample");
            var now = DateTimeOffset.UtcNow; b.Set(new(70, now), now);
            await pump.TickAsync(Send, default);
            for (var i = 1; i <= 20; i++) { b.Set(new(70 + i, now.AddMilliseconds(i)), now); await pump.TickAsync(Send, default); }
            Check(messages.SequenceEqual(new[] { "70" }), "rate limit exceeded");
            await Task.Delay(150); await pump.TickAsync(Send, default);
            Check(messages.SequenceEqual(new[] { "70", "90" }), "did not send newest sample");
            b.Clear(); await Task.Delay(150); await pump.TickAsync(Send, default);
            Check(messages.Count == 2, "sent after clear");
            using var cts = new CancellationTokenSource(); cts.Cancel();
            await CheckThrowsAsync<OperationCanceledException>(() => pump.TickAsync(Send, cts.Token));
        });
        await Test("failed UDP send does not consume throttle slot", async () =>
        {
            var b = new SampleBuffer(); b.Set(new(75, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow);
            var pump = new ChatPump(b, "{bpm}", TimeSpan.FromSeconds(20));
            await CheckThrowsAsync<IOException>(() => pump.TickAsync((_, _) => throw new IOException(), default));
            Check(await pump.TickAsync((_, _) => Task.CompletedTask, default) == "75", "failed send consumed slot");
        });
        await Test("token validation: bearer header, 401, 403 and missing scopes", async () =>
        {
            foreach (var code in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.OK })
            {
                using var http = new HttpClient(new FakeHandler(request =>
                {
                    Check(request.Headers.Authorization?.ToString() == "Bearer TEST_ONLY", "missing bearer header");
                    Check(!request.RequestUri!.Query.Contains("TEST_ONLY"), "token leaked into URL");
                    return new(code) { Content = new StringContent("{\"scopes\":[]}") };
                }));
                await CheckThrowsAsync<PulsoidAuthException>(() => new PulsoidClient().ValidateAsync(http, "TEST_ONLY", default));
            }
            using var good = new HttpClient(new FakeHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"scopes\":[\"data:heart_rate:read\"]}") }));
            await new PulsoidClient().ValidateAsync(good, "TEST_ONLY", default);
        });
        await Test("DPAPI roundtrip and no plaintext credentials in settings", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "PulsoidVRC-Test-" + Guid.NewGuid());
            try
            {
                const string secret = "TEST_ONLY_NOT_A_REAL_TOKEN";
                var encrypted = SettingsStore.Protect(secret);
                Check(SettingsStore.Unprotect(encrypted) == secret, "DPAPI roundtrip");
                var store = new SettingsStore(dir);
                store.Save(new Settings { ProtectedToken = encrypted });
                Check(!File.ReadAllText(Path.Combine(dir, "settings.json")).Contains(secret), "plaintext credential saved");
                Check(store.Load().ProtectedToken == encrypted, "settings load mismatch");
                store.Save(new Settings()); Check(store.Load().ProtectedToken == null, "forgotten token retained");
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            return Task.CompletedTask;
        });
        await Test("local WebSocket → heart rate → OSC UDP, pause, reconnect and cancellation", Integration);
        await Test("web reader URL restrictions", () =>
        {
            Check(BrowserReader.ValidateUrl("https://pulsoid.net/widget/view/EXAMPLE").Host == "pulsoid.net", "widget URL rejected");
            foreach (var url in new[] { "http://pulsoid.net/", "https://pulsoid.net.evil.example/", "javascript:alert(1)", "file:///C:/test", "https://user:password@pulsoid.net/" })
                CheckThrows<ArgumentException>(() => BrowserReader.ValidateUrl(url));
            return Task.CompletedTask;
        });
        await Test("automatic headless DOM → OSC, unchanged replaced text expires, cancellation", () => BrowserIntegration(0));
        await Test("ambiguous numeric page does not publish", () => BrowserIntegration(1));
        await Test("saved selector chooses correct BPM among multiple numbers", () => BrowserIntegration(2));
        await Test("missing saved selector does not fall back to unrelated numbers", () => BrowserIntegration(3));
        await Test("calibration returns reusable selector and ends without streaming", () => BrowserIntegration(4));
        Console.WriteLine($"PASS: {passed} test groups. No live Pulsoid token or VRChat instance used.");
    }

    private static async Task BrowserIntegration(int mode)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.MapGet("/", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            var extra = mode is 1 or 2 or 3 ? "<div id='other'>99</div>" : "";
            await context.Response.WriteAsync("<!doctype html><html><title>PulsoidVRC headless fixture</title><body><section><div id='heart'>81</div></section>" + extra +
                "<script>setInterval(()=>{document.getElementById('heart').outerHTML='<div id=heart>81</div>';},500);</script></body></html>");
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var buffer = new SampleBuffer();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stale = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? selected = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var run = new BrowserReader().RunAsync(new Uri(address), buffer, status =>
        {
            if (mode == 1 ? status.Contains("多个数字") : mode == 3 ? status.Contains("未找到") : status.Contains("正在后台读取")) received.TrySetResult();
            if (status.Contains("15 秒未变化")) stale.TrySetResult();
        }, timeout.Token, selector: mode is 2 or 4 ? "#heart" : mode == 3 ? "#missing" : null,
            onSelected: mode == 4 ? value => selected = value : null);
        try
        {
            var winner = await Task.WhenAny(received.Task, run).WaitAsync(timeout.Token);
            if (winner == run) await run;
            if (mode == 4)
            {
                Check(selected == "#heart", "calibration did not return selected element");
                Check(buffer.Fresh(DateTimeOffset.UtcNow) == null, "calibration produced streaming data");
                return;
            }
            await received.Task.WaitAsync(timeout.Token);
            if (mode is 1 or 3)
            {
                await Task.Delay(800, timeout.Token);
                Check(buffer.Fresh(DateTimeOffset.UtcNow) == null, "ambiguous/missing target published data");
                return;
            }
            Check(buffer.Fresh(DateTimeOffset.UtcNow)?.Bpm == 81, "DOM heart rate missing");
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var destination = (IPEndPoint)listener.Client.LocalEndPoint!;
            using var sender = new OscSender(destination.Address, destination.Port);
            var pump = new ChatPump(buffer, "♥ {bpm} BPM", TimeSpan.FromSeconds(2));
            await pump.TickAsync((text, ct) => sender.SendAsync(text, false, ct), timeout.Token);
            var packet = await listener.ReceiveAsync(timeout.Token);
            Check(DecodeOsc(packet.Buffer).Text == "♥ 81 BPM", "DOM to OSC mismatch");
            if (mode == 0)
            {
                await stale.Task.WaitAsync(timeout.Token);
                Check(buffer.Fresh(DateTimeOffset.UtcNow) == null, "unchanged replacement DOM kept data alive");
            }
        }
        finally
        {
            timeout.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
            await app.StopAsync();
        }
        Check(buffer.Fresh(DateTimeOffset.UtcNow) == null, "browser cancellation kept data alive");
    }

    private static async Task Integration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.UseWebSockets();
        var pause = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnect = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = 0; var authorized = 0;
        app.MapGet("/validate", async context =>
        {
            if (context.Request.Headers.Authorization.ToString() != "Bearer TEST_ONLY") { context.Response.StatusCode = 401; return; }
            Interlocked.Increment(ref authorized);
            await context.Response.WriteAsJsonAsync(new { scopes = new[] { "data:heart_rate:read" } });
        });
        app.Map("/stream", async context =>
        {
            if (context.Request.Headers.Authorization.ToString() != "Bearer TEST_ONLY") { context.Response.StatusCode = 401; return; }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var count = Interlocked.Increment(ref connections);
            var payload = Encoding.UTF8.GetBytes(Payload(count == 1 ? 81 : 84, DateTimeOffset.UtcNow));
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(payload, 0, 12), WebSocketMessageType.Text, false, context.RequestAborted);
                await socket.SendAsync(new ArraySegment<byte>(payload, 12, payload.Length - 12), WebSocketMessageType.Text, true, context.RequestAborted);
                if (count == 1)
                {
                    await pause.Task.WaitAsync(TimeSpan.FromSeconds(12));
                    await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"ingestion_pause\",\"ingestionPause\":{}}"), WebSocketMessageType.Text, true, context.RequestAborted);
                    await disconnect.Task.WaitAsync(TimeSpan.FromSeconds(12));
                    socket.Abort();
                }
                else
                {
                    var bytes = new byte[128];
                    await socket.ReceiveAsync(bytes, context.RequestAborted);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException) { }
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var buffer = new SampleBuffer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var client = new PulsoidClient(new Uri(address + "/validate"), new Uri(address.Replace("http://", "ws://") + "/stream"));
        var run = client.RunAsync("TEST_ONLY", buffer, status =>
        {
            Check(!status.Contains("TEST_ONLY"), "status exposed token");
            if (status.Contains("暂停接收")) paused.TrySetResult();
        }, sample => { if (sample.Bpm == 81) first.TrySetResult(); if (sample.Bpm == 84) second.TrySetResult(); }, timeout.Token);
        try
        {
            await first.Task.WaitAsync(timeout.Token);
            using var listener = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var destination = (IPEndPoint)listener.Client.LocalEndPoint!;
            using var sender = new OscSender(destination.Address, destination.Port);
            var pump = new ChatPump(buffer, "♥ 心率 {bpm} BPM", TimeSpan.FromMilliseconds(10));
            await pump.TickAsync((text, ct) => sender.SendAsync(text, false, ct), timeout.Token);
            var packet = await listener.ReceiveAsync(timeout.Token);
            Check(DecodeOsc(packet.Buffer) == ("/chatbox/input", ",sTF", "♥ 心率 81 BPM"), "pipeline output mismatch");
            pause.TrySetResult(); await paused.Task.WaitAsync(timeout.Token);
            Check(buffer.Fresh(DateTimeOffset.UtcNow) == null, "pause left heart rate active");
            disconnect.TrySetResult(); await second.Task.WaitAsync(timeout.Token);
            Check(connections >= 2 && authorized >= 2, "reconnect did not revalidate token");
            Check(buffer.Fresh(DateTimeOffset.UtcNow)?.Bpm == 84, "reconnected sample missing");
        }
        finally
        {
            timeout.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(3)); } catch (OperationCanceledException) { }
            await app.StopAsync();
        }
        Check(buffer.Fresh(DateTimeOffset.UtcNow) == null, "cancel left sample active");
    }

    private static string Payload(int bpm, DateTimeOffset at) => JsonSerializer.Serialize(new { type = "heart_rate", timestamp = at.ToString("O"), heartRate = new { bpm } });
    private static (string Address, string Tags, string Text) DecodeOsc(byte[] data)
    {
        var index = 0;
        string Read()
        {
            var start = index;
            while (index < data.Length && data[index] != 0) index++;
            Check(index < data.Length, "missing string terminator");
            var result = Encoding.UTF8.GetString(data, start, index - start);
            index++;
            while (index % 4 != 0) { Check(data[index] == 0, "nonzero padding"); index++; }
            return result;
        }
        var result = (Read(), Read(), Read());
        Check(index == data.Length, "unexpected OSC bytes");
        return result;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void CheckThrows<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task CheckThrowsAsync<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Test(string name, Func<Task> test)
    { await test(); passed++; Console.WriteLine("PASS " + name); }
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
