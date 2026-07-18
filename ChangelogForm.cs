namespace SharpEmuUpdater;

/// <summary>Small read-only dialog listing the commits between two shas (see
/// GitHubUpdaterService.GetCommitsBetweenAsync) -- lets you see what's actually changing before
/// installing an update, beyond just the single latest commit's own title.</summary>
public sealed class ChangelogForm : Form
{
    public ChangelogForm(string title, IReadOnlyList<(string ShortSha, string Message)> commits)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ClientSize = new Size(UiScale.S(560), UiScale.S(420));
        MinimumSize = new Size(UiScale.S(360), UiScale.S(240));
        BackColor = Theme.BgMid;
        Font = Theme.UiFont(9f);
        DwmHelper.ApplyDarkTitleBar(Handle, Theme.IsDark);

        Icon = AppIcon.TryGetClone();

        var root = new GradientPanel { Dock = DockStyle.Fill, Padding = new Padding(UiScale.S(20)) };
        Controls.Add(root);

        var content = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(content);

        var card = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, UiScale.S(16)), Padding = new Padding(UiScale.S(4)) };
        var box = new RichTextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BackColor = Theme.Console,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.MonoFont(9f),
            DetectUrls = false,
        };

        if (commits.Count == 0)
        {
            box.Text = "No commits found between these two builds.";
        }
        else
        {
            foreach (var (sha, message) in commits)
            {
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionColor = Theme.Accent;
                box.AppendText(sha + "  ");
                box.SelectionStart = box.TextLength;
                box.SelectionLength = 0;
                box.SelectionColor = Theme.Text;
                box.AppendText(message + Environment.NewLine);
            }
        }
        card.Controls.Add(box);
        content.Controls.Add(card, 0, 0);

        var buttonRow = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Color.Transparent,
            WrapContents = false,
        };
        var closeButton = new RoundedButton { Text = "Close", Variant = ButtonVariant.Accent, DialogResult = DialogResult.OK };
        buttonRow.Controls.Add(closeButton);
        content.Controls.Add(buttonRow, 0, 1);

        CancelButton = closeButton;
        AcceptButton = closeButton;
    }
}
