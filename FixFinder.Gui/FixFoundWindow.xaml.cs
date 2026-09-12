using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FixFinder.Core.Engine;
using FixFinder.Core.Http;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

// System.Windows.Media has a CacheMode of its own; alias the HTTP one apart from it.
using HttpCacheMode = FixFinder.Core.Http.CacheMode;

namespace FixFinder.Gui;

/// <summary>What the prompt needs: the outcome it is about, and the means to act on it.</summary>
/// <param name="Round">
/// Which error of this run it is. Shown from the second onwards, so that a prompt appearing for
/// the third time reads as the loop working rather than as the tool repeating itself.
/// </param>
/// <param name="PreviousChoice">
/// How the round before this one ended, so the banner can say how we got here. Applying a fix and
/// stepping past a problem both land on a later error, and telling the user their last change
/// worked when they skipped it would be describing something that did not happen.
/// </param>
public sealed record FixFoundContext(
    SessionOutcome Outcome,
    FixFinderHttpClient Http,
    FixFinderLogger? Logger,
    int Round = 1,
    RoundChoice? PreviousChoice = null);

/// <summary>
/// The prompt: here is what went wrong, here is what was found, shall I apply it.
/// </summary>
/// <remarks>
/// The one screen the whole tool exists to produce. It only ever appears when there is a real
/// decision to make - a program that ran fine, or crashed with nothing published about it, is
/// reported in the main window and never interrupts, because a dialog that sometimes says
/// "nothing happened" is a dialog people learn to dismiss without reading.
/// <para>
/// Applying does not happen here. The button opens the preview, which shows every file that
/// would change and where each one resolved to, defaults to a dry run, and asks for the word
/// APPLY to be typed. Collapsing that into a single "yes" on this window would be the one
/// simplification worth refusing: this dialog says what was <i>found</i>, and the preview is
/// where you see what would actually be <i>done</i>.
/// </para>
/// </remarks>
public partial class FixFoundWindow : Window
{
    private readonly FixFoundContext _context;
    private readonly ObservableCollection<DiffRow> _rows = [];
    private readonly CandidateBrowser _browser;

    private ExaminedCandidate? _current;

    /// <summary>True when files were written, so the caller can say so.</summary>
    public bool Applied { get; private set; }

    public string? OutcomeSummary { get; private set; }

    /// <summary>
    /// What the user decided, and what was done about it. Null means the prompt was closed.
    /// </summary>
    /// <remarks>
    /// The apply happens inside the preview, behind its typed confirmation, so the result is
    /// handed back rather than left for the loop to redo - applying the same patch twice would
    /// fail its own context check the second time and read as a broken patch.
    /// </remarks>
    public RoundDecision? Decision { get; private set; }

    public FixFoundWindow(FixFoundContext context)
    {
        InitializeComponent();

        _context = context;
        ContentListBox.ItemsSource = _rows;

        _browser = new CandidateBrowser(context.Http, context.Outcome, HttpCacheMode.Normal);
        _browser.Log += OnBrowserLog;

        RenderError();

        Loaded += async (_, _) => await ShowCurrentAsync();
    }

    /// <summary>The parts that are about the error rather than about any one result.</summary>
    private void RenderError()
    {
        var outcome = _context.Outcome;

        if (_context.Round > 1)
        {
            RoundText.Text = _context.PreviousChoice == RoundChoice.Skip
                ? $"Error {_context.Round} of this run — you skipped the last one, and this is what it reported next."
                : $"Error {_context.Round} of this run — the last change worked, and this is what came next.";

            RoundText.Visibility = Visibility.Visible;
        }

        HeadlineText.Text = outcome.Headline;
        ErrorText.Text = outcome.Error?.Summary ?? "";
        ExplanationText.Text = outcome.Detail;
    }

