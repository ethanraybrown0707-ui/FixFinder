using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Security;
using FixFinder.Core.Sources;
using Microsoft.Win32;

namespace FixFinder.Gui;

/// <summary>Pick a program, press its language, and read the report.</summary>
public partial class MainWindow : Window
{
    private sealed record LanguageBadge(string Short, string Colour, string TextColour = "#FFFFFF");

    private static readonly Dictionary<string, LanguageBadge> Badges = new()
    {
        ["Python"] = new("Py", "#3776AB"),
        ["Java"] = new("Java", "#E76F00"),
        ["C#"] = new("C#", "#7B3FB8"),
        ["C"] = new("C", "#5C6BC0"),
        ["C++"] = new("C++", "#00599C"),
        ["JavaScript"] = new("JS", "#F7DF1E", "#2B2B2B"),
        ["Go"] = new("Go", "#00ADD8"),
        ["Any language"] = new("Auto", "#5B6679"),
    };

    private readonly ObservableCollection<OutputRow> _output = [];
    private readonly ObservableCollection<ExpectedRunRow> _extraRuns = [];
    private readonly ObservableCollection<FindingRow> _visibleFindings = [];
    private readonly ObservableCollection<string> _notes = [];

    private readonly FixFinderHttpClient _http = new();
    private readonly FixSourceRegistry _sources = new();

    private List<FindingRow> _findings = [];

    /// <summary>What the person chose last time they used FixFinder, read once when the window opens.</summary>
    private readonly Preferences _preferences = Preferences.Load();

    /// <summary>What was found the last few times, so a report can say whether things are getting better.</summary>
    private readonly CheckHistory _history = CheckHistory.Load();
    private Severity? _filter;

    /// <summary>Set instead of <see cref="_filter"/> when the reader wants only what would make the program quicker.</summary>
    private bool _performanceOnly;

    private string? _chosenPath;
    private LaunchPlan? _launch;
    private CodeLanguage _language = CodeLanguage.Any;

    private CancellationTokenSource? _cancellation;
    private FixFinderLogger? _logger;

    public MainWindow(string? initialFile = null)
    {
        InitializeComponent();

        OutputListBox.ItemsSource = _output;
        ExtraRunsList.ItemsSource = _extraRuns;
        FindingsList.ItemsSource = _visibleFindings;
        NotesList.ItemsSource = _notes;

        AddLanguageTiles();
        UpdateFilterCounts();

        ExplanationLevelBox.SelectedIndex = (int)_preferences.Explanations;

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
        _cancellation?.Cancel();
        _logger?.Dispose();
        _http.Dispose();

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

        NothingChosenPanel.Visibility = Visibility.Collapsed;
        ChosenPanel.Visibility = Visibility.Collapsed;
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
                if (report is not null) _history.Record(CheckRecord.Of(row.Program.Entry, report.Findings));
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

        var checker = new ProgramChecker(_http, _sources) { Language = CodeLanguage.Of(file) ?? CodeLanguage.Any };

        return await Task.Run(() => checker.CheckAsync(launch, cancellation), cancellation);
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedItem is not ProgramRow row) return;

        ShowChosen(row.Program.Entry);
        _chosenPath = row.Program.Entry;

        ShowFindings(row.Findings ?? []);

