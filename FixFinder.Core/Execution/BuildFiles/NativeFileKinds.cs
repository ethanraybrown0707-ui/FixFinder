namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>What a file named in a C or C++ build is, told by its extension the way gcc tells it.</summary>
internal static class NativeFileKinds
{
    public static bool IsC(string path) => Extension(path) == ".c";

    public static bool IsCpp(string path) => Extension(path) is ".cpp" or ".cc" or ".cxx" or ".c++";

    public static bool IsSource(string path) => IsC(path) || IsCpp(path);

    public static bool IsHeader(string path) => Extension(path) is ".h" or ".hpp" or ".hh" or ".hxx" or ".inc";

    public static bool IsObject(string path) => Extension(path) is ".o" or ".obj";

    /// <summary>A library of object files given by its file: libshapes.a, or a .lib.</summary>
    public static bool IsArchive(string path) => Extension(path) is ".a" or ".lib";

    /// <summary>Assembly, which a C build can be given too and FixFinder does not build.</summary>
    public static bool IsAssembly(string path) => Extension(path) is ".s" or ".asm";

    private static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();
}
