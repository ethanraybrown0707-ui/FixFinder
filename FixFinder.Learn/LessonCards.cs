using System.ComponentModel;
using System.Runtime.CompilerServices;
using FixFinder.Core.Teaching;

namespace FixFinder.Learn;

/// <summary>What every card on the page has in common: telling the page when something on it changed.</summary>
public abstract class Card : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    protected void Changed(params string[] properties)
    {
        foreach (var property in properties) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}

/// <summary>
/// One worked example on the page: the program with the mistake and the program put right, what each does, and - once
/// the learner presses Run - what running each did on this computer.
/// </summary>
public sealed class ExampleCard(WorkedExample example, Lesson lesson) : Card
{
    public WorkedExample Example { get; } = example;

    public string Title => Example.Title;

    public string BrokenCode => Example.Broken.TrimEnd();

    public string BrokenDoes => Example.BrokenDoes.Described(lesson.Language.Name);

    public string FixedCode => Example.Fixed.TrimEnd();

    public string FixedDoes => Example.FixedDoes.Described(lesson.Language.Name);

    public string WhatChanged => Example.WhatChanged;

    private string _brokenRan = "";
    private string _fixedRan = "";

    /// <summary>What running the program with the mistake did here, once it has been run.</summary>
    public string BrokenRan
    {
        get => _brokenRan;
        set { _brokenRan = value; Changed(nameof(BrokenRan), nameof(HasBrokenRan)); }
    }

    public bool HasBrokenRan => BrokenRan.Length > 0;

    /// <summary>What running the program put right did here, once it has been run.</summary>
    public string FixedRan
    {
        get => _fixedRan;
        set { _fixedRan = value; Changed(nameof(FixedRan), nameof(HasFixedRan)); }
    }

    public bool HasFixedRan => FixedRan.Length > 0;
}

/// <summary>One of a question's answers, as a choice the learner can pick.</summary>
public sealed class AnswerChoice(Answer answer, string group) : Card
{
    public Answer Answer { get; } = answer;

    public string Text => Answer.Text.TrimEnd();

    public bool IsCode => Answer.IsCode;

    public bool IsWords => !Answer.IsCode;

    /// <summary>The radio group the choice is in: its question's own, so picking one answer leaves other questions alone.</summary>
    public string Group { get; } = group;

    private bool _isPicked;

    public bool IsPicked
    {
        get => _isPicked;
        set { _isPicked = value; Changed(); }
    }
}

/// <summary>A question on the page: what it asks, the code it is about, the answers, and once checked, whether it was right.</summary>
public sealed class QuestionCard(Question question, int number) : Card
{
    public Question Question { get; } = question;

    public string Asks => $"{number}. {Question.Asks}";

    public string Code => Question.Code?.TrimEnd() ?? "";

    public bool HasCode => Code.Length > 0;

    public IReadOnlyList<AnswerChoice> Answers { get; } =
        question.Answers.Select(answer => new AnswerChoice(answer, $"question{number}")).ToList();

    private string _result = "";
    private bool _wasRight;

    /// <summary>What checking the answer picked said: right or not, and why.</summary>
    public string Result
    {
        get => _result;
        set { _result = value; Changed(nameof(Result), nameof(HasResult)); }
    }

    public bool HasResult => Result.Length > 0;

    public bool WasRight
    {
        get => _wasRight;
        set { _wasRight = value; Changed(); }
    }
}

/// <summary>A problem FixFinder found, as the list of recent problems shows it.</summary>
public sealed record ProblemChoice(ProblemDetails Details)
{
    public string Title => Details.Title;

    public string Where => $"{Details.Location}  ·  found {Details.FoundAt.LocalDateTime:d MMM, HH:mm}";
}

/// <summary>A lesson as the list of lessons shows it, with how much of it has been done.</summary>
public sealed record LessonChoice(Lesson Lesson, string Progress)
{
    public string Title => Lesson.Concept.Title;
}
