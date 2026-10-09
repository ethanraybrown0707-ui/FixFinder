using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// Code pasted in rather than chosen as a file. It is saved as a file of its own, in a folder of its own, named as its
/// language needs - a Java class in the file of its name, under the folders of its package - and checked as any program
/// is. Saved exactly as pasted, so a line the report names is that line of the pasted code.
/// </summary>
public static partial class PastedCode
{
    /// <summary>The folder pasted code is saved in, for this run of FixFinder: one of its own in the temp folder.</summary>
    public static string NewFolder() => Path.Combine(Path.GetTempPath(), "FixFinder-pasted", Guid.NewGuid().ToString("N")[..12]);

    [GeneratedRegex(@"^\s*package\s+(?<name>[A-Za-z_][\w]*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*;", RegexOptions.Multiline)]
    private static partial Regex JavaPackage();

    /// <summary>A class, interface, enum or record declared in Java, and whether it is public.</summary>
    [GeneratedRegex(@"^[ \t]*(?<public>public\s+)?(?:(?:abstract|final|sealed|non-sealed|strictfp|static)\s+)*(?:class|interface|enum|record)\s+(?<name>[A-Za-z_$][\w$]*)", RegexOptions.Multiline)]
    private static partial Regex JavaTypeDeclaration();

    [GeneratedRegex(@"\bstatic\s+(?:public\s+)?void\s+main\s*\(|\bvoid\s+main\s*\(\s*\)")]
    private static partial Regex JavaMain();

    /// <summary>
    /// Saves the code as the file its language needs, in the folder - emptied first, so nothing pasted before is left to
    /// be read with it - and says where.
    /// </summary>
    public static string Save(string code, CodeLanguage language, string folder)
    {
        if (language.IsAny) throw new ArgumentException("Pasted code is saved as one language or another.", nameof(language));

        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);

