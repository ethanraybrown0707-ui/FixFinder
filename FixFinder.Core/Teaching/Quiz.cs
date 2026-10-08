namespace FixFinder.Core.Teaching;

/// <summary>One answer a question offers, whether it is right, and what to say when it is chosen.</summary>
/// <param name="Text">The answer as shown: a sentence, or a whole program when <paramref name="IsCode"/>.</param>
/// <param name="IsCode">Whether the answer is code, shown in the code font.</param>
/// <param name="IsRight">Whether it is the right answer.</param>
/// <param name="Because">Why it is right or wrong - drawn from what running the code showed.</param>
public sealed record Answer(string Text, bool IsCode, bool IsRight, string Because);

/// <summary>A question about a lesson, with the code it is about and the answers to choose from - exactly one of them right.</summary>
/// <param name="Asks">The question.</param>
/// <param name="Code">The program the question is about, or null when the answers are programs themselves.</param>
/// <param name="Answers">The answers, in the order they are offered.</param>
public sealed record Question(string Asks, string? Code, IReadOnlyList<Answer> Answers)
{
    public Answer Right => Answers.Single(answer => answer.IsRight);
}

/// <summary>
/// Makes a lesson's questions from its examples. Every answer is worked out from what running the example showed - what it
/// printed, the error it stopped with and the line - so a question never says something about code that is not so.
/// </summary>
/// <remarks>
/// A wrong answer is always something that really happens to a related program - the fixed one, a change that looks like a
/// fix - and that running this one showed it does not do. The order the answers come in is fixed for each question, so the
/// right one is not always first, and a question is the same each time it is asked.
/// </remarks>
public static class Quiz
{
    public static IReadOnlyList<Question> For(Lesson lesson)
    {
        var language = lesson.Language.Name;
        var questions = new List<Question>();

        foreach (var example in lesson.Examples)
        {
            if (WhatHappens(example, lesson, language) is { } whatHappens) questions.Add(whatHappens);
            if (WhichLine(example, language) is { } whichLine) questions.Add(whichLine);
            if (WhichFix(example, language) is { } whichFix) questions.Add(whichFix);
        }

        return questions;
    }

    /// <summary>"What happens when this runs?" - the broken program's real behaviour, beside behaviours it does not have.</summary>
    private static Question? WhatHappens(WorkedExample example, Lesson lesson, string language)
    {
        var right = example.BrokenDoes.Described(language);

        // Things related programs really do - the fixed one, the near-misses, the lesson's other examples - that this one does not.
        var others = new[] { example.FixedDoes }
            .Concat(example.WrongFixes.Select(wrong => wrong.Does))
            .Concat(lesson.Examples.Where(other => other != example).SelectMany(other => new[] { other.BrokenDoes, other.FixedDoes }))
            .Select(behaviour => behaviour.Described(language))
            .Where(described => described != right)
            .Distinct()
            .Take(3)
            .ToList();

        if (others.Count == 0) return null;

        var because = $"Running it shows it: {Sentence(right)} {example.WhatChanged}";
        var answers = others.Select(other => new Answer(other, false, false, $"Not this one. {because}"))
            .Prepend(new Answer(right, false, true, because));

        return new Question("What happens when this program runs?", example.Broken, InFixedOrder(answers, example.Title + "|what"));
    }

    /// <summary>"Which line does it stop on?" - the line the language itself names, for a program that stops with an error.</summary>
    private static Question? WhichLine(WorkedExample example, string language)
    {
        if (example.BrokenDoes is not { StopsWith: { } error, OnLine: { } line }) return null;

        var lines = example.BrokenLines;
        if (lines.Count < 3 || line < 1 || line > lines.Count) return null;

        var who = example.BrokenDoes.BeforeRunning ? $"{language} report the mistake" : $"{language} stop";
        var because = $"{Sentence(example.BrokenDoes.Described(language))} {example.WhatChanged}";

        var answers = lines
            .Select((code, index) => (Code: code.Trim(), Number: index + 1))
            .Where(each => each.Code.Length > 0 && !IsOnlyABracket(each.Code))
            .Select(each => new Answer($"Line {each.Number}: {each.Code}", false, each.Number == line,
                each.Number == line ? because : $"Not line {each.Number}. {because}"))
            .ToList();

        return answers.Count(answer => answer.IsRight) == 1 && answers.Count >= 2
            ? new Question($"Which line does {who} on, with {error}?", example.Broken, answers)
            : null;
    }

    /// <summary>"Which change fixes it?" - the fixed program beside changes that look like fixes, each run to see what it does.</summary>
    private static Question? WhichFix(WorkedExample example, string language)
    {
        if (example.WrongFixes.Count == 0) return null;

        var right = new Answer(example.Fixed, true, true, $"{example.WhatChanged} {Sentence(example.FixedDoes.Described(language))}");
        var wrong = example.WrongFixes.Select(fix => new Answer(fix.Code, true, false, $"{fix.WhyNot} {Sentence(fix.Does.Described(language))}"));

        return new Question($"The program above has a mistake: {Lowered(example.BrokenDoes.Described(language))}. Which of these puts it right?",
            example.Broken, InFixedOrder(wrong.Prepend(right), example.Title + "|fix"));
    }

    /// <summary>The answers in an order of their own for each question - the same every time - so the right one is not always first.</summary>
    private static IReadOnlyList<Answer> InFixedOrder(IEnumerable<Answer> answers, string question) =>
        answers.OrderBy(answer => StableNumber(question + "|" + answer.Text)).ToList();

    /// <summary>A number worked out from the text alone, so the order is the same in every run of every version.</summary>
    private static uint StableNumber(string text)
    {
        var hash = 2166136261u;
        foreach (var character in text)
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return hash;
    }

    private static bool IsOnlyABracket(string code) => code.All(character => character is '{' or '}' or '(' or ')' or ';' or ' ');

    private static string Sentence(string described) => described.EndsWith('.') || described.Contains('\n') ? described : described + ".";

    private static string Lowered(string described) => described.Length == 0 ? described : char.ToLowerInvariant(described[0]) + described[1..];
}
