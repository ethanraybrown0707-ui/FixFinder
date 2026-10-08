using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FixFinder.Core;
using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Reporting;
using FixFinder.Core.Security;
using FixFinder.Core.Sources;
using FixFinder.Core.Teaching;
using FixFinder.Desktop;
using Microsoft.Win32;

namespace FixFinder.Gui;

/// <summary>Pick a program - it is checked straight away, in the language its name says - and read the report.</summary>
public partial class MainWindow : Window
{
    /// <summary>What the language box for pasted code offers first: working the language out from the code.</summary>
    private const string WorkItOut = "Work the language out from the code";

    private readonly ObservableCollection<OutputRow> _output = [];
    private readonly ObservableCollection<ExpectedRunRow> _extraRuns = [];
    private readonly ObservableCollection<FindingRow> _visibleFindings = [];
    private readonly ObservableCollection<string> _notes = [];

    private readonly FixFinderHttpClient _http = new();
    private readonly FixSourceRegistry _sources = new();

    private List<FindingRow> _findings = [];

    /// <summary>
    /// What the person chose last time they used FixFinder, read when the window opens - and again when Settings closes,
    /// which writes each choice down as it is made, so saving these never puts back what Settings changed.
    /// </summary>
    private Preferences _preferences = Preferences.Load();

    /// <summary>What was found the last few times, so a report can say whether things are getting better.</summary>
    private readonly CheckHistory _history = CheckHistory.Load();

    /// <summary>What each problem found said, kept on this computer for FixFinder Learn to show beside the lesson.</summary>
    private readonly ProblemStore _problems = new();

    /// <summary>
    /// The last check of each program in this session, findings and all - the only place a fixed finding can be named from,
    /// as the history on disk keeps no more than a fingerprint of each.
    /// </summary>
    private readonly Dictionary<string, (CheckRecord Record, IReadOnlyList<Finding> Findings)> _checkedThisSession = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>This check beside the last check of the same program, when there was one; null while a check runs.</summary>
    private CheckComparison? _comparison;

    /// <summary>The report on screen, as it finished, for saving: the summaries the lanes show, and whether it was only read.</summary>
    private CheckReport? _shownReport;
    private bool _shownReportOnlyRead;
    private DateTimeOffset _shownReportAt;

    private Severity? _filter;

    /// <summary>
    /// Set while the Efficiency tab is open. It lists only what would make the program quicker, none of which is a
    /// problem, so the severity filters do not apply to it.
    /// </summary>
    private bool _showingEfficiency;

    private string? _chosenPath;
    private LaunchPlan? _launch;
    private CodeLanguage _language = CodeLanguage.Any;

    /// <summary>Whether the program being checked was pasted in, rather than chosen as a file.</summary>
    private bool _pasting;

    /// <summary>The folder this window saves pasted code in, to check it; removed when the window closes.</summary>
    private string? _pastedFolder;

    /// <summary>The language pasted code was checked as when Auto-detect worked it out from the code; null otherwise.</summary>
    private CodeLanguage? _pastedLanguage;

    private CancellationTokenSource? _cancellation;
    private FixFinderLogger? _logger;

    /// <summary>
    /// What the analyses found in each function during this session, so checking again after an edit only analyses the
    /// functions the edit could have changed.
    /// </summary>
    private readonly AnalysisCache _analysisCache = new();

    /// <summary>Watches the program's folder while Check on save is on, so the code is read again after every save.</summary>
    private FileSystemWatcher? _saveWatcher;

    /// <summary>The program's own files, which are the only ones whose saving means anything here.</summary>
    private HashSet<string> _watchedFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Waits for a burst of writes to settle before the code is read again: an editor often saves in several steps, and
    /// reading half a file would report mistakes that are not there.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _saveSettling = new() { Interval = TimeSpan.FromMilliseconds(600) };

    /// <summary>The code check running after a save, if one is; a newer save replaces it.</summary>
    private CancellationTokenSource? _codeCheck;

    public MainWindow(string? initialFile = null)
    {
        InitializeComponent();
        ScreenFit.Apply(this);

        OutputListBox.ItemsSource = _output;
        ExtraRunsList.ItemsSource = _extraRuns;
        FindingsList.ItemsSource = _visibleFindings;
        NotesList.ItemsSource = _notes;

        FillPastedLanguageBox();
        UpdateFilterCounts();
        _saveSettling.Tick += SaveSettled_Tick;

        var stored = TokenStore.Load();
        _http.SetGitHubToken(stored.GitHubToken);
        _http.SetStackExchangeKey(stored.StackExchangeKey);

        var github = new GitHubFixSource(_http);
        var stackOverflow = new StackOverflowFixSource(_http);

        github.Log += OnLog;
        stackOverflow.Log += OnLog;
        _sources.Log += OnLog;

        _sources.Add(github);
        _sources.Add(stackOverflow);

        if (!string.IsNullOrWhiteSpace(initialFile)) Loaded += (_, _) => Choose(initialFile);
    }

