using System.Drawing.Drawing2D;

namespace SharpEmuUpdater;

/// <summary>Shared owner-draw row renderer for the two places a ClassifiedRun list is shown:
/// the "Recent Builds" panel on the main window, and the build picker dialog. The build picker
/// only ever shows Success runs (see BuildPickerForm.LoadAsync), so spinnerAngle only matters for
/// the Recent Builds panel -- that's the only place InProgress rows can actually appear.</summary>
public static class BuildListRenderer
{
    // Reused across every row/every repaint instead of a fresh Bitmap+Graphics per call -- the
    // list can now be 1800-2500px wide (the window itself got much wider this session) and show
    // 20-40+ visible rows at once, so allocating a large Bitmap from scratch for every single one
    // of those, every time the list needs to repaint (e.g. right when it first populates), was a
    // real, measurable stutter. Grown (never shrunk) to fit the largest row bounds seen; a leftover
    // few extra pixels of buffer past what's actually blitted every call is harmless.
    private static Bitmap? _rowBuffer;
    private static Graphics? _rowBufferGraphics;

    private static (Bitmap Buffer, Graphics Graphics) GetRowBuffer(int w, int h)
    {
        if (_rowBuffer == null || _rowBuffer.Width < w || _rowBuffer.Height < h)
        {
            _rowBufferGraphics?.Dispose();
            _rowBuffer?.Dispose();
            _rowBuffer = new Bitmap(Math.Max(w, 1), Math.Max(h, 1));
            _rowBufferGraphics = Graphics.FromImage(_rowBuffer);
        }
        return (_rowBuffer, _rowBufferGraphics!);
    }

