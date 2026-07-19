namespace SharpEmuUpdater;

/// <summary>
/// "Update Available" dialog for SharpEmu Updater's own self-update check (see
/// SelfUpdateChecker/MainForm.CheckForSelfUpdateAsync) -- modeled on PCSX2's own Automatic
/// Updater dialog (current/new version, download size, changelog, Download and Install / Skip
/// This Update / Remind Me Later), restyled to this app's own dark purple theme.
/// </summary>
public sealed class SelfUpdateAvailableForm : Form
{
    /// <summary>True if "Skip This Update" was clicked -- the caller should persist this version
    /// as skipped so it's never shown again. "Remind Me Later" and closing the dialog both leave
    /// this false, so the same version prompts again on the next check.</summary>
    public bool Skipped { get; private set; }

    private readonly SelfUpdateInfo _info;
    private RichTextBox _notesBox = null!;
    private RoundedButton _downloadButton = null!;
    private RoundedButton _skipButton = null!;
    private RoundedButton _laterButton = null!;
    private AccentProgress _progressBar = null!;
    private Label _statusLabel = null!;
    private bool _installing;

    public SelfUpdateAvailableForm(SelfUpdateInfo info)
    {
        _info = info;

        Text = "SharpEmu Updater";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        // Wide enough for all three bottom buttons ("Download and Install...", "Skip This
        // Update", "Remind Me Later") side by side without clipping -- confirmed live that 520
        // was too narrow: the RightToLeft-flowed button row overflowed past the left edge, and
        // since FlowLayoutPanel with WrapContents=false doesn't shrink or wrap, "Download and
        // Install..." (the widest, and the leftmost once laid out right-to-left) was the one that
        // got clipped, showing as "load and Install...".
        ClientSize = new Size(UiScale.S(640), UiScale.S(480));
        BackColor = Theme.BgMid;
        Font = Theme.UiFont(9f);
        DwmHelper.ApplyDarkTitleBar(Handle, Theme.IsDark);
        Icon = AppIcon.TryGetClone();

        var root = new GradientPanel { Dock = DockStyle.Fill, Padding = new Padding(UiScale.S(20)) };
        Controls.Add(root);

        var content = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.Transparent };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: heading
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: version/size details
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: "Changes:" label
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 3: changelog (fills remaining space)
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: progress + status + buttons
        root.Controls.Add(content);

        content.Controls.Add(BuildHeading(), 0, 0);
        content.Controls.Add(BuildDetailsBlock(), 0, 1);

        var changesLabel = new Label
        {
            Text = "Changes:",
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = Theme.UiFont(10f, FontStyle.Bold),
            Margin = new Padding(0, UiScale.S(14), 0, UiScale.S(6)),
        };
        content.Controls.Add(changesLabel, 0, 2);

