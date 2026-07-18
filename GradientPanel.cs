using System.Drawing.Drawing2D;

namespace SharpEmuUpdater;

/// <summary>Diagonal gradient fill mirroring the app-wide BgBrush.</summary>
public sealed class GradientPanel : Panel
{
    public GradientPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var rect = new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height));
        using var brush = new LinearGradientBrush(rect, Theme.BgTop, Theme.BgBottom, LinearGradientMode.ForwardDiagonal);
        var blend = new ColorBlend(3)
        {
            Colors = new[] { Theme.BgTop, Theme.BgMid, Theme.BgBottom },
            Positions = new[] { 0f, 0.55f, 1f },
        };
        brush.InterpolationColors = blend;
        e.Graphics.FillRectangle(brush, rect);
    }
}
