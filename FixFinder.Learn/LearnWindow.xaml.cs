using System.Windows;
using System.Windows.Controls;
using FixFinder.Core;
using FixFinder.Core.Teaching;
using FixFinder.Desktop;

namespace FixFinder.Learn;

/// <summary>
/// Opens a problem by its code, or a lesson from the list, and teaches it: the learner's own problem when it was found on
/// this computer, the idea behind it, what their language does about it, examples to run, a fix to try and questions.
/// </summary>
public partial class LearnWindow : Window
{
    private readonly ProblemStore _problems = new();
    private readonly LearnProgress _progress = LearnProgress.Load();

    /// <summary>The lesson on the page, or null when the page shows a concept with no lesson in its language.</summary>
    private Lesson? _lesson;

    private List<QuestionCard> _questions = [];

    public LearnWindow(string? codeToOpen = null)
    {
        InitializeComponent();
        ScreenFit.Apply(this);

        foreach (var language in Lessons.All.Select(lesson => lesson.Language.Name).Distinct())
            LanguageBox.Items.Add(new ComboBoxItem { Content = language, Tag = language });
        LanguageBox.SelectedIndex = LanguageBox.Items.Count > 0 ? 0 : -1;

        ShowRecentProblems();

        // FixFinder may have found more problems while this window was in the background.
        Activated += (_, _) => ShowRecentProblems();

        if (codeToOpen is { Length: > 0 })
        {
            CodeBox.Text = codeToOpen;
            Loaded += (_, _) => OpenCode(codeToOpen);
        }
    }

    private void OpenCodeButton_Click(object sender, RoutedEventArgs e) => OpenCode(CodeBox.Text);

    /// <summary>Opens the problem a code names: its own details when they were kept here, and its lesson either way.</summary>
    private void OpenCode(string typed)
    {
        var reading = ProblemCode.Read(typed);

        if (reading.Code is not { } code)
        {
            CodeProblemText.Text = reading.Problem;
            CodeProblemText.Visibility = Visibility.Visible;
            return;
        }

        CodeProblemText.Visibility = Visibility.Collapsed;
        CodeBox.Text = code.Text;

        var found = _problems.Find(code);
        Show(code.Concept, code.Language, found);
    }

