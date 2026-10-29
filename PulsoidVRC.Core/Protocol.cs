// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PulsoidVRC.Core;

public sealed record HeartSample(int Bpm, DateTimeOffset MeasuredAt);

public static class HeartProtocol
{
    public static HeartSample? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("heartRate", out var heart) || heart.ValueKind != JsonValueKind.Object ||
                !heart.TryGetProperty("bpm", out var bpm) || bpm.ValueKind != JsonValueKind.Number ||
                !bpm.TryGetInt32(out var value) || value is < 1 or > 300 ||
                !root.TryGetProperty("timestamp", out var stamp) || stamp.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var measured)) return null;
            return new(value, measured);
        }
        catch (JsonException) { return null; }
    }

    public static bool IsPause(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("ingestionPause", out var p) && p.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException) { return false; }
    }
}

public sealed class SampleBuffer
{
    private readonly object sync = new();
    private HeartSample? sample;
    private long received;
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(15);

    public bool Set(HeartSample value, DateTimeOffset now)
    {
        lock (sync)
        {
            if (now - value.MeasuredAt > MaxAge || value.MeasuredAt - now > TimeSpan.FromSeconds(30) ||
                (sample != null && value.MeasuredAt <= sample.MeasuredAt)) return false;
            sample = value;
            received = Stopwatch.GetTimestamp();
            return true;
        }
    }

    public HeartSample? Fresh(DateTimeOffset now)
    {
        lock (sync)
            return sample != null && now - sample.MeasuredAt <= MaxAge &&
                Stopwatch.GetElapsedTime(received) <= MaxAge ? sample : null;
    }

    public void Clear() { lock (sync) sample = null; }
}

public static class ChatText
{
    public static void Validate(string template)
    {
        if (string.IsNullOrWhiteSpace(template) || !template.Contains("{bpm}", StringComparison.Ordinal))
            throw new ArgumentException("消息模板必须包含 {bpm}，例如：♥ 心率 {bpm} BPM");
        if (template.Any(char.IsControl)) throw new ArgumentException("消息模板不能包含换行或控制字符。");
        var expanded = template.Replace("{bpm}", "300", StringComparison.Ordinal);
        if (expanded.Contains('{') || expanded.Contains('}')) throw new ArgumentException("模板只支持 {bpm} 占位符。");
        if (expanded.Length > 144) throw new ArgumentException("消息模板过长：替换心率后不能超过 144 个字符。");
    }

    public static string Format(string template, int bpm) => template.Replace("{bpm}", bpm.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}

public static class OscPacket
{
    public static byte[] Chat(string text, bool sound = false)
    {
        if (text.Length is 0 or > 144 || text.Contains('\0')) throw new ArgumentException("OSC 文本长度或内容无效。");
        using var stream = new MemoryStream();
        WriteString(stream, "/chatbox/input");
        WriteString(stream, sound ? ",sTT" : ",sTF");
        WriteString(stream, text);
        return stream.ToArray();
    }

    private static void WriteString(Stream stream, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
        stream.WriteByte(0);
        while (stream.Position % 4 != 0) stream.WriteByte(0);
    }
}

public sealed class OscSender : IDisposable
{
    private readonly UdpClient udp;
    public OscSender(IPAddress host, int port)
    {
        udp = new UdpClient(host.AddressFamily);
        udp.Connect(host, port);
    }
    public async Task SendAsync(string text, bool sound, CancellationToken ct)
    {
        var data = OscPacket.Chat(text, sound);
        await udp.SendAsync(data, ct);
    }
    public void Dispose() => udp.Dispose();
}

// One latest sample, no queue: a fast input stream cannot create a chat backlog.
public sealed class ChatPump(SampleBuffer buffer, string template, TimeSpan interval)
{
    private long? lastSent;
    public async Task<string?> TickAsync(Func<string, CancellationToken, Task> send, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (lastSent is { } last && Stopwatch.GetElapsedTime(last) < interval) return null;
        var fresh = buffer.Fresh(DateTimeOffset.UtcNow);
        if (fresh == null) return null;
        var text = ChatText.Format(template, fresh.Bpm);
        await send(text, ct);
        lastSent = Stopwatch.GetTimestamp();
        return text;
    }
}
