using System.Text;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Analysis.Diffing;

/// <summary>What one proposed fix does to the program's behaviour, found by reading the file before and after it.</summary>
public static class FixDiffs
{
    public static bool Supports(string file) =>
        Path.GetExtension(file).ToLowerInvariant() is ".py" or ".java" or ".cs" or ".go" or ".js" or ".mjs" or ".c" or ".cpp" or ".cc" or ".h" or ".hpp";

    /// <summary>The fix's effect in sentences, or null when the file cannot be read in both versions.</summary>
    public static async Task<IReadOnlyList<string>?> DescribeAsync(LocalFix fix, string? python, CancellationToken cancellationToken)
    {
        if (!Supports(fix.File) || SourceFile.Read(fix.File) is not { } source || fix.ApplyTo(source) is not { } changed) return null;

        var folder = Path.Combine(Path.GetTempPath(), "FixFinder-diff", Guid.NewGuid().ToString("N")[..12]);

        try
        {
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileName(fix.File));
            await File.WriteAllTextAsync(copy, string.Join("\n", changed) + "\n", new UTF8Encoding(false), cancellationToken);

            var before = await ReadAsync(fix.File, python, cancellationToken);
            var after = await ReadAsync(copy, python, cancellationToken);
            if (before is null || after is null || before.Problems.Count > 0 || after.Problems.Count > 0) return null;

            var shift = fix.NewLines.Count - fix.RemoveCount;
            var end = fix.StartLine + fix.RemoveCount;
            return SemanticDiff.Describe(SemanticDiff.Compare(before, after, line => line >= end ? line + shift : line));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static async Task<IrProgram?> ReadAsync(string file, string? python, CancellationToken cancellationToken) =>
        Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".py" when python is not null => await PythonFrontend.ReadAsync([file], python, cancellationToken),
            ".java" when JavaFrontend.FindTools() is { } tools => await JavaFrontend.ReadAsync([file], tools.Javac, tools.Java, cancellationToken),
            ".cs" => await CSharpFrontend.ReadAsync([file], cancellationToken),
            ".go" when GoFrontend.FindGo() is { } go => await GoFrontend.ReadAsync([file], go, cancellationToken),
            ".js" or ".mjs" => await JavaScriptFrontend.ReadAsync([file], cancellationToken),
            ".c" or ".cpp" or ".cc" or ".h" or ".hpp" => await CFrontend.ReadAsync([file], cancellationToken),
            _ => null,
        };
}
