using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Which version of each language a program is written to: for C, C++ and Java the compiler flags that hold the compiler
/// to it, and for Python, Go and JavaScript the release of the toolchain that runs it.
/// </summary>
/// <remarks>
/// A course that says "Java 8" or "C99" makes any fix using a later feature simply wrong for the student who follows it.
/// FixFinder does not keep its own list of which feature arrived in which version: it tells the compiler, and the
/// compiler knows. Every fix is compiled before it is offered, so under Java 8 a fix written with <c>var</c> fails that
/// check and is never shown - the version is enforced by the one thing that is certainly right about it.
/// <para>
/// Python, Go and JavaScript have no flag that holds them to an older release, so for them a version chosen here is the
/// toolchain itself: a Python, Go or Node.js of that release on this computer runs the program, checks its fixes and
/// tries them. With none of it here, the program is not run with another one instead - that would be a promise nothing
/// keeps - and how to install it is said.
/// </para>
/// </remarks>
public sealed partial record LanguageStandards
{
    public static readonly string[] CChoices = ["", "c89", "c99", "c11", "c17", "c23"];
    public static readonly string[] CppChoices = ["", "c++11", "c++14", "c++17", "c++20", "c++23"];
    /// <summary>
    /// Java's releases with long-term support up to 21, and every release since: each is a release a course may be taught
    /// in, and javac from 21 to 27 compiles for any of them up to its own.
    /// </summary>
    public static readonly string[] JavaChoices = ["", "8", "11", "17", "21", "22", "23", "24", "25", "26", "27"];

    /// <summary>Python's releases from 3.8 to 3.14, each a release a course may be taught in.</summary>
    public static readonly string[] PythonChoices = ["", "3.8", "3.9", "3.10", "3.11", "3.12", "3.13", "3.14"];

    /// <summary>Go's releases from 1.21 - since when every Go holds to a module's go line - to 1.27.</summary>
    public static readonly string[] GoChoices = ["", "1.21", "1.22", "1.23", "1.24", "1.25", "1.26", "1.27"];

    /// <summary>Node.js's even-numbered releases from 18 to 26: the ones that are given long-term support.</summary>
    public static readonly string[] NodeChoices = ["", "18", "20", "22", "24", "26"];

    /// <summary>What the compilers have always been given, so nothing changes until somebody chooses otherwise.</summary>
    public static readonly LanguageStandards Default = new();

    /// <summary>The C++ standard a program is built with when nobody has chosen one and its build file names none.</summary>
    public const string UsualCpp = "c++17";

    /// <summary>The C standard, or empty for the one the program's Makefile or CMakeLists.txt gives - or, when it gives none, the compiler's own.</summary>
    public string C { get; init; } = "";

    /// <summary>The C++ standard, or empty for the one the program's Makefile or CMakeLists.txt gives - or, when it gives none, C++17.</summary>
    public string Cpp { get; init; } = "";

    /// <summary>
    /// The Java release, or empty to detect it: the one the program's project names - or, when it names none, the JDK's own,
    /// with a JDK of the Java its code needs and the preview features it uses turned on.
    /// </summary>
    public string Java { get; init; } = "";

    /// <summary>
    /// The Python release, or empty to detect it: the one the program's project asks for and its code needs, from the
    /// Pythons on this computer.
    /// </summary>
    public string Python { get; init; } = "";

    /// <summary>The Go release, or empty to detect it: the one the program's go.mod asks for and its code needs.</summary>
    public string Go { get; init; } = "";

    /// <summary>The Node.js release, or empty to detect it: the one the program's project files ask for and its code needs.</summary>
    public string Node { get; init; } = "";

    /// <summary>The Python release chosen - 3.12 - or null when it is to be detected.</summary>
    public Versions.LanguageVersion? PythonRelease => Release(Python, PythonChoices);

    /// <summary>The Go release chosen - 1.24 - or null when it is to be detected.</summary>
    public Versions.LanguageVersion? GoRelease => Release(Go, GoChoices);

