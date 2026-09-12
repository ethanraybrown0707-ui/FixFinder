using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;
using FixFinder.Core.Verification;

// System.Windows.Media has a CacheMode of its own - a WPF bitmap-caching hint - and both
// namespaces are in scope here. Aliased under a different name so the intent reads plainly.
using HttpCacheMode = FixFinder.Core.Http.CacheMode;

namespace FixFinder.Gui;

/// <summary>Everything the preview needs to fetch, plan, apply and verify one candidate.</summary>
/// <param name="Outcome">
/// The round this preview is about. Carried whole rather than picked apart, because applying goes
/// through <see cref="FixStep"/>, which takes an outcome - and rebuilding one here from separate
/// fields is how the interactive path and the unattended path would start to differ.
/// </param>
/// <param name="KeepGoing">
/// True when the user asked for everything to be applied. It does not change what this window
/// does to the files; it changes the word that has to be typed, and whether the window closes
/// itself once there is plainly another error waiting.
/// </param>
public sealed record PreviewContext(
    SessionOutcome Outcome,
    FixFinderHttpClient Http,
    FixFinderLogger? Logger,
    HttpCacheMode CacheMode,
    bool KeepGoing = false)
{
    public FixCandidate Candidate => Outcome.Best!;

    /// <summary>The confirmed source root. Nothing outside it is ever written.</summary>
    public string SourceRoot => Outcome.SourceRoot!;

    /// <summary>Files named in the crash, for tie-breaking and the relevance gate.</summary>
    public IReadOnlyList<string> StackTraceFiles => Outcome.StackTraceFiles;

    public TargetSpec? Target => Outcome.Spec;

    public ErrorFingerprint? Fingerprint => Outcome.Fingerprint;

    /// <summary>
    /// Set when this patch writes into an installed dependency rather than the project.
    /// </summary>
    public InstalledPackage? Into { get; init; }

    /// <summary>The word that has to be typed in full before anything is written.</summary>
    /// <remarks>
    /// Naming the package is the point of it. "APPLY" typed for the hundredth time is a reflex;
    /// "APPLY TO REQUESTS" cannot be typed without having read which library is about to be
    /// edited, which is the one fact that makes this different from every other apply.
    /// </remarks>
    public string ConfirmationWord =>
        Into is { } package
            ? $"APPLY TO {package.Name.ToUpperInvariant()}"
            : KeepGoing ? "APPLY ALL" : "APPLY";
}

/// <summary>
/// Shows exactly what a patch would do, and is the only place FixFinder ever writes a file.
/// </summary>
/// <remarks>
/// Deliberately one window rather than a wizard. Everything that decides whether a change is
/// safe - which files it touches, where each one resolved to and how, every hunk it would alter,
/// and where the backup will go - has to be visible at the moment of deciding, not two screens
/// back.
/// <para>
/// Three things gate the write, and all three are independent: the plan must have located every
/// hunk exactly, the dry-run tick must be cleared deliberately, and the word APPLY must be typed
/// in full. The first is the tool's judgement, the second and third are the user's.
/// </para>
/// </remarks>
public partial class PatchPreviewWindow : Window
{
    private readonly PreviewContext _context;
    private readonly BackupStore _backups = new();

    private readonly ObservableCollection<DiffRow> _diffRows = [];
    private readonly ObservableCollection<DiffRow> _snippetRows = [];
    private readonly ObservableCollection<DiffRow> _pathRows = [];

    private readonly PatchApplier _applier = new();

    private ApplyPlan? _plan;
    private string? _backupFolder;
    private bool _applied;

    /// <summary>True once files were actually written, so the caller can refresh.</summary>
    public bool ChangedAnything { get; private set; }

    /// <summary>
    /// What applying did, once it has been tried. Null while nothing has been written.
    /// </summary>
    /// <remarks>
    /// Handed back so the loop can read the verdict rather than apply the same patch a second
    /// time to find out what it was.
    /// </remarks>
    public StepResult? Step { get; private set; }