        EmptyState.Visibility = row.Findings is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>"3 problems", and "1 problem" rather than "1 problems".</summary>
    private static string Many(int count, string thing) => count == 1 ? $"1 {thing}" : $"{count} {thing}s";

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e) => PickFile();

    private bool PickFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = _language.IsAny ? "Choose the program to check" : $"Choose the {_language.Name} program to check",
            Filter = _language.FileDialogFilter(TargetFactory.FileDialogFilter),
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true) return false;

        Choose(dialog.FileName);
        return true;
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

    private void Choose(string path)
    {
        _chosenPath = path;
        _launch = TargetFactory.FromFile(path);

        NothingChosenPanel.Visibility = Visibility.Collapsed;
        ChosenPanel.Visibility = Visibility.Visible;

        ShowChosen(_launch.ChosenFile ?? path);
        HowItRunsText.Text = _launch.Ok ? _launch.Explanation : _launch.Problem ?? "";
        HowItRunsText.Foreground = (Brush)FindResource(_launch.Ok ? "HintBrush" : "ErrorBrush");

        var detected = CodeLanguage.Of(path);
        UseDetectedLanguageButton.Visibility = Visibility.Collapsed;

        LanguageHintText.Text = detected is null
            ? "Press the language it was written in, or Auto to let FixFinder work it out."
            : $"This looks like {detected.Name}. Press {detected.Name} to check its syntax and logic together.";
    }

    private void ShowChosen(string path)
    {
        ChosenFileText.Text = Path.GetFileName(path) is { Length: > 0 } name ? name : path;
        ChosenFileText.ToolTip = path;
        ChosenFolderText.Text = Path.GetDirectoryName(path) ?? "";
        ChosenFolderText.ToolTip = path;
    }

    private void AddLanguageTiles()
    {
        var order = new[] { CodeLanguage.Python, CodeLanguage.Java, CodeLanguage.CSharp, CodeLanguage.C, CodeLanguage.Cpp, CodeLanguage.JavaScript, CodeLanguage.Go };

        foreach (var language in order.Concat(CodeLanguage.All.Except(order)))
        {
            var badge = Badges.GetValueOrDefault(language.Name) ?? new LanguageBadge(language.Name[..1], "#5B6679");
            var name = language.IsAny ? "Auto-detect" : language.Name;

            var tile = new RadioButton
            {
                GroupName = "Language",
                Tag = language,
                Style = (Style)FindResource("LanguageTile"),
                ToolTip = language.IsAny
                    ? "Work out the language from the file, and check it with everything FixFinder knows."
                    : $"Check it as {language.Name}: compile and run it, and read it for logic mistakes, at the same time.",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Border
                        {
                            Width = 34, Height = 26, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 10, 0),
                            Background = (Brush)new BrushConverter().ConvertFromString(badge.Colour)!,
                            Child = new TextBlock
                            {
                                Text = badge.Short, FontSize = 11, FontWeight = FontWeights.Bold,
                                Foreground = (Brush)new BrushConverter().ConvertFromString(badge.TextColour)!,
                                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                            },
                        },
                        new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };

            var id = language.Name.Replace("C#", "CSharp").Replace("C++", "Cpp");
            AutomationProperties.SetAutomationId(tile, "Language" + new string(id.Where(char.IsLetterOrDigit).ToArray()));
            AutomationProperties.SetName(tile, $"Check as {name}");

            tile.Click += LanguageTile_Click;
            LanguagePanel.Children.Add(tile);
        }
    }

    private async void LanguageTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: CodeLanguage language } || _cancellation is not null) return;

        _language = language;

        if (_chosenPath is null && !PickFile()) return;

        if (_language.Refuses(_chosenPath!) is { } refusal)
        {
            var detected = CodeLanguage.Of(_chosenPath!);
            ShowProblem("That file is in a different language.", $"{refusal} Press {detected!.Name} instead, or choose a {_language.Name} file.");
            OfferLanguage(detected);
            return;
        }

        await CheckAsync();
    }

    private void OfferLanguage(CodeLanguage? detected)
    {
        if (detected is null || detected == _language)
        {
            UseDetectedLanguageButton.Visibility = Visibility.Collapsed;
            return;
        }

        UseDetectedLanguageButton.Content = $"This looks like {detected.Name} - check it as {detected.Name}";
        UseDetectedLanguageButton.Tag = detected;
        UseDetectedLanguageButton.Visibility = Visibility.Visible;
    }

    private void UseDetectedLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (UseDetectedLanguageButton.Tag is not CodeLanguage detected) return;

        var tile = LanguagePanel.Children.OfType<RadioButton>().FirstOrDefault(t => t.Tag is CodeLanguage language && language == detected);
        if (tile is null) return;

        tile.IsChecked = true;
        tile.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, tile));
    }

    private void AddRunButton_Click(object sender, RoutedEventArgs e) => _extraRuns.Add(new ExpectedRunRow());

    private void RemoveRunButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExpectedRunRow row) _extraRuns.Remove(row);
    }

    private async Task CheckAsync()
    {
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

        var checker = new ProgramChecker(_http, _sources) { Language = _language, Expected = expected.IsEmpty ? null : expected };
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
        EmptyState.Visibility = Visibility.Collapsed;
        CopyReportButton.IsEnabled = false;

        ReportSubtitleText.Text = $"{Path.GetFileName(launch.ChosenFile ?? launch.Spec!.ExecutablePath)}  ·  " +
                                  $"{(_language.IsAny ? "language worked out automatically" : _language.Name)}  ·  checking…";

        SetLane(SyntaxStatusText, SyntaxIcon, SyntaxProgress, launch.NeedsCompiling ? "Compiling…" : "Reading the code…", LaneState.Running);
        SetLane(LogicStatusText, LogicIcon, LogicProgress, "Reading the code for logic mistakes…", LaneState.Running);

        SetBusy(true);
    }

    private void FinishReport(CheckReport report)
    {
        foreach (var note in report.Notes) _notes.Add(note);

        ShowFindings(report.Findings);
        RememberThisCheck(report);

        SetLane(SyntaxStatusText, SyntaxIcon, SyntaxProgress, report.SyntaxSummary,
            report.Findings.Any(f => f.Severity == Severity.Error && f.Kind is FindingKind.Syntax or FindingKind.Runtime && f.FoundBy is null) ? LaneState.Failed : LaneState.Passed);
        SetLane(LogicStatusText, LogicIcon, LogicProgress, report.LogicSummary,
            report.Findings.Any(f => (f.Kind == FindingKind.Logic || f.FoundBy is not null) && f.Severity != Severity.Suggestion) ? LaneState.Warned : LaneState.Passed);

        ReportSubtitleText.Text = $"{Path.GetFileName(_launch?.ChosenFile ?? "")}  ·  {(_language.IsAny ? "Auto-detected" : _language.Name)}  ·  " +
                                  $"checked at {DateTime.Now:HH:mm}";

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
        if (_chosenPath is not { Length: > 0 } file) return;

        var now = CheckRecord.Of(file, report.Findings);
        var said = CheckHistory.Since(_history.LastTime(file), now);

        SinceLastText.Text = said ?? "";
        SinceLastText.Visibility = said is null ? Visibility.Collapsed : Visibility.Visible;

        // Recorded after the comparison, so this check is not compared with itself.
        _history.Record(now);
    }

    private void ShowFindings(IReadOnlyList<Finding> findings)
    {
        // A finding can come back with more in it - what its fix changes - so it is known by where it is and what it says.
        static string Key(Finding f) => $"{f.File}|{f.Line}|{f.RuleId}|{f.Title}";

        var expanded = _findings.Where(r => r.IsExpanded).Select(r => Key(r.Finding)).ToHashSet();
        var collapsed = _findings.Where(r => !r.IsExpanded).Select(r => Key(r.Finding)).ToHashSet();

        // Which findings follow from which is worked out in Core; the window only has to look up the lines, which it
        // can do because it is the one place that can see the whole report at once.
        var byId = findings.ToDictionary(f => f.Id, f => f, StringComparer.Ordinal);
        var followers = findings.Where(f => f.CausedBy is not null)
            .GroupBy(f => f.CausedBy!.RootId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(f => f.Line ?? 0).Where(line => line > 0).Order()], StringComparer.Ordinal);

        _findings = findings.Select(f => new FindingRow(f)
        {
            IsExpanded = expanded.Contains(Key(f)) || (!collapsed.Contains(Key(f)) && f.Severity == Severity.Error),
            Level = _preferences.Explanations,
            FollowsLine = f.CausedBy is { } cause && byId.TryGetValue(cause.RootId, out var root) ? root.Line : null,
            ExplainsLines = followers.GetValueOrDefault(f.Id, []),
        }).ToList();

        FilterPanel.Visibility = _findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_findings.Count > 0) EmptyState.Visibility = Visibility.Collapsed;

        UpdateFilterCounts();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _visibleFindings.Clear();

        var shown = _performanceOnly
            ? _findings.Where(r => r.Finding.Kind == FindingKind.Performance)
            : _findings.Where(r => _filter is null || r.Finding.Severity == _filter);

        foreach (var row in shown) _visibleFindings.Add(row);
    }

    private void UpdateFilterCounts()
    {
        int Count(Severity severity) => _findings.Count(r => r.Finding.Severity == severity);

        FilterAll.Content = $"All  {_findings.Count}";
        FilterErrors.Content = $"Errors  {Count(Severity.Error)}";
        FilterWarnings.Content = $"Warnings  {Count(Severity.Warning)}";
        FilterSuggestions.Content = $"Suggestions  {Count(Severity.Suggestion)}";
        FilterPerformance.Content = $"Performance  {_findings.Count(f => f.Finding.Kind == FindingKind.Performance)}";
    }

    /// <summary>
    /// Changes how the findings on screen are worded, and nothing else about them.
    /// </summary>
    /// <remarks>
    /// Every wording a finding has was worked out when the program was checked, so this hands each row the new level
    /// and the rows read a different string. Nothing is compiled again, nothing is run again, and no finding appears
    /// or disappears - which is the whole point of the setting.
    /// </remarks>
    private void ExplanationLevel_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ExplanationLevelBox.SelectedIndex < 0) return;

        var chosen = (ExplanationLevel)ExplanationLevelBox.SelectedIndex;
        if (chosen == _preferences.Explanations) return;

        _preferences.Explanations = chosen;
        foreach (var row in _findings) row.Level = chosen;

        // Failing to write a preference is not worth interrupting anybody over; it is remembered for this session
        // either way.
        _preferences.Save();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        _performanceOnly = sender == FilterPerformance;

        _filter = sender == FilterErrors ? Severity.Error
            : sender == FilterWarnings ? Severity.Warning
            : sender == FilterSuggestions ? Severity.Suggestion
            : null;

        if (_findings is not null) ApplyFilter();
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

        LanguagePanel.IsEnabled = !busy;
        ChooseFileButton.IsEnabled = !busy;
        ChooseAnotherButton.IsEnabled = !busy;
        ChooseFolderButton.IsEnabled = !busy;
        ChooseAnotherFolderButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        UseDetectedLanguageButton.IsEnabled = !busy;
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

        EmptyState.Visibility = Visibility.Visible;
        EmptyTitleText.Text = title;
        EmptyBodyText.Text = text;
    }

    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FindingRow row) row.IsExpanded = !row.IsExpanded;
    }

    private void CopyExample_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is FindingRow row && Copy(row.CorrectedExample) && sender is Button button)
            Flash(button, "Copied");
    }

    private void CopyReportButton_Click(object sender, RoutedEventArgs e)
    {
        var header = $"FixFinder report for {Path.GetFileName(_launch?.ChosenFile ?? "")} ({_language.Name})";
        var text = string.Join(Environment.NewLine + Environment.NewLine,
            new[] { header }.Concat(_notes).Concat(_findings.Select(r => r.AsText())));

        if (Copy(text)) Flash(CopyReportButton, "Copied");
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not FindingRow row || !File.Exists(row.Finding.File)) return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Finding.File}\"") { UseShellExecute = true });
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
