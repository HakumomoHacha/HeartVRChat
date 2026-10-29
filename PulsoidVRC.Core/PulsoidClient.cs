// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PulsoidVRC.Core;

public sealed class PulsoidAuthException(string message) : Exception(message);

public sealed class PulsoidClient
{
    private readonly Uri validationUri;
    private readonly Uri streamUri;
    public PulsoidClient(Uri? validationUri = null, Uri? streamUri = null)
    {
        this.validationUri = validationUri ?? new("https://dev.pulsoid.net/api/v1/token/validate");
        this.streamUri = streamUri ?? new("wss://dev.pulsoid.net/api/v2/data/real_time?types=heart_rate,ingestion_pause");
    }

    public async Task ValidateAsync(HttpClient http, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, validationUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new PulsoidAuthException("Pulsoid Token 无效或已过期，请重新创建并粘贴 Token。");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array ||
            !scopes.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "data:heart_rate:read"))
            throw new PulsoidAuthException("Token 缺少 data:heart_rate:read 权限，请重新创建 Token。");
    }

    public async Task RunAsync(string token, SampleBuffer buffer, Action<string> status,
        Action<HeartSample> received, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            buffer.Clear();
            var connectedAt = DateTimeOffset.UtcNow;
            try
            {
                status("正在验证 Pulsoid Token…");
                await ValidateAsync(http, token, ct);
                using var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    status("正在连接 Pulsoid…");
                    await socket.ConnectAsync(streamUri, timeout.Token);
                }
                connectedAt = DateTimeOffset.UtcNow;
                status("已连接，等待心率数据");
                await ReadMessagesAsync(socket, json =>
                {
                    if (HeartProtocol.IsPause(json))
                    {
                        buffer.Clear();
                        status("Pulsoid 暂停接收数据，等待恢复");
                        return;
                    }
                    var sample = HeartProtocol.Parse(json);
                    if (sample != null && buffer.Set(sample, DateTimeOffset.UtcNow))
                    {
                        received(sample);
                        status("已连接 · 正在接收心率");
                    }
                }, ct);
                status("Pulsoid 连接已关闭");
            }
            catch (PulsoidAuthException) { buffer.Clear(); throw; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            // Never log response bodies, raw exceptions, tokens or authenticated URLs.
            catch (Exception ex) when (ex is HttpRequestException or WebSocketException or OperationCanceledException or JsonException or IOException)
            {
                status("Pulsoid 连接暂时不可用，请检查网络和 Token");
            }
            finally { buffer.Clear(); }
            if (ct.IsCancellationRequested) break;
            if (DateTimeOffset.UtcNow - connectedAt > TimeSpan.FromMinutes(1)) failures = 0;
            var delay = Math.Min(60, 2 * Math.Pow(2, Math.Min(failures++, 5))) + Random.Shared.NextDouble();
            status($"连接中断，{delay:F0} 秒后自动重连");
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);
        }
    }

    public static async Task ReadMessagesAsync(WebSocket socket, Action<string> receive, CancellationToken ct)
    {
        var chunk = new byte[4096];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            // Reconnect a silent/broken stream periodically, even if a peer never sends Close.
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idle.CancelAfter(TimeSpan.FromSeconds(90));
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(chunk), idle.Token);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType != WebSocketMessageType.Text) throw new IOException("Unexpected WebSocket message.");
            if (message.Length + result.Count > 64 * 1024) throw new IOException("WebSocket message too large.");
            message.Write(chunk, 0, result.Count);
            if (!result.EndOfMessage) continue;
            receive(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
            message.SetLength(0);
        }
    }
}