    public PatchPreviewWindow(PreviewContext context)
    {
        InitializeComponent();

        _context = context;

        ConfirmationWordText.Text = $" {context.ConfirmationWord} ";
        System.Windows.Automation.AutomationProperties.SetName(
            ConfirmationText, $"Type {context.ConfirmationWord} to confirm");

        DiffListBox.ItemsSource = _diffRows;
        SnippetsListBox.ItemsSource = _snippetRows;
        MappedPathsListBox.ItemsSource = _pathRows;

        CandidateTitleText.Text = context.Candidate.Title;
        CandidateUrlText.Text = context.Candidate.Url;

        if (context.Candidate.Attribution is { Length: > 0 } attribution)
        {
            AttributionText.Text = attribution;
            AttributionText.Visibility = Visibility.Visible;
        }

        Loaded += async (_, _) => await LoadAsync();
    }

    // ================================================================== loading

    private async Task LoadAsync()
    {
        var harvester = new PatchHarvester(_context.Http);
        harvester.Log += message => _context.Logger?.Write(message);

        SetVerdict("Fetching the patch...", "", neutral: true);

        HarvestResult harvest;

        try
        {
            harvest = await harvester.HarvestAsync(_context.Candidate, _context.CacheMode);
        }
        catch (Exception ex)
        {
            SetVerdict("The patch could not be fetched.", ex.Message, neutral: false);
            return;
        }

        foreach (var row in DiffRow.Render(harvest.Snippets)) _snippetRows.Add(row);

        SnippetsTab.Header = $"Code to read ({harvest.Snippets.Count})";

        // Logged before the outcome is known, so the audit trail records that a candidate was
        // opened for preview even when the answer turns out to be "nothing to apply". A log
        // that only mentions the candidates which produced a patch would misrepresent the run.
        _context.Logger?.WriteSection("Patch preview");
        _context.Logger?.Write($"Candidate: {_context.Candidate.Id} · {_context.Candidate.Title}");
        _context.Logger?.Write($"Source root: {_context.SourceRoot}");
        _context.Logger?.Write($"Harvest: {harvest.Summary}");

        if (!harvest.HasAppliablePatch)
        {
            _context.Logger?.Write("Nothing appliable: this candidate stays advisory.");

            // The expected outcome most of the time, and the UI says so in those words rather
            // than presenting it as a failure.
            SetVerdict(
                "Nothing here can be applied automatically.",
                harvest.Snippets.Count > 0
                    ? $"{harvest.Summary} Open the 'Code to read' tab - for an answer written as prose, that is " +
                      "where the fix is, and applying it is a job for you rather than for this tool."
                    : $"{harvest.Summary} There is no unified diff in this candidate and no linked commit that produced one.",
                neutral: true);

            ContentTabs.SelectedItem = SnippetsTab;
            return;
        }

        // Only the first patch is planned. A candidate linking several commits is linking a
        // sequence, and applying them out of order or in isolation is not something this tool
        // can reason about.
        var patch = harvest.Patches[0];

        var mapper = new SourcePathMapper(_context.SourceRoot, _context.StackTraceFiles);
        _plan = _applier.Plan(patch, mapper, _context.StackTraceFiles);

        foreach (var file in _plan.Files)
            _pathRows.Add(new DiffRow { Text = file.Path.Display, Kind = RowKindFor(file) });

        foreach (var row in DiffRow.Render(_plan)) _diffRows.Add(row);

        _context.Logger?.Write($"Plan: {_plan.Outcome} — {_plan.Explanation}");

        foreach (var file in _plan.Files) _context.Logger?.Write($"  {file.Path.Display}");

        if (_plan.CanApply)
        {
            var into = _context.Into is { } package ? $" into {package.Name}" : "";

            var notes = string.Join(
                "\n\n", new[] { DependencyWarning(), BuildWarning() }.Where(n => n.Length > 0));

            SetVerdict(
                $"This patch applies cleanly{into}. {_plan.Explanation}",
                notes,
                neutral: false,
                good: _context.Into is null);

            DryRunCheckBox.IsEnabled = true;
        }
        else
        {
            SetVerdict($"This patch will not be applied. {_plan.Explanation}",
                "Nothing here can be written. That is the tool working correctly - a patch whose context " +
                "does not match, or that would touch a file outside the source root, is refused rather " +
                "than forced.",
                neutral: false);

            DryRunCheckBox.IsEnabled = false;
        }

        UpdateGate();
    }

    private static DiffRowKind RowKindFor(FilePlan file) =>
        file.Ok ? DiffRowKind.Context : DiffRowKind.Removed;

