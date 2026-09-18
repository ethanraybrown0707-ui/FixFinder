using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FixFinder.Core;
using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Logic;
using FixFinder.Core.Patching;
using FixFinder.Core.Security;
using FixFinder.Core.Sources;
using Microsoft.Win32;

// UIElement has a CacheMode property of its own - a WPF bitmap-caching hint - and an inherited
// member beats a using alias in name lookup, so the HTTP one needs a different name here.
using HttpCacheMode = FixFinder.Core.Http.CacheMode;

namespace FixFinder.Gui;

/// <summary>
/// The whole tool: pick a program, run it, and be asked about a fix if there is one.
/// </summary>
/// <remarks>
/// Deliberately thin. Everything the window used to ask for up front - where the source is,
/// which query to send, which of thirty results to open, whether its patch fits - is worked out
/// by <see cref="FixFinderSession"/>, because every one of those had a defensible default and
/// none of them is a decision worth making before seeing whether the program even crashes.
/// <para>
/// Two gates survive the simplification, and they are the two that matter: a confirmation naming
/// the exact command line before anything is launched, and the typed confirmation in the preview
/// before anything is written. Neither is a setting.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<OutputRow> _output = [];

    /// <summary>Runs beyond the first, each with its own input and the output it should produce.</summary>
    private readonly ObservableCollection<ExpectedRunRow> _extraRuns = [];

    private readonly FixFinderHttpClient _http = new();
    private readonly FixSourceRegistry _sources = new();

    private LaunchPlan? _launch;

    /// <summary>The file last chosen, so changing the language can check it again.</summary>
    private string? _chosenPath;

    /// <summary>The language chosen with the buttons at the top; Any until one is pressed.</summary>
    private CodeLanguage _language = CodeLanguage.Any;
    private CancellationTokenSource? _cancellation;
    private FixFinderLogger? _logger;
    private LoopResult? _result;

    /// <summary>How the last round was answered, so the next prompt can say how it got there.</summary>
    private RoundChoice? _previousChoice;

    /// <param name="initialFile">
    /// A program named on the command line, or dropped onto the exe in Explorer. Selected but
    /// never run - the confirmation before launching still has to be answered.
    /// </param>
    public MainWindow(string? initialFile = null)
    {
        InitializeComponent();

        OutputListBox.ItemsSource = _output;
        ExtraRunsList.ItemsSource = _extraRuns;

        AddLanguageButtons();

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

    // ================================================================== choosing

    private void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Pick the program to run",
            Filter = _language.FileDialogFilter(TargetFactory.FileDialogFilter),
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
    /// Works out how the file would be launched, and says so before anything runs.
    /// </summary>
    /// <remarks>
    /// Shown rather than done quietly. "Running it with python" is the one piece of guesswork in
    /// the whole simplified flow, and it is the piece most likely to be wrong - a file with two
    /// plausible interpreters, or the wrong Python on PATH - so it is stated where it can be
    /// disagreed with.
    /// </remarks>
    private void Choose(string path)
    {
        _chosenPath = path;
        _launch = TargetFactory.FromFile(path);
        _result = null;
        _previousChoice = null;

        ResultBorder.Visibility = Visibility.Collapsed;
        UseDetectedLanguageButton.Visibility = Visibility.Collapsed;
        _output.Clear();

        ShowChosen(path);

        if (!_launch.Ok)
        {
            HowItRunsText.Text = "";
            FindFixButton.IsEnabled = false;

            StatusText.Text = "That one cannot be run.";
            ShowResult(_launch.Problem!, problem: true);
            return;
        }

        ShowChosen(_launch.ChosenFile ?? _launch.Spec!.ExecutablePath);
        HowItRunsText.Text = _launch.Explanation;

        var detected = CodeLanguage.Of(path);

        // A .py file is Python whichever button is pressed. Running it as Java would read its output
        // with no parser that understands it and offer it to no rule that could fix it.
        if (_language.Refuses(path) is { } refusal)
        {
            FindFixButton.IsEnabled = false;
            StatusText.Text = "That file is not in the language selected.";
            ShowResult($"{refusal} Choose {detected!.Name} above, or pick a {_language.Name} file.", problem: true);
            OfferLanguage(detected);
            return;
        }

        if (_language.IsAny) OfferLanguage(detected);

        FindFixButton.IsEnabled = true;
        StatusText.Text = _language.IsAny ? "Ready." : $"Ready. Only {_language.Name} will be checked.";
    }

    /// <summary>The file's name where it is easy to read, and its folder underneath, shortened if it has to be.</summary>
    private void ShowChosen(string path)
    {
        ChosenFileText.Text = Path.GetFileName(path) is { Length: > 0 } name ? name : path;
        ChosenFileText.ToolTip = path;

        ChosenFolderText.Text = Path.GetDirectoryName(path) ?? "";
        ChosenFolderText.ToolTip = path;
        ChosenFolderText.Visibility = ChosenFolderText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================================================================== language

    /// <summary>One pill per language, built from the list the engine uses, so the two cannot drift apart.</summary>
    private void AddLanguageButtons()
    {
        foreach (var language in CodeLanguage.All)
        {
            var button = new RadioButton
            {
                Content = language.IsAny ? "Any language" : language.Name,
                GroupName = "Language",
                Tag = language,
                Style = (Style)FindResource("ChoicePill"),
                IsChecked = language == _language,
                ToolTip = language.IsAny
                    ? "Work out the language from the program itself, and check it against every language FixFinder knows."
                    : $"Only {language.Name}'s error formats are read and only its fixes are tried. Files: {string.Join(", ", language.Extensions)}",
            };

            var id = language.Name.Replace("C#", "CSharp").Replace("C++", "Cpp");
            AutomationProperties.SetAutomationId(button, "Language" + new string(id.Where(char.IsLetterOrDigit).ToArray()));
            AutomationProperties.SetName(button, language.IsAny ? "Any language" : language.Name);

            button.Checked += LanguageButton_Checked;
            LanguagePanel.Children.Add(button);
        }
    }

    private void LanguageButton_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: CodeLanguage language }) return;

        _language = language;

        LanguageHintText.Text = language.IsAny
            ? "Pick the language the program is written in, and only that language is checked."
            : $"Only {language.Name} is checked: its error messages are read and its fixes are tried, and nothing else.";

        // The choice changes what the chosen file may be, so it is looked at again.
        if (_chosenPath is not null && _cancellation is null) Choose(_chosenPath);
    }

    /// <summary>Offers the language a file is plainly written in, one click away.</summary>
    private void OfferLanguage(CodeLanguage? detected)
    {
        if (detected is null || detected == _language)
        {
            UseDetectedLanguageButton.Visibility = Visibility.Collapsed;
            return;
        }

        UseDetectedLanguageButton.Content = $"This looks like {detected.Name} - check only {detected.Name}";
        UseDetectedLanguageButton.Tag = detected;
        UseDetectedLanguageButton.Visibility = Visibility.Visible;
    }

    private void AddRunButton_Click(object sender, RoutedEventArgs e) => _extraRuns.Add(new ExpectedRunRow());

    private void RemoveRunButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExpectedRunRow row) _extraRuns.Remove(row);
    }

    private void UseDetectedLanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (UseDetectedLanguageButton.Tag is not CodeLanguage detected) return;

        foreach (var button in LanguagePanel.Children.OfType<RadioButton>())
        {
            if (button.Tag is CodeLanguage language && language == detected) button.IsChecked = true;
        }
    }

    // ================================================================== running

    private async void FindFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_launch is not { Ok: true, Spec: not null }) return;

        // Arguments and typed answers belong to this run and every re-run of it, so they go into the spec itself.
        var launch = _launch with
        {
            Spec = _launch.Spec.WithArguments(ArgumentsTextBox.Text).WithInput(InputTextBox.Text is { Length: > 0 } typed ? typed : null),
        };

        var spec = launch.Spec!;

        // What it should print: the first run is the one about to happen; any others are run only to check the logic.
        var expected = ExpectedBehaviour.From(
            new[] { new ExpectedRun(InputTextBox.Text is { Length: > 0 } first ? first : null, ExpectedOutputTextBox.Text) }
                .Concat(_extraRuns.Select(r => new ExpectedRun(r.Input.Length > 0 ? r.Input : null, r.Expected))),
            ArgumentsTextBox.Text);

        // The gate that has to stay. FixFinder is about to run a program as this user, with
        // this user's environment, and the exact command line is the one thing nobody should
        // have to infer from a file name.
        // A compiled language runs two commands, and the confirmation names both. Showing only
        // the second would be describing something other than what is about to happen.
        var commands = _launch.Compile is { } compile
            ? $"{compile.DisplayCommandLine}\n\nthen:\n\n{spec.DisplayCommandLine}"
            : spec.DisplayCommandLine;

        var confirmed = MessageBox.Show(this,
            "FixFinder is about to run this, as you, and capture everything it prints:\n\n" +
            $"{commands}\n\n" +
            $"In: {spec.WorkingDirectory}\n\n" +
            (spec.StandardInput is { } answers ? $"Typing into it:\n\n{answers}\n\n" : "") +
            (expected.IsEmpty
                ? ""
                : $"Then it will compare what it prints with what you expected{(expected.Runs.Count > 1 ? $", for all {expected.Runs.Count} runs" : "")}. " +
                  "If the output is wrong, it builds and runs changed copies of the program in a temporary folder to find the change " +
                  "that makes it right - your own files are not changed.\n\n") +
            "Continue?",
            "Run this program?", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);

        if (confirmed != MessageBoxResult.OK) return;

        _output.Clear();
        ResultBorder.Visibility = Visibility.Collapsed;
        SetBusy(true);

        _logger?.Dispose();
        _logger = new FixFinderLogger();
        LogPathText.Text = _logger.FilePath;
        _logger.WriteSection("Target");
        _logger.Write($"language: {_language.Name}");
        if (_launch.Compile is { } logged) _logger.Write($"build: {logged.DisplayCommandLine}");
        _logger.Write(spec.DisplayCommandLine);

        _cancellation = new CancellationTokenSource();

        var session = new FixFinderSession(_http, _sources) { Language = _language, Expected = expected.IsEmpty ? null : expected };
        session.Progress += OnProgress;
        session.Log += OnLog;
        session.LineCaptured += OnLineCaptured;

        try
        {
            var budget = SearchBudget.Default with
            {
                Cache = OfflineCheckBox.IsChecked == true ? HttpCacheMode.CacheOnly : HttpCacheMode.Normal,
            };

            var loop = new FixLoop(session, new FixStep(language: _language)) { Ask = AskAboutAsync };
            loop.Log += OnLog;
            loop.RoundStarting += OnRoundStarting;

            try
            {
                _result = await loop.RunAsync(launch, budget, _cancellation.Token);
            }
            finally
            {
                loop.Log -= OnLog;
                loop.RoundStarting -= OnRoundStarting;
            }

            _logger.WriteSection("Result");
            _logger.Write($"{_result.End} after {_result.Rounds.Count} round(s): {_result.Headline}");
            foreach (var warning in _result.Last.Warnings) _logger.Write($"warning: {warning}");

            Show(_result);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Stopped.";
            _logger.Write("Stopped by the user.");
        }
        catch (Exception ex)
        {
            // The session already turns expected failures into outcomes, so reaching here means
            // something unforeseen. Say so rather than leaving a spinner running forever.
            StatusText.Text = "FixFinder itself failed.";
            ShowResult($"Something inside FixFinder went wrong: {ex.Message}", problem: true);
            _logger.Write($"FixFinder itself failed: {ex}");
        }
        finally
        {
            session.Progress -= OnProgress;
            session.Log -= OnLog;
            session.LineCaptured -= OnLineCaptured;

            _cancellation.Dispose();
            _cancellation = null;
            SetBusy(false);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        StatusText.Text = "Stopping...";
        _cancellation?.Cancel();
    }

    // ================================================================== reporting

    /// <summary>
    /// Puts one round's findings in front of the user, and reports the answer to the loop.
    /// </summary>
    /// <remarks>
    /// Only two of the outcomes open anything. A program that ran fine, or crashed with nothing
    /// published about it, is a complete answer on its own - interrupting with a dialog to say
    /// "nothing happened" would train people to dismiss the dialog that matters.
    /// <para>
    /// Runs on the UI thread: the loop is awaited from here, so its continuations come back to
    /// the dispatcher and <c>ShowDialog</c> is legal. That is also what makes the loop block on
    /// the answer rather than racing ahead of it.
    /// </para>
    /// </remarks>
    private Task<RoundDecision> AskAboutAsync(SessionOutcome outcome, int round, CancellationToken cancellationToken)
    {
        if (round > 1) ShowOutputOf(outcome);

        if (!outcome.WorthShowing || cancellationToken.IsCancellationRequested)
            return Task.FromResult(RoundDecision.Stop);

        var found = new FixFoundWindow(
            new FixFoundContext(outcome, _http, _logger, round, _previousChoice)) { Owner = this };

        found.ShowDialog();

        var decision = found.Decision ?? RoundDecision.Stop;
        _previousChoice = decision.Choice;

        return Task.FromResult(decision);
    }

    /// <summary>Says what happened across every round, once the loop has finished.</summary>
    private void Show(LoopResult result)
    {
        StatusText.Text = result.Headline;

        // An unattended run answers its own prompts, so nothing has refilled the pane since the
        // first round. What is worth seeing is the run that ended it.
        if (result.Looped) ShowOutputOf(result.Last);

        ShowResult(result.Detail, problem: result.End is LoopEnd.CouldNotRun);

        if (result.Last.Warnings.Count > 0)
            ResultText.Text += "\n\n" + string.Join("\n", result.Last.Warnings);

        // The captured output is worth a glance when something went wrong, and is noise when
        // nothing did. After a loop it is the last round's output, which is the one still failing.
        DetailsExpander.IsExpanded = result.Last.Error is not null && _output.Count > 0;
    }

    private void OnRoundStarting(int round) => Dispatcher.BeginInvoke(() =>
    {
        if (round > 1) StatusText.Text = $"That worked. Error {round} — looking for the next fix...";
    });

    /// <summary>
    /// Replaces the output pane with the run this round is about.
    /// </summary>
    /// <remarks>
    /// Only the first round streams. Every round after it is the verifier's re-run - the program
    /// has already been built and run again to decide whether the last patch helped, and the loop
    /// deliberately reuses that run rather than launching a third time. Its output arrives with
    /// the result rather than line by line, so the pane is refilled from it here.
    /// <para>
    /// Replaced rather than appended: three crashes stacked in one list, with nothing marking
    /// where each began, would bury the error actually being asked about.
    /// </para>
    /// </remarks>
    private void ShowOutputOf(SessionOutcome outcome)
    {
        if (outcome.Run is not { } run) return;

        _output.Clear();

        foreach (var line in run.Lines)
            _output.Add(new OutputRow { DisplayLine = line.DisplayLine, IsError = line.IsError });
    }

    private void ShowResult(string text, bool problem)
    {
        ResultText.Text = text;
        ResultText.Foreground = (Brush)FindResource(problem ? "DangerBrush" : "TextBrush");

        ResultBorder.Background = (Brush)FindResource(problem ? "DangerSoftBrush" : "SuccessSoftBrush");
        ResultBorder.BorderBrush = (Brush)FindResource(problem ? "DangerBrush" : "CardBorderBrush");
        ResultBorder.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool busy)
    {
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = busy;

        FindFixButton.IsEnabled = !busy && _launch is { Ok: true };
        ChooseFileButton.IsEnabled = !busy;
        SettingsButton.IsEnabled = !busy;
        OfflineCheckBox.IsEnabled = !busy;
        LanguagePanel.IsEnabled = !busy;
        UseDetectedLanguageButton.IsEnabled = !busy;
    }

    private void OnProgress(string message) => Dispatcher.BeginInvoke(() => StatusText.Text = message);

    private void OnLog(string message) => _logger?.Write(message);

    private void OnLineCaptured(CapturedLine line) =>
        Dispatcher.BeginInvoke(() =>
            _output.Add(new OutputRow { DisplayLine = line.DisplayLine, IsError = line.IsError }));

    // ================================================================== the small print

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_http) { Owner = this };
        settings.ShowDialog();

        if (settings.Saved) _logger?.Write("Credentials updated.");
    }

    private void BackupsButton_Click(object sender, RoutedEventArgs e)
    {
        var store = new BackupStore();

        try
        {
            Directory.CreateDirectory(store.Root);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(store.Root)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, $"Could not open the backups folder:\n\n{ex.Message}",
                "FixFinder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
