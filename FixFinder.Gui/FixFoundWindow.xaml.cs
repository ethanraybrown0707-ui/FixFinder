using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Patching;
using FixFinder.Core.Sources;

// System.Windows.Media has a CacheMode of its own; alias the HTTP one apart from it.
using HttpCacheMode = FixFinder.Core.Http.CacheMode;

namespace FixFinder.Gui;

/// <summary>What the search results window needs: the outcome it shows, and the means to open more of it.</summary>
public sealed record FixFoundContext(SessionOutcome Outcome, FixFinderHttpClient Http, FixFinderLogger? Logger);

/// <summary>
/// The prompt: here is what went wrong, here is what was found, here is the fix to paste.
/// </summary>
/// <remarks>
/// The one screen the whole tool exists to produce. It only ever appears when there is a real
/// decision to make - a program that ran fine, or crashed with nothing published about it, is
/// reported in the main window and never interrupts, because a dialog that sometimes says
/// "nothing happened" is a dialog people learn to dismiss without reading.
/// <para>
/// <b>FixFinder does not write to your files.</b> It finds the fix and hands it over; you paste
/// it. That is a narrower promise than applying, and a much easier one to keep - the whole
/// machinery of source roots, containment, exact-context matching, backups and rollback existed
/// to make writing safe, and not writing is safer still. It also removes the limit that mattered
/// most: a fix that cannot be applied to your tree can always be read and copied.
/// </para>
/// <para>
/// What goes on the clipboard is never the diff. A unified diff is written for a machine - the
/// markers and removed lines are instructions - so pasting one into a source file produces
/// something that does not compile. <see cref="PasteableFix"/> hands over the code as it should
/// end up instead.
/// </para>
/// </remarks>
public partial class FixFoundWindow : Window
{
    private readonly FixFoundContext _context;
    private readonly ObservableCollection<DiffRow> _rows = [];
    private readonly CandidateBrowser _browser;

    private ExaminedCandidate? _current;

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

        HeadlineText.Text = outcome.Headline;
        ErrorText.Text = outcome.Error?.Summary ?? "";
        ExplanationText.Text = outcome.Detail;
    }

    /// <summary>Opens whichever result the browser is on, and shows it.</summary>
    private async Task ShowCurrentAsync()
    {
        if (_context.Outcome.Best is null)
        {
            UpdateNextButton();
            return;
        }

        NextResultButton.IsEnabled = false;

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

        CopyButton.IsEnabled = false;
        CopiedText.Visibility = Visibility.Collapsed;
        AttributionText.Visibility = Visibility.Collapsed;

        UpdateNextButton();

        if (examined is null)
        {
            CandidateTitleText.Text = "That result could not be opened.";
            CandidateSourceText.Text = "";
            CandidateUrlText.Text = "";
            CopyButton.ToolTip = "Nothing was fetched, so there is nothing to copy.";
            return;
        }

        // What the button will put on the clipboard, worked out once so its state and its label
        // cannot disagree with what it copies.
        var pasteable = PasteableFix.For(examined);

        CopyButton.IsEnabled = pasteable is not null;
        CopyButton.ToolTip = pasteable is { } fix
            ? $"Copies {fix.Description}, ready to paste."
            : "There is no code in this one to copy - open the page to read it.";

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

        // A missing package is not a patch to anything, so it gets its own button rather than
        // being squeezed through the preview. There is no diff to show and no file to back up -
        // what there is is a command, and the decision is whether to run it.
        if (candidate is { Tier: FixTier.Dependency, Command: { Length: > 0 } command })
        {
            ContentHeaderText.Text = "WHAT IT RUNS";

            _rows.Add(Note("The code is right; the environment is short of a package."));
            _rows.Add(Note(""));
            _rows.Add(new DiffRow { Kind = DiffRowKind.Added, Text = $"  {command}" });
            _rows.Add(Note(""));
            _rows.Add(Note("Nothing in your files changes. Only what is installed does."));
            _rows.Add(Note("Copy it and run it in a terminal."));

            return;
        }

        if (examined.CanApply)
        {
            ContentHeaderText.Text = "THE CHANGE, AND WHERE IT GOES";

            if (examined.Into is { } package)
            {
                ContentHeaderText.Text = $"THE CHANGE, IN {package.Name.ToUpperInvariant()}";

                _rows.Add(Note(
                    $"This is a fix for {package.Name} itself, installed at {package.Root} - not a " +
                    "change to your own code."));

                _rows.Add(Note(""));
            }
            else if (pasteable?.Where is { Length: > 0 } where)
            {
                _rows.Add(Note($"Paste over {where}."));
                _rows.Add(Note(""));
            }

            foreach (var row in DiffRow.Render(examined.Plan!)) _rows.Add(row);

            return;
        }

        // Advisory. Why it is advisory goes on screen rather than into the button's tooltip:
        // "Apply is greyed out" with the reason hidden behind a hover reads as the tool being
        // broken, which is exactly how it was read. A patch that exists but belongs to somebody
        // else's files is a specific, checkable fact, and saying which files it touches lets the
        // reader confirm the refusal instead of taking it on trust.
        if (examined.Plan is { CanApply: false } refused)
        {
            ContentHeaderText.Text = "WHY THIS WILL NOT APPLY TO YOUR CODE";

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
            ContentHeaderText.Text = "WHAT IT SAYS";
        }

        if (examined.Harvest is { } harvest)
        {
            if (_rows.Count > 0 && harvest.Snippets.Count > 0) _rows.Add(Note("What it says:"));

            foreach (var row in DiffRow.Render(harvest.Snippets)) _rows.Add(row);
        }

        if (_rows.Count == 0)
            _rows.Add(Note("  There is no code in it - open the page to read it."));

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

    private static DiffRow Note(string text) => new() { Kind = DiffRowKind.Note, Text = text };

    private void UpdateNextButton()
    {
        NextResultButton.IsEnabled = _browser.HasNext;
        NextResultButton.ToolTip = _browser.HasNext
            ? $"Show the next of {_browser.Remaining} more result{(_browser.Remaining == 1 ? "" : "s")} for this error."
            : "This is the last result that was found for this error.";
    }

    private void OnBrowserLog(string message) => _context.Logger?.Write(message);

    // ================================================================== actions

    /// <summary>
    /// Puts the fix on the clipboard in the form you can actually paste.
    /// </summary>
    /// <remarks>
    /// Not the diff. Every diff here is written for a machine to apply - the markers, the hunk
    /// header and the removed lines are instructions, and pasting them into a source file
    /// produces something that does not compile. What goes on the clipboard is the code as it
    /// should end up, context lines included, so it replaces the old block exactly.
    /// </remarks>
    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || PasteableFix.For(_current) is not { } fix) return;

        try
        {
            Clipboard.SetText(fix.Text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is a shared OS resource and another process can be holding it open.
            // Losing a copy is a nuisance; crashing the prompt over it is not acceptable.
            _context.Logger?.Write($"Could not copy: {ex.Message}");

            MessageBox.Show(this,
                "Windows would not let FixFinder use the clipboard just then - something else is " +
                "holding it. Try again in a moment.",
                "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);

            return;
        }

        _context.Logger?.Write($"Copied {fix.Description} ({fix.Text.Length} characters).");

        var lines = fix.Text.Split('\n').Length;

        CopiedText.Text = fix.Where is { Length: > 0 } where
            ? $"Copied {lines} line{(lines == 1 ? "" : "s")} - paste over {where}."
            : $"Copied {fix.Description}.";

        CopiedText.Visibility = Visibility.Visible;
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
