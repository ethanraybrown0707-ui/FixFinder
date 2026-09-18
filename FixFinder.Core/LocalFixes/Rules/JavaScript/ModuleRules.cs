using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary><c>require is not defined in ES module scope</c> - CommonJS <c>require</c> in a module.</summary>
public sealed partial class JsRequireInModule : ILocalFixRule
{
    public string Id => "js-require-in-module";

    [GeneratedRegex(@"^require is not defined in ES module scope")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*)(?:const|let|var)\s+(?<binding>[A-Za-z_$][\w$]*|\{[^:}]*\})\s*=\s*require\(\s*(?<quote>[""'])(?<module>[^""']+)\k<quote>\s*\)\s*;?(?<tail>\s*)$")]
    private static partial Regex Require();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;
        if (Require().Match(at.Line) is not { Success: true } require) return null;

        var binding = require.Groups["binding"].Value;
        var module = require.Groups["module"].Value;

        return LocalFix.ReplaceLine(
            Id, $"Import {module} instead of requiring it",
            "This file is an ES module - a `.mjs` file, or one in a package that says `\"type\": \"module\"` - and modules have no " +
            "`require`. They load other modules with `import`.",
            at.Source.Path, at.Number, $"{require.Groups["lead"].Value}import {binding} from \"{module}\";{require.Groups["tail"].Value}");
    }
}

/// <summary><c>Cannot find module 'fss'</c> a letter from a core module - a typo, not a package to install.</summary>
public sealed partial class JsCoreModuleTypo : ILocalFixRule
{
    public string Id => "js-core-module-typo";

    [GeneratedRegex(@"^Cannot find module '(?<name>[a-z_][a-z0-9_/]*)'")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "Error", Message()) is not { } message) return null;

        var name = message.Groups["name"].Value;
        var builtins = NodeRuntime.BuiltinModules();
        if (builtins.Count == 0 || builtins.Contains(name)) return null;
        if (CodeText.Nearest(name, builtins.Where(b => !b.StartsWith('_'))) is not { } right) return null;

        foreach (var frame in context.Error.Frames)
        {
            if (context.Read(frame.File) is not { } source || !JavaScriptCode.IsJavaScript(source)) continue;

            var loading = new Regex($@"(?:require\(\s*|\bfrom\s+|\bimport\s+)(?<quote>[""'])(?<name>{Regex.Escape(name)})\k<quote>");
            var lines = Enumerable.Range(0, source.Count).Where(i => loading.IsMatch(source.Lines[i])).ToList();
            if (lines is not [var index]) return null;

            var hit = loading.Match(source.Lines[index]).Groups["name"];

            return LocalFix.ReplaceLine(
                Id, $"Change '{name}' to '{right}'",
                $"There is no module called `{name}`, and `{right}` - one of Node's own - is a letter away. Installing a package called " +
                $"`{name}` would fetch a stranger's code to fix a spelling mistake.",
                source.Path, index + 1, source.Lines[index][..hit.Index] + right + source.Lines[index][(hit.Index + hit.Length)..]);
        }

        return null;
    }
}

/// <summary><c>does not provide an export named 'readFileSnyc'</c> - a misspelt import from a core module.</summary>
public sealed partial class JsNamedExportTypo : ILocalFixRule
{
    public string Id => "js-named-export-typo";

    [GeneratedRegex(@"^The requested module '(?<module>[^']+)' does not provide an export named '(?<name>[\w$]+)'$")]
    private static partial Regex Message();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "SyntaxError", Message()) is not { } message || JavaScriptCode.Locate(context) is not { } at) return null;

        var name = message.Groups["name"].Value;
        var exports = NodeRuntime.Exports(message.Groups["module"].Value);
        if (CodeText.Nearest(name, exports) is not { } right) return null;

        var source = at.Source;
        var masked = CodeText.MaskAll(source.Lines, Syntax.CLike);
        var word = new Regex($@"(?<![\w$.]){Regex.Escape(name)}(?![\w$])");
        var used = Enumerable.Range(0, masked.Count).Where(i => word.IsMatch(masked[i])).ToList();

        if (used.Count == 0 || used[^1] - used[0] > 30) return null;

        var lines = Enumerable.Range(used[0], used[^1] - used[0] + 1)
            .Select(i => CCode.ReplaceEach(source.Lines[i], word.Matches(masked[i]), _ => right))
            .ToList();

        return new LocalFix
        {
            RuleId = Id,
            Title = $"Change {name} to {right}",
            Explanation =
                $"`{message.Groups["module"].Value}` has no export called `{name}`. `{right}` is the only thing it exports within a letter or two - " +
                "read from the module itself - and every use of the name is changed with the import.",
            File = source.Path,
            StartLine = used[0] + 1,
            RemoveCount = lines.Count,
            NewLines = lines,
        };
    }
}