    public static void DrawItem(DrawItemEventArgs e, IReadOnlyList<ClassifiedRun> items, string? currentSha = null, float spinnerAngle = 0f)
    {
        if (e.Index < 0 || e.Index >= items.Count) return;
        var cr = items[e.Index];

        // Composited off-screen and blitted in a single call, rather than drawn straight onto
        // e.Graphics -- DoubleBuffered plus suppressing the host ListBox's native WM_ERASEBKGND
        // (see BufferedListBox) still weren't quite enough to fully kill flicker on a row
        // repainting every spinner tick. Rendering to a Bitmap first means there's no
        // intermediate partially-painted state for the screen to ever actually show; the final
        // DrawImage is the only thing that touches the real device context.
        int w = Math.Max(1, e.Bounds.Width), h = Math.Max(1, e.Bounds.Height);
        var (buffer, g) = GetRowBuffer(w, h);

        var localBounds = new Rectangle(0, 0, w, h);
        using (var baseBrush = new SolidBrush(Theme.Console))
            g.FillRectangle(baseBrush, localBounds);

        bool isCurrent = currentSha != null && cr.Run.ShortSha == currentSha;
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        if (selected)
        {
            using var selBrush = new SolidBrush(Theme.Elevated);
            g.FillRectangle(selBrush, localBounds);
        }
        else if (isCurrent)
        {
            // A translucent accent wash across the whole row -- the "* " marker plus colored
            // text alone was easy to miss scrolling through a long list; a background tint
            // reads at a glance the way selection highlighting does, without competing with
            // it when a row happens to be both selected and the installed one.
            using var currentBrush = new SolidBrush(Color.FromArgb(36, Theme.Accent));
            g.FillRectangle(currentBrush, localBounds);
        }

        int dotSize = UiScale.S(11);
        var dotRect = new Rectangle(UiScale.S(10), h / 2 - dotSize / 2, dotSize, dotSize);
        DrawStatusDot(g, dotRect, cr.Outcome, spinnerAngle);

        // Fixed-width like sizeText/durationText below, for the same reason: an empty string here
        // (instead of two blank spaces) would shift every column after it two characters to the
        // left for every row except the installed one, throwing off row-to-row alignment for the
        // sha/date/status/size/duration/title columns across the entire list.
        string marker = isCurrent ? "* " : "  ";
        // Size/duration are only ever known for Success rows (see GetRecentClassifiedRunsAsync,
        // which only looks up the Windows artifact -- and so only sets WindowsArtifactSizeBytes
        // -- for that outcome); both columns just render blank for everything else, still
        // reserving their width so the title column stays aligned row to row.
        string sizeText = cr.WindowsArtifactSizeBytes is long bytes ? FormatSize(bytes) : "";
        string durationText = cr.Outcome == BuildOutcome.Success ? FormatDuration(cr.Run.UpdatedAt - cr.Run.CreatedAt) : "";
        // Fixed 3-char width (W/M/L or '-') always rendered, same reasoning as sizeText/durationText
        // above -- a variable-width label (e.g. only showing letters that are actually present)
        // would shift every column after it row to row depending on which platforms that specific
        // build happened to produce, breaking alignment down the whole list.
        string platformsText = FormatPlatforms(cr.AvailablePlatforms);
        // Fixed-width, same reasoning as sizeText/durationText/platformsText above -- this used to
        // be safe left unpadded since an Actions run's ShortSha is always exactly a 7-char git
        // sha, but a Releases-backed row's ShortSha is a tag name (see WorkflowRun.ShortShaOverride),
        // which varies in length ("v0.0.1" vs "win64-main-fa2616d"), so every column after it needs
        // a reserved width to stay aligned row to row instead of drifting with the sha/tag length.
        // "hh" (zero-padded), not "h" -- an unpadded hour renders "7:46 PM" (7 chars) vs
        // "11:04 AM" (8 chars), a variable width that shifts every column after it row to row
        // depending on whether that row's build happened to land on a single- or double-digit
        // hour. Latent since this list has existed, just rarely visible before; became obvious
        // once Recent Builds started mixing rows with very different sha/tag lengths too.
        string text = $"{marker}{cr.Run.ShortSha,-20}{cr.Run.DisplayNumber,-16}{cr.Run.CreatedAt.ToLocalTime():MM-dd hh:mm tt}   {Theme.OutcomeLabel(cr.Outcome),-11}  {platformsText}  {sizeText,-8}{durationText,-8}{cr.Run.DisplayTitle}{(isCurrent ? "  (installed)" : "")}";
        var textRect = new Rectangle(UiScale.S(26), 0, w - UiScale.S(30), h);
        TextRenderer.DrawText(g, text, e.Font, textRect, isCurrent ? Theme.Accent : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        // Explicit source rectangle, not DrawImageUnscaled -- the cached buffer can be larger than
        // this particular row (see GetRowBuffer, which only ever grows it), so blitting the whole
        // thing would paint stale leftover pixels from a previously-drawn, larger row past this
        // row's actual w x h bounds.
        e.Graphics.DrawImage(buffer, e.Bounds, new Rectangle(0, 0, w, h), GraphicsUnit.Pixel);
    }

    /// <summary>"Win/Mac/Lnx" if all three platforms have an artifact, "Win/---/---" if only
    /// Windows, etc. -- fixed 11-char width regardless (three 3-char segments, always spelled
    /// out or "---") so this reads clearly as platform names rather than a bare letter code, while
    /// still preserving the row's column alignment the same way sizeText/durationText do. Blank
    /// (still 11 spaces) for InProgress/Queued/Pending, which haven't produced anything yet.</summary>
    private static string FormatPlatforms(BuildPlatforms platforms)
    {
        if (platforms == BuildPlatforms.None) return "           ";
        string w = platforms.HasFlag(BuildPlatforms.Windows) ? "Win" : "---";
        string m = platforms.HasFlag(BuildPlatforms.MacOS) ? "Mac" : "---";
        string l = platforms.HasFlag(BuildPlatforms.Linux) ? "Lnx" : "---";
        return $"{w}/{m}/{l}";
    }

    private static string FormatSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1 ? $"{mb:0} MB" : $"{Math.Max(1, bytes / 1024.0):0} KB";
    }

    private static string FormatDuration(TimeSpan span)
    {
        // Guards against a nonsensical value rather than showing something misleading -- UpdatedAt
        // is "last touched", not a dedicated "completed at" field, so a run that got re-touched
        // some other way (or one with clock skew) could otherwise render a negative or
        // implausibly long duration.
        if (span <= TimeSpan.Zero || span > TimeSpan.FromHours(3)) return "";
        return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m{span.Seconds:00}s" : $"{span.Seconds}s";
    }