    /// <summary>Opens whichever result the browser is on, and shows it.</summary>
    private async Task ShowCurrentAsync()
    {
        if (_context.Outcome.Best is null)
        {
            UpdateSkip();
            return;
        }

        NextResultButton.IsEnabled = false;
        SkipButton.IsEnabled = false;

        try
        {
            _current = await _browser.CurrentAsync();
        }
        catch (Exception ex)
        {
            // Opening a result is a network call over untrusted input. Losing it costs this one
            // result rather than the prompt.
            _context.Logger?.Write($"Could not open result {_browser.Index + 1}: {ex}");
            _current = null;
        }

        Render(_current);
    }

    private void Render(ExaminedCandidate? examined)
    {
        _rows.Clear();

        ApplyButton.IsEnabled = false;
        ApplyAllButton.IsEnabled = false;
        ApplyAllButton.Visibility = Visibility.Collapsed;
        AttributionText.Visibility = Visibility.Collapsed;

        UpdateSkip();

        if (examined is null)
        {
            CandidateTitleText.Text = "That result could not be opened.";
            CandidateSourceText.Text = "";
            CandidateUrlText.Text = "";
            ApplyButton.ToolTip = "Nothing was fetched, so there is nothing to apply.";
            return;
        }

        var candidate = examined.Candidate;

        CandidateTitleText.Text = candidate.Title;
        CandidateSourceText.Text =
            $"{candidate.SourceName}  ·  {candidate.StateLabel}  ·  scored {candidate.Score:0}/100";
        CandidateUrlText.Text = candidate.Url;

        if (Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri)) CandidateLink.NavigateUri = uri;

        if (candidate.Attribution is { Length: > 0 } attribution)
        {
            AttributionText.Text = attribution;
            AttributionText.Visibility = Visibility.Visible;
        }

        OtherResultsText.Text = examined.Total > 1
            ? $"Result {examined.Position} of {examined.Total}."
            : "";

        if (examined.CanApply)
        {
            ContentGroup.Header = "What it changes";
            foreach (var row in DiffRow.Render(examined.Plan!)) _rows.Add(row);

            ApplyButton.IsEnabled = true;
            ApplyButton.ToolTip = null;

            if (examined.Into is { } package)
            {
                ContentGroup.Header = $"What it changes in {package.Name}";

                _rows.Insert(0, Note(""));
                _rows.Insert(0, Note(
                    $"This is a fix for {package.Name} itself, so it writes into the installed package " +
                    "rather than into your code."));
            }

            // Offered only where it means something. On the first error of a run nobody knows yet
            // whether there is a second, and a button promising to work through them all is worth
            // having; where the program cannot be re-run afterwards there is no way to find the
            // next error, so "all" would be a promise this tool cannot keep.
            //
            // Never for a dependency. Working unattended through a machine's installed packages
            // is a different proposition from working through one project, and it is not one
            // anybody should be able to start with a single button.
            if (_context.Outcome.Spec is not null && examined.Into is null)
            {
                ApplyAllButton.IsEnabled = true;
                ApplyAllButton.Visibility = Visibility.Visible;
            }

            return;
        }