        var notesCard = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, UiScale.S(14)), Padding = new Padding(UiScale.S(4)) };
        _notesBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Console,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.UiFont(9f),
            ReadOnly = true,
            Text = string.IsNullOrWhiteSpace(_info.ReleaseNotes) ? "(No release notes provided.)" : _info.ReleaseNotes,
        };
        notesCard.Controls.Add(_notesBox);
        content.Controls.Add(notesCard, 0, 3);

        content.Controls.Add(BuildBottomStack(), 0, 4);
    }

    private Control BuildHeading()
    {
        var row = new BufferedTableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiScale.S(10)) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var icon = new Label
        {
            Text = "⬇", // downward arrow, same spirit as PCSX2's own green download icon
            AutoSize = true,
            ForeColor = Theme.Success,
            BackColor = Color.Transparent,
            Font = Theme.UiFont(18f, FontStyle.Bold),
            Margin = new Padding(0, 0, UiScale.S(10), 0),
        };
        var heading = new Label
        {
            Text = "Update Available",
            AutoSize = true,
            ForeColor = Theme.Text,
            BackColor = Color.Transparent,
            Font = Theme.UiFont(15f, FontStyle.Bold),
            Margin = new Padding(0, UiScale.S(2), 0, 0),
        };
        row.Controls.Add(icon, 0, 0);
        row.Controls.Add(heading, 1, 0);
        return row;
    }

    private Control BuildDetailsBlock()
    {
        var stack = new BufferedTableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        string currentLine = $"Current Version: v{AppVersion.Current}" +
            (_info.CurrentVersionPublishedAt is { } curDate ? $" ({curDate.ToLocalTime():f})" : "");
        string newLine = $"New Version: {_info.Version} ({_info.PublishedAt.ToLocalTime():f})";
        string sizeLine = $"Download Size: {FormatSize(_info.DownloadSizeBytes)}";

        foreach (string text in new[] { currentLine, newLine, sizeLine })
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Muted,
                BackColor = Color.Transparent,
                Font = Theme.UiFont(9f),
                Margin = new Padding(0, 0, 0, UiScale.S(4)),
            };
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(label, 0, stack.Controls.Count);
        }
        return stack;
    }

    private Control BuildBottomStack()
    {
        var stack = new BufferedTableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.Transparent };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _progressBar = new AccentProgress { Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, UiScale.S(6)), Visible = false };
        _statusLabel = new Label
        {
            Text = "",
            AutoSize = true,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            Font = Theme.UiFont(8.5f),
            Margin = new Padding(0, 0, 0, UiScale.S(6)),
            Visible = false,
        };

        _laterButton = new RoundedButton { Text = "Remind Me Later", Variant = ButtonVariant.Ghost };
        _skipButton = new RoundedButton { Text = "Skip This Update", Variant = ButtonVariant.Ghost };
        _downloadButton = new RoundedButton { Text = "Download and Install...", Variant = ButtonVariant.Accent };

        _laterButton.Click += (_, _) => { if (!_installing) Close(); };
        _skipButton.Click += (_, _) => { if (!_installing) { Skipped = true; Close(); } };
        _downloadButton.Click += async (_, _) => await DownloadAndInstallAsync();

        int breathingRoom = UiScale.S(20);
        foreach (var b in new[] { _laterButton, _skipButton, _downloadButton })
            b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + b.Padding.Horizontal + breathingRoom;
        int buttonHeight = UiScale.S(34);
        foreach (var b in new[] { _laterButton, _skipButton, _downloadButton })
            b.Height = buttonHeight;

        // BufferedPanel (double-buffered), not a plain Panel -- a plain Panel here left the
        // RoundedButton children's own corner-rounding looking wrong (square corners) after
        // LayoutButtonsCentered repositions them on Resize, the same class of stale/torn redraw
        // this codebase's other Buffered* container variants exist to avoid. Explicit Location
        // math (not a FlowLayoutPanel) since that was tried first and didn't reliably resolve to
        // the cell's real full width -- see git history/PR discussion for that attempt.
        int gap = UiScale.S(8);
        var buttonPanel = new BufferedPanel { Dock = DockStyle.Top, Height = buttonHeight, BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, UiScale.S(10)) };
        buttonPanel.Resize += (_, _) => LayoutButtonsCentered(buttonPanel, gap);
        buttonPanel.Controls.Add(_laterButton);
        buttonPanel.Controls.Add(_skipButton);
        buttonPanel.Controls.Add(_downloadButton);

        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, buttonHeight + UiScale.S(10)));
        stack.Controls.Add(_progressBar, 0, 0);
        stack.Controls.Add(_statusLabel, 0, 1);
        stack.Controls.Add(buttonPanel, 0, 2);
        stack.RowCount = 3;

        CancelButton = _laterButton;
        return stack;
    }

    /// <summary>Centers the three buttons as a group, left to right in reading order -- called on
    /// Resize (not just once at construction) since buttonPanel's actual width isn't known until
    /// the TableLayoutPanel that owns it finishes its own layout pass, which happens after this
    /// method first runs. Forces a repaint of each button after moving it -- without this, a
    /// RoundedButton's clipped corners can show stale/torn pixels from its previous position
    /// until something else happens to invalidate that screen region.</summary>
    private static void LayoutButtonsCentered(Panel buttonPanel, int gap)
    {
        Control[] buttons = { buttonPanel.Controls[0], buttonPanel.Controls[1], buttonPanel.Controls[2] };
        int totalWidth = buttons.Sum(b => b.Width) + gap * (buttons.Length - 1);
        int left = Math.Max(0, (buttonPanel.Width - totalWidth) / 2);
        foreach (var button in buttons)
        {
            button.Left = left;
            button.Top = 0;
            button.Invalidate();
            left = button.Right + gap;
        }
        buttonPanel.Invalidate();
    }

    private async Task DownloadAndInstallAsync()
    {
        _installing = true;
        _downloadButton.Enabled = false;
        _skipButton.Enabled = false;
        _laterButton.Enabled = false;
        _progressBar.Visible = true;
        _statusLabel.Visible = true;
        _statusLabel.ForeColor = Theme.Muted;
        _statusLabel.Text = "Downloading update...";

        var progress = new Progress<double>(fraction => _progressBar.DownloadFraction = fraction);

        try
        {
            string newExePath = await SelfUpdateInstaller.DownloadAndStageAsync(_info, progress, CancellationToken.None);
            _statusLabel.Text = "Restarting to install update...";
            SelfUpdateInstaller.LaunchSwapAndRestart(newExePath);
            // DialogResult.OK signals the caller (MainForm) that the handoff script is now
            // waiting for this process to exit -- it should call its own ExitApplication()
            // immediately afterward rather than sit through the script's wait timeout for no
            // reason.
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Theme.Danger;
            _statusLabel.Text = $"Update failed: {ex.Message}";
            _installing = false;
            _downloadButton.Enabled = true;
            _skipButton.Enabled = true;
            _laterButton.Enabled = true;
            _progressBar.Visible = false;
        }
    }

    private static string FormatSize(long bytes)
    {
        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1 ? $"{mb:0.0} MB" : $"{Math.Max(1, bytes / 1024.0):0} KB";
    }
}
