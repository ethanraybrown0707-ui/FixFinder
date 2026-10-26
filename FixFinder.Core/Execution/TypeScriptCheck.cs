using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FixFinder.Core.Execution;

/// <summary>
/// How a TypeScript file is checked and run: by Node.js itself, which runs TypeScript by stripping its types without checking
/// them - nothing is downloaded. The types are checked by TypeScript's own compiler, as TypeScriptCompiler runs it, only when
/// this computer has one already.
/// </summary>
/// <remarks>
/// <para>
/// node --check reads a .ts file as JavaScript and calls its types syntax errors, so the check is a small script of
/// FixFinder's instead: Node's own stripTypeScriptTypes takes the types out - failing, in Node's words and at the line it
/// names, on TypeScript it cannot read, or cannot run with its types stripped, such as an enum - and what is left is
/// compiled as a module, which finds JavaScript's own errors, such as a name declared twice. None of the file runs.
/// </para>
/// <para>
/// npx tsx is not used: npm installs a package it does not have, and with no one at a terminal to ask, it says yes by
/// itself - "When standard input is not a TTY or a CI environment is detected, --yes is assumed" - and FixFinder never
/// downloads anything.
/// </para>
/// </remarks>
public static class TypeScriptCheck
{
    /// <summary>The files Node.js runs as TypeScript.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ts", ".mts", ".cts" };

    public static bool IsTypeScript(string file) => Extensions.Contains(Path.GetExtension(file));

    private const string CheckSource = """
        const { stripTypeScriptTypes } = require("node:module");
        const { readFileSync } = require("node:fs");
        const { resolve } = require("node:path");
        const vm = require("node:vm");

        const file = resolve(process.argv[2]);

        if (typeof stripTypeScriptTypes !== "function") {
          console.error("FixFinder: this Node.js cannot strip TypeScript's types, so the file was not checked.");
          process.exit(0);
        }

        const code = stripTypeScriptTypes(readFileSync(file, "utf8"), { sourceUrl: file });
        new vm.SourceTextModule(code, { identifier: file });
        """;

    /// <summary>
    /// The arguments that run the check, before the file's name: Node's warnings that stripTypeScriptTypes and modules in vm
    /// are experimental left out, and the check's own file, written once where it is read from.
    /// </summary>
    public static string Arguments => $"--no-warnings --experimental-vm-modules \"{Written("fixfinder_typescript_check", CheckSource)}\"";

    /// <summary>Writes a script of FixFinder's where Node.js can read it - the temp folder - named for its own text, and says where.</summary>
    private static string Written(string name, string source)
    {
        var file = Path.Combine(Path.GetTempPath(), "FixFinder-tests",
            $"{name}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12].ToLowerInvariant()}.cjs");

        if (!File.Exists(file))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, source, new UTF8Encoding(false));
        }

        return file;
    }

    private const string StripSource = """
        const { stripTypeScriptTypes } = require("node:module");

        let code = "";
        process.stdin.setEncoding("utf8");
        process.stdin.on("data", (chunk) => (code += chunk));
        process.stdin.on("end", () => process.stdout.write(stripTypeScriptTypes(code)));
        """;

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>?> Stripped = new(StringComparer.Ordinal);

    /// <summary>
    /// A TypeScript file's lines with their types taken out by Node.js itself - each type replaced by spaces, so every line and
    /// column is where it was - as the JavaScript Node runs. Null when no Node.js here runs TypeScript, or Node cannot read
    /// the file; worked out once for each text.
    /// </summary>
    public static IReadOnlyList<string>? StrippedLines(IReadOnlyList<string> lines)
    {
        var text = string.Join("\n", lines);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

        return Stripped.GetOrAdd(key, _ => Strip(text, lines.Count));
    }

    private static IReadOnlyList<string>? Strip(string text, int lineCount)
    {
        if (TargetFactory.FindOnPath("node") is not { } node || !NodeRunsTypeScript(node)) return null;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(node, $"--no-warnings \"{Written("fixfinder_typescript_strip", StripSource)}\"")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
            });

            if (process is null) return null;

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            var stripped = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(30_000) || process.ExitCode != 0) return null;

            var strippedLines = stripped.Split('\n');
            return strippedLines.Length == lineCount ? strippedLines : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static readonly ConcurrentDictionary<string, (DateTime Written, bool RunsTypeScript)> Asked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this Node.js runs TypeScript itself - what its process.features.typescript says: "strip" or "transform" when it
    /// does, false or nothing when it does not - asked once while the program is unchanged.
    /// </summary>
    public static bool NodeRunsTypeScript(string node)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(node);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (Asked.TryGetValue(node, out var known) && known.Written == written) return known.RunsTypeScript;

        var runs = Ask(node) is "strip" or "transform";
        Asked[node] = (written, runs);
        return runs;
    }

    private static string? Ask(string node)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(node, "-p process.features.typescript")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null) return null;

            var said = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(30_000) ? said.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}
