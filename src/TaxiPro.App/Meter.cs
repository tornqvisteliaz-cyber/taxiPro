using System.Drawing;
using System.Windows.Forms;

namespace TaxiPro.App;

/// <summary>A thin horizontal bar: a fill from 0..1 and an optional marker (e.g. breakaway thrust).</summary>
public sealed class Meter : Control
{
    private double _value, _marker = -1;

    public Meter()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 8;
    }

    public Color Track { get; set; } = Theme.Field;
    public Color Fill { get; set; } = Theme.Accent;
    public Color MarkerColor { get; set; } = Theme.Ink;
    /// <summary>Centre-zero: the fill grows left or right from the middle (value −1..1).</summary>
    public bool Centred { get; set; }

    public double Value
    {
        get => _value;
        set { if (Math.Abs(value - _value) > 1e-4) { _value = value; Invalidate(); } }
    }

    /// <summary>Marker position 0..1, or negative for none.</summary>
    public double Marker
    {
        get => _marker;
        set { if (Math.Abs(value - _marker) > 1e-4) { _marker = value; Invalidate(); } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var track = new SolidBrush(Track))
            g.FillRectangle(track, ClientRectangle);
        using (var fill = new SolidBrush(Fill))
        {
            if (Centred)
            {
                var mid = Width / 2;
                var w = (int)Math.Round(Math.Min(1, Math.Abs(_value)) * Width / 2);
                g.FillRectangle(fill, _value >= 0 ? mid : mid - w, 0, w, Height);
            }
            else
            {
                g.FillRectangle(fill, 0, 0, (int)Math.Round(Math.Max(0, Math.Min(1, _value)) * Width), Height);
            }
        }
        if (_marker >= 0)
        {
            using var pen = new Pen(MarkerColor, 2);
            var x = (int)Math.Round(Math.Min(1, _marker) * (Width - 2)) + 1;
            g.DrawLine(pen, x, 0, x, Height);
        }
    }
}

/// <summary>Rotaidem's dark palette, shared with HeavyPro.</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(0x11, 0x13, 0x15);
    public static readonly Color Surface = Color.FromArgb(0x16, 0x19, 0x1C);
    public static readonly Color Field = Color.FromArgb(0x1B, 0x1F, 0x22);
    public static readonly Color Border = Color.FromArgb(0x26, 0x2A, 0x2E);
    public static readonly Color Ink = Color.FromArgb(0xEE, 0xF0, 0xF2);
    public static readonly Color Muted = Color.FromArgb(0x87, 0x8D, 0x94);
    public static readonly Color Accent = Color.FromArgb(0xAA, 0xB6, 0xC2);
    public static readonly Color Good = Color.FromArgb(0x4C, 0xC3, 0x8A);
    public static readonly Color Warn = Color.FromArgb(0xE0, 0xA9, 0x3B);
    public static readonly Color Bad = Color.FromArgb(0xE5, 0x48, 0x4D);
    public static readonly Color OnAccent = Color.FromArgb(0x0B, 0x0C, 0x0E);
}
