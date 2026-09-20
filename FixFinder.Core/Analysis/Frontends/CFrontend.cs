using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Reads C and C++ files into the IR with FixFinder's own reader - there is no parser on this machine to ask.</summary>
public static class CFrontend
{
    public static bool IsCpp(string file) => Path.GetExtension(file).ToLowerInvariant() is ".cpp" or ".cc" or ".cxx" or ".hpp" or ".hh" or ".hxx" or ".c++";

    public static async Task<IrProgram> ReadAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default)
    {
        var cpp = files.Any(IsCpp);
        var functions = new List<IrFunction>();
        var classes = new List<IrClass>();
        var problems = new List<string>();

        foreach (var file in files)
        {
            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            var lexer = new CLexer(text, cpp);
            var tokens = lexer.Tokens();
            if (lexer.Problem is { } trouble)
            {
                problems.Add($"{Path.GetFileName(file)}: {trouble}");
                continue;
            }

            var parser = new CParser(file, tokens, cpp);
            var (read, types) = parser.Parse();

            if (parser.Problem is { } problem)
            {
                problems.Add($"{Path.GetFileName(file)}: {problem}");
                continue;
            }

            functions.AddRange(read);
            classes.AddRange(types);
        }

        return new IrProgram(cpp ? SourceLanguage.Cpp : SourceLanguage.C, files, classes, functions, problems);
    }
}
