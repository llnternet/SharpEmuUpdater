namespace SharpEmuUpdater;

/// <summary>Owner-draw row renderer for the fork picker list, styled to match BuildListRenderer.</summary>
public static class ForkListRenderer
{
    public static void DrawItem(DrawItemEventArgs e, IReadOnlyList<ForkInfo> items, string? currentOwner, string? currentBranch = null)
    {
        if (e.Index < 0 || e.Index >= items.Count) return;
        var fork = items[e.Index];

        using (var baseBrush = new SolidBrush(Theme.Console))
            e.Graphics.FillRectangle(baseBrush, e.Bounds);

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        if (selected)
        {
            using var selBrush = new SolidBrush(Theme.Elevated);
            e.Graphics.FillRectangle(selBrush, e.Bounds);
        }

        bool isCurrent = currentOwner != null && string.Equals(fork.Owner, currentOwner, StringComparison.OrdinalIgnoreCase)
            && (currentBranch == null || string.Equals(fork.Branch, currentBranch, StringComparison.OrdinalIgnoreCase));

        int dotSize = UiScale.S(8);
        var dotRect = new Rectangle(e.Bounds.X + UiScale.S(10), e.Bounds.Y + e.Bounds.Height / 2 - dotSize / 2, dotSize, dotSize);
        using (var dotBrush = new SolidBrush(isCurrent ? Theme.Accent : Theme.Muted))
            e.Graphics.FillEllipse(dotBrush, dotRect);

        // Fixed-width, not empty, when absent -- see BuildListRenderer's identical fix. An empty
        // string here would shift every column after it two characters left for every row except
        // the active one, throwing off owner/branch/date alignment across the whole list.
        string marker = isCurrent ? "* " : "  ";
        // PullRequestNumber is null for a direct push not tied to an open PR -- e.g. the upstream
        // owner's own main branch -- so this naturally reads as blank for those rather than a
        // misleading "PR #" label with nothing after it.
        string prSuffix = fork.PullRequestNumber is int pr ? $" (PR #{pr})" : "";
        string branchPart = $"branch:{fork.Branch}{prSuffix}";
        // "hh" (zero-padded), not "h" -- an unpadded hour renders "7:46 PM" (7 chars) vs
        // "11:04 AM" (8 chars), a variable width that shifts the RepoFullName column left/right
        // row to row depending on whether that row's push happened to land on a single- or
        // double-digit hour (see the identical fix/comment in BuildListRenderer.cs).
        string text = $"{marker}{fork.OwnerLogin,-20} {branchPart,-42} {fork.PushedAt.ToLocalTime():MM-dd hh:mm tt}   {fork.RepoFullName}{(isCurrent ? "  (active)" : "")}";
        var textRect = new Rectangle(e.Bounds.X + UiScale.S(26), e.Bounds.Y, e.Bounds.Width - UiScale.S(30), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, e.Font, textRect, isCurrent ? Theme.Accent : Theme.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}