/// <summary><c>module.exports.add is not a function</c> after <c>module.export = ...</c> - one letter short.</summary>
public sealed partial class JsModuleExportsTypo : ILocalFixRule
{
    public string Id => "js-module-exports-typo";

    [GeneratedRegex(@"^module\.exports\.[\w$]+ is not a function$")]
    private static partial Regex Message();

    [GeneratedRegex(@"^(?<lead>\s*module\s*\.\s*)export(?<rest>\s*=[^=].*)$")]
    private static partial Regex Export();

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "TypeError", Message()) is null || JavaScriptCode.Locate(context) is not { } at) return null;

        var lines = Enumerable.Range(0, at.Source.Count).Where(i => Export().IsMatch(at.Source.Lines[i])).ToList();
        if (lines is not [var index]) return null;

        var match = Export().Match(at.Source.Lines[index]);

        return LocalFix.ReplaceLine(
            Id, "Write module.exports",
            $"Line {index + 1} sets `module.export`, which is just a new property nobody reads. What a file hands to `require` is " +
            "`module.exports` - with an s.",
            at.Source.Path, index + 1, match.Groups["lead"].Value + "exports" + match.Groups["rest"].Value);
    }
}

/// <summary><c>fs is not defined</c> - a built-in module used without being loaded.</summary>
public sealed partial class JsBuiltinNotLoaded : ILocalFixRule
{
    public string Id => "js-builtin-not-loaded";

    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*) is not defined$")]
    private static partial Regex Message();

    private static readonly Dictionary<string, (string Module, bool Named)> Known = new(StringComparer.Ordinal)
    {
        ["fs"] = ("fs", false), ["path"] = ("path", false), ["os"] = ("os", false), ["http"] = ("http", false), ["https"] = ("https", false),
        ["crypto"] = ("crypto", false), ["readline"] = ("readline", false), ["util"] = ("util", false), ["events"] = ("events", false),
        ["child_process"] = ("child_process", false), ["net"] = ("net", false), ["url"] = ("url", false), ["assert"] = ("assert", false),
        ["zlib"] = ("zlib", false), ["EventEmitter"] = ("events", true), ["promisify"] = ("util", true), ["createServer"] = ("http", true),
        ["readFileSync"] = ("fs", true), ["writeFileSync"] = ("fs", true), ["existsSync"] = ("fs", true), ["execSync"] = ("child_process", true),
    };

    public LocalFix? Propose(LocalFixContext context)
    {
        if (JavaScriptCode.ErrorMessage(context.Error, "ReferenceError", Message()) is not { } message || context.Read(context.Frame?.File) is not { } source) return null;
        if (!JavaScriptCode.IsJavaScript(source)) return null;

        var name = message.Groups["name"].Value;
        if (!Known.TryGetValue(name, out var known)) return null;

        var lines = source.Lines;
        var masked = CodeText.MaskAll(lines, Syntax.CLike);
        var escapedName = Regex.Escape(name);
        var declares = new Regex($@"\b(?:(?:const|let|var)\s+(?:{escapedName}|\{{[^}}]*\b{escapedName}\b[^}}]*\}})\s*=|function\s+{escapedName}\b|class\s+{escapedName}\b|import\s+(?:{escapedName}\b|\{{[^}}]*\b{escapedName}\b[^}}]*\}}|\*\s+as\s+{escapedName}\b))");
        if (masked.Any(l => declares.IsMatch(l))) return null;

        var module = source.Path.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) || masked.Any(l => Regex.IsMatch(l, @"^\s*(?:import\s|export\s)"));

        var statement = (module, known.Named) switch
        {
            (true, false) => $"import {name} from \"node:{known.Module}\";",
            (true, true) => $"import {{ {name} }} from \"node:{known.Module}\";",
            (false, false) => $"const {name} = require(\"{known.Module}\");",
            (false, true) => name == "EventEmitter" ? "const EventEmitter = require(\"events\");" : $"const {{ {name} }} = require(\"{known.Module}\");",
        };

        var at = 0;
        while (at < lines.Count && (lines[at].StartsWith("#!", StringComparison.Ordinal) || Regex.IsMatch(lines[at], @"^\s*[""']use strict[""'];?\s*$"))) at++;
        while (at < lines.Count && Regex.IsMatch(masked[at], @"^\s*(?:(?:const|let|var)\s+[^=]+=\s*require\s*\(.*\)\s*;?|import\s.*from\s.*;?|import\s+[""'].*)\s*$")) at++;

        return LocalFix.Insert(
            Id, $"Load {name}: {statement.TrimEnd(';')}",
            $"`{name}` comes from Node's built-in `{known.Module}` module, and a module is only available in a file that loads it - " +
            $"{(module ? "with `import`, since this file is an ES module" : "with `require`")}.",
            source.Path, at + 1, [statement]);
    }
}