        // Advisory. Why it is advisory goes on screen rather than into the button's tooltip:
        // "Apply is greyed out" with the reason hidden behind a hover reads as the tool being
        // broken, which is exactly how it was read. A patch that exists but belongs to somebody
        // else's files is a specific, checkable fact, and saying which files it touches lets the
        // reader confirm the refusal instead of taking it on trust.
        if (examined.Plan is { CanApply: false } refused)
        {
            ContentGroup.Header = "Why this will not apply to your code";

            _rows.Add(Note(refused.Explanation));

            // The commonest refusal by far, and the least self-explanatory. A fix published for
            // a library patches that library's files, which sit in site-packages or node_modules
            // rather than in the project - so the patch is perfectly real, perfectly relevant,
            // and lands nowhere FixFinder is allowed to write.
            if (_context.Outcome.Fingerprint?.NearestThirdPartyModule is { Length: > 0 } library &&
                refused.Outcome is ApplyOutcome.RejectedPathNotFound)
            {
                _rows.Add(Note(""));
                _rows.Add(Note(
                    $"This is a fix for {library} itself, and {library} is installed outside your project."));
                _rows.Add(Note(
                    "FixFinder only ever writes inside your own source folder, so it will not patch an"));
                _rows.Add(Note(
                    "installed package. Upgrading " + library + " is usually the real fix here."));
            }

            _rows.Add(Note(""));

            if (refused.Files.Count > 0)
            {
                _rows.Add(Note("Where each file in the patch resolved to:"));

                foreach (var file in refused.Files)
                    _rows.Add(new DiffRow { Kind = DiffRowKind.Removed, Text = $"  {file.Path.Display}" });
            }
            else if (examined.Harvest?.Patches is [{ } patch, ..])
            {
                _rows.Add(Note("The patch changes these files, none of which are part of your program:"));

                foreach (var file in patch.Files)
                    _rows.Add(new DiffRow { Kind = DiffRowKind.Removed, Text = $"  {file.TargetPath}" });
            }

            _rows.Add(Note(""));
        }
        else
        {
            ContentGroup.Header = "What it says";
        }

        if (examined.Harvest is { } harvest)
        {
            if (_rows.Count > 0 && harvest.Snippets.Count > 0) _rows.Add(Note("What it says:"));

            foreach (var row in DiffRow.Render(harvest.Snippets)) _rows.Add(row);
        }

        if (_rows.Count == 0)
            _rows.Add(Note("  There is no code in it - open the page to read it."));

