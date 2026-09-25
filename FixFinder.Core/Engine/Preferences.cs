using System.Text.Json;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Engine;

/// <summary>Which colours the window uses, and whether that is a choice or whatever Windows is set to.</summary>
public enum AppearanceChoice
{
    /// <summary>Follow whatever Windows is set to, and what FixFinder does when nobody has chosen.</summary>
    System,

    Light,

    Dark,
}

/// <summary>
/// The few choices that belong to the person rather than to any one run, kept between sessions.
/// </summary>
/// <remarks>
/// Deliberately not where credentials go: those are encrypted for this user by <c>TokenStore</c> and a plain JSON file
/// is the wrong place for them. What is here is a preference - readable, and harmless to lose. A file that cannot be
/// read is treated as one that was never written, because refusing to open the window over a malformed preference
/// would be a poor trade.
/// </remarks>
public sealed class Preferences
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FixFinder", "preferences.json");

    /// <summary>How much the reader wants explained. Student is the middle, and what is used when nobody has chosen.</summary>
    public ExplanationLevel Explanations { get; set; } = ExplanationLevel.Student;

    /// <summary>Light, dark, or whatever Windows is set to - which is the one chosen for anybody who has not.</summary>
    public AppearanceChoice Appearance { get; set; } = AppearanceChoice.System;

    public static Preferences Load(string? path = null)
    {
        var file = path ?? FilePath;

        try
        {
            if (!File.Exists(file)) return new Preferences();

            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file), JsonOptions.Default) ?? new Preferences();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Preferences();
        }
    }

    /// <summary>Writes the preferences, and says whether it managed to - saving one is never worth an error dialog.</summary>
    public bool Save(string? path = null)
    {
        var file = path ?? FilePath;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions.Default));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
