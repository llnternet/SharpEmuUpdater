namespace SharpEmuUpdater;

/// <summary>Minimize/maximize/close glyph button for the custom title bar.</summary>
public sealed class CaptionButton : Label
{
    public Color HoverColor { get; set; } = Theme.Elevated;

    public CaptionButton()
    {
        BackColor = Theme.Chrome;
        ForeColor = Theme.Muted;
        Font = Theme.UiFont(10f);
        TextAlign = ContentAlignment.MiddleCenter;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        BackColor = HoverColor;
        ForeColor = Theme.Text;
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        BackColor = Theme.Chrome;
        ForeColor = Theme.Muted;
        base.OnMouseLeave(e);
    }
}
