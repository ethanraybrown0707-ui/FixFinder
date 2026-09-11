using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FixFinder.Core.Engine;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;

// System.Windows.Media has a CacheMode of its own; alias the HTTP one apart from it.
using HttpCacheMode = FixFinder.Core.Http.CacheMode;

namespace FixFinder.Gui;

/// <summary>What the prompt needs: the outcome it is about, and the means to act on it.</summary>
public sealed record FixFoundContext(
    SessionOutcome Outcome, FixFinderHttpClient Http, FixFinderLogger? Logger);

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

    /// <summary>True when files were written, so the caller can say so.</summary>
    public bool Applied { get; private set; }

    public string? OutcomeSummary { get; private set; }

    public FixFoundWindow(FixFoundContext context)
    {
        InitializeComponent();

        _context = context;
        ContentListBox.ItemsSource = _rows;

        Render();
    }

    private void Render()
    {
        var outcome = _context.Outcome;
        var best = outcome.Best;

        HeadlineText.Text = outcome.Headline;
        ErrorText.Text = outcome.Error?.Summary ?? "";
        ExplanationText.Text = outcome.Detail;

        if (best is null) return;

        CandidateTitleText.Text = best.Title;
        CandidateSourceText.Text = $"{best.SourceName}  ·  {best.StateLabel}  ·  scored {best.Score:0}/100";
        CandidateUrlText.Text = best.Url;

        if (Uri.TryCreate(best.Url, UriKind.Absolute, out var uri)) CandidateLink.NavigateUri = uri;

        if (best.Attribution is { Length: > 0 } attribution)
        {
            AttributionText.Text = attribution;
            AttributionText.Visibility = Visibility.Visible;
        }

        var others = outcome.Candidates.Count - 1;
        OtherResultsText.Text = others > 0
            ? $"{others} other result{(others == 1 ? "" : "s")} were found and ranked below this one."
            : "";

        if (outcome.CanApply)
        {
            ContentGroup.Header = "What it changes";
            foreach (var row in DiffRow.Render(outcome.Plan!)) _rows.Add(row);

            ApplyButton.IsEnabled = true;
            return;
        }

        // Advisory. The code blocks from the discussion are the useful part, and the button that
        // cannot be honoured is disabled with the reason on it rather than hidden.
        ContentGroup.Header = "What it says";

        if (outcome.Harvest is { } harvest)
            foreach (var row in DiffRow.Render(harvest.Snippets)) _rows.Add(row);

        if (_rows.Count == 0)
            _rows.Add(new DiffRow { Kind = DiffRowKind.Note, Text = "  There is no code in it - open the page to read it." });

        ApplyButton.IsEnabled = false;
        ApplyButton.ToolTip =
            outcome.SourceRoot is null
                ? "FixFinder could not find your source code, so it has nothing to apply this to."
                : "This one is an explanation rather than a patch, so it cannot be applied automatically.";
    }

    // ================================================================== actions

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var outcome = _context.Outcome;

        if (outcome.Best is null || outcome.SourceRoot is null) return;

        var preview = new PatchPreviewWindow(new PreviewContext(
            outcome.Best,
            _context.Http,
            outcome.SourceRoot,
            outcome.StackTraceFiles,
            outcome.Spec,
            outcome.Fingerprint,
            _context.Logger,
            HttpCacheMode.Normal))
        {
            Owner = this,
        };

        preview.ShowDialog();

        if (!preview.ChangedAnything) return;

        Applied = true;
        OutcomeSummary = $"Applied: {outcome.Best.Title}";

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
