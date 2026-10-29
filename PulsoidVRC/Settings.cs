// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PulsoidVRC;

public sealed record Settings
{
    public string SourceMode { get; init; } = "web";
    public string? ProtectedWebUrl { get; init; }
    public string? WebSelector { get; init; }
    public string? WebSelectorUrlHash { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 9000;
    public decimal Interval { get; init; } = 2;
    public string Template { get; init; } = "♥ 心率 {bpm} BPM";
    public bool Sound { get; init; }
    public string? ProtectedToken { get; init; }
}

public sealed class SettingsStore(string directory)
{
    private string FileName => Path.Combine(directory, "settings.json");
    public Settings Load() => File.Exists(FileName)
        ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FileName)) ?? new()
        : new();

    public static string Protect(string token) => Convert.ToBase64String(ProtectedData.Protect(
        Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    public static string Unprotect(string encrypted) => Encoding.UTF8.GetString(ProtectedData.Unprotect(
        Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser));

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(directory);
        var temp = FileName + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FileName, true);
    }
}
