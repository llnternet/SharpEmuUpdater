using System.Drawing.Drawing2D;

namespace SharpEmuUpdater;

/// <summary>
/// Small hand-drawn magnifying glass glyph for the two search boxes (Recent Builds, Switch Fork).
/// Drawn with GDI+ primitives rather than the 🔍 emoji character -- that renders as a full-color
/// Segoe UI Emoji image on Windows, which would clash with this app's flat, monochrome icon style
/// (matches the same approach BuildListRenderer already uses for its own status glyphs:
/// DrawCheckmark/DrawCross/DrawClockHands, small shapes drawn directly rather than relying on a
/// font glyph that might not render the way this app wants).
/// </summary>
public sealed class SearchIcon : Panel
{
    public SearchIcon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float penWidth = Math.Max(1.3f, UiScale.S(1) * 1.3f);
        using var pen = new Pen(Theme.Muted, penWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        // This control docks Right against the search box, so it's normally much taller than it
        // is wide -- the icon's own footprint (lens + handle) is sized off the smaller dimension,
        // then explicitly centered within the full control bounds on both axes. Anchoring at
        // (0,0) instead would leave it pinned to the top-left rather than centered next to the
        // vertically-centered search text.
        float iconSize = Math.Min(Width, Height) - penWidth * 2;
        float offsetX = (Width - iconSize) / 2f;
        float offsetY = (Height - iconSize) / 2f;

        float lensD = iconSize * 0.62f;
        var lens = new RectangleF(offsetX, offsetY, lensD, lensD);
        g.DrawEllipse(pen, lens);

        double angle = Math.PI / 4; // 45 degrees, toward the bottom-right
        float cx = lens.X + lens.Width / 2f;
        float cy = lens.Y + lens.Height / 2f;
        float handleStartX = cx + (float)(Math.Cos(angle) * lensD / 2);
        float handleStartY = cy + (float)(Math.Sin(angle) * lensD / 2);
        g.DrawLine(pen, handleStartX, handleStartY, offsetX + iconSize, offsetY + iconSize);
    }
}
