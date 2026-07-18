using System.Drawing.Drawing2D;

namespace SharpEmuUpdater;

/// <summary>Slim progress bar in the accent purple, styled like the Avalonia ProgressBar rows.
/// Two modes: indeterminate (Active=true, sliding highlight -- used for "something's happening,
/// no known duration or byte count" like checking for updates) and determinate (DownloadFraction
/// set to a 0..1 value -- used while an artifact download is in flight, where real progress is
/// known from Content-Length vs bytes read so far).</summary>
public sealed class AccentProgress : Panel
{
    // ~30fps, not 60 -- imperceptible for a 4px-tall sliding highlight, and halves how often this
    // repaints for the whole time any check/download is in progress.
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private double _phase;
    private bool _active;
    private double? _downloadFraction;

    public AccentProgress()
    {
        Height = UiScale.S(4);
        DoubleBuffered = true;
        _timer.Tick += (_, _) =>
        {
            _phase += 0.02;
            if (_phase > 1.4) _phase = -0.4;
            Invalidate();
        };
    }

    // Without this, closing the app while Active (a check or download still in flight) leaves
    // _timer running after this control itself is disposed -- WinForms' Timer has no automatic
    // tie to its owning control's lifecycle, so the next Tick's Invalidate() would throw
    // ObjectDisposedException. Stopping it here, before the base Panel disposal tears down the
    // control, closes that window.
    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (_active) _timer.Start(); else _timer.Stop();
            Invalidate();
        }
    }

    /// <summary>Stops the animation timer without touching Active -- used while the host window
    /// is being dragged (see MainForm's WM_ENTERSIZEMOVE handling), so a still-genuinely-busy
    /// state (Active stays true) doesn't keep repainting 60x/sec and competing with the OS's own
    /// per-frame window-move work. Resume() restarts it exactly where Active/DownloadFraction
    /// already say it should be, with no state to reconcile.</summary>
    public void Suspend() => _timer.Stop();

    public void Resume()
    {
        if (_active) _timer.Start();
    }

    /// <summary>Set to a 0..1 value while a download's real progress is known -- takes over
    /// rendering from the indeterminate sliding animation with a proportional fill instead. Set
    /// back to null once the download finishes (or wasn't a download at all, e.g. a local
    /// build reuse) so Active's own indeterminate animation shows again if it's still running.</summary>
    public double? DownloadFraction
    {
        get => _downloadFraction;
        set { _downloadFraction = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 0, Width, Height);
        using (var trackPath = RoundedPanel.RoundedRect(track, Height / 2))
        using (var trackBrush = new SolidBrush(Theme.Elevated))
            g.FillPath(trackBrush, trackPath);

        if (_downloadFraction is double fraction)
        {
            int fillWidth = (int)(Math.Clamp(fraction, 0, 1) * Width);
            if (fillWidth <= 0) return;
            var determinateBar = new Rectangle(0, 0, fillWidth, Height);
            using var determinatePath = RoundedPanel.RoundedRect(determinateBar, Height / 2);
            using var determinateBrush = new SolidBrush(Theme.Accent);
            g.FillPath(determinateBrush, determinatePath);
            return;
        }

        if (!_active) return;

        int barWidth = Math.Max(UiScale.S(40), Width / 4);
        int x = (int)(_phase * (Width + barWidth)) - barWidth;
        var bar = new Rectangle(x, 0, barWidth, Height);
        using var barPath = RoundedPanel.RoundedRect(bar, Height / 2);
        using var barBrush = new SolidBrush(Theme.Accent);
        // Graphics.SetClip has a Rectangle overload, so clipping to the track doesn't need an
        // actual Region object -- avoids allocating (and, this being OnPaint, leaking) one every
        // single frame of the ~30fps indeterminate animation for the entire time it's active.
        g.SetClip(track, CombineMode.Replace);
        g.FillPath(barBrush, barPath);
        g.ResetClip();
    }
}
