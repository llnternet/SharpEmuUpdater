using System.Diagnostics;

namespace SharpEmuUpdater;

/// <summary>Right-click "Copy Commit SHA" / "View on GitHub" for a row in any BufferedListBox
/// showing ClassifiedRun items -- shared between MainForm's Recent Builds list and
/// BuildPickerForm's build picker, since both are otherwise independent BufferedListBox instances
/// with their own backing list.</summary>
public static class BuildListContextMenu
{
    /// <summary>getRunAt resolves whatever backing list the caller currently has (a field that
    /// gets reassigned on every refresh) at click time, rather than a snapshot passed in once --
    /// so this keeps working correctly across refreshes without needing to be re-attached.</summary>
    public static void Attach(BufferedListBox listBox, Func<int, WorkflowRun?> getRunAt)
    {
        var menu = new ContextMenuStrip();
        var copyShaItem = menu.Items.Add("Copy Commit SHA");
        var viewItem = menu.Items.Add("View on GitHub");
        WorkflowRun? contextRun = null;

        // ContextMenuStrip has no built-in notion of "which row was right-clicked" for a plain
        // ListBox -- Opening fires before the menu is shown, in time to select the row under the
        // cursor (so it's visually obvious which one the menu applies to) and resolve which run
        // that actually is.
        menu.Opening += (_, e) =>
        {
            var pt = listBox.PointToClient(Cursor.Position);
            int index = listBox.IndexFromPoint(pt);
            if (index < 0)
            {
                e.Cancel = true;
                return;
            }
            listBox.SelectedIndex = index;
            contextRun = getRunAt(index);
            if (contextRun == null) e.Cancel = true;
        };

        copyShaItem.Click += (_, _) =>
        {
            if (contextRun != null) Clipboard.SetText(contextRun.HeadSha);
        };
        viewItem.Click += (_, _) =>
        {
            if (contextRun == null || string.IsNullOrEmpty(contextRun.HtmlUrl)) return;
            try { Process.Start(new ProcessStartInfo(contextRun.HtmlUrl) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Log($"Could not open {contextRun.HtmlUrl}: {ex.Message}"); }
        };

        listBox.ContextMenuStrip = menu;
    }
}