    protected override void OnClosed(EventArgs e)
    {
        StopWatchingForSaves();
        _codeCheck?.Cancel();
        _cancellation?.Cancel();
        _logger?.Dispose();
        _http.Dispose();

        // The pasted code was saved only to be checked; it is not kept.
        try
        {
            if (_pastedFolder is not null && Directory.Exists(_pastedFolder)) Directory.Delete(_pastedFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        base.OnClosed(e);
    }


    private readonly ObservableCollection<ProgramRow> _programs = [];
    private CancellationTokenSource? _folderCheck;

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the folder to check" };
        if (dialog.ShowDialog(this) != true) return;

        _ = CheckFolderAsync(dialog.FolderName);
    }

    /// <summary>
    /// Checks every program a folder holds, one at a time, filling in the list as each finishes.
    /// </summary>
    /// <remarks>
    /// One at a time on purpose: checking a program compiles it and runs it, and a folder's worth of that at once
    /// would fight itself for the machine and make each one slower. The scan itself and every check happen away from
    /// the window, so the list stays usable and a finished program can be read while the rest are still going.
    /// </remarks>
    private async Task CheckFolderAsync(string folder)
    {
        _folderCheck?.Cancel();
        _folderCheck = new CancellationTokenSource();
        var cancellation = _folderCheck.Token;

        _programs.Clear();
        FolderList.ItemsSource = _programs;

        _pasting = false;
        _pastedLanguage = null;
        NothingChosenPanel.Visibility = Visibility.Collapsed;
        ChosenPanel.Visibility = Visibility.Collapsed;
        PastePanel.Visibility = Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;

        FolderSummaryText.Text = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        FolderProgressText.Text = "Looking through the folder…";

        SetBusy(true);
        ShowActivity(true, "Looking through the folder…");

        var plan = await Task.Run(() => ProjectScan.Of(folder), cancellation);

        if (cancellation.IsCancellationRequested)
        {
            SetBusy(false);
            ShowActivity(false, "Stopped");
            return;
        }

        foreach (var program in plan.Programs) _programs.Add(new ProgramRow(program));

        FolderSummaryText.Text = plan.Summary;

        if (_programs.Count == 0)
        {
            FolderProgressText.Text = "Nothing in this folder is a program FixFinder can check.";
            SetBusy(false);
            ShowActivity(false, "Nothing to check");
            return;
        }

        var checkedSoFar = 0;

        foreach (var row in _programs)
        {
            if (cancellation.IsCancellationRequested)
            {
                SetBusy(false);
                ShowActivity(false, "Stopped");
                return;
            }

            row.Starting();
            FolderProgressText.Text = $"Checking {row.Program.Name} - {checkedSoFar} of {_programs.Count} done";
            ShowActivity(true, $"Checking {checkedSoFar + 1} of {_programs.Count}…");

            try
            {
                var report = await CheckOneAsync(row.Program.Entry, cancellation);
                row.Finished(report?.Findings ?? []);

                // A program checked as part of a folder was still checked, so it counts: the next look at that file on
                // its own has something to be compared with.
                if (report is not null)
                {
                    var record = CheckRecord.Of(row.Program.Entry, report.Findings, ran: report.Run is not null);
                    _history.Record(record);
                    _checkedThisSession[row.Program.Entry] = (record, report.Findings);
                    KeepForLearn(report.Findings);
                }
            }
            catch (OperationCanceledException)
            {
                SetBusy(false);
                ShowActivity(false, "Stopped");
                return;
            }
            catch (Exception ex)
            {
                _logger?.Write($"Checking {row.Program.Entry} stopped early: {ex.Message}");
                row.CouldNotCheck();
            }

            checkedSoFar++;

            // Show the first program that has something wrong with it, so the report is not empty while the rest run.
            if (FolderList.SelectedItem is null && row.State == ProgramState.HasProblems) FolderList.SelectedItem = row;
        }

        SetBusy(false);
        ShowActivity(false, "Finished");

        var withProblems = _programs.Count(r => r.State == ProgramState.HasProblems);
        var problems = _programs.Where(r => r.Findings is not null).Sum(r => r.Findings!.Count(f => f.Severity != Severity.Suggestion));

        FolderProgressText.Text = problems == 0
            ? $"Nothing wrong found in {Many(_programs.Count, "program")}."
            : $"{Many(problems, "problem")} in {Many(withProblems, "file")}.";
    }

    /// <summary>Checks one program of a folder, away from the window, and gives back what it found.</summary>
    private async Task<CheckReport?> CheckOneAsync(string file, CancellationToken cancellation)
    {
        var launch = TargetFactory.FromFile(file);
        if (!launch.Ok) return null;

        var checker = new ProgramChecker(_http, _sources) { Language = CodeLanguage.Of(file) ?? CodeLanguage.Any, Cache = _analysisCache };

        return await Task.Run(() => checker.CheckAsync(launch, cancellation), cancellation);
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedItem is not ProgramRow row) return;

        ShowChosen(row.Program.Entry);
        _chosenPath = row.Program.Entry;

        // Saves are followed for one program checked on its own; a folder's programs were each checked once, as they were.
        StopWatchingForSaves();

        ShowFindings(row.Findings ?? []);

        // With findings, the open tab has already said whether it has any of them to show.
        if (row.Findings is not { Count: > 0 }) EmptyState.Visibility = Visibility.Visible;
    }