    private void OpenProblem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ProblemChoice choice) return;

        CodeBox.Text = choice.Details.Code;
        OpenCode(choice.Details.Code);
    }

    private void OpenLesson_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LessonChoice choice) Show(choice.Lesson.Concept, choice.Lesson.Language, problems: null);
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowLessonList();

    /// <summary>The lessons of the language chosen, each with how many of its questions have been answered right.</summary>
    private void ShowLessonList()
    {
        if ((LanguageBox.SelectedItem as ComboBoxItem)?.Tag is not string language) return;

        LessonsList.ItemsSource = Lessons.All
            .Where(lesson => lesson.Language.Name == language)
            .Select(lesson => new LessonChoice(lesson, ProgressOf(lesson)))
            .ToList();
    }

    private string ProgressOf(Lesson lesson) =>
        _progress.Finished(lesson) ? "done" : _progress.RightIn(lesson) is var right and > 0 ? $"{right} of {lesson.Questions.Count}" : "";

    private void ShowRecentProblems()
    {
        var recent = _problems.Recent(12).Select(details => new ProblemChoice(details)).ToList();

        ProblemsList.ItemsSource = recent;
        NoProblemsText.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ForgetProblemsButton.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ForgetProblemsButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Delete what FixFinder kept of the problems it found - what it said of each, and the line of code it is on?\n\n" +
            "Their codes will still open their lessons, but no longer your own lines.",
            "FixFinder Learn", MessageBoxButton.OKCancel, MessageBoxImage.Question);

        if (answer != MessageBoxResult.OK) return;

        _problems.Forget();
        ShowRecentProblems();
        YourProblemCard.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// The page for a concept in a language: the learner's own problem first, when there is one, then the idea, then the
    /// lesson - or, when there is no lesson on it in this language yet, the languages there is one in.
    /// </summary>
    private void Show(Concept concept, CodeLanguage language, IReadOnlyList<ProblemDetails>? problems)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        LessonScroller.Visibility = Visibility.Visible;
        LessonScroller.ScrollToTop();

        ConceptTitleText.Text = concept.Title;
        LanguageText.Text = language.Name;
        IdeaText.Text = concept.Idea;

        ShowProblem(problems);

        _lesson = Lessons.For(concept, language);
        var hasLesson = _lesson is not null;

        InThisLanguageCard.Visibility = hasLesson ? Visibility.Visible : Visibility.Collapsed;
        TryItCard.Visibility = hasLesson ? Visibility.Visible : Visibility.Collapsed;
        QuestionsCard.Visibility = hasLesson && _lesson!.Questions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoLessonCard.Visibility = hasLesson ? Visibility.Collapsed : Visibility.Visible;

        if (_lesson is not { } lesson)
        {
            ExamplesList.ItemsSource = null;
            ShowNoLesson(concept, language);
            return;
        }

        InThisLanguageLabel.Text = $"IN {language.Name.ToUpperInvariant()}";
        InThisLanguageText.Text = lesson.InThisLanguage;
        ExamplesList.ItemsSource = lesson.Examples.Select(example => new ExampleCard(example, lesson)).ToList();

        TryItIntroText.Text = $"Here is the first program again. Change it so it works - so it prints what the program put right prints - and press Check my fix. It is run here with {language.Name}, as FixFinder would run it.";
        TryItBox.Text = lesson.Examples[0].Broken.TrimEnd();
        TryItResultText.Visibility = Visibility.Collapsed;
        TryItOutputBox.Visibility = Visibility.Collapsed;

        _questions = lesson.Questions.Select((question, index) => new QuestionCard(question, index + 1)).ToList();
        QuestionsList.ItemsSource = _questions;
        ShowQuestionsProgress();
    }

    /// <summary>What FixFinder said of the learner's own problem, when it was found on this computer.</summary>
    private void ShowProblem(IReadOnlyList<ProblemDetails>? problems)
    {
        NotKeptHereCard.Visibility = problems is { Count: 0 } ? Visibility.Visible : Visibility.Collapsed;

        if (problems is not [var newest, ..])
        {
            YourProblemCard.Visibility = Visibility.Collapsed;
            return;
        }

        YourProblemCard.Visibility = Visibility.Visible;
        InlineCode.SetText(ProblemTitleText, newest.Title);
        ProblemWhereText.Text = $"{newest.Location}  ·  found {newest.FoundAt.LocalDateTime:d MMMM yyyy, HH:mm}";

        ProblemLineBox.Text = newest.LineOfCode ?? "";
        ProblemLineBox.Visibility = newest.LineOfCode is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        InlineCode.SetText(ProblemExplanationText, newest.Explanation);
        InlineCode.SetText(ProblemWhyText, newest.WhyItMatters);
        InlineCode.SetText(ProblemFixText, newest.SuggestedFix);

        ProblemChangeBox.Text = string.Join("\n", newest.Change);
        ProblemChangePanel.Visibility = newest.Change.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Two problems whose codes happen to match are both kept; the other one is named rather than mixed in with this.
        ProblemOthersText.Visibility = problems.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ProblemOthersText.Text = problems.Count > 1
            ? $"Another problem FixFinder found has this code too: {problems[1].Title} ({problems[1].Location}). This is the newer of the two."
            : "";
    }

    /// <summary>For a concept with no lesson in this language yet: the languages there is one in, to open instead.</summary>
    private void ShowNoLesson(Concept concept, CodeLanguage language)
    {
        var others = Lessons.Of(concept);

        NoLessonText.Text = others.Count == 0
            ? $"There is no lesson on this yet, in {language.Name} or any other language - the idea above, and what FixFinder said of your problem, are what there is for now."
            : $"There is no lesson on this in {language.Name} yet. There is one in each of these - the idea is the same in all of them:";

        OtherLanguagesPanel.Children.Clear();

        foreach (var other in others)
        {
            var button = new Button { Content = other.Language.Name, Style = (Style)FindResource("SmallButton"), Tag = other, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => Show(other.Concept, other.Language, problems: null);
            OtherLanguagesPanel.Children.Add(button);
        }
    }

    private async void RunBroken_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExampleCard card || _lesson is not { } lesson) return;

        card.BrokenRan = "Running it…";
        card.BrokenRan = await RunAndSayAsync(lesson, card.Example.Broken, card.Example.BrokenDoes, sender as Button);
    }

    private async void RunFixed_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExampleCard card || _lesson is not { } lesson) return;

        card.FixedRan = "Running it…";
        card.FixedRan = await RunAndSayAsync(lesson, card.Example.Fixed, card.Example.FixedDoes, sender as Button);
    }

    /// <summary>
    /// Runs a lesson's program here and says what it did, with everything it printed - and, should this computer's
    /// toolchain do something else than the lesson says, says that too rather than leave the two to disagree unremarked.
    /// </summary>
    private static async Task<string> RunAndSayAsync(Lesson lesson, string code, Behaviour lessonSays, Button? button)
    {
        if (button is not null) button.IsEnabled = false;

        try
        {
            var observed = await Task.Run(() => SnippetRunner.RunAsync(lesson.Language, code, SnippetRunner.TimeLimitFor(lessonSays)));
            var printed = observed.Output.Count > 0 ? string.Join("\n", observed.Output) + "\n\n" : "";
            var differs = observed.Ran && !observed.Shows(lessonSays)
                ? $"\n\nThat is not what the lesson says it does - it was checked with the {lesson.Language.Name} FixFinder is tested with, and this computer's may differ."
                : "";

            return $"{printed}{observed.Said(lesson.Language.Name)}{differs}";
        }
        finally
        {
            if (button is not null) button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Runs the learner's own version and says what it did beside what the program put right does - in what was observed,
    /// not in a verdict on their code: a different fix that does the job well can print something else and still be right.
    /// </summary>
    private async void CheckFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lesson is not { } lesson) return;

        var example = lesson.Examples[0];
        var language = lesson.Language.Name;

        CheckFixButton.IsEnabled = false;
        TryItResultText.Visibility = Visibility.Visible;
        InlineCode.SetText(TryItResultText, "Running your version…");

        try
        {
            var observed = await Task.Run(() => SnippetRunner.RunAsync(lesson.Language, TryItBox.Text));

            var said = !observed.Ran ? observed.WhyNotRun ?? $"It could not be run: nothing on this computer runs {language}."
                : observed.Shows(example.FixedDoes) ? $"It works: {Lowered(example.FixedDoes.Described(language))} - just as the program put right does."
                : observed.Shows(example.BrokenDoes) ? $"Not yet - it still does what the program with the mistake does: {Lowered(example.BrokenDoes.Described(language))}."
                : $"Your version: {observed.Said(language)}\n\nThe program put right: {example.FixedDoes.Described(language)}.";

            InlineCode.SetText(TryItResultText, said);
            TryItOutputBox.Text = string.Join("\n", observed.Output);
            TryItOutputBox.Visibility = observed.Output.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            CheckFixButton.IsEnabled = true;
        }
    }

    private void StartAgainButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lesson is not { } lesson) return;

        TryItBox.Text = lesson.Examples[0].Broken.TrimEnd();
        TryItResultText.Visibility = Visibility.Collapsed;
        TryItOutputBox.Visibility = Visibility.Collapsed;
    }

    /// <summary>Says whether the answer picked is right, and why - and remembers a right one, so the lesson shows as done.</summary>
    private void CheckAnswer_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not QuestionCard card || _lesson is not { } lesson) return;

        if (card.Answers.FirstOrDefault(choice => choice.IsPicked) is not { } picked)
        {
            card.WasRight = false;
            card.Result = "Pick an answer first.";
            return;
        }

        card.WasRight = picked.Answer.IsRight;
        card.Result = (picked.Answer.IsRight ? "Right. " : "Not quite. ") + picked.Answer.Because;

        if (!picked.Answer.IsRight) return;

        _progress.AnsweredRightly(lesson, card.Question);
        _progress.Save();
        ShowQuestionsProgress();
        ShowLessonList();
    }

    private void ShowQuestionsProgress()
    {
        if (_lesson is not { } lesson) return;

        QuestionsProgressText.Text = $"{_progress.RightIn(lesson)} of {lesson.Questions.Count} answered right";
    }

    private static string Lowered(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
