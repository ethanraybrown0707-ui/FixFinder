using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Execution;

/// <summary>
/// The files an OCaml program is built from, in the order OCaml needs them: every file is a module named after it -
/// marks.ml is Marks - and a module has to be compiled after the modules it uses, with its interface (.mli) before it.
/// </summary>
/// <remarks>
/// The program is the chosen file and the .ml files beside it that its code uses - by <c>Helper.total</c>, <c>open
/// Helper</c>, <c>include Helper</c> or <c>module H = Helper</c> - and the files those use, and so on. A file nothing
/// reaches is another program, such as another exercise. A dune project is built the same way, from the files beside the
/// chosen one: FixFinder never runs a build tool, and a library its dune file names from another folder is not built with it.
/// </remarks>
public static partial class OCamlProgram
{
    /// <summary>The most files one program is built from.</summary>
    public const int MostFiles = 200;

    /// <param name="Files">The program's files in the order they are compiled: each interface before its module, each module after those it uses.</param>
    /// <param name="Libraries">The libraries that come with OCaml that its code uses - str, unix - which are named when it is linked.</param>
    /// <param name="DuneFile">The dune file beside it, when it has one, which is not what builds it here.</param>
    public sealed record Layout(IReadOnlyList<string> Files, IReadOnlyList<string> Libraries, string? DuneFile);

    /// <summary>A module named in the code: <c>Helper.total</c>, <c>open Helper</c>, <c>include Helper</c>, <c>module H = Helper</c>.</summary>
    [GeneratedRegex(@"(?<![\w.'])(?<module>[A-Z][\w']*)\s*\.|\b(?:open!?|include)\s+(?<module>[A-Z][\w']*)|\bmodule\s+[A-Z][\w']*\s*=\s*(?<module>[A-Z][\w']*)")]
    private static partial Regex ModuleNamed();

    /// <summary>The libraries that come with OCaml and are linked only when named: what their modules are called in code.</summary>
    private static readonly (string Module, string Library)[] DistributedLibraries = [("Str", "str"), ("Unix", "unix")];

    public static bool IsOCaml(string file) => Path.GetExtension(file).Equals(".ml", StringComparison.OrdinalIgnoreCase);

    /// <summary>The module a file is: its name, with its first letter a capital.</summary>
    public static string ModuleOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static Layout Of(string chosen)
    {
        var file = Path.GetFullPath(chosen);
        var folder = Path.GetDirectoryName(file)!;

        var beside = ModulesBeside(file);
        var uses = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var reached = new List<string> { file };

        for (var next = 0; next < reached.Count && reached.Count < MostFiles; next++)
        {
            var used = ModulesUsedIn(reached[next]);
            uses[reached[next]] = used;

            foreach (var module in used)
            {
                if (beside.TryGetValue(module, out var other) && !reached.Contains(other, StringComparer.OrdinalIgnoreCase)) reached.Add(other);
            }
        }

        var ordered = InCompileOrder(reached, uses, beside);
        var withInterfaces = ordered.SelectMany(module => InterfaceOf(module) is { } interfaceFile ? [interfaceFile, module] : new[] { module }).ToList();

        var everyModuleUsed = uses.Values.SelectMany(used => used).ToHashSet(StringComparer.Ordinal);
        var libraries = DistributedLibraries.Where(library => everyModuleUsed.Contains(library.Module) && !beside.ContainsKey(library.Module))
            .Select(library => library.Library)
            .ToList();

        var dune = Path.Combine(folder, "dune");
        return new Layout(withInterfaces, libraries, File.Exists(dune) ? dune : null);
    }

    /// <summary>The .ml files beside the chosen one, by the module each is.</summary>
    private static Dictionary<string, string> ModulesBeside(string file)
    {
        var modules = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var other in Directory.EnumerateFiles(Path.GetDirectoryName(file)!, "*.ml").Take(MostFiles))
                modules.TryAdd(ModuleOf(other), Path.GetFullPath(other));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        modules[ModuleOf(file)] = file;
        return modules;
    }

    /// <summary>Every module a file's code names, outside its strings and comments - and not the file's own.</summary>
    private static HashSet<string> ModulesUsedIn(string file)
    {
        var code = string.Join("\n", CodeText.MaskAll(TextOf(file).Split('\n'), Syntax.OCaml));
        var own = ModuleOf(file);

        return ModuleNamed().Matches(code).Select(match => match.Groups["module"].Value).Where(module => module != own).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The modules in an order where each comes after the ones it uses - the chosen file last of all, as it uses the rest.</summary>
    private static List<string> InCompileOrder(IReadOnlyList<string> modules, Dictionary<string, HashSet<string>> uses, Dictionary<string, string> beside)
    {
        var ordered = new List<string>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Visit(string module)
        {
            if (ordered.Contains(module, StringComparer.OrdinalIgnoreCase) || !visiting.Add(module)) return;

            foreach (var used in uses.GetValueOrDefault(module) ?? [])
            {
                if (beside.TryGetValue(used, out var file) && modules.Contains(file, StringComparer.OrdinalIgnoreCase)) Visit(file);
            }

            ordered.Add(module);
        }

        foreach (var module in modules) Visit(module);
        return ordered;
    }

    /// <summary>The module's interface - marks.mli beside marks.ml - when it has one.</summary>
    private static string? InterfaceOf(string module)
    {
        var interfaceFile = Path.ChangeExtension(module, ".mli");
        return File.Exists(interfaceFile) ? interfaceFile : null;
    }

    private static string TextOf(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }
}