        var file = Path.Combine(folder, RelativePathFor(code, language));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, code, new UTF8Encoding(false));
        return file;
    }

    /// <summary>
    /// Where in its folder the code is saved, and as what: pasted.py, pasted.c and so on - except Java, whose file has to
    /// be named for its class, the one with main if it has one, and sit under the folders of its package, in src.
    /// </summary>
    public static string RelativePathFor(string code, CodeLanguage language)
    {
        if (language != CodeLanguage.Java) return "pasted" + language.Extensions[0];

        var types = JavaTypeDeclaration().Matches(code).Cast<Match>().ToList();

        // The public class has to be in the file of its name; failing that, the class with main is the one java is told to run.
        var named = types.FirstOrDefault(type => type.Groups["public"].Success)
                    ?? types.FirstOrDefault(type => JavaMain().IsMatch(BodyAfter(code, type.Index)))
                    ?? types.FirstOrDefault();

        var fileName = (named?.Groups["name"].Value ?? "Main") + ".java";

        return JavaPackage().Match(code) is { Success: true } package
            ? Path.Combine(["src", .. Regex.Replace(package.Groups["name"].Value, @"\s+", "").Split('.'), fileName])
            : fileName;
    }

    /// <summary>The code from a type's declaration up to the next one: near enough to say whether main is in it.</summary>
    private static string BodyAfter(string code, int start)
    {
        var next = JavaTypeDeclaration().Match(code, Math.Min(code.Length, start + 1));
        return code[start..(next.Success ? next.Index : code.Length)];
    }

    /// <summary>What marks code as each language, each mark one only that language writes.</summary>
    private static readonly (CodeLanguage Language, Regex[] Marks)[] LanguageMarks =
    [
        (CodeLanguage.Java, [
            new(@"\bpublic\s+static\s+void\s+main\s*\(\s*(?:final\s+)?String"), new(@"\bSystem\.(?:out|err)\.print"),
            new(@"^\s*import\s+java(?:x)?\.[\w.*]+\s*;", RegexOptions.Multiline), new(@"\bnew\s+Scanner\s*\(\s*System\.in\s*\)")]),
        (CodeLanguage.CSharp, [
            new(@"\bConsole\.(?:Write|WriteLine|ReadLine|Read)\s*\("), new(@"^\s*using\s+System(?:\.[\w.]+)?\s*;", RegexOptions.Multiline),
            new(@"\bstatic\s+(?:async\s+)?(?:void|int|Task)\s+Main\s*\(")]),
        (CodeLanguage.Python, [
            new(@"^\s*def\s+\w+\s*\([^)]*\)\s*(?:->\s*[^:]+)?:\s*(?:#.*)?$", RegexOptions.Multiline),
            new(@"^\s*if\s+__name__\s*==\s*['""]__main__['""]\s*:", RegexOptions.Multiline),
            new(@"^\s*print\s*\(.*\)\s*$", RegexOptions.Multiline),
            new(@"^\s*(?:elif\s.*|except\b.*|else\s*):\s*$", RegexOptions.Multiline),
            new(@"^\s*(?:from\s+[\w.]+\s+)?import\s+[\w.]+(?:\s+as\s+\w+)?(?:\s*,\s*[\w.]+(?:\s+as\s+\w+)?)*\s*$", RegexOptions.Multiline)]),
        (CodeLanguage.Cpp, [
            new(@"^\s*#\s*include\s*<(?:iostream|vector|string|map|unordered_map|algorithm|memory|fstream|sstream)>", RegexOptions.Multiline),
            new(@"\bstd::"), new(@"\b(?:cout|cin|cerr)\s*(?:<<|>>)"), new(@"^\s*using\s+namespace\s+std\s*;", RegexOptions.Multiline)]),
        (CodeLanguage.C, [
            new(@"^\s*#\s*include\s*<(?:stdio|stdlib|string|math|ctype|stdbool)\.h>", RegexOptions.Multiline),
            // Not as a method - Java's System.out.printf is not C's.
            new(@"(?<![.\w])(?:printf|scanf|malloc|free)\s*\(")]),
        (CodeLanguage.JavaScript, [
            new(@"\bconsole\.(?:log|error|warn)\s*\("), new(@"\brequire\s*\(\s*['""][^'""]+['""]\s*\)"),
            new(@"^\s*(?:const|let)\s+\w+\s*=", RegexOptions.Multiline), new(@"\bfunction\s+\w+\s*\([^)]*\)\s*\{")]),
        (CodeLanguage.Go, [
            new(@"^\s*package\s+main\s*$", RegexOptions.Multiline), new(@"^\s*func\s+\w+\s*\(", RegexOptions.Multiline),
            new(@"\bfmt\.(?:Print|Sprint|Fprint|Scan)")]),
        (CodeLanguage.Scala, [
            new(@"\bdef\s+main\s*\(\s*\w+\s*:\s*Array\s*\[\s*String\s*\]\s*\)"), new(@"^\s*@main\s+def\b", RegexOptions.Multiline),
            new(@"^\s*object\s+\w+\s+extends\s+App\b", RegexOptions.Multiline), new(@"^\s*(?:lazy\s+)?val\s+\w+(?:\s*:\s*[^=\n]+)?\s*=", RegexOptions.Multiline),
            new(@"^\s*import\s+scala\.", RegexOptions.Multiline)]),
        (CodeLanguage.OCaml, [
            new(@"^\s*let\s+\(\)\s*=", RegexOptions.Multiline), new(@"^\s*let\s+rec\s+\w+", RegexOptions.Multiline),
            new(@"\bprint_(?:endline|string|int|float|newline)\b"), new(@"\bPrintf\.printf\b"),
            new(@"\bmatch\b[^\n]*\bwith\s*$", RegexOptions.Multiline), new(@";;\s*$", RegexOptions.Multiline)]),
    ];

    /// <summary>
    /// The language code looks to be written in, from marks only one language writes - or null when it has none, or as many
    /// of another's: then the person says which, rather than FixFinder guessing. C++ is taken over C when it has a mark of
    /// its own, since most C is C++ too.
    /// </summary>
    public static CodeLanguage? LanguageOf(string code)
    {
        var counted = LanguageMarks
            .Select(entry => (entry.Language, Count: entry.Marks.Count(mark => mark.IsMatch(code))))
            .Where(entry => entry.Count > 0)
            .ToList();

        if (counted.Any(entry => entry.Language == CodeLanguage.Cpp)) counted.RemoveAll(entry => entry.Language == CodeLanguage.C);
        if (counted.Count == 0) return null;

        var most = counted.Max(entry => entry.Count);
        var leaders = counted.Where(entry => entry.Count == most).ToList();

        return leaders.Count == 1 ? leaders[0].Language : null;
    }
}