    /// <summary>
    /// Warns when a compiled language has no build command.
    /// </summary>
    /// <remarks>
    /// Without one, the re-run afterwards runs the binary from before the patch, reports the
    /// original error, and rolls back a change that may have been perfectly correct.
    /// </remarks>
    /// <summary>
    /// Said loudly when the change lands in an installed package rather than in the project.
    /// </summary>
    /// <remarks>
    /// Three facts, because each one surprises somebody: it is shared, so every program on this
    /// machine that imports the library gets the change; it is temporary, because the next
    /// install of that package overwrites it; and it is not the usual fix, which is to upgrade.
    /// </remarks>
    private string DependencyWarning()
    {
        if (_context.Into is not { } package) return "";

        return
            $"This writes into {package.Name}, which is installed at {package.Root} - not into your " +
            "own code.\n\n" +
            $"Every program on this machine that uses {package.Name} will get this change, and the " +
            $"next time {package.Name} is installed or upgraded it will be overwritten. Upgrading to " +
            "a release that already contains the fix is the durable version of this. The backup is " +
            "taken either way, and Roll back puts it straight.";
    }

    private string BuildWarning()
    {
        if (_context.Fingerprint is null) return "";
        if (!FixVerifier.NeedsBuild(_context.Fingerprint.LanguageId)) return "";
        if (_context.Target?.BuildCommand is { Length: > 0 }) return "";

        return $"This is {_context.Fingerprint.LanguageId}, which has to be rebuilt before the change takes " +
               "effect. No build command is set, so the result cannot be verified by re-running - " +
               "set one in step 1 first.";
    }

    // ================================================================== the gate

    private void DryRun_Changed(object sender, RoutedEventArgs e)
    {
        // IsChecked="True" in the XAML raises Checked while InitializeComponent is still running
        // its way down the file, so the controls declared below the checkbox do not exist yet.
        // Every handler wired to a control with an initial value needs this guard.
        if (ConfirmationText is null) return;

        ConfirmationText.Clear();
        UpdateGate();
    }

    private void ConfirmationText_TextChanged(object sender, TextChangedEventArgs e) => UpdateGate();

    /// <summary>
    /// Decides whether Apply may be pressed. All three conditions, every time.
    /// </summary>
    private void UpdateGate()
    {
        var dryRun = DryRunCheckBox.IsChecked == true;

        var confirmed = string.Equals(
            ConfirmationText.Text.Trim(), _context.ConfirmationWord, StringComparison.Ordinal);

        var planned = _plan is { CanApply: true };

        ConfirmationText.IsEnabled = planned && !dryRun && !_applied;
        ApplyButton.IsEnabled = planned && !dryRun && confirmed && !_applied;

        GateHintText.Text = !planned
            ? "This patch cannot be applied, so nothing here will write to your files."
            : _applied
                ? "Already applied. Use Roll back to undo it."
                : dryRun
                    ? "Dry run is on, so nothing will be written. Untick it and type " +
                      $"{_context.ConfirmationWord} to make the change."
                    : confirmed
                        ? _context.KeepGoing
                            ? "Ready. Every file is backed up first, and if a different error appears " +
                              "afterwards FixFinder will look that one up and apply its fix too, without asking again."
                            : "Ready. Apply will back every file up first, then write the change."
                        : $"Type {_context.ConfirmationWord} in full to enable the button.";
    }

