// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 HakuChya (HakumomoHacha)

using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PulsoidVRC;

internal static class Palette
{
    public static readonly bool MotionEnabled = !SystemParametersInfo(0x1042, 0, out var enabled, 0) || enabled;
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    public static readonly Color Ink = Color.FromArgb(43, 57, 83);
    public static readonly Color Muted = Color.FromArgb(116, 129, 154);
    public static readonly Color Blue = Color.FromArgb(112, 157, 238);
    public static readonly Color Pink = Color.FromArgb(232, 144, 185);
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

public sealed class GradientSurface : Panel
{
    public GradientSurface()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width < 1 || Height < 1) return;
        using var gradient = new LinearGradientBrush(ClientRectangle, Color.White, Color.White, 38f);
        gradient.InterpolationColors = new ColorBlend
        {
            Colors = [Color.FromArgb(217, 235, 255), Color.FromArgb(246, 249, 255), Color.FromArgb(255, 232, 242)],
            Positions = [0f, .48f, 1f]
        };
        e.Graphics.FillRectangle(gradient, ClientRectangle);
    }
}

public sealed class GlassPanel : Panel
{
    public int Radius { get; set; } = 20;
    public int GlassAlpha { get; set; } = 185;
    public GlassPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = Palette.Round(new RectangleF(.5f, .5f, Width - 1f, Height - 1f), Radius * DeviceDpi / 96f);
        using var fill = new SolidBrush(Color.FromArgb(GlassAlpha, Color.White));
        e.Graphics.FillPath(fill, shape);
        using var line = new Pen(Color.FromArgb(210, Color.White));
        e.Graphics.DrawPath(line, shape);
        base.OnPaint(e);
    }
}

public sealed class SoftButton : Button
{
    public bool Primary { get; set; }
    public bool Quiet { get; set; }
    private float hover;
    private float target;
    private bool pressed;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 16 };
    public SoftButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Palette.Ink;
        Cursor = Cursors.Hand;
        Height = 44;
        animation.Tick += (_, _) =>
        {
            hover += (target - hover) * .24f;
            if (Math.Abs(target - hover) < .012f) { hover = target; animation.Stop(); }
            Invalidate();
        };
    }
    private void Animate(float value)
    {
        target = value;
        if (Palette.MotionEnabled && Visible) animation.Start();
        else { hover = target; Invalidate(); }
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Animate(1); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); pressed = false; Animate(0); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = true; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) Animate(0); Invalidate(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible) animation.Stop(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(.5f, pressed ? 2f : .5f, Width - 1f, Height - (pressed ? 3f : 1f));
        using var path = Palette.Round(r, 12 * DeviceDpi / 96f);
        if (Primary && Enabled)
        {
            using var gradient = new LinearGradientBrush(r,
                Palette.Mix(Color.FromArgb(99, 151, 236), Color.FromArgb(86, 137, 224), hover),
                Palette.Mix(Color.FromArgb(208, 145, 207), Color.FromArgb(220, 128, 186), hover), 10f);
            e.Graphics.FillPath(gradient, path);
        }
        else
        {
            var baseColor = Quiet ? Color.FromArgb(0, Color.White) : Color.FromArgb(210, Color.White);
            using var fill = new SolidBrush(Enabled ? Color.FromArgb((int)(baseColor.A + (255 - baseColor.A) * hover), Palette.Mix(Color.White, Color.FromArgb(230, 239, 255), hover)) : Color.FromArgb(100, Color.White));
            e.Graphics.FillPath(fill, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Round(r),
            !Enabled ? Color.FromArgb(158, 169, 187) : Primary ? Color.White : Palette.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Color.FromArgb(110, Palette.Blue)) { DashStyle = DashStyle.Dot };
            using var focus = Palette.Round(new RectangleF(4, 4, Width - 9, Height - 9), 9);
            e.Graphics.DrawPath(pen, focus);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}

public sealed class SoftToggle : CheckBox
{
    public SoftToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        AutoSize = false;
        Height = 28;
        Cursor = Cursors.Hand;
        ForeColor = Palette.Muted;
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var track = new RectangleF(0, (Height - 18 * scale) / 2f, 30 * scale, 18 * scale);
        using var path = Palette.Round(track, 9 * scale);
        using var fill = new SolidBrush(!Enabled ? Color.FromArgb(224, 229, 239) : Checked ? Palette.Blue : Color.FromArgb(218, 226, 240));
        e.Graphics.FillPath(fill, path);
        using var dot = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(dot, track.X + (Checked ? 14 : 3) * scale, track.Y + 3 * scale, 12 * scale, 12 * scale);
        var r = new Rectangle((int)(40 * scale), 0, Width - (int)(40 * scale), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, r, Enabled ? Palette.Muted : Color.FromArgb(165, 175, 192), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, r);
    }
}

public sealed class HeartDisplay : Control
{
    private readonly Font numberFont = new("Segoe UI", 42, FontStyle.Bold);
    private readonly Font unitFont = new("Segoe UI", 10, FontStyle.Regular);
    private int? heartRate;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 33 };
    public int? HeartRate
    {
        get => heartRate;
        set { if (heartRate == value) return; heartRate = value; UpdateAnimation(); Invalidate(); }
    }
    public HeartDisplay()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        animation.Tick += (_, _) => Invalidate();
    }
    private void UpdateAnimation()
    {
        if (Visible && heartRate != null && Palette.MotionEnabled) animation.Start();
        else animation.Stop();
    }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdateAnimation(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var beat = heartRate.HasValue && Palette.MotionEnabled
            ? Math.Pow(Math.Max(0, Math.Cos(clock.Elapsed.TotalSeconds * Math.PI * 2 * heartRate.GetValueOrDefault(60) / 60)), 12) : 0;
        var size = (float)(29 + 4 * beat) * scale;
        var cx = 30 * scale; var cy = Height / 2f;
        using var heart = new GraphicsPath();
        heart.AddBezier(cx, cy + size * .4f, cx - size * 1.1f, cy - size * .15f, cx - size * .45f, cy - size * .9f, cx, cy - size * .35f);
        heart.AddBezier(cx, cy - size * .35f, cx + size * .45f, cy - size * .9f, cx + size * 1.1f, cy - size * .15f, cx, cy + size * .4f);
        using var pink = new LinearGradientBrush(new RectangleF(cx - size, cy - size, size * 2, size * 2), Color.FromArgb(241, 149, 187), Color.FromArgb(202, 156, 225), 55f);
        g.FillPath(pink, heart);
        var number = heartRate?.ToString() ?? "—";
        var left = (int)(76 * scale);
        TextRenderer.DrawText(g, number, numberFont, new Rectangle(left, (int)(Height / 2f - 42 * scale), Width - left, (int)(74 * scale)), Palette.Ink, TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "BPM", unitFont, new Rectangle(left + (int)(TextRenderer.MeasureText(number, numberFont).Width), (int)(Height / 2f + 5 * scale), (int)(45 * scale), (int)(24 * scale)), Palette.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { animation.Dispose(); numberFont.Dispose(); unitFont.Dispose(); }
        base.Dispose(disposing);
    }
}
