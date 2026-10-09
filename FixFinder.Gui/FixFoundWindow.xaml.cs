using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FixFinder.Core.Engine;
using FixFinder.Core.Http;
using FixFinder.Core.Patching;
using FixFinder.Core.Ranking;
using FixFinder.Core.Sources;
using FixFinder.Desktop;

using HttpCacheMode = FixFinder.Core.Http.CacheMode;

namespace FixFinder.Gui;

/// <summary>What the search results window needs: the outcome it shows, and the means to open more of it.</summary>
public sealed record FixFoundContext(SessionOutcome Outcome, FixFinderHttpClient Http, FixFinderLogger? Logger);

/// <summary>The prompt: here is what went wrong, here is what was found, here is the fix to paste.</summary>
public partial class FixFoundWindow : Window
{
    private readonly FixFoundContext _context;
    private readonly ObservableCollection<DiffRow> _rows = [];
    private readonly CandidateBrowser _browser;

    private ExaminedCandidate? _current;

    public FixFoundWindow(FixFoundContext context)
    {
        InitializeComponent();
        ScreenFit.Apply(this);

        _context = context;
        ContentListBox.ItemsSource = _rows;

        _browser = new CandidateBrowser(context.Http, context.Outcome, HttpCacheMode.Normal);
        _browser.Log += OnBrowserLog;

        RenderError();

        Loaded += async (_, _) => await ShowCurrentAsync();
    }

    private void RenderError()
    {
        var outcome = _context.Outcome;

        HeadlineText.Text = outcome.Headline;
        ErrorText.Text = outcome.Error?.Summary ?? "";
        ExplanationText.Text = outcome.Detail;
    }

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

        // Only a fix that fits this program is offered to paste: FixFinder's own, an install, or a patch to its own files.
        // Code from somebody else's answer is shown to read, never handed over as this program's fix.
        var pasteable = PasteableFix.For(examined);
        var fitsThisProgram = PasteableFix.FitsThisProgram(examined);

        CopyButton.IsEnabled = pasteable is not null && fitsThisProgram;
        CopyButton.ToolTip = pasteable is null ? "There is no code in this one to copy - open the page to read it."
            : fitsThisProgram ? $"Copies {pasteable.Description}, ready to paste."
            : "This is code from somebody else's program, written for their version of the problem. Read it, then make the change in your own code yourself.";

        var candidate = examined.Candidate;

        CandidateTitleText.Text = candidate.Title;
        CandidateSourceText.Text = RelevanceCheck.IsFixFindersOwn(candidate)
            ? candidate.SourceName
            : $"{candidate.SourceName}  ·  {candidate.StateLabel}  ·  scored {candidate.Score:0}/100";
        CandidateUrlText.Text = candidate.Url;

        WhyShownText.Text = candidate.Relevance is { Because.Count: > 0 } relevance ? $"Shown because {relevance.Said}." : "";
        WhyShownText.Visibility = WhyShownText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri)) CandidateLink.NavigateUri = uri;

        if (candidate.Attribution is { Length: > 0 } attribution)
        {
            AttributionText.Text = attribution;
            AttributionText.Visibility = Visibility.Visible;
        }

        var leftOut = _context.Outcome.LeftOutAsUnrelated;
        OtherResultsText.Text = (examined.Total > 1 ? $"Result {examined.Position} of {examined.Total}." : "") +
            (leftOut > 0 ? $" {(leftOut == 1 ? "One more" : $"{leftOut} more")} came back about a different error or another language, and {(leftOut == 1 ? "is" : "are")} not shown." : "");

        if (candidate is { Tier: FixTier.Dependency, Command: { Length: > 0 } addition, CommandGoesIn: { } buildFile })
        {
            ContentHeaderText.Text = $"WHAT TO ADD TO {buildFile.ToUpperInvariant()}";

            _rows.Add(Note($"The code needs a library its {buildFile} does not name."));
            _rows.Add(Note(""));
            foreach (var line in addition.Split('\n')) _rows.Add(new DiffRow { Kind = DiffRowKind.Added, Text = $"  {line}" });
            _rows.Add(Note(""));
            _rows.Add(Note($"Copy it into {buildFile}. FixFinder does not change your files."));

            return;
        }

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

        if (examined.Plan is { CanApply: false } refused)
        {
            ContentHeaderText.Text = "WHY THIS WILL NOT APPLY TO YOUR CODE";

            _rows.Add(Note(refused.Explanation));

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
            ContentHeaderText.Text = RelevanceCheck.IsFixFindersOwn(candidate)
                ? "WHAT IT SAYS"
                : "WHAT THE ANSWER SAYS  ·  CODE FROM SOMEBODY ELSE'S PROGRAM, NOT YOURS";
        }

        if (examined.Harvest is { } harvest)
        {
            if (_rows.Count > 0 && harvest.Snippets.Count > 0) _rows.Add(Note("What it says:"));

            foreach (var row in DiffRow.Render(harvest.Snippets)) _rows.Add(row);
        }

        if (_rows.Count == 0)
            _rows.Add(Note("  There is no code in it - open the page to read it."));
    }

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

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || PasteableFix.For(_current) is not { } fix) return;

        try
        {
            Clipboard.SetText(fix.Text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException)
        {
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
