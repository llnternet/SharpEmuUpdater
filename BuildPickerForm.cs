namespace SharpEmuUpdater;

/// <summary>
/// Lets the user jump to any successful build, not just the newest -- upgrade or downgrade.
/// Regression/still-failing runs are deliberately left out of the list entirely; there's never
/// a good reason to install a build the app itself knows is broken.
/// </summary>
public sealed class BuildPickerForm : Form
{
    private const int RunsToFetch = 60;

    private readonly GitHubUpdaterService _service;
    private readonly string? _currentSha;
    private readonly string _branch;

    private BufferedListBox _listBox = null!;
    private Label _statusLabel = null!;
    private RoundedButton _installButton = null!;
    private RoundedButton _cancelButton = null!;
    private List<ClassifiedRun> _successfulRuns = new();
    private bool _loading;
    // Single branch's run history, same cost as the main window's own check -- refreshes at a
    // similarly modest cadence while this dialog's open; build history doesn't need sub-minute
    // polling to still feel current.
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 30000 };

    public WorkflowRun? SelectedRun { get; private set; }

    public BuildPickerForm(GitHubUpdaterService service, string? currentSha, string branch)
    {
        _service = service;
        _currentSha = currentSha;
        _branch = branch;

        Text = "Select a Build to Install";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ClientSize = new Size(UiScale.S(680), UiScale.S(520));
        MinimumSize = new Size(UiScale.S(480), UiScale.S(360));
        BackColor = Theme.BgMid;
        Font = Theme.UiFont(9f);
        DwmHelper.ApplyDarkTitleBar(Handle, Theme.IsDark);

        Icon = AppIcon.TryGetClone();

        var root = new GradientPanel { Dock = DockStyle.Fill, Padding = new Padding(UiScale.S(20)) };
        Controls.Add(root);

        var content = new BufferedTableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(content);

        var intro = new Label
        {
            Text = "Choose any successful build to install. This upgrades or downgrades your\n" +
                   "installed copy -- regressions and still-failing runs aren't shown here.\n" +
                   "* marks your currently installed build. Right-click a build to copy its SHA\n" +
                   "or view it on GitHub.",
            AutoSize = true,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            Font = Theme.UiFont(9f),
            Margin = new Padding(0, 0, 0, UiScale.S(12)),
        };
        content.Controls.Add(intro, 0, 0);

        var listCard = new RoundedPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, UiScale.S(16)), Padding = new Padding(UiScale.S(4)) };
        _listBox = new BufferedListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Console,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = UiScale.S(22),
            Font = Theme.MonoFont(9f),
        };
        _listBox.DrawItem += (_, e) => BuildListRenderer.DrawItem(e, _successfulRuns, _currentSha);
        _listBox.SelectedIndexChanged += (_, _) => _installButton.Enabled = _listBox.SelectedIndex >= 0;
        _listBox.DoubleClick += (_, _) => { if (_listBox.SelectedIndex >= 0) AcceptSelection(); };
        BuildListContextMenu.Attach(_listBox, index => index < _successfulRuns.Count ? _successfulRuns[index].Run : null);
        listCard.Controls.Add(_listBox);
        content.Controls.Add(listCard, 0, 1);

        var buttonRow = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Color.Transparent,
            WrapContents = false,
        };
        // Explicit Margin(0), not left unset -- this is the rightmost button in a RightToLeft
        // FlowLayoutPanel, so it needs no margin of its own (the gap comes entirely from
        // _installButton's own right margin below), but leaving it unset falls back to WinForms'
        // default Padding(3,3,3,3), which widened the gap between these two buttons to 11px
        // instead of the 8px used everywhere else.
        _cancelButton = new RoundedButton { Text = "Cancel", Variant = ButtonVariant.Ghost, DialogResult = DialogResult.Cancel, Margin = new Padding(0) };
        _installButton = new RoundedButton { Text = "Install Selected", Variant = ButtonVariant.Accent, Enabled = false, Margin = new Padding(0, 0, UiScale.S(8), 0) };
        _installButton.Click += (_, _) => AcceptSelection();

        // RoundedButton doesn't auto-size to its own text (WinForms' default Button width is a
        // fixed ~75px, nowhere near enough for "Install Selected") -- same measured-width
        // approach as the main window's button row.
        int breathingRoom = UiScale.S(20);
        foreach (var b in new[] { _cancelButton, _installButton })
            b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + b.Padding.Horizontal + breathingRoom;

        buttonRow.Controls.Add(_cancelButton);
        buttonRow.Controls.Add(_installButton);
        content.Controls.Add(buttonRow, 0, 2);

        // AutoSize = true directly contradicts Dock = DockStyle.Fill -- AutoSize shrinks the
        // Label to exactly fit its text, which fights Dock.Fill + TextAlign.MiddleCenter's whole
        // point (centering the text within the full card). With both set, the label never
        // actually rendered its "Loading..." text where TextAlign/Dock intended, and there was no
        // visible loading indication at all during the load -- which read as "the dialog doesn't
        // load / looks broken" even though the load itself was working fine underneath.
        _statusLabel = new Label
        {
            Text = "Loading build history...",
            ForeColor = Theme.Muted,
            BackColor = Theme.Console,
            Font = Theme.UiFont(9f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _listBox.Visible = false;
        listCard.Controls.Add(_statusLabel);

        CancelButton = _cancelButton;
        AcceptButton = _installButton;
        Shown += async (_, _) => await LoadAsync(preserveSelection: false);
        _refreshTimer.Tick += async (_, _) => await LoadAsync(preserveSelection: true);
        _refreshTimer.Start();
        FormClosed += (_, _) => _refreshTimer.Dispose();
    }

    private void AcceptSelection()
    {
        if (_listBox.SelectedIndex < 0 || _listBox.SelectedIndex >= _successfulRuns.Count) return;
        SelectedRun = _successfulRuns[_listBox.SelectedIndex].Run;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// preserveSelection is true for the periodic background refresh -- re-selects whatever the
    /// user had clicked on rather than snapping back to the installed build every time, so a live
    /// update happening mid-interaction doesn't yank their choice out from under them.
    /// </summary>
    private async Task LoadAsync(bool preserveSelection)
    {
        if (_loading || IsDisposed) return;
        _loading = true;
        try
        {
            string? previouslySelectedSha = preserveSelection && _listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _successfulRuns.Count
                ? _successfulRuns[_listBox.SelectedIndex].Run.ShortSha
                : null;

            var classified = GitHubUpdaterService.IsUpstreamMainBranch(_service.Owner, _service.Repo, _branch)
                ? await _service.GetRecentReleaseBuildsAsync(RunsToFetch, CancellationToken.None)
                : await _service.GetRecentClassifiedRunsAsync(RunsToFetch, _branch, CancellationToken.None);
            // The caller (MainForm.OpenBuildPickerAsync) wraps this dialog in a `using` -- if the
            // user closed it (Cancel, or the window X) while the fetch above was still in flight,
            // it's already disposed by the time this continuation resumes, and every control
            // touched below would throw ObjectDisposedException.
            if (IsDisposed) return;

            _successfulRuns = classified.Where(c => c.Outcome == BuildOutcome.Success).ToList();

            if (_successfulRuns.Count == 0)
            {
                _statusLabel.Text = "No successful builds found in recent history.";
                _statusLabel.Visible = true;
                _listBox.Visible = false;
                return;
            }

            _statusLabel.Visible = false;
            _listBox.Visible = true;

            _listBox.Items.Clear();
            _listBox.Items.AddRange(_successfulRuns.Cast<object>().ToArray());

            string? targetSha = previouslySelectedSha ?? _currentSha;
            int indexToSelect = targetSha == null ? -1 : _successfulRuns.FindIndex(c => c.Run.ShortSha == targetSha);
            if (indexToSelect >= 0) _listBox.SelectedIndex = indexToSelect;
        }
        catch (Exception ex)
        {
            // Only surface the error state on the very first load -- once the list is already
            // showing, a transient failure on a background refresh shouldn't yank it away or
            // bury it behind an error the user didn't ask to see; the next tick will try again.
            if (_successfulRuns.Count == 0)
                _statusLabel.Text = $"Could not load build history: {ex.Message}";
        }
        finally
        {
            _loading = false;
        }
    }
}
