using System.Drawing.Drawing2D;

namespace SharpEmuUpdater;

public enum ButtonVariant { Accent, Ghost }

/// <summary>Flat, rounded button mirroring Button.accent / Button.ghost from the Avalonia theme.</summary>
public class RoundedButton : Button
{
    private ButtonVariant _variant = ButtonVariant.Ghost;
    private bool _hover;

    public ButtonVariant Variant
    {
        get => _variant;
        set { _variant = value; ApplyVariantColors(); Invalidate(); }
    }

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = Theme.UiFont(9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        Height = UiScale.S(34);
        Padding = new Padding(UiScale.S(14), 0, UiScale.S(14), 0);
        DoubleBuffered = true;
        ApplyVariantColors();
    }

    // OnPaint below only fills a rounded path, but without this, the button's actual rectangular
    // bounds are never clipped to match -- the square corners outside that rounded path still get
    // erased with the raw BackColor (Theme.Accent's bright purple, for the Accent variant) by the
    // base Button's own background painting, and just sit there uncovered by anything OnPaint
    // draws. Invisible when BackColor happens to be close to whatever's behind the button, but a
    // visibly wrong colored triangle at each corner otherwise -- most obvious on Accent buttons
    // (bright purple against this app's dark background) but present on every RoundedButton.
    // Matches RoundedPanel's own identical fix: Control.Region's setter doesn't dispose whatever
    // was previously assigned, so the old one has to be captured and disposed explicitly here too.
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        var oldRegion = Region;
        // Deliberately the full (Width, Height), not the (Width-1, Height-1) rect OnPaint fills/
        // strokes -- see RoundedPanel's identical fix/comment: a border Pen draws centered on its
        // path, so clipping the region exactly to that path cuts off the outward half of the
        // stroke, making the Ghost variant's 1px border look patchy along the bottom/right edge.
        // The full bounds still remove essentially all of the square-corner leak this exists to
        // fix (a whole corner-radius-sized area, not a 1px margin).
        using var path = RoundedPanel.RoundedRect(new Rectangle(0, 0, Width, Height), UiScale.S(8));
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    private void ApplyVariantColors()
    {
        if (_variant == ButtonVariant.Accent)
        {
            BackColor = Theme.Accent;
            ForeColor = Color.White;
        }
        else
        {
            BackColor = Theme.Elevated;
            ForeColor = Theme.Text;
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill = Enabled
            ? (_variant == ButtonVariant.Accent ? (_hover ? Theme.AccentHover : Theme.Accent) : (_hover ? Theme.CardBorder : Theme.Elevated))
            : Theme.CardBorder;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedPanel.RoundedRect(rect, UiScale.S(8));
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);

        if (_variant == ButtonVariant.Ghost)
        {
            using var pen = new Pen(Theme.CardBorder);
            g.DrawPath(pen, path);
        }

        Color textColor = Enabled ? ForeColor : Theme.Faint;
        TextRenderer.DrawText(g, Text, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