    /// <summary>The Node.js release chosen - 22 - or null when it is to be detected.</summary>
    public Versions.LanguageVersion? NodeRelease => Release(Node, NodeChoices);

    private static Versions.LanguageVersion? Release(string value, string[] choices) =>
        Allowed(value, choices, "") is { Length: > 0 } chosen ? Versions.LanguageVersion.Find(chosen) : null;

    /// <summary>
    /// The standards every build and every fix check uses. One setting for the whole application, because it belongs
    /// to the person and the course rather than to any one program.
    /// </summary>
    public static LanguageStandards Current { get; set; } = Default;

    /// <summary>A standard as gcc and clang name one with -std=, and nothing else: c11, gnu99, c++20, gnu++2b, iso9899:1999.</summary>
    [GeneratedRegex(@"^(?:(?:c|gnu)(?:89|90|99|9x|11|1x|17|18|2x|23)|iso9899:(?:1990|199409|1999|199x|2011|2017|2018|2024))$")]
    private static partial Regex GnuCStandard();

    [GeneratedRegex(@"^(?:c|gnu)\+\+(?:98|03|0x|11|1y|14|1z|17|2a|20|2b|23|2c|26)$")]
    private static partial Regex GnuCppStandard();

    /// <summary>Whether a standard a build file names is one gcc takes for the language - the only kind put on a compiler's command line.</summary>
    public static bool IsGnuStandard(string? standard, bool cpp) => standard is not null && (cpp ? GnuCppStandard() : GnuCStandard()).IsMatch(standard);

    /// <summary>Whether a standard is chosen in Settings - which then holds over the one a program's build file gives it.</summary>
    public bool Chooses(bool cpp) => Chosen(cpp).Length > 0;

    /// <summary>
    /// The standard flag for gcc and clang, with the trailing space the flags it is joined to expect: the standard chosen
    /// in Settings; or, with none chosen, the one the program's build file gives; or, with neither, none for C - the
    /// compiler's own - and C++17 for C++.
    /// </summary>
    public string Gnu(bool cpp, string? projectStandard = null) => Standard(cpp, projectStandard) is { Length: > 0 } standard ? $"-std={GnuName(standard)} " : "";

    /// <summary>
    /// The standard flag for MSVC. It accepts fewer standards than gcc - C11 and C17 for C, C++14 onwards for C++ - so
    /// a standard it has no flag for leaves it on its own default, which the settings say rather than hide.
    /// </summary>
    public string Msvc(bool cpp, string? projectStandard = null)
    {
        // MSVC has no GNU dialects: gnu11 is C11 to it, and the older names gcc keeps are the standards they stand for.
        var standard = Standard(cpp, projectStandard).Replace("gnu", "c", StringComparison.Ordinal) switch
        {
            "c1x" => "c11",
            "c18" or "iso9899:2017" or "iso9899:2018" => "c17",
            "iso9899:2011" => "c11",
            "c++1y" => "c++14",
            "c++1z" => "c++17",
            "c++2a" => "c++20",
            "c++2b" or "c++2c" or "c++26" => "c++23",
            var named => named,
        };

        return standard switch
        {
            "c11" => " /std:c11",
            "c17" => " /std:c17",
            "c++14" => " /std:c++14",
            "c++17" => " /std:c++17",
            "c++20" => " /std:c++20",
            "c++23" => " /std:c++latest",
            _ => "",
        };
    }

    private string Chosen(bool cpp) => cpp ? Allowed(Cpp, CppChoices, Default.Cpp) : Allowed(C, CChoices, Default.C);

    private string Standard(bool cpp, string? projectStandard) =>
        Chosen(cpp) is { Length: > 0 } chosen ? chosen
        : IsGnuStandard(projectStandard, cpp) ? projectStandard!
        : cpp ? UsualCpp : "";

    /// <summary>C23 as c2x, and its GNU dialect as gnu2x: gcc 9 to 13 know them by no other names, and gcc 14 and later still take them.</summary>
    private static string GnuName(string standard) => standard switch
    {
        "c23" => "c2x",
        "gnu23" => "gnu2x",
        _ => standard,
    };

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