    /// <summary>"3 problems", and "1 problem" rather than "1 problems".</summary>
    private static string Many(int count, string thing) => count == 1 ? $"1 {thing}" : $"{count} {thing}s";

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e) => PickFile();

    private void PickFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the program to check",
            Filter = TargetFactory.FileDialogFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true) Choose(dialog.FileName);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) Choose(files[0]);
        e.Handled = true;
    }

    /// <summary>
    /// Takes a program to check, and checks it straight away: in the language its file's name says, or - for a file whose
    /// name says none - with everything FixFinder knows, as Auto-detect did.
    /// </summary>
    private void Choose(string path)
    {
        _pasting = false;
        _pastedLanguage = null;
        PastePanel.Visibility = Visibility.Collapsed;

        _chosenPath = path;
        _language = CodeLanguage.Of(path) ?? CodeLanguage.Any;
        _launch = TargetFactory.FromFile(path);

        // Saves to the program chosen before mean nothing now; this one is followed once it has been checked.
        StopWatchingForSaves();

        NothingChosenPanel.Visibility = Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Collapsed;
        ChosenPanel.Visibility = Visibility.Visible;

        ShowChosen(_launch.ShownFile ?? path);
        ChosenLanguageText.Text = _language.IsAny
            ? "Its name says no language FixFinder checks, so it is run as its kind of file is run."
            : $"{_language.Name}, from the file's name.";
        HowItRunsText.Text = _launch.Ok ? _launch.Explanation : _launch.Problem ?? "";
        HowItRunsText.Foreground = (Brush)FindResource(_launch.Ok ? "HintBrush" : "ErrorBrush");

        _ = CheckAsync();
    }

    private void ShowChosen(string path)
    {
        ChosenFileText.Text = Path.GetFileName(path) is { Length: > 0 } name ? name : path;
        ChosenFileText.ToolTip = path;
        ChosenFolderText.Text = Path.GetDirectoryName(path) ?? "";
        ChosenFolderText.ToolTip = path;
    }

    /// <summary>Checks the chosen program again - after it was changed, or given input and what it should print.</summary>
    private async void CheckAgainButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null || _chosenPath is null) return;

        await CheckAsync();
    }

    /// <summary>The languages pasted code can be checked as, after the choice to work it out from the code.</summary>
    private void FillPastedLanguageBox()
    {
        PastedLanguageBox.Items.Add(new ComboBoxItem { Content = WorkItOut, Tag = CodeLanguage.Any });

        foreach (var language in CodeLanguage.All.Where(language => !language.IsAny))
            PastedLanguageBox.Items.Add(new ComboBoxItem { Content = language.Name, Tag = language });

        PastedLanguageBox.SelectedIndex = 0;
    }

    /// <summary>Checks the pasted code: as the language chosen for it, or the one the code shows it is written in.</summary>
    private async void CheckPastedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null) return;

        _language = (PastedLanguageBox.SelectedItem as ComboBoxItem)?.Tag as CodeLanguage ?? CodeLanguage.Any;
        if (!SavePastedCode()) return;

        await CheckAsync();
    }

    /// <summary>Shows the box code can be pasted into, for a program that is hard to find as a file.</summary>
    private void PasteCodeButton_Click(object sender, RoutedEventArgs e)
    {
        _pasting = true;
        _chosenPath = null;
        _launch = null;

        // Pasted code has no file of the person's to follow for saves: Check this code checks it again.
        StopWatchingForSaves();

        NothingChosenPanel.Visibility = Visibility.Collapsed;
        ChosenPanel.Visibility = Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Collapsed;
        PastePanel.Visibility = Visibility.Visible;
        PastedSavedAsText.Visibility = Visibility.Collapsed;

        PastedCodeBox.Focus();
    }

    private void ClearPastedCodeButton_Click(object sender, RoutedEventArgs e)
    {
        PastedCodeBox.Clear();
        PastedSavedAsText.Visibility = Visibility.Collapsed;
        PastedCodeBox.Focus();
    }

    /// <summary>
    /// Saves the pasted code as the file its language needs, for the check to read: as the language chosen, or the one
    /// the code shows it is written in; and says why not, when it cannot be.
    /// </summary>
    private bool SavePastedCode()
    {
        var code = PastedCodeBox.Text;

        // Said again only once this code is saved: until then, what it said was of code checked before.
        PastedSavedAsText.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(code))
        {
            ShowProblem("There is no code to check.", "Paste the program's code into the box, then press Check this code.");
            return false;
        }

        var language = _language.IsAny ? PastedCode.LanguageOf(code) : _language;
        if (language is null)
        {
            ShowProblem("FixFinder cannot tell which language this is.",
                "The code does not show clearly enough which language it is written in. Choose it in the box beside Check this code, and it is checked as that.");
            return false;
        }

        try
        {
            _chosenPath = PastedCode.Save(code, language, _pastedFolder ??= PastedCode.NewFolder());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowProblem("The pasted code could not be saved to be checked.", ex.Message);
            return false;
        }

        _pastedLanguage = _language.IsAny ? language : null;
        PastedSavedAsText.Text = $"Checked as {language.Name}{(_language.IsAny ? ", worked out from the code" : "")} - saved as " +
                                 $"{Path.GetFileName(_chosenPath)} in a folder of its own.";
        PastedSavedAsText.Visibility = Visibility.Visible;
        return true;
    }

    /// <summary>What the report calls the program: its file's name, or pasted code.</summary>
    private string ProgramShown(string? path) => _pasting ? "Pasted code" : Path.GetFileName(path ?? "");

    /// <summary>What the report calls the language: the one its name or the box gave, or the one worked out, and how.</summary>
    private string LanguageShown(bool finished) =>
        !_language.IsAny ? _language.Name
        : _pastedLanguage is { } workedOut ? $"{workedOut.Name}, worked out from the code"
        : finished ? "Auto-detected" : "language worked out automatically";

    private void AddRunButton_Click(object sender, RoutedEventArgs e) => _extraRuns.Add(new ExpectedRunRow());

    private void RemoveRunButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExpectedRunRow row) _extraRuns.Remove(row);
    }

    private async Task CheckAsync()
    {
        // A full check reads the code as well, and does more besides.
        _codeCheck?.Cancel();
        _launch = TargetFactory.FromFile(_chosenPath!);

        if (!_launch.Ok || _launch.Spec is null)
        {
            ShowProblem("That program cannot be run.", _launch.Problem ?? "");
            return;
        }

        var input = InputTextBox.Text is { Length: > 0 } typed ? typed : null;
        var launch = _launch with { Spec = _launch.Spec.WithArguments(ArgumentsTextBox.Text).WithInput(input) };
        _launch = launch;

        var expected = ExpectedBehaviour.From(
            new[] { new ExpectedRun(input, ExpectedOutputTextBox.Text) }
                .Concat(_extraRuns.Select(r => new ExpectedRun(r.Input.Length > 0 ? r.Input : null, r.Expected))),
            ArgumentsTextBox.Text);

        StartReport(launch);

        _logger?.Dispose();
        _logger = new FixFinderLogger();
        LogPathText.Text = _logger.FilePath;
        OpenLogButton.Visibility = Visibility.Visible;
        _logger.WriteSection("Check");
        _logger.Write($"language: {_language.Name}");
        if (launch.Compile is { } compile) _logger.Write($"build: {compile.DisplayCommandLine}");
        _logger.Write($"run: {launch.Spec!.DisplayCommandLine}");

        _cancellation = new CancellationTokenSource();

        var checker = new ProgramChecker(_http, _sources)
        {
            Language = _language,
            Expected = expected.IsEmpty ? null : expected,
            Cache = _analysisCache,
            WindowsRunUntilClosed = _preferences.WindowsRunUntilClosed,
        };
        checker.FindingsChanged += OnFindingsChanged;
        checker.Progress += OnProgress;
        checker.LaneFinished += OnLaneFinished;
        checker.LineCaptured += OnLineCaptured;
        checker.Log += OnLog;

        try
        {
            var report = await checker.CheckAsync(launch, _cancellation.Token);
            FinishReport(report);
        }
        catch (OperationCanceledException)
        {
            SetLane(SyntaxStatusText, SyntaxIcon, SyntaxProgress, "Stopped", LaneState.Stopped);
            SetLane(LogicStatusText, LogicIcon, LogicProgress, "Stopped", LaneState.Stopped);
            _logger.Write("Stopped by the user.");
        }
        catch (Exception ex)
        {
            ShowProblem("FixFinder itself failed.", ex.Message);
            _logger.Write($"FixFinder itself failed: {ex}");
        }
        finally
        {
            checker.FindingsChanged -= OnFindingsChanged;
            checker.Progress -= OnProgress;
            checker.LaneFinished -= OnLaneFinished;
            checker.LineCaptured -= OnLineCaptured;
            checker.Log -= OnLog;

            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private void StartReport(LaunchPlan launch)
    {
        _output.Clear();
        _notes.Clear();
        _findings = [];
        _visibleFindings.Clear();
        FilterAll.IsChecked = true;
        UpdateFilterCounts();

        FilterPanel.Visibility = Visibility.Collapsed;
        ViewTabs.Visibility = Visibility.Collapsed;
        EfficiencyIntro.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;
        CopyReportButton.IsEnabled = false;
        SaveReportButton.IsEnabled = false;
        SinceLastPanel.Visibility = Visibility.Collapsed;
        _comparison = null;
        _shownReport = null;

        ReportSubtitleText.Text = $"{ProgramShown(launch.ShownFile ?? launch.Spec!.ExecutablePath)}  ·  {LanguageShown(finished: false)}  ·  checking…";

        SetLane(SyntaxStatusText, SyntaxIcon, SyntaxProgress, launch.NeedsCompiling ? "Compiling…" : "Reading the code…", LaneState.Running);
        SetLane(LogicStatusText, LogicIcon, LogicProgress, "Reading the code for logic mistakes…", LaneState.Running);

        SetBusy(true);
    }

    private void FinishReport(CheckReport report)
    {
        // Said first, as it bears on everything else: the code was checked with nothing of its program beside it.
        if (_pasting && _chosenPath is { } saved)
        {
            _notes.Add($"This code was pasted in, so it was checked on its own, saved as {Path.GetFileName(saved)}: anything else of its " +
                       "program - another file it imports, a file it reads - was not there with it.");
        }

        foreach (var note in report.Notes) _notes.Add(note);

        // Compared before the findings are shown, so each is shown with where it stands against the last check.
        RememberThisCheck(report);
        ShowFindings(report.Findings);

        SetLane(SyntaxStatusText, SyntaxIcon, SyntaxProgress, report.SyntaxSummary,
            report.Findings.Any(f => f.Severity == Severity.Error && f.Kind is FindingKind.Syntax or FindingKind.Runtime && f.FoundBy is null) ? LaneState.Failed : LaneState.Passed);
        SetLane(LogicStatusText, LogicIcon, LogicProgress, report.LogicSummary,
            report.Findings.Any(f => (f.Kind == FindingKind.Logic || f.FoundBy is not null) && f.Severity != Severity.Suggestion) ? LaneState.Warned : LaneState.Passed);

        ReportSubtitleText.Text = $"{ProgramShown(_launch?.ShownFile)}  ·  {LanguageShown(finished: true)}  ·  checked at {DateTime.Now:HH:mm}";

        if (report.Run?.Run is { } run && _output.Count == 0)
        {
            foreach (var line in run.Lines) _output.Add(new OutputRow { DisplayLine = line.DisplayLine, IsError = line.IsError });
        }

        if (report.Findings.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitleText.Text = "No problems found";
            EmptyBodyText.Text = "It builds and runs, and nothing in the code looks like a logic mistake. Give it the output it should print, " +
                                 "under Input and expected output, to check what it prints as well.";
        }

        _logger?.WriteSection("Findings");
        foreach (var row in _findings) _logger?.Write(row.AsText() + Environment.NewLine);

        CopyReportButton.IsEnabled = report.Findings.Count > 0;
        KeepForSaving(report, onlyRead: false);
        KeepForLearn(report.Findings);

        // From now on every save of a program chosen as a file is read again at once - no box to tick for it.
        if (!_pasting && _launch is { Ok: true }) WatchForSaves();
    }

    /// <summary>
    /// Keeps what each finding said, with its line of code, on this computer under the finding's problem code - so pasting
    /// the code into FixFinder Learn shows the reader their own mistake beside the lesson. Done away from the window, as it
    /// reads each finding's file for its line.
    /// </summary>
    private void KeepForLearn(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0) return;

        var foundAt = DateTimeOffset.Now;
        _ = Task.Run(() => _problems.Keep(findings, foundAt));
    }

    private void OnFindingsChanged(IReadOnlyList<Finding> findings) => Dispatcher.BeginInvoke(() => ShowFindings(findings));

    /// <summary>
    /// Writes this check down and says how it compares with the last one of the same program.
    /// </summary>
    /// <remarks>
    /// The comparison is the point of keeping any of it. Five problems is good news or bad news depending on what
    /// there were before, and only the history knows which. Counts and times are kept, never code.
    /// </remarks>
    private void RememberThisCheck(CheckReport report)
    {
        _comparison = null;
        SinceLastPanel.Visibility = Visibility.Collapsed;
        if (_chosenPath is not { Length: > 0 } file) return;

        var ran = report.Run is not null;
        var now = CheckRecord.Of(file, report.Findings, ran: ran);
        var last = _history.LastTime(file);

        // The last check's findings themselves are to hand only when it was made in this session, and only then is a fixed
        // one named; the history on disk keeps no more than a fingerprint of each.
        var lastFindings = last is not null && _checkedThisSession.TryGetValue(file, out var held) && held.Record.When == last.When ? held.Findings : null;

        _comparison = CheckComparison.Of(last, report.Findings, ran, lastFindings);
        var said = _comparison?.Summary(now.When) ?? CheckHistory.Since(last, now);

        SinceLastText.Text = said ?? "";
        FixedSinceLastList.ItemsSource = _comparison?.Fixed.Select(finding => new FixedRow(finding.Title, FindingText.Location(finding))).ToList();
        SinceLastPanel.Visibility = said is null ? Visibility.Collapsed : Visibility.Visible;

        // Recorded after the comparison, so this check is not compared with itself.
        _history.Record(now);
        _checkedThisSession[file] = (now, report.Findings);
    }

    /// <summary>A finding the last check of this program found and this one did not, as the report names it.</summary>
    private sealed record FixedRow(string Title, string Where);

    /// <summary>Keeps the report on screen as it finished, so it can be saved as it stands.</summary>
    private void KeepForSaving(CheckReport report, bool onlyRead)
    {
        _shownReport = report;
        _shownReportOnlyRead = onlyRead;
        _shownReportAt = DateTimeOffset.Now;
        SaveReportButton.IsEnabled = true;
    }

    /// <summary>
    /// Saves the report on screen as a web page: everything the window shows of each finding, with the comparison with the
    /// last check and what the program printed - to keep, print or hand in.
    /// </summary>
    private void SaveReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shownReport is not { } report) return;

        var page = new ReportPage
        {
            Program = ProgramShown(_launch?.ShownFile ?? _chosenPath),
            Language = LanguageShown(finished: true),
            CheckedAt = _shownReportAt,
            HowItRan = _shownReportOnlyRead ? "Read again as it was saved, and not compiled or run." : _launch?.Explanation,
            SyntaxSummary = report.SyntaxSummary,
            LogicSummary = report.LogicSummary,
            Notes = [.. _notes],
            Findings = [.. _findings.Select(row => row.Finding)],
            SinceLastCheck = _comparison,
            Output = [.. _output.Select(row => row.DisplayLine)],
        };

        // Beside the program, where the person will look for it - unless it was pasted, and lives in the temp folder.
        var programFolder = _pasting ? null : Path.GetDirectoryName(_chosenPath);
        var dialog = new SaveFileDialog
        {
            Title = "Save the report",
            Filter = "Web page (*.html)|*.html",
            FileName = page.SuggestedFileName,
            DefaultExt = ".html",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = programFolder is not null && Directory.Exists(programFolder) ? programFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, page.ToHtml(), new System.Text.UTF8Encoding(false));
            Flash(SaveReportButton, "Saved");
            _logger?.Write($"Report saved to {dialog.FileName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"The report could not be saved there:\n\n{ex.Message}", "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Opens the page a fix was taken from, so the reader can judge it themselves.
    /// </summary>
    /// <remarks>
    /// Only http and https are opened. The address comes from a page somebody else wrote, so handing it to the
    /// shell without looking would be handing a stranger the choice of what runs.
    /// </remarks>
    private void OpenFurtherReading_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FindingRow row } && row.Finding.FurtherReading is { } reading) Open(reading.Url);
    }

    private void OpenWeakness_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FindingRow row } && row.Finding.Weakness is { } weakness) Open(weakness.Url);
    }

    private void OpenOrigin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FindingRow row } && row.Finding.CameFrom is { HasLink: true } came) Open(came.Url!);
    }

    /// <summary>
    /// Opens a web address, and only a web address.
    /// </summary>
    /// <remarks>
    /// Some of these come from pages other people wrote, so the scheme is checked here rather than trusted: handing an
    /// arbitrary address to the shell is handing somebody else the choice of what runs.
    /// </remarks>
    private void Open(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            MessageBox.Show(this, $"That link is not an ordinary web address, so it was not opened:\n\n{address}",
                "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UriFormatException)
        {
            MessageBox.Show(this, $"Could not open that link:\n\n{ex.Message}", "FixFinder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowFindings(IReadOnlyList<Finding> findings)
    {
        // A finding can come back with more in it - what its fix changes - so it is known by where it is and what it says.
        static string Key(Finding f) => $"{f.File}|{f.Line}|{f.RuleId}|{f.Title}";

        // What the reader opened stays open when the findings come back - More detail, and the corrected code.
        var expanded = _findings.Where(r => r.IsExpanded).Select(r => Key(r.Finding)).ToHashSet();
        var corrected = _findings.Where(r => r.CorrectionShown).Select(r => Key(r.Finding)).ToHashSet();

        // Which findings follow from which is worked out in Core, across the whole report, as a saved report works it out.
        var links = FindingLinks.Of(findings);

        _findings = findings.Select(f => new FindingRow(f)
        {
            IsExpanded = expanded.Contains(Key(f)),
            CorrectionShown = corrected.Contains(Key(f)),
            FollowsFrom = links.FollowsFrom(f),
            LeadsTo = links.LeadsTo(f),
            Status = _comparison?.StatusOf(f),
        }).ToList();

        FilterPanel.Visibility = _findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ViewTabs.Visibility = FilterPanel.Visibility;
        if (_findings.Count > 0) EmptyState.Visibility = Visibility.Collapsed;

        UpdateFilterCounts();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _visibleFindings.Clear();

        var shown = _showingEfficiency
            ? _findings.Where(IsEfficiency)
            : _findings.Where(r => !IsEfficiency(r) && (_filter is null || r.Finding.Severity == _filter));

        foreach (var row in shown) _visibleFindings.Add(row);

        // The pills are checked while the window is still being built, before these exist.
        if (!IsInitialized) return;

        EfficiencyIntro.Visibility = _showingEfficiency && _findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowEmptyTab();
    }

    /// <summary>A way to make the program quicker, which belongs on the Efficiency tab rather than among the problems.</summary>
    private static bool IsEfficiency(FindingRow row) => row.Finding.Kind == FindingKind.Performance;

    /// <summary>What a tab with nothing on it says, so an empty tab is not mistaken for one still waiting for results.</summary>
    private void ShowEmptyTab()
    {
        if (_findings.Count == 0) return;

        var empty = _visibleFindings.Count == 0 && (_showingEfficiency || _filter is null);
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty) return;

        (EmptyTitleText.Text, EmptyBodyText.Text) = _showingEfficiency
            ? ("Nothing to speed up was found",
               "FixFinder looks for work that grows with the data - a list searched from the start on every pass of a loop, for " +
               "example - and found none here. That is not a promise the program is quick: it is only what FixFinder can show.")
            : ("No problems found",
               "Nothing in the code looks like a mistake. The Efficiency tab lists ways the program could do less work.");
    }

    private void UpdateFilterCounts()
    {
        var problems = _findings.Where(r => !IsEfficiency(r)).ToList();
        int Count(Severity severity) => problems.Count(r => r.Finding.Severity == severity);

        ProblemsTab.Content = $"Problems  {problems.Count}";
        EfficiencyTab.Content = $"Efficiency  {_findings.Count - problems.Count}";

        FilterAll.Content = $"All  {problems.Count}";
        FilterErrors.Content = $"Errors  {Count(Severity.Error)}";
        FilterWarnings.Content = $"Warnings  {Count(Severity.Warning)}";
        FilterSuggestions.Content = $"Suggestions  {Count(Severity.Suggestion)}";
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        _filter = sender == FilterErrors ? Severity.Error
            : sender == FilterWarnings ? Severity.Warning
            : sender == FilterSuggestions ? Severity.Suggestion
            : null;

        if (_findings is not null) ApplyFilter();
    }

    /// <summary>
    /// Moves between what is wrong with the program and how it could do less work. Both tabs show their findings the
    /// same way; the severity filters only make sense among problems, so they are hidden on the Efficiency tab.
    /// </summary>
    private void View_Checked(object sender, RoutedEventArgs e)
    {
        _showingEfficiency = sender == EfficiencyTab;
        if (!IsInitialized) return;

        SeverityFilters.Visibility = _showingEfficiency ? Visibility.Collapsed : Visibility.Visible;
        ApplyFilter();
    }

    private void WatchForSaves()
    {
        StopWatchingForSaves();
        if (_chosenPath is not { } file || Path.GetDirectoryName(file) is not { } folder || !Directory.Exists(folder)) return;

        _watchedFiles = new HashSet<string>(ProgramFiles.Of(file).Append(file).Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        _saveWatcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _saveWatcher.Changed += OnFileSaved;
        _saveWatcher.Created += OnFileSaved;
        _saveWatcher.Renamed += OnFileSaved;
        _saveWatcher.EnableRaisingEvents = true;
    }

    private void StopWatchingForSaves()
    {
        _saveSettling.Stop();
        if (_saveWatcher is null) return;

        _saveWatcher.EnableRaisingEvents = false;
        _saveWatcher.Dispose();
        _saveWatcher = null;
    }

    /// <summary>Raised on a background thread for every file in the folder; only the program's own files count.</summary>
    private void OnFileSaved(object sender, FileSystemEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!_watchedFiles.Contains(Path.GetFullPath(e.FullPath))) return;

        _saveSettling.Stop();
        _saveSettling.Start();
    });

    private async void SaveSettled_Tick(object? sender, EventArgs e)
    {
        _saveSettling.Stop();
        await CheckCodeAgainAsync();
    }

    /// <summary>
    /// Reads the saved code again and replaces the report with what it finds. The program is not compiled or run, and
    /// the report says so; functions that have not changed since the last check keep what was found in them then.
    /// </summary>
    private async Task CheckCodeAgainAsync()
    {
        // A full check is already reading the code, and more besides.
        if (_cancellation is not null || _launch is not { Ok: true } launch) return;

        _codeCheck?.Cancel();
        var reading = new CancellationTokenSource();
        _codeCheck = reading;

        var checker = new ProgramChecker(_http, _sources) { Language = _language, Cache = _analysisCache };
        checker.Log += OnLog;

        try
        {
            SetLane(LogicStatusText, LogicIcon, LogicProgress, "Reading the saved code again...", LaneState.Running);
            var report = await checker.CheckCodeAsync(launch, reading.Token);
            if (!reading.IsCancellationRequested) ShowCodeReport(report);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SetLane(LogicStatusText, LogicIcon, LogicProgress, "The saved code could not be read", LaneState.Failed);
            _logger?.Write($"Reading the saved code failed: {ex}");
        }
        finally
        {
            checker.Log -= OnLog;
            if (ReferenceEquals(_codeCheck, reading)) _codeCheck = null;
            reading.Dispose();
        }
    }

    private void ShowCodeReport(CheckReport report)
    {
        _notes.Clear();
        foreach (var note in report.Notes) _notes.Add(note);

        // Only read, so it is not set beside a check that compiled and ran the program.
        _comparison = null;
        SinceLastPanel.Visibility = Visibility.Collapsed;

        ShowFindings(report.Findings);

        if (report.Findings.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyTitleText.Text = "No mistakes found in the code";
            EmptyBodyText.Text = "It was read again as it was saved, but not compiled or run. Press Check again to check what it does when it runs.";
        }

        SetLane(SyntaxStatusText, SyntaxIcon, SyntaxProgress, report.SyntaxSummary, LaneState.Stopped);
        SetLane(LogicStatusText, LogicIcon, LogicProgress, report.LogicSummary,
            report.Findings.Any(f => f.Severity != Severity.Suggestion) ? LaneState.Warned : LaneState.Passed);

        ReportSubtitleText.Text = $"{Path.GetFileName(_chosenPath ?? "")}  ·  read again as saved at {DateTime.Now:HH:mm:ss}  ·  not compiled or run";
        CopyReportButton.IsEnabled = report.Findings.Count > 0;
        KeepForSaving(report, onlyRead: true);
        KeepForLearn(report.Findings);

        _logger?.WriteSection("Checked on save");
        foreach (var row in _findings) _logger?.Write(row.AsText() + Environment.NewLine);
    }

    private void OnProgress(CheckLane lane, string message) => Dispatcher.BeginInvoke(() =>
    {
        if (lane == CheckLane.Syntax) SyntaxStatusText.Text = message;
        else LogicStatusText.Text = message;
    });

    private void OnLaneFinished(CheckLane lane, string _) => Dispatcher.BeginInvoke(() =>
    {
        (lane == CheckLane.Syntax ? SyntaxProgress : LogicProgress).Visibility = Visibility.Collapsed;
    });

    private void OnLineCaptured(CapturedLine line) =>
        Dispatcher.BeginInvoke(() => _output.Add(new OutputRow { DisplayLine = line.DisplayLine, IsError = line.IsError }));

    private void OnLog(string message) => _logger?.Write(message);

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        SyntaxStatusText.Text = "Stopping…";
        LogicStatusText.Text = "Stopping…";
        _cancellation?.Cancel();
    }

    /// <summary>
    /// Says whether FixFinder is working, in one place that is always on screen.
    /// </summary>
    /// <remarks>
    /// The two lane cards show how far each check has got, but only while a single file is being checked, and a
    /// check of a folder can run for minutes with the lanes idle between programs. This says the plain thing - it is
    /// running, or it is not - so nobody has to work that out from what a status line last said.
    /// </remarks>
    private void ShowActivity(bool running, string doing)
    {
        ActivityText.Text = doing;
        ActivityPill.ToolTip = doing;
        ActivityProgress.Visibility = running ? Visibility.Visible : Visibility.Collapsed;

        ActivityIcon.Text = running ? "" : "";
        ActivityIcon.Foreground = (Brush)FindResource(running ? "AccentBrush" : "HintBrush");
        ActivityPill.Background = (Brush)FindResource(running ? "AccentSoftBrush" : "SubtleBrush");
    }

    private void SetBusy(bool busy)
    {
        ShowActivity(busy, busy ? "Checking…" : "Not running");

        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = busy;

        CheckAgainButton.IsEnabled = !busy;
        CheckPastedButton.IsEnabled = !busy;
        PastedLanguageBox.IsEnabled = !busy;
        ChooseFileButton.IsEnabled = !busy;
        ChooseAnotherButton.IsEnabled = !busy;
        ChooseFolderButton.IsEnabled = !busy;
        ChooseAnotherFolderButton.IsEnabled = !busy;
        PasteCodeButton.IsEnabled = !busy;
        PasteInsteadButton.IsEnabled = !busy;
        ChooseFileInsteadButton.IsEnabled = !busy;
        ClearPastedCodeButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        AllowDrop = !busy;
    }

    private enum LaneState
    {
        Running,
        Passed,
        Warned,
        Failed,
        Stopped,
    }

    private void SetLane(TextBlock status, TextBlock icon, ProgressBar progress, string text, LaneState state)
    {
        status.Text = text;
        status.ToolTip = text;
        progress.Visibility = state == LaneState.Running ? Visibility.Visible : Visibility.Collapsed;

        (icon.Text, icon.Foreground) = state switch
        {
            LaneState.Passed => ("", (Brush)FindResource("SuccessBrush")),
            LaneState.Warned => ("", (Brush)FindResource("WarningBrush")),
            LaneState.Failed => ("", (Brush)FindResource("ErrorBrush")),
            LaneState.Stopped => ("", (Brush)FindResource("HintBrush")),
            _ => (icon == SyntaxIcon ? "" : "", (Brush)FindResource("AccentBrush")),
        };
    }

    private void ShowProblem(string title, string text)
    {
        _findings = [];
        _visibleFindings.Clear();
        FilterPanel.Visibility = Visibility.Collapsed;
        ViewTabs.Visibility = Visibility.Collapsed;
        EfficiencyIntro.Visibility = Visibility.Collapsed;

        // Nothing said of an earlier check stands beside the problem that stopped this one.
        _notes.Clear();
        _output.Clear();
        SinceLastPanel.Visibility = Visibility.Collapsed;
        _comparison = null;
        _shownReport = null;
        CopyReportButton.IsEnabled = false;
        SaveReportButton.IsEnabled = false;
        ReportSubtitleText.Text = "Not checked";
        SetLane(SyntaxStatusText, SyntaxIcon, SyntaxProgress, "Not checked", LaneState.Stopped);
        SetLane(LogicStatusText, LogicIcon, LogicProgress, "Not checked", LaneState.Stopped);

        EmptyState.Visibility = Visibility.Visible;
        EmptyTitleText.Text = title;
        EmptyBodyText.Text = text;
    }

    private void ToggleCorrection_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FindingRow row) row.CorrectionShown = !row.CorrectionShown;
    }

    private void CopyProblemCode_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FindingRow { HasProblemCode: true } row && Copy(row.ProblemCodeText) && sender is Button button)
            Flash(button, "Copied");
    }

    /// <summary>
    /// Opens a problem in FixFinder Learn. Its code is copied as well, so it can be pasted there by hand if FixFinder Learn
    /// cannot be started from here.
    /// </summary>
    private void OpenInLearn_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FindingRow { HasProblemCode: true } row) return;

        // Kept now as well as when the check finished, so the newest wording is the one FixFinder Learn shows.
        _problems.Keep([row.Finding], DateTimeOffset.Now);

        if (LearnLauncher.Open(row.ProblemCodeText) is { } problem)
        {
            Copy(row.ProblemCodeText);
            MessageBox.Show(this, problem, "FixFinder", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void CopyExample_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FindingRow row && Copy(row.CorrectedExample) && sender is Button button)
            Flash(button, "Copied");
    }

    private void CopyReportButton_Click(object sender, RoutedEventArgs e)
    {
        var header = $"FixFinder report for {Path.GetFileName(_launch?.ShownFile ?? "")} ({_language.Name})";
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { header }.Concat(_notes).Concat(_findings.Select(r => r.AsText())));

        if (Copy(text)) Flash(CopyReportButton, "Copied");
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        // A notebook's code is checked as a script FixFinder writes, but the file the reader has is the notebook.
        if ((sender as FrameworkElement)?.Tag is not FindingRow row || (row.Finding.InNotebook?.Notebook ?? row.Finding.File) is not { } file
            || !File.Exists(file)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _logger?.Write($"Could not open the folder: {ex.Message}");
        }
    }

    private async void SearchOnline_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FindingRow { Finding.Error: { } error } row || _launch?.Spec is not { } spec) return;

        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;

        try
        {
            var session = new FixFinderSession(_http, _sources) { Language = _language };
            session.Log += OnLog;

            var run = new TargetRunResult
            {
                Outcome = RunOutcome.ExitedNonZero,
                Lines = [],
                Duration = TimeSpan.Zero,
                Explanation = "",
                Error = error,
            };

            var outcome = await session.LookUpAsync(run, error, spec, _launch.SourceFolder, row.Finding.Kind == FindingKind.Syntax);
            session.Log -= OnLog;

            new FixFoundWindow(new FixFoundContext(outcome, _http, _logger)) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The search did not work:\n\n{ex.Message}", "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    private bool Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show(this, "Another program is using the clipboard. Try again in a moment.", "FixFinder",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
    }

    private static void Flash(Button button, string text)
    {
        var original = button.Content;
        button.Content = text;

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) =>
        {
            button.Content = original;
            timer.Stop();
        };
        timer.Start();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_http) { Owner = this };
        settings.ShowDialog();

        if (settings.Saved) _logger?.Write("Credentials updated.");

        // Settings wrote down what it changed - the time a run is given, whether a program with a window runs until it is
        // closed - so this window's copy is read again, and the next check runs as was chosen.
        _preferences = Preferences.Load();
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e)
    {
        if (_logger?.FilePath is not { } path || !File.Exists(path)) return;

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            MessageBox.Show(this, $"Could not open the log:\n\n{ex.Message}", "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
