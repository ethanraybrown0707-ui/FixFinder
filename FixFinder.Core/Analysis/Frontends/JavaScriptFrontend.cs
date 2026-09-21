using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Reads JavaScript files into the IR with FixFinder's own reader - there is no JavaScript parser to ask.</summary>
public static class JavaScriptFrontend
{
    public static async Task<IrProgram> ReadAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default)
    {
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

            var lexer = new JsLexer(text);
            var tokens = lexer.Tokens();
            if (lexer.Problem is { } trouble)
            {
                problems.Add($"{Path.GetFileName(file)}: {trouble}");
                continue;
            }

            var parser = new JsParser(file, tokens);
            var (read, types) = parser.Parse();

            if (parser.Problem is { } problem)
            {
                problems.Add($"{Path.GetFileName(file)}: {problem}");
                continue;
            }

            functions.AddRange(read);
            classes.AddRange(types);
        }

        return new IrProgram(SourceLanguage.JavaScript, files, classes, functions, problems);
    }
}
