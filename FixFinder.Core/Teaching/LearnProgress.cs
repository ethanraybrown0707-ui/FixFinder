using System.Text.Json;
using FixFinder.Core.Engine;

namespace FixFinder.Core.Teaching;

/// <summary>
/// What a learner has done in FixFinder Learn: for each lesson, which of its questions they have answered right. Kept on
/// this computer, for this account, as a small JSON file; losing it loses nothing but the ticks.
/// </summary>
public sealed class LearnProgress
{
    public static string DefaultFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FixFinder", "learn", "progress.json");

    /// <summary>For each lesson - "DIVZERO|Python" - the questions answered right, each by its question's words.</summary>
    public Dictionary<string, HashSet<string>> AnsweredRight { get; set; } = [];

    /// <summary>The key a lesson's progress is kept under: its concept's code and its language.</summary>
    public static string KeyOf(Lesson lesson) => $"{lesson.Concept.Code}|{lesson.Language.Name}";

    /// <summary>The key a question is known by within its lesson: what it asks and the code it asks about.</summary>
    public static string KeyOf(Question question) => $"{question.Asks}|{question.Code}";

    /// <summary>Notes that a question was answered right.</summary>
    public void AnsweredRightly(Lesson lesson, Question question)
    {
        if (!AnsweredRight.TryGetValue(KeyOf(lesson), out var questions)) AnsweredRight[KeyOf(lesson)] = questions = [];
        questions.Add(KeyOf(question));
    }

    /// <summary>How many of a lesson's questions have been answered right.</summary>
    public int RightIn(Lesson lesson) =>
        AnsweredRight.TryGetValue(KeyOf(lesson), out var questions) ? lesson.Questions.Count(question => questions.Contains(KeyOf(question))) : 0;

    /// <summary>Whether every question of a lesson has been answered right.</summary>
    public bool Finished(Lesson lesson) => lesson.Questions.Count > 0 && RightIn(lesson) == lesson.Questions.Count;

    public static LearnProgress Load(string? file = null)
    {
        try
        {
            var path = file ?? DefaultFile;
            return File.Exists(path) ? JsonSerializer.Deserialize<LearnProgress>(File.ReadAllText(path), JsonOptions.Default) ?? new LearnProgress() : new LearnProgress();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LearnProgress();
        }
    }

    /// <summary>Writes the progress down, and says whether it managed to - failing to is not worth interrupting a lesson for.</summary>
    public bool Save(string? file = null)
    {
        try
        {
            var path = file ?? DefaultFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions.Default));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
