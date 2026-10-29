// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

namespace PulsoidVRC;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, "Local\\PulsoidVRC-Desktop", out var isNew);
        if (!isNew)
        {
            MessageBox.Show("程序已在运行，请查看任务栏或系统托盘。", "心中的VRChat");
            return;
        }
        Application.Run(new MainForm(args.Contains("--smoke-test", StringComparer.Ordinal)));
    }
}
