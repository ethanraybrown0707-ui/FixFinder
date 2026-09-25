namespace FixFinder.Core.Execution;

/// <summary>
/// Which version of C, C++ and Java a program is written to, and the compiler flags that hold the compiler to it.
/// </summary>
/// <remarks>
/// A course that says "Java 8" or "C99" makes any fix using a later feature simply wrong for the student who follows it.
/// FixFinder does not keep its own list of which feature arrived in which version: it tells the compiler, and the
/// compiler knows. Every fix is compiled before it is offered, so under Java 8 a fix written with <c>var</c> fails that
/// check and is never shown - the version is enforced by the one thing that is certainly right about it.
/// <para>
/// Only these three, because only for these does a compiler flag decide the version. Python, JavaScript and Go run on
/// whichever interpreter or toolchain is installed, and choosing a version for them here would be a promise nothing
/// keeps.
/// </para>
/// </remarks>
public sealed record LanguageStandards
{
    public static readonly string[] CChoices = ["", "c89", "c99", "c11", "c17", "c23"];
    public static readonly string[] CppChoices = ["c++11", "c++14", "c++17", "c++20", "c++23"];
    public static readonly string[] JavaChoices = ["", "8", "11", "17", "21"];

    /// <summary>What the compilers have always been given, so nothing changes until somebody chooses otherwise.</summary>
    public static readonly LanguageStandards Default = new();

    /// <summary>The C standard, or empty for whatever the installed compiler defaults to.</summary>
    public string C { get; init; } = "";

    public string Cpp { get; init; } = "c++17";

    /// <summary>The Java release, or empty for whatever the installed JDK is.</summary>
    public string Java { get; init; } = "";

    /// <summary>
    /// The standards every build and every fix check uses. One setting for the whole application, because it belongs
    /// to the person and the course rather than to any one program.
    /// </summary>
    public static LanguageStandards Current { get; set; } = Default;

    /// <summary>The standard flag for gcc and clang, with the trailing space the flags it is joined to expect.</summary>
    public string Gnu(bool cpp)
    {
        var chosen = cpp ? Allowed(Cpp, CppChoices, Default.Cpp) : Allowed(C, CChoices, Default.C);
        return chosen.Length > 0 ? $"-std={chosen} " : "";
    }

    /// <summary>
    /// The standard flag for MSVC. It accepts fewer standards than gcc - C11 and C17 for C, C++14 onwards for C++ - so
    /// a standard it has no flag for leaves it on its own default, which the settings say rather than hide.
    /// </summary>
    public string Msvc(bool cpp)
    {
        if (cpp)
        {
            return Allowed(Cpp, CppChoices, Default.Cpp) switch
            {
                "c++14" => " /std:c++14",
                "c++17" => " /std:c++17",
                "c++20" => " /std:c++20",
                "c++23" => " /std:c++latest",
                _ => "",
            };
        }

        return Allowed(C, CChoices, Default.C) switch
        {
            "c11" => " /std:c11",
            "c17" => " /std:c17",
            _ => "",
        };
    }

    /// <summary>The Java release chosen, or empty for the installed JDK's own.</summary>
    public string JavaReleaseNumber => Allowed(Java, JavaChoices, Default.Java);

    /// <summary>The release flag for javac, with a trailing space, or nothing when no release was chosen.</summary>
    public string JavaRelease => JavaReleaseNumber is { Length: > 0 } release ? $"--release {release} " : "";

    /// <summary>
    /// A choice only counts when it is one of the listed ones. The value comes from a preferences file anybody can edit,
    /// and it is handed to a compiler on its command line, so anything else is treated as the default rather than
    /// passed along.
    /// </summary>
    private static string Allowed(string value, string[] choices, string fallback) =>
        choices.Contains(value, StringComparer.Ordinal) ? value : fallback;
}
