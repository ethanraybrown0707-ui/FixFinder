namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>One program a Makefile or CMakeLists.txt builds, as it says to build it.</summary>
/// <param name="Name">The program's name in the build file: the target, or what -o names.</param>
/// <param name="Sources">Its C or C++ files, in the build file's order, each a full path.</param>
/// <param name="IncludeFolders">The folders its headers are looked for in, full paths of folders that are there.</param>
/// <param name="Definitions">What it defines for the preprocessor: NAME or NAME=value.</param>
/// <param name="Libraries">The libraries it links by name: m for the maths library.</param>
/// <param name="LibraryFolders">The folders those libraries are looked for in.</param>
/// <param name="LinkedFiles">Libraries and object files it links by their file, which are there already.</param>
/// <param name="Threads">Whether it is built with -pthread.</param>
/// <param name="Standards">The -std= each of its sources is compiled with, where one is given - gnu11, c++20.</param>
/// <param name="MadeByDefault">Whether building without naming anything - make, or CMake's all - builds it.</param>
/// <param name="IsATest">Whether the build file runs it as a test.</param>
/// <param name="NotFollowed">What the build file does for it that FixFinder could not do without running something.</param>
/// <param name="CannotBeBuilt">Why it cannot be built the way the build file says, when it cannot.</param>
internal sealed record BuiltProgram(
    string Name,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> IncludeFolders,
    IReadOnlyList<string> Definitions,
    IReadOnlyList<string> Libraries,
    IReadOnlyList<string> LibraryFolders,
    IReadOnlyList<string> LinkedFiles,
    bool Threads,
    IReadOnlyDictionary<string, string> Standards,
    bool MadeByDefault,
    bool IsATest,
    IReadOnlyList<string> NotFollowed,
    string? CannotBeBuilt)
{
    public bool Builds(string source) => Sources.Contains(Path.GetFullPath(source), StringComparer.OrdinalIgnoreCase);
}
