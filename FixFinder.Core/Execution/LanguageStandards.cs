using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// The standard of C or C++ a program is built to: the one its Makefile or CMakeLists.txt gives it, or - with none -
/// the compiler's own for C and C++17 for C++, as the compiler flags that hold gcc, clang and MSVC to it.
/// </summary>
/// <remarks>
/// FixFinder does not keep its own list of which feature arrived in which standard: it tells the compiler, and the
/// compiler knows. Every fix is compiled before it is offered, so a fix that needs a later standard than the program is
/// built to fails that check and is never shown. Which version of every other language a program is written for is
/// worked out from its project and its code, by each language's own setup.
/// </remarks>
public static partial class LanguageStandards
{
    /// <summary>The C++ standard a program is built with when its build file names none.</summary>
    public const string UsualCpp = "c++17";

    /// <summary>A standard as gcc and clang name one with -std=, and nothing else: c11, gnu99, c++20, gnu++2b, iso9899:1999.</summary>
    [GeneratedRegex(@"^(?:(?:c|gnu)(?:89|90|99|9x|11|1x|17|18|2x|23)|iso9899:(?:1990|199409|1999|199x|2011|2017|2018|2024))$")]
    private static partial Regex GnuCStandard();

    [GeneratedRegex(@"^(?:c|gnu)\+\+(?:98|03|0x|11|1y|14|1z|17|2a|20|2b|23|2c|26)$")]
    private static partial Regex GnuCppStandard();

    /// <summary>Whether a standard a build file names is one gcc takes for the language - the only kind put on a compiler's command line.</summary>
    public static bool IsGnuStandard(string? standard, bool cpp) => standard is not null && (cpp ? GnuCppStandard() : GnuCStandard()).IsMatch(standard);

    /// <summary>
    /// The standard flag for gcc and clang, with the trailing space the flags it is joined to expect: the one the program's
    /// build file gives, or, with none, none for C - the compiler's own - and C++17 for C++.
    /// </summary>
    public static string Gnu(bool cpp, string? projectStandard = null) => Standard(cpp, projectStandard) is { Length: > 0 } standard ? $"-std={GnuName(standard)} " : "";

    /// <summary>
    /// The standard flag for MSVC. It accepts fewer standards than gcc - C11 and C17 for C, C++14 onwards for C++ - so a
    /// standard it has no flag for leaves it on its own default.
    /// </summary>
    public static string Msvc(bool cpp, string? projectStandard = null)
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

    private static string Standard(bool cpp, string? projectStandard) =>
        IsGnuStandard(projectStandard, cpp) ? projectStandard! : cpp ? UsualCpp : "";

    /// <summary>C23 as c2x, and its GNU dialect as gnu2x: gcc 9 to 13 know them by no other names, and gcc 14 and later still take them.</summary>
    private static string GnuName(string standard) => standard switch
    {
        "c23" => "c2x",
        "gnu23" => "gnu2x",
        _ => standard,
    };
}