    /// <summary>
    /// Mirrors GitHub's own Actions-tab status glyphs: a filled badge with a white checkmark for
    /// Success, a filled badge with a white X for Regression/Failed, a filled amber badge with
    /// clock hands for Pending (awaiting maintainer approval), a hollow ring for Queued (not
    /// running yet, nothing to animate), a hollow ring with a diagonal slash for Cancelled, a
    /// hollow ring with a horizontal dash for Skipped, and a partial spinning arc for InProgress
    /// -- driven by spinnerAngle, which the caller advances on a timer (see MainForm's spinner
    /// Timer) and passes in fresh every repaint; this method itself is stateless.
    /// </summary>
    private static void DrawStatusDot(Graphics g, Rectangle dotRect, BuildOutcome outcome, float spinnerAngle)
    {
        var priorMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            Color color = Theme.OutcomeColor(outcome);
            switch (outcome)
            {
                case BuildOutcome.InProgress:
                    using (var pen = new Pen(color, Math.Max(1.5f, dotRect.Width / 4f)))
                        g.DrawArc(pen, dotRect, spinnerAngle, 270f);
                    break;
                case BuildOutcome.Queued:
                    using (var pen = new Pen(color, Math.Max(1f, dotRect.Width / 5f)))
                        g.DrawEllipse(pen, dotRect);
                    break;
                case BuildOutcome.Cancelled:
                    using (var pen = new Pen(color, Math.Max(1f, dotRect.Width / 5f)))
                    {
                        g.DrawEllipse(pen, dotRect);
                        float inset = dotRect.Width * 0.2f;
                        g.DrawLine(pen, dotRect.X + inset, dotRect.Bottom - inset, dotRect.Right - inset, dotRect.Y + inset);
                    }
                    break;
                case BuildOutcome.Skipped:
                    using (var pen = new Pen(color, Math.Max(1f, dotRect.Width / 5f)))
                    {
                        g.DrawEllipse(pen, dotRect);
                        float midY = dotRect.Y + dotRect.Height / 2f;
                        float inset = dotRect.Width * 0.26f;
                        g.DrawLine(pen, dotRect.X + inset, midY, dotRect.Right - inset, midY);
                    }
                    break;
                case BuildOutcome.Success:
                    using (var brush = new SolidBrush(color))
                        g.FillEllipse(brush, dotRect);
                    DrawCheckmark(g, dotRect);
                    break;
                case BuildOutcome.Regression:
                case BuildOutcome.Failed:
                    using (var brush = new SolidBrush(color))
                        g.FillEllipse(brush, dotRect);
                    DrawCross(g, dotRect);
                    break;
                case BuildOutcome.Pending:
                    using (var brush = new SolidBrush(color))
                        g.FillEllipse(brush, dotRect);
                    DrawClockHands(g, dotRect);
                    break;
                default:
                    using (var brush = new SolidBrush(color))
                        g.FillEllipse(brush, dotRect);
                    break;
            }
        }
        finally
        {
            g.SmoothingMode = priorMode;
        }
    }

    private static void DrawCheckmark(Graphics g, Rectangle rect)
    {
        using var pen = new Pen(Color.White, Math.Max(1f, rect.Width / 5.5f))
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        float w = rect.Width, h = rect.Height;
        g.DrawLines(pen, new[]
        {
            new PointF(rect.X + w * 0.20f, rect.Y + h * 0.52f),
            new PointF(rect.X + w * 0.42f, rect.Y + h * 0.74f),
            new PointF(rect.X + w * 0.82f, rect.Y + h * 0.26f),
        });
    }

    private static void DrawCross(Graphics g, Rectangle rect)
    {
        using var pen = new Pen(Color.White, Math.Max(1f, rect.Width / 5.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float inset = rect.Width * 0.24f;
        g.DrawLine(pen, rect.X + inset, rect.Y + inset, rect.Right - inset, rect.Bottom - inset);
        g.DrawLine(pen, rect.Right - inset, rect.Y + inset, rect.X + inset, rect.Bottom - inset);
    }

    private static void DrawClockHands(Graphics g, Rectangle rect)
    {
        using var pen = new Pen(Color.White, Math.Max(1f, rect.Width / 7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        // Minute hand pointing to 12, hour hand pointing toward 3 -- the classic clock silhouette.
        g.DrawLine(pen, center, new PointF(center.X, rect.Y + rect.Height * 0.18f));
        g.DrawLine(pen, center, new PointF(rect.X + rect.Width * 0.74f, center.Y));
    }
}
