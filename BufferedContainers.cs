namespace SharpEmuUpdater;

// WinForms only exposes the DoubleBuffered property to subclasses -- stock Panel/TableLayoutPanel/
// FlowLayoutPanel can't have it set from outside. Every plain container in the window hierarchy
// (header, content rows) needs one of these instead, or resizing/dragging visibly flickers even
// though the custom-painted leaf controls (RoundedPanel, GradientPanel, AccentProgress) are already
// double-buffered themselves.

public class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }
}

public class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }
}

public class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }
}

// Same double-buffering need as the others (matters most for the Recent Builds list, whose
// spinning-arc glyph on an InProgress row repaints on a timer and was visibly tearing without
// this) but NOT the same fix -- ListBox wraps a real native Win32 listbox that renders itself via
// WM_DRAWITEM, unlike Panel/TableLayoutPanel/FlowLayoutPanel which have no native painting of
// their own. Adding ControlStyles.UserPaint on top (as those three do) tells WinForms the control
// owns 100% of its own painting, which suppresses that native owner-draw pipeline entirely --
// the list still has all its items, they just never get drawn. DoubleBuffered=true alone already
// sets OptimizedDoubleBuffer + AllPaintingInWmPaint internally (see Control.DoubleBuffered's
// setter), which is the double-buffering ListBox can actually use.
//
// DoubleBuffered alone still wasn't enough to kill flicker on rapidly-repainted rows (the
// spinner). The native listbox erases each item's background (WM_ERASEBKGND) immediately before
// sending WM_DRAWITEM for it -- .NET's double-buffering wraps *its own* OnPaint, not that native
// erase step, so it never intercepts it. Since BuildListRenderer's DrawItem already fills the
// entire item rectangle itself on every WM_DRAWITEM, that native erase is pure redundant paint
// happening right before ours -- suppressing it here (returning "handled, nothing to erase"
// instead of forwarding to the base window proc) removes the double-paint that was flashing.
public class BufferedListBox : ListBox
{
    private const int WM_ERASEBKGND = 0x0014;

    public BufferedListBox()
    {
        DoubleBuffered = true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_ERASEBKGND)
        {
            // WM_DRAWITEM only ever fires for actual items -- if the box is taller than
            // Items.Count * ItemHeight (routine now that the list card can be much taller than a
            // fixed-size item list, e.g. MainForm's RecentRunsToShow), whatever's below the last
            // item is never painted by anything else. Blanket-suppressing the erase (as this used
            // to do unconditionally) left that leftover strip showing whatever pixels happened to
            // already be there -- invisible under normal painting, but the literal desktop
            // wallpaper/icons once DWM's compositor surface gets reset (Hide()/Show() from the
            // tray, or a maximize/restore) and there's nothing left to have painted over it. Still
            // erase that leftover strip; still skip erasing the item rows themselves.
            int itemsHeight = Items.Count * ItemHeight;
            if (itemsHeight < ClientSize.Height)
            {
                using var g = Graphics.FromHdc(m.WParam);
                using var brush = new SolidBrush(BackColor);
                g.FillRectangle(brush, new Rectangle(0, itemsHeight, ClientSize.Width, ClientSize.Height - itemsHeight));
            }
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }
}
