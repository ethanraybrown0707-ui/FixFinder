using System.Globalization;
using FixFinder.Core.Analysis.Ir;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FixFinder.Core.Analysis.Frontends;

/// <summary>Reads C# files into the IR with Roslyn, the C# compiler's own parser.</summary>
public static class CSharpFrontend
{
    private const int MostProblemsListed = 3;

    private static readonly CSharpParseOptions Options = new(LanguageVersion.Preview);

    public static async Task<IrProgram> ReadAsync(IReadOnlyList<string> files, CancellationToken cancellationToken = default)
    {
        var classes = new List<IrClass>();
        var functions = new List<IrFunction>();
        var problems = new List<string>();

        foreach (var file in files)
        {
            var tree = CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(file, cancellationToken), Options, file, cancellationToken: cancellationToken);

            var errors = tree.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                problems.Add($"{Path.GetFileName(file)}: " + string.Join("; ", errors.Take(MostProblemsListed)
                    .Select(e => $"line {e.Location.GetLineSpan().StartLinePosition.Line + 1}: {e.GetMessage(CultureInfo.InvariantCulture)}")));
                continue;
            }

            var (unitFunctions, unitClasses) = new CSharpSyntaxReader(file).ReadUnit((CompilationUnitSyntax)await tree.GetRootAsync(cancellationToken));
            functions.AddRange(unitFunctions);
            classes.AddRange(unitClasses);
        }

        return new IrProgram(SourceLanguage.CSharp, files, classes, functions, problems);
    }
}
