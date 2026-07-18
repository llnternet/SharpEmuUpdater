namespace SharpEmuUpdater;

/// <summary>
/// Lets the user switch which fork (and branch) of sharpemu this app tracks builds from. The
/// list is always derived live from the upstream repo's own Actions run history -- no local
/// caching, so it reflects whoever is actually active right now. A fork that hasn't shown up in
/// upstream's Actions tab recently just isn't offered. "Upstream" is resolved fresh each time
/// this loads (see GitHubUpdaterService.ResolveRepoAsync), so if it ever moves again this just
/// follows automatically instead of needing GitHubUpdaterService.UpstreamOwner/UpstreamRepo
/// hand-updated.
/// </summary>
public sealed class ForkPickerForm : Form
{
    private readonly GitHubUpdaterService _service;
    private readonly string _currentOwner;
    private readonly string _currentBranch;

    private BufferedListBox _listBox = null!;
    private Label _introLabel = null!;
    private Label _statusLabel = null!;
    // Visible, animated proof-of-life while the (real, can take several seconds -- up to a
    // ~40-request contributor scan) initial load is in flight. A static "Loading fork list..."
    // label with nothing moving reads as frozen/broken to someone who doesn't know that scan is
    // normally slow, same problem MainForm's own AccentProgress bar exists to avoid elsewhere.
    private AccentProgress _loadingProgress = null!;
    // Groups _statusLabel + _loadingProgress so both toggle Visible together as one unit, instead
    // of the loading text and the loading bar needing to be kept in sync at every call site.
    private TableLayoutPanel _loadingPanel = null!;
    private RoundedButton _switchButton = null!;
    private RoundedButton _cancelButton = null!;
    // _allForks is everything LoadAsync fetched from GitHub; _forks is _allForks filtered by
    // whatever's currently in the search box (see ApplyFilterAndPopulate) -- what's actually in
    // the ListBox, and so what AcceptSelection/ForkListRenderer/IsCurrent all index into.
    private List<ForkInfo> _allForks = new();
    private List<ForkInfo> _forks = new();
    private string _searchText = "";
    private bool _loading;
    // Fork discovery is a lot more expensive than the main window's own live-status poll (a
    // two-stage scan across every active contributor -- up to MaxForksToScan repos, two requests
    // each, throttled but still a real burst -- not one branch's run history). Originally 60s, but
    // that meant leaving this dialog open for even a few minutes could burn several hundred
    // requests re-scanning the exact same contributor list; who's actively building rarely changes
    // minute to minute, so 5 minutes still keeps the dialog reasonably current for as long as
    // someone would realistically leave it open, without re-running the scan anywhere near that
    // often.
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 300000 };

    public ForkInfo? SelectedFork { get; private set; }

    public ForkPickerForm(GitHubUpdaterService service, string currentOwner, string currentBranch)
    {
        _service = service;
        _currentOwner = currentOwner;
        _currentBranch = currentBranch;

        Text = "Switch Fork";
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

        _introLabel = new Label
        {
            Text = IntroText(GitHubUpdaterService.UpstreamOwner, GitHubUpdaterService.UpstreamRepo),
            AutoSize = true,
            ForeColor = Theme.Muted,
            BackColor = Color.Transparent,
            Font = Theme.UiFont(9f),
            Margin = new Padding(0, 0, 0, UiScale.S(12)),
        };
        content.Controls.Add(_introLabel, 0, 0);

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
        _listBox.DrawItem += (_, e) => ForkListRenderer.DrawItem(e, _forks, _currentOwner, _currentBranch);
        _listBox.SelectedIndexChanged += (_, _) => _switchButton.Enabled = _listBox.SelectedIndex >= 0;
        _listBox.DoubleClick += (_, _) => { if (_listBox.SelectedIndex >= 0) AcceptSelection(); };
        listCard.Controls.Add(_listBox);

        // BorderStyle.FixedSingle draws a plain, non-themeable native border (a fixed system
        // gray, not respecting Theme.CardBorder or dark mode at all) -- see BuildBuildsCard's own
        // search box in MainForm.cs for the same fix and fuller reasoning. Wrapping a
        // BorderStyle.None TextBox in a RoundedPanel instead matches the rounded, theme-colored
        // border used everywhere else in this app, including listCard itself.
        var searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Elevated,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.UiFont(9f),
        };
        searchBox.TextChanged += (_, _) =>
        {
            _searchText = searchBox.Text;
            ApplyFilterAndPopulate();
        };
        var searchIcon = new SearchIcon { Dock = DockStyle.Right, Width = UiScale.S(24) };
        // See MainForm.BuildBuildsCard's identical search box for why this is sized off the
        // TextBox's own PreferredHeight (plus a few px of extra slack -- PreferredHeight alone
        // still clipped comma descenders in practice) rather than a guessed constant, and why it's
        // a fixed, modest width (Dock.Left inside a full-width spacer) instead of spanning the
        // whole card -- search terms here are always short.
        int searchVPad = UiScale.S(6);
        int searchBoxHeight = searchBox.PreferredHeight + searchVPad * 2 + UiScale.S(4);
        var searchBoxWrap = new RoundedPanel
        {
            Dock = DockStyle.Left,
            Width = UiScale.S(240),
            BackColor = Theme.Elevated,
            CornerRadius = UiScale.S(6),
            Padding = new Padding(UiScale.S(8), searchVPad, UiScale.S(8), searchVPad),
        };
        // Added before searchBox -- Dock.Right needs to be added first so it reserves its own
        // slice of the panel before searchBox's Dock.Fill claims whatever's left.
        searchBoxWrap.Controls.Add(searchIcon);
        searchBoxWrap.Controls.Add(searchBox);

        var searchRow = new Panel { Dock = DockStyle.Top, Height = searchBoxHeight, Margin = new Padding(UiScale.S(4)), BackColor = Color.Transparent };
        searchRow.Controls.Add(searchBoxWrap);
        listCard.Controls.Add(searchRow);

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
        // See BuildPickerForm's identical fix -- an unset Margin here fell back to WinForms'
        // default Padding(3,3,3,3), widening the gap to this button to 11px instead of 8px.
        _cancelButton = new RoundedButton { Text = "Cancel", Variant = ButtonVariant.Ghost, DialogResult = DialogResult.Cancel, Margin = new Padding(0) };
        _switchButton = new RoundedButton { Text = "Switch to This Fork", Variant = ButtonVariant.Accent, Enabled = false, Margin = new Padding(0, 0, UiScale.S(8), 0) };
        _switchButton.Click += (_, _) => AcceptSelection();

        int breathingRoom = UiScale.S(20);
        foreach (var b in new[] { _cancelButton, _switchButton })
            b.Width = TextRenderer.MeasureText(b.Text, b.Font).Width + b.Padding.Horizontal + breathingRoom;

        buttonRow.Controls.Add(_cancelButton);
        buttonRow.Controls.Add(_switchButton);
        content.Controls.Add(buttonRow, 0, 2);

        // AutoSize = true directly contradicts Dock = DockStyle.Fill -- see BuildPickerForm's own
        // identical fix for the full reasoning. Without this, the "Loading fork list..." text
        // never actually rendered where TextAlign/Dock intended, making the dialog look blank
        // (no loading indication, no rows) for however long the two-stage contributor scan takes.
        _statusLabel = new Label
        {
            Text = "Loading fork list... this can take a little while.",
            ForeColor = Theme.Muted,
            BackColor = Theme.Console,
            Font = Theme.UiFont(9f),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        // A TableLayoutPanel (explicit row indices), not two plain Dock=Top/Bottom siblings --
        // guarantees the bar always lands directly under the text regardless of add-order. Row 1
        // is Absolute, not AutoSize -- AutoSize + a Dock=Fill child is the exact bug class this
        // codebase already hit once (see BuildPickerForm/ForkPickerForm's own _statusLabel fix):
        // an AutoSize row has nothing to measure a Dock=Fill child by, so it can't be trusted to
        // converge on the intended 4px-tall bar. Absolute sidesteps that entirely.
        _loadingProgress = new AccentProgress
        {
            Dock = DockStyle.Fill,
            // Vertical insets of 6px top/bottom inside a 16px-tall Absolute row leave exactly the
            // 4px this control actually paints at -- see AccentProgress's own Height, set once in
            // its constructor and otherwise overridden the instant Dock=Fill is applied.
            Margin = new Padding(UiScale.S(60), UiScale.S(6), UiScale.S(60), UiScale.S(6)),
        };
        _loadingPanel = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Console,
        };
        _loadingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _loadingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _loadingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, UiScale.S(16)));
        _loadingPanel.Controls.Add(_statusLabel, 0, 0);
        _loadingPanel.Controls.Add(_loadingProgress, 0, 1);

        _listBox.Visible = false;
        listCard.Controls.Add(_loadingPanel);

        CancelButton = _cancelButton;
        AcceptButton = _switchButton;
        Shown += async (_, _) => await LoadAsync(preserveSelection: false);
        _refreshTimer.Tick += async (_, _) => await LoadAsync(preserveSelection: true);
        _refreshTimer.Start();
        FormClosed += (_, _) => _refreshTimer.Dispose();
    }

    private void AcceptSelection()
    {
        if (_listBox.SelectedIndex < 0 || _listBox.SelectedIndex >= _forks.Count) return;
        SelectedFork = _forks[_listBox.SelectedIndex];
        DialogResult = DialogResult.OK;
        Close();
    }

    private bool IsCurrent(ForkInfo f) =>
        string.Equals(f.Owner, _currentOwner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(f.Branch, _currentBranch, StringComparison.OrdinalIgnoreCase);

    private static string IntroText(string upstreamOwner, string upstreamRepo) =>
        $"Developers currently active on {upstreamOwner}/{upstreamRepo}'s own\n" +
        "Actions history, alphabetical by developer (most recent branch first within each),\n" +
        "one row per branch they're building. * marks the fork/branch you're currently on,\n" +
        "pinned to the top. Fetched fresh every time.";

    /// <summary>Re-filters _allForks by the search box's current text and repopulates the ListBox
    /// -- purely local, no network call, so typing filters instantly rather than waiting on
    /// another ~40-request contributor scan.</summary>
    private void ApplyFilterAndPopulate()
    {
        // Deliberately NOT matching against RepoFullName -- every fork keeps the same repo name
        // as upstream by default ("<owner>/sharpemu"), so matching on it makes the single most
        // natural search term anyone would try ("sharpemu") match nearly every row in the list
        // instead of narrowing to the one actually named "sharpemu" (the upstream org itself),
        // unlike searching any other owner's actual username. Confirmed live against the real
        // fork list -- OwnerLogin/Branch alone is what actually distinguishes rows here.
        _forks = string.IsNullOrWhiteSpace(_searchText)
            ? _allForks
            : _allForks.Where(f =>
                f.OwnerLogin.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                f.Branch.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
              .ToList();

        _listBox.Items.Clear();
        _listBox.Items.AddRange(_forks.Cast<object>().ToArray());
    }

    /// <summary>
    /// preserveSelection is true for the periodic background refresh -- re-selects whatever the
    /// user had clicked on rather than snapping back to the originally-active fork every time, so
    /// a live update happening mid-interaction doesn't yank their choice out from under them.
    /// </summary>
    private async Task LoadAsync(bool preserveSelection)
    {
        if (_loading || IsDisposed) return;
        _loading = true;
        // Only animate while the loading panel is actually the thing on screen -- a background
        // refresh (preserveSelection=true) runs this exact same method again every 5 minutes
        // without ever showing _loadingPanel at all, so there's nothing to animate for those.
        if (_loadingPanel.Visible) _loadingProgress.Active = true;
        try
        {
            ForkInfo? previouslySelected = preserveSelection && _listBox.SelectedIndex >= 0 && _listBox.SelectedIndex < _forks.Count
                ? _forks[_listBox.SelectedIndex]
                : null;

            // Resolved once here (rather than letting GetActiveContributorsAsync resolve it
            // again internally) so the intro label above can show the CURRENT upstream name too
            // if sharpemu/sharpemu ever moves again -- not just the fork list itself.
            var (upstreamOwner, upstreamRepo) = await _service.ResolveRepoAsync(
                GitHubUpdaterService.UpstreamOwner, GitHubUpdaterService.UpstreamRepo, CancellationToken.None);
            // The caller (MainForm.OpenForkPickerAsync) wraps this dialog in a `using` -- if the
            // user closed it while either await above/below was still in flight, it's already
            // disposed by the time this continuation resumes, and every control touched below
            // would throw ObjectDisposedException. GetActiveContributorsAsync in particular can
            // take several seconds (a ~40-request scan), so this is a very reachable window.
            if (IsDisposed) return;
            _introLabel.Text = IntroText(upstreamOwner, upstreamRepo);

            var loaded = await _service.GetActiveContributorsAsync(upstreamOwner, upstreamRepo, CancellationToken.None);
            if (IsDisposed) return;

            // Alphabetical by developer, then most-recently-active branch first within that
            // developer (branch *name* alphabetically would sort "astro-bot..." above "astro-
            // menu..." regardless of which was actually pushed more recently, which reads as
            // wrong when scanning one person's branches) -- except whichever fork/branch is
            // currently tracked always goes first, regardless of where it'd otherwise sort.
            _allForks = loaded
                .OrderBy(f => IsCurrent(f) ? 0 : 1)
                .ThenBy(f => f.OwnerLogin, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(f => f.PushedAt)
                .ToList();

            _loadingPanel.Visible = false;
            _loadingProgress.Active = false;
            _listBox.Visible = true;
            ApplyFilterAndPopulate();

            int indexToSelect = previouslySelected != null
                ? _forks.FindIndex(f => string.Equals(f.Owner, previouslySelected.Owner, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(f.Branch, previouslySelected.Branch, StringComparison.OrdinalIgnoreCase))
                : -1;
            if (indexToSelect < 0) indexToSelect = _forks.FindIndex(IsCurrent);
            if (indexToSelect >= 0) _listBox.SelectedIndex = indexToSelect;
        }
        catch (Exception ex)
        {
            // Only surface the error state on the very first load -- once the list is already
            // showing, a transient failure on a background refresh shouldn't yank it away or
            // bury it behind an error the user didn't ask to see; the next tick will try again.
            if (_allForks.Count == 0)
            {
                _statusLabel.Text = $"Could not load fork list: {ex.Message}";
                // Stops the bar rather than leaving it animating over a message that's telling the
                // user this attempt already failed -- an animated bar next to "Could not load..."
                // would itself look like a contradiction (still working, or already gave up?).
                _loadingProgress.Active = false;
            }
        }
        finally
        {
            _loading = false;
        }
    }
}
