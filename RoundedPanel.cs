using System.Drawing.Drawing2D;

namespace SharpEmuUpdater;

/// <summary>A "card" panel: rounded rect, flat fill, 1px border -- mirrors the Border.card style.</summary>
public class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = UiScale.S(12);
    public Color BorderColor { get; set; } = Theme.CardBorder;
    public int BorderWidth { get; set; } = Math.Max(1, UiScale.S(1));

    public RoundedPanel()
    {
        BackColor = Theme.Card;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(UiScale.S(16));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, CornerRadius);
        using var fill = new SolidBrush(BackColor);
        e.Graphics.FillPath(fill, path);
        if (BorderWidth > 0)
        {
            using var pen = new Pen(BorderColor, BorderWidth);
            e.Graphics.DrawPath(pen, path);
        }
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        // Control.Region's setter does not dispose whatever Region was previously assigned --
        // every resize (and there are several during startup's layout convergence alone, see
        // MainForm.ForceLayoutConvergence, plus one for every RoundedPanel recreated on each
        // RebuildUi) would otherwise leak one native GDI region.
        //
        // Deliberately the full (Width, Height) here, NOT the (Width-1, Height-1) rect OnPaint
        // itself fills/strokes -- a border Pen draws centered on its path by default, so roughly
        // half its stroke width extends outward past that path's edge. A region clipped exactly
        // to the fill path cuts that outer half off, making thin (1px) borders look patchy or
        // disappear along the bottom/right edge entirely -- confirmed live on the Recent Builds
        // search box, which uses this same class for its border. Using the full bounds for the
        // region leaves a little slack past the painted path, comfortably wide enough for the
        // border pen's outward half to render fully, while the square-corner leak this Region
        // exists to fix in the first place (see RoundedButton's own doc comment) is a whole
        // corner-radius-sized area, not a 1px margin -- clipping to the full bounds instead of
        // the exact fill rect still removes essentially all of it.
        var oldRegion = Region;
        using var path = RoundedRect(new Rectangle(0, 0, Width, Height), CornerRadius);
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        if (d <= 0 || bounds.Width < d || bounds.Height < d)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