        ApplyButton.ToolTip = _context.Outcome.SourceRoot is null
            ? "FixFinder could not find your source code, so it has nothing to apply this to."
            : examined.WhyNotAppliable;
    }

    private static DiffRow Note(string text) => new() { Kind = DiffRowKind.Note, Text = text };

    /// <summary>
    /// Sets both ways of moving on, and says on the button when one of them is not possible.
    /// </summary>
    /// <remarks>
    /// They are different questions. "Show me another answer to this problem" walks the ranked
    /// results, which almost always exist; "this problem is not worth my time, what else broke"
    /// needs another error, which only compiler output has. Where the second is impossible the
    /// button carries the reason rather than sitting dim - the program stopped at this error, so
    /// whatever would have failed next has not happened yet, and no parser could find it.
    /// </remarks>
    private void UpdateSkip()
    {
        var moreResults = _browser.HasNext;
        var moreErrors = _context.Outcome.OtherErrors.Count;

        NextResultButton.IsEnabled = moreResults;
        NextResultButton.ToolTip = moreResults
            ? $"Show the next of {_browser.Remaining} more result{(_browser.Remaining == 1 ? "" : "s")} for this error."
            : "This is the last result that was found for this error.";

        SkipButton.IsEnabled = moreErrors > 0;
        SkipButton.ToolTip = moreErrors > 0
            ? $"Leave this problem and look up the next error this run reported ({moreErrors} left)."
            : _context.Outcome.FailedToCompile
                ? "That was the last error this build reported."
                : "The program stopped at this error, so there is nothing behind it to move on to until it is fixed.";
    }

    private void OnBrowserLog(string message) => _context.Logger?.Write(message);

    // ================================================================== actions

    private void ApplyButton_Click(object sender, RoutedEventArgs e) => Preview(keepGoing: false);

    /// <summary>
    /// Asks once for the whole sequence, then hands over to the same preview.
    /// </summary>
    /// <remarks>
    /// The consent is real and it is given here, not weakened: what changes is its <i>scope</i>,
    /// from one patch to however many this run turns up, so it is spelled out in those terms and
    /// still ends at the typed confirmation in the preview. Everything that made the first write
    /// safe is unchanged for the rest - the same score floor, the same containment inside the
    /// source root, exact context with no fuzz, a backup of every file, and a rollback the moment
    /// a change fails to help.
    /// </remarks>
    private void ApplyAllButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "FixFinder will apply this fix, then run the program again. If a different error comes " +
            "up, it will look that one up and apply its fix too, and keep going without asking again.\n\n" +
            $"It stops after {FixLoop.DefaultMaxRounds} changes, the moment the program runs cleanly, or as " +
            "soon as one of them fails to help.\n\n" +
            "Nothing else changes: every file is copied to a backup first, a change that does not help is " +
            "put straight back, and nothing outside your source folder is ever written.\n\n" +
            "Carry on?",
            "Apply every fix it finds?", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);

        if (answer != MessageBoxResult.OK) return;

        Preview(keepGoing: true);
    }

    /// <summary>
    /// Shows the next of the ranked results for the same error.
    /// </summary>
    /// <remarks>
    /// Kept separate from Skip rather than falling through to it. With thirty-odd results, one
    /// button doing both would put "move past this problem" thirty-seven clicks away - which is
    /// the one thing it was asked for.
    /// </remarks>
    private async void NextResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_browser.HasNext) return;

        _context.Logger?.Write($"Moving past result {_browser.Index + 1} to the next one.");

        NextResultButton.IsEnabled = false;
        CandidateTitleText.Text = "Opening the next result...";

        try
        {
            _current = await _browser.NextAsync();
        }
        catch (Exception ex)
        {
            _context.Logger?.Write($"Could not open result {_browser.Index + 1}: {ex}");
            _current = null;
        }

        Render(_current);
    }

    /// <summary>
    /// Leaves this error alone and moves to the next one the run reported.
    /// </summary>
    /// <remarks>
    /// The loop's business rather than this window's, because the run carries on afterwards, so
    /// the answer leaves through <see cref="Decision"/> instead of being handled here.
    /// </remarks>
    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_context.Outcome.OtherErrors.Count == 0) return;

        _context.Logger?.Write("Skipped this problem; moving to the next error this run reported.");

        Decision = new RoundDecision(RoundChoice.Skip);

        DialogResult = true;
        Close();
    }

    private void Preview(bool keepGoing)
    {
        var outcome = _context.Outcome;

        if (_current is null || outcome.SourceRoot is null) return;

        // The result on screen, not the one the session opened with. After stepping to another
        // result these are different, and previewing the other one would apply a patch nobody
        // was looking at.
        //
        // A patch that landed in a dependency is rooted there, so every containment check below
        // measures against the package rather than against the project - the rule is unchanged,
        // what it is applied to is what moved.
        var shown = outcome with
        {
            Best = _current.Candidate,
            Harvest = _current.Harvest,
            Plan = _current.Plan,
            SourceRoot = _current.Into?.Root ?? outcome.SourceRoot,
        };

        var preview = new PatchPreviewWindow(
            new PreviewContext(shown, _context.Http, _context.Logger, HttpCacheMode.Normal, keepGoing)
            {
                Into = _current.Into,
            })
        {
            Owner = this,
        };

        preview.ShowDialog();

        // Closing the preview without applying is not an answer to this prompt, so this one stays
        // up: the user is back where they were, free to read the page or to try again.
        if (preview.Step is not { } step) return;

        Decision = new RoundDecision(
            keepGoing ? RoundChoice.ApplyEverything : RoundChoice.Apply, step);

        Applied = preview.ChangedAnything;
        OutcomeSummary = $"Applied: {_current.Candidate.Title}";

        DialogResult = true;
        Close();
    }

    private void OpenPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_context.Outcome.Best?.Url is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            Launch(uri);
    }

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        Launch(e.Uri);
    }

    /// <summary>
    /// Opens a URL in the default browser, after checking it is one.
    /// </summary>
    /// <remarks>
    /// The address came off the public internet with the search result. Only http and https are
    /// launched, because handing an arbitrary scheme to the shell turns a click on a link into
    /// running something local.
    /// </remarks>
    private void Launch(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, $"That link is not an http or https address, so it was not opened:\n\n{uri}",
                "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, $"Could not open the link:\n\n{ex.Message}",
                "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