    // ================================================================== applying

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_plan is null || !_plan.CanApply) return;

        ApplyButton.IsEnabled = false;
        DryRunCheckBox.IsEnabled = false;
        ConfirmationText.IsEnabled = false;

        // The plan shown is the one applied. The session planned this candidate too, but what the
        // user has just read is what came out of this window's own harvest, and applying anything
        // else would make the preview a description of a different change.
        var outcome = _context.Outcome with { Plan = _plan };

        var step = new FixStep(_backups);
        step.Log += OnApplierLog;

        VerificationText.Text = _context.Target is null
            ? ""
            : "Building and re-running to see whether the error is gone...";

        try
        {
            var result = await step.ApplyAsync(outcome);

            Step = result;

            _context.Logger?.Write($"Apply: {result.Apply.Summary}");

            OutcomeBorder.Visibility = Visibility.Visible;

            if (!result.Apply.Ok)
            {
                OutcomeText.Text = result.Apply.Summary;
                OutcomeText.Foreground = Brushes.Firebrick;
                BackupPathText.Text = result.Apply.BackupFolder ?? "";
                VerificationText.Text = "";
                return;
            }

            _applied = true;
            ChangedAnything = true;
            _backupFolder = result.Apply.BackupFolder;

            OutcomeText.Text = $"Written to {result.Apply.Written.Count} file(s).";
            OutcomeText.Foreground = Brushes.Black;
            BackupPathText.Text = $"Backup: {result.Apply.BackupFolder}";
            RollbackButton.IsEnabled = true;

            ShowVerification(result.Verification);
        }
        catch (Exception ex)
        {
            // Applying is guarded; verifying runs a build and a program, and those can fail in
            // ways nothing here anticipates. Saying where the backup is matters more than the
            // exception text, because that is what makes the change undoable by hand.
            OutcomeBorder.Visibility = Visibility.Visible;

            VerificationText.Text = _backupFolder is null
                ? $"Applying failed part-way through: {ex.Message}"
                : $"The change was applied, but checking it failed: {ex.Message}. " +
                  $"Nothing was rolled back. The backup is at {_backupFolder}.";

            VerificationText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            step.Log -= OnApplierLog;
            UpdateGate();
        }

        // Only when there is plainly another round to do. "Apply all" is a promise not to ask
        // again, not a promise to hide the answer: if this was the end of it - fixed, rolled
        // back, or unverifiable - nothing is waiting on the window, so it stays open and says so.
        if (_context.KeepGoing && Step?.Verdict == FixVerdict.DifferentError)
        {
            DialogResult = true;
            Close();
        }
    }

    /// <summary>Renders a verdict, and stops offering a rollback that has already happened.</summary>
    private void ShowVerification(VerificationResult? result)
    {
        if (result is null)
        {
            VerificationText.Text =
                "The change was not verified: there is no target run to repeat. Run the program again yourself " +
                "to see whether it helped.";

            return;
        }

        _context.Logger?.WriteSection("Verification");
        _context.Logger?.Write($"{result.Verdict}: {result.Explanation}");

        VerificationText.Text = $"{result.Headline}\n{result.Explanation}";

        VerificationText.Foreground = result.Verdict switch
        {
            FixVerdict.Fixed => new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
            FixVerdict.DifferentError => new SolidColorBrush(Color.FromRgb(0x8A, 0x6D, 0x00)),
            FixVerdict.Inconclusive => Brushes.Gray,
            _ => Brushes.Firebrick,
        };

        if (!result.RolledBack) return;

        // The verifier put everything back, so the window must not still offer to.
        _applied = false;
        ChangedAnything = false;
        RollbackButton.IsEnabled = false;
        OutcomeText.Text = "The change was applied, then rolled back automatically.";
    }

    private void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_backupFolder is null) return;

        var answer = MessageBox.Show(this,
            $"Put every file back as it was before this patch?\n\nFrom: {_backupFolder}\n\n" +
            "Each file is checked against its recorded checksum before it is restored.",
            "FixFinder", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK) return;

        var restore = _backups.Restore(_backupFolder);

        _context.Logger?.Write($"Rollback: {restore.Summary}");

        OutcomeText.Text = restore.Summary;
        OutcomeText.Foreground = restore.Ok ? Brushes.Black : Brushes.Firebrick;

        if (restore.Ok)
        {
            _applied = false;
            ChangedAnything = false;
            RollbackButton.IsEnabled = false;
        }

        UpdateGate();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OnApplierLog(string message) =>
        Dispatcher.BeginInvoke(() => _context.Logger?.Write(message));

    // ================================================================== presentation

    private void SetVerdict(string headline, string detail, bool neutral, bool good = false)
    {
        VerdictText.Text = headline;
        VerdictDetailText.Text = detail;
        VerdictDetailText.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        (VerdictBorder.Background, VerdictBorder.BorderBrush, VerdictText.Foreground) = (neutral, good) switch
        {
            (false, true) => (new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)),
                              new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)),
                              (Brush)new SolidColorBrush(Color.FromRgb(0x1B, 0x5E, 0x20))),

            (false, false) => (new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)),
                               new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
                               new SolidColorBrush(Color.FromRgb(0xB7, 0x1C, 0x1C))),

            _ => (new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                  new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD)),
                  Brushes.Black),
        };
    }
}
