using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FixFinder.Core.LocalFixes;

namespace FixFinder.Core.Execution;

/// <summary>Where a line of a notebook's code is: the notebook, its cell counted from the top, and the line in that cell.</summary>
public sealed record NotebookPlace(string Notebook, int Cell, int Line);

/// <summary>An IPython command in a notebook that FixFinder does not carry out, and that could change what the cells after it see.</summary>
public sealed record CommandNotCarriedOut(int Cell, int Line, string Command);

/// <summary>
/// A Jupyter notebook's code as one Python script, to check as a program: its code cells in order, as Jupyter's Run All
/// runs them. Each line of the script knows the cell and line it came from, so what is found in it is said of the notebook.
/// </summary>
/// <remarks>
/// <para>
/// IPython's own commands are not Python, so each becomes a line of Python in its place. Those that change what the cells
/// after them see, and can be done in Python without touching anything outside the run, are done: %cd changes the folder,
/// %env sets a variable, %run runs a script and keeps what it defines, and %time keeps the statement it times. Those that
/// only show something, or install a package - which FixFinder never does - do nothing. The rest - a shell command, a file
/// written by %%writefile - are not carried out, and are listed, so a failure that follows from one can be told from a
/// mistake in the notebook's code. A cell of another language, such as %%bash, stays as comments.
/// </para>
/// <para>
/// The script is named after the whole notebook with .py after it - analysis.ipynb.py - a name no import can reach, so a
/// notebook called pandas.ipynb that imports pandas is given the library and not itself. Two blank lines stand between one
/// cell's code and the next, so the line shown either side of a line of code is never one from the cell next to it.
/// </para>
/// </remarks>
public sealed partial class NotebookScript
{
    /// <summary>How the name of a script of a notebook's code ends: the notebook's own name, then .py.</summary>
    public const string ScriptEnding = ".ipynb.py";

    /// <summary>The blank lines put between one cell's code and the next, and after FixFinder's own lines at the top.</summary>
    private const int LinesBetweenCells = 2;

    private static readonly ConcurrentDictionary<string, NotebookScript> Written = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cell commands whose cell is Python all the same: they time it, profile it, or catch what it prints.</summary>
    private static readonly HashSet<string> PythonCellCommands = new(StringComparer.Ordinal) { "time", "timeit", "capture", "prun" };

    /// <summary>Cell commands whose cell only shows something - a page, a formula - which no later cell reads.</summary>
    private static readonly HashSet<string> ShowingCellCommands = new(StringComparer.Ordinal) { "html", "javascript", "js", "markdown", "latex", "svg" };

    /// <summary>Line commands that run the statement after them, which the script keeps and runs once.</summary>
    private static readonly string[] StatementCommands = ["%time ", "%timeit ", "%prun "];

    /// <summary>
    /// Line commands that set up how things are shown, show something, or install a package, so leaving them out changes
    /// nothing the cells after them read - a package that is not installed is said to be missing when it is imported.
    /// </summary>
    private static readonly HashSet<string> CommandsThatChangeNothingRead = new(StringComparer.Ordinal)
    {
        "matplotlib", "config", "load_ext", "reload_ext", "autoreload", "aimport", "precision", "xmode", "pip", "conda", "mamba",
        "who", "whos", "who_ls", "history", "lsmagic", "magic", "quickref", "pwd", "ls", "dirs", "pdef", "pdoc", "psource",
        "pinfo", "pinfo2", "time", "timeit", "clear", "env",
    };

    /// <summary>
    /// Line commands a line may start with and no %: IPython runs them as commands by default - its automagic - unless the
    /// notebook has a name of its own called that. These are the ones people type that way.
    /// </summary>
    private static readonly HashSet<string> CommandsRunWithoutAPercent = new(StringComparer.Ordinal)
    {
        "pip", "conda", "mamba", "matplotlib", "cd", "ls", "pwd", "mkdir", "clear", "who", "whos", "history", "time", "timeit",
        "prun", "env", "run", "load_ext", "reload_ext", "autoreload", "config",
    };

    /// <summary>
    /// What makes plotly's show() carry on as it does in Jupyter, where it shows the figure beside the cell, rather than open
    /// a browser and wait for it: outside Jupyter there is nowhere to show a figure, so it shows nothing. Only put in when
    /// the notebook uses plotly, and does nothing when plotly is not installed.
    /// </summary>
    private static readonly string[] PlotlyShowsNothing =
    [
        "try:",
        "    import plotly.io as fixfinder_plotly_io",
        "    fixfinder_plotly_io.show = lambda *shown, **options: None",
        "    del fixfinder_plotly_io",
        "except ImportError:",
        "    pass",
    ];

    /// <summary>Shell commands that install a package, or only look, so leaving them out changes nothing the cells after them read.</summary>
    private static readonly HashSet<string> ShellCommandsThatChangeNothingRead = new(StringComparer.OrdinalIgnoreCase)
    {
        "pip", "pip3", "conda", "mamba", "ls", "dir", "pwd", "echo", "cat", "type", "head", "tail", "which", "where", "whoami", "nvidia-smi",
    };

    /// <summary>
    /// What stands in for IPython's display() outside Jupyter: IPython's own, when it is installed where the script runs,
    /// and printing what it is given otherwise. Only put in when the notebook calls display.
    /// </summary>
    private static readonly string[] DisplayFallback =
    [
        "try:",
        "    from IPython.display import display",
        "except ImportError:",
        "    def display(*shown, **options):",
        "        for value in shown:",
        "            print(repr(value))",
    ];

    /// <summary><c>%cd data</c>, <c>%cd "My data"</c> - a folder to go to, not one of %cd's options.</summary>
    [GeneratedRegex(@"^%cd\s+(?<folder>""[^""]+""|'[^']+'|[^\s\-""'][^""']*?)\s*$")]
    private static partial Regex ChangeFolder();

    /// <summary><c>%env NAME=value</c> or <c>%env NAME value</c>.</summary>
    [GeneratedRegex(@"^%env\s+(?<name>[A-Za-z_]\w*)(?:\s*=\s*|\s+)(?<value>\S.*?)\s*$")]
    private static partial Regex SetVariable();

    /// <summary><c>%run helpers.py</c> - one Python file, with no options and nothing given to it.</summary>
    [GeneratedRegex(@"^%run\s+(?<script>""[^""]+\.py""|'[^']+\.py'|[^\s\-""']\S*\.py)\s*$")]
    private static partial Regex RunScript();

    /// <summary>The name a line starts with, and what follows it.</summary>
    [GeneratedRegex(@"^(?<name>[A-Za-z_]\w*)(?<rest>.*)$")]
    private static partial Regex LeadingName();

    // The ways code gives a name a value, each naming the names it gives: `total = 0`, `a, b = pair`, `def ls():`,
    // `for history in runs:`, `import time`, `from os import getcwd as pwd`.

    [GeneratedRegex(@"^\s*(?<names>[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*)\s*(?:[-+*/%&|^@]|//|\*\*|<<|>>)?=(?!=)")]
    private static partial Regex Assigned();

    [GeneratedRegex(@"^\s*(?:(?:async\s+)?def|class)\s+(?<names>\w+)")]
    private static partial Regex Defined();

    [GeneratedRegex(@"^\s*(?:async\s+)?for\s+(?<names>[\w\s,()]+?)\s+in\b")]
    private static partial Regex Looped();

    [GeneratedRegex(@"^\s*import\s+(?<names>[\w.]+(?:\s*,\s*[\w.]+)*)\s*$")]
    private static partial Regex Imported();

    [GeneratedRegex(@"^\s*from\s+\S+\s+import\s+(?<names>[\w\s,()]+)$")]
    private static partial Regex ImportedFrom();

    [GeneratedRegex(@"\bas\s+(?<names>\w+)")]
    private static partial Regex Aliased();

    /// <summary>An await, or an async for or with, which a function makes legal and Jupyter allows outside one too.</summary>
    [GeneratedRegex(@"\bawait\b|^\s*async\s+(?:for|with)\b")]
    private static partial Regex Awaiting();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s")]
    private static partial Regex FunctionHeader();

    /// <summary><c>File "C:\...\analysis.ipynb.py", line 57</c>, as a traceback names a place.</summary>
    [GeneratedRegex(@"File ""(?<path>[^""]+)"", line (?<line>\d+)")]
    private static partial Regex TracebackPlace();

    /// <summary><c>files = !ls</c>, <c>result = %sx ls</c> - what an IPython command gives, kept in a variable.</summary>
    [GeneratedRegex(@"^[A-Za-z_][\w.]*(?:\s*,\s*[A-Za-z_][\w.]*)*\s*=\s*[!%]")]
    private static partial Regex CommandKept();

    /// <summary><c>len?</c>, <c>?len</c>, <c>np.mean??</c> - IPython's help, which only shows something.</summary>
    [GeneratedRegex(@"^(?:\?{1,2}[\w.]+|[\w.]+\?{1,2})$")]
    private static partial Regex HelpAsked();

    private readonly IReadOnlyList<NotebookPlace?> _placeOfLine;
    private readonly IReadOnlyList<string?> _codeOfLine;
    private readonly IReadOnlySet<int> _lastStatementLines;

    private NotebookScript(
        string notebook, string script, IReadOnlyList<NotebookPlace?> placeOfLine, IReadOnlyList<string?> codeOfLine,
        IReadOnlySet<int> lastStatementLines, IReadOnlyList<CommandNotCarriedOut> commandsNotCarriedOut)
    {
        Notebook = notebook;
        Script = script;
        _placeOfLine = placeOfLine;
        _codeOfLine = codeOfLine;
        _lastStatementLines = lastStatementLines;
        CommandsNotCarriedOut = commandsNotCarriedOut;
    }

    public string Notebook { get; }

    /// <summary>The script the notebook's code was written to, in the temp folder - never beside the notebook.</summary>
    public string Script { get; }

    /// <summary>The notebook's IPython commands that FixFinder does not carry out, in the order they come.</summary>
    public IReadOnlyList<CommandNotCarriedOut> CommandsNotCarriedOut { get; }

    /// <summary>How many lines the script has, FixFinder's own among them.</summary>
    public int LineCount => _placeOfLine.Count;

    /// <summary>The notebook, cell and line a line of the script came from; null for a line FixFinder put in itself.</summary>
    public NotebookPlace? PlaceOf(int scriptLine) => scriptLine >= 1 && scriptLine <= _placeOfLine.Count ? _placeOfLine[scriptLine - 1] : null;

    /// <summary>
    /// A line of the script as the notebook has it - <c>%matplotlib inline</c>, not the line that stands in for it - or
    /// null for a line FixFinder put in itself.
    /// </summary>
    public string? CodeOf(int scriptLine) => scriptLine >= 1 && scriptLine <= _codeOfLine.Count ? _codeOfLine[scriptLine - 1] : null;

    /// <summary>
    /// Whether a line of the script is part of the last statement of its cell, at the cell's top level - whose value Jupyter
    /// shows under the cell, so an expression there is not one whose value is thrown away.
    /// </summary>
    public bool InACellsLastStatement(int scriptLine) => _lastStatementLines.Contains(scriptLine);

    /// <summary>The script a notebook's code was written to, when this is one.</summary>
    public static NotebookScript? Of(string scriptPath) => Written.TryGetValue(Path.GetFullPath(scriptPath), out var script) ? script : null;

    /// <summary>
    /// Whether a Python file is a notebook's code: a script FixFinder wrote, or a copy of one in a copy of the program made
    /// to try a change in. A file of the person's own that happens to be named like one is not.
    /// </summary>
    public static bool IsCodeOfANotebook(string pythonFile)
    {
        var full = Path.GetFullPath(pythonFile);

        return Of(full) is not null ||
               (full.EndsWith(ScriptEnding, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ProgramCopy.OriginalOf(full), full, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The folder a program file's code runs from and finds its own modules and files in: the file's own folder - or, for
    /// the script a notebook's code was written to, which is in the temp folder, the notebook's.
    /// </summary>
    public static string FolderOfCode(string file) =>
        Of(file) is { } notebook ? Path.GetDirectoryName(notebook.Notebook)! : Path.GetDirectoryName(Path.GetFullPath(file))!;

    /// <summary>
    /// The code of a notebook's code cells, one after another, as text to read rather than to run - what it imports, say -
    /// or null when the file cannot be read as a notebook.
    /// </summary>
    public static string? CodeIn(string notebookPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(notebookPath));

            if (!document.RootElement.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array) return null;

            return string.Join("\n", cells.EnumerateArray().Where(cell => Text(cell, "cell_type") == "code").SelectMany(SourceLines));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the notebook's code as a script and says where; or why it cannot be - a notebook of another language, say.</summary>
    public static (NotebookScript? Script, string? Problem) Write(string notebookPath)
    {
        var notebook = Path.GetFullPath(notebookPath);
        var name = Path.GetFileName(notebook);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(notebook));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (null, $"{name} could not be read as a Jupyter notebook: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;

            if (LanguageOf(root) is { } language && !language.Equals("python", StringComparison.OrdinalIgnoreCase))
                return (null, $"{name} is a notebook of {language}, and FixFinder checks the Python ones.");

            if (!root.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array)
                return (null, $"{name} has no cells FixFinder can read: it checks notebooks saved in Jupyter's current format, version 4.");

            var codeCells = cells.EnumerateArray()
                .Select((cell, index) => (Cell: index + 1, Kind: Text(cell, "cell_type"), Lines: SourceLines(cell)))
                .Where(cell => cell.Kind == "code")
                .Select(cell => (cell.Cell, cell.Lines))
                .ToList();

            var lines = new List<string>();
            var places = new List<NotebookPlace?>();
            var codes = new List<string?>();
            var lastStatementLines = new HashSet<int>();
            var notCarriedOut = new List<CommandNotCarriedOut>();

            void AddFixFindersOwn(string line)
            {
                lines.Add(line);
                places.Add(null);
                codes.Add(null);
            }

            if (codeCells.Any(cell => cell.Lines.Any(line => line.Contains("display(", StringComparison.Ordinal))))
            {
                foreach (var line in DisplayFallback) AddFixFindersOwn(line);
            }

            if (codeCells.Any(cell => cell.Lines.Any(line => line.Contains("plotly", StringComparison.Ordinal))))
            {
                foreach (var line in PlotlyShowsNothing) AddFixFindersOwn(line);
            }

            var namesGiven = NamesGivenIn(codeCells.SelectMany(cell => cell.Lines));

            foreach (var (cell, source) in codeCells)
            {
                if (lines.Count > 0)
                {
                    for (var blank = 0; blank < LinesBetweenCells; blank++) AddFixFindersOwn("");
                }

                var first = lines.Count + 1;
                var converted = Converted(source, cell, namesGiven, notCarriedOut);

                for (var index = 0; index < converted.Count; index++)
                {
                    lines.Add(converted[index]);
                    places.Add(new NotebookPlace(notebook, cell, index + 1));
                    codes.Add(source[index]);
                }

                if (LastStatementStart(converted) is { } start)
                {
                    for (var line = start; line < converted.Count; line++) lastStatementLines.Add(first + line);
                }
            }

            var script = ScriptPathFor(notebook);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(script)!);
                File.WriteAllText(script, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return (null, $"The code of {name} could not be written out to check: {ex.Message}");
            }

            var written = new NotebookScript(notebook, script, places, codes, lastStatementLines, notCarriedOut);
            Written[script] = written;
            return (written, null);
        }
    }

    /// <summary>The notebook's language, as its kernel or its language information names it; null when it names none.</summary>
    private static string? LanguageOf(JsonElement root)
    {
        if (!root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object) return null;

        if (metadata.TryGetProperty("kernelspec", out var kernel) && kernel.ValueKind == JsonValueKind.Object && Text(kernel, "language") is { Length: > 0 } language)
            return language;

        return metadata.TryGetProperty("language_info", out var info) && info.ValueKind == JsonValueKind.Object ? Text(info, "name") : null;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A cell's source as lines: Jupyter saves it as one text or as a list of lines, each ending in its newline.</summary>
    private static IReadOnlyList<string> SourceLines(JsonElement cell)
    {
        if (!cell.TryGetProperty("source", out var source)) return [];

        var text = source.ValueKind switch
        {
            JsonValueKind.String => source.GetString() ?? "",
            JsonValueKind.Array => string.Concat(source.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.String).Select(part => part.GetString())),
            _ => "",
        };

        return text.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
    }

    /// <summary>A code cell's lines with IPython's commands made plain Python, one line for one, so each keeps its number.</summary>
    private static List<string> Converted(
        IReadOnlyList<string> source, int cell, IReadOnlySet<string> namesGiven, List<CommandNotCarriedOut> notCarriedOut)
    {
        if (source.Count > 0 && source[0].TrimStart().StartsWith("%%", StringComparison.Ordinal))
        {
            var command = source[0].TrimStart()[2..].Split(' ', 2)[0];

            if (PythonCellCommands.Contains(command))
            {
                return [$"pass  # IPython's %%{command}",
                        .. source.Skip(1).Select((line, index) => ConvertedLine(line, cell, index + 2, namesGiven, notCarriedOut))];
            }

            // A cell of another language - %%bash, %%writefile, %%html - is not Python: it stays, as comments, and runs nothing.
            if (!ShowingCellCommands.Contains(command)) notCarriedOut.Add(new CommandNotCarriedOut(cell, 1, source[0].Trim()));
            return source.Select(line => "# " + line).ToList();
        }

        return source.Select((line, index) => ConvertedLine(line, cell, index + 1, namesGiven, notCarriedOut)).ToList();
    }

    private static string ConvertedLine(string line, int cell, int number, IReadOnlySet<string> namesGiven, List<CommandNotCarriedOut> notCarriedOut)
    {
        var body = line.TrimStart();
        var indent = line[..(line.Length - body.Length)];

        if (HelpAsked().IsMatch(body)) return $"{indent}pass  # IPython: {body}";

        if (CommandKept().IsMatch(body))
        {
            notCarriedOut.Add(new CommandNotCarriedOut(cell, number, body));
            return $"{indent}pass  # IPython: {body}";
        }

        // IPython's commands start with % or !, and the ones it runs by their name alone start with nothing at all.
        var shellCommand = body.StartsWith('!') && !body.StartsWith("!=", StringComparison.Ordinal);
        var command = body.StartsWith('%') || shellCommand ? body
            : IsCommandWithoutItsPercent(body, namesGiven) ? "%" + body
            : null;

        if (command is null) return line;

        if (StatementCommands.FirstOrDefault(prefix => command.StartsWith(prefix, StringComparison.Ordinal)) is { } timing)
            return indent + command[timing.Length..];

        if (InPython(command) is { } python) return $"{indent}{python}  # IPython: {body}";

        if (!ChangesNothingRead(command)) notCarriedOut.Add(new CommandNotCarriedOut(cell, number, body));
        return $"{indent}pass  # IPython: {body}";
    }

    /// <summary>
    /// Whether a line is one of IPython's commands written without its %, as IPython reads it: the line starts with the
    /// command's name, the notebook gives nothing that name itself, and what follows does not give the name a value -
    /// `ls = []` is Python - nor use it as a name, as `ls.sort()` and `ls(1)` do.
    /// </summary>
    private static bool IsCommandWithoutItsPercent(string body, IReadOnlySet<string> namesGiven)
    {
        if (LeadingName().Match(body) is not { Success: true } leading) return false;

        var name = leading.Groups["name"].Value;
        var rest = leading.Groups["rest"].Value;

        if (!CommandsRunWithoutAPercent.Contains(name) || namesGiven.Contains(name)) return false;
        if (rest.Length == 0) return true;
        if (!char.IsWhiteSpace(rest[0])) return false;

        var after = rest.TrimStart();
        return after.Length == 0 || after[0] is not ('=' or ',');
    }

    /// <summary>Every name the notebook's code gives a value to, anywhere in it - a name IPython would not take for a command.</summary>
    private static HashSet<string> NamesGivenIn(IEnumerable<string> code)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Regex[] givingNames = [Assigned(), Defined(), Looped(), Imported(), ImportedFrom(), Aliased()];

        foreach (var line in code)
        {
            foreach (var giving in givingNames)
            {
                foreach (Match given in giving.Matches(line))
                {
                    // `import os.path` gives os; `for (a, b) in pairs` gives a and b.
                    foreach (var named in given.Groups["names"].Value.Split([',', '(', ')', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                        names.Add(named.Split('.')[0]);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Whether a script's code has an await - or an async for or with - outside any function: Jupyter runs a cell like that,
    /// and plain Python refuses the whole file. Reads the code as its indenting lays it out, so a doubtful case counts as
    /// one, which costs nothing - such code is run by a runner that runs code without one just as Python would.
    /// </summary>
    public static bool AwaitsOutsideAFunction(IReadOnlyList<string> lines)
    {
        var masked = CodeText.MaskAll(lines, Syntax.Python);
        var functionIndents = new Stack<int>();

        foreach (var line in masked)
        {
            if (line.Trim().Length == 0) continue;

            var indent = line.Length - line.TrimStart().Length;
            while (functionIndents.Count > 0 && indent <= functionIndents.Peek()) functionIndents.Pop();

            if (FunctionHeader().IsMatch(line))
            {
                functionIndents.Push(indent);
                continue;
            }

            if (functionIndents.Count == 0 && Awaiting().IsMatch(line)) return true;
        }

        return false;
    }

    /// <summary>
    /// A line the program printed, with each place in this script put as the notebook has it - as Jupyter puts the places in
    /// a traceback: File "...analysis.ipynb", cell 3, line 2. A place in a line FixFinder put in itself is left as printed.
    /// </summary>
    public string InCellTerms(string printed)
    {
        if (!printed.Contains(Path.GetFileName(Script), StringComparison.OrdinalIgnoreCase)) return printed;

        var traced = TracebackPlace().Replace(printed, place =>
            SameFile(place.Groups["path"].Value) && PlaceOf(int.Parse(place.Groups["line"].Value)) is { } at
                ? $"File \"{Notebook}\", cell {at.Cell}, line {at.Line}"
                : place.Value);

        // A warning is printed as path:line: category: message.
        var warned = Regex.Match(traced, $@"^{Regex.Escape(Script)}:(?<line>\d+):", RegexOptions.IgnoreCase);
        return warned.Success && PlaceOf(int.Parse(warned.Groups["line"].Value)) is { } warnedAt
            ? $"{Notebook}, cell {warnedAt.Cell}, line {warnedAt.Line}:{traced[warned.Length..]}"
            : traced;
    }

    private bool SameFile(string path)
    {
        try
        {
            return string.Equals(Path.GetFullPath(path), Script, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The name the runner is written under, so an error of the runner's own can be told from the notebook's.</summary>
    public const string RunnerPrefix = "fixfinder_notebook_runner";

    /// <summary>
    /// Runs a notebook's code as Jupyter runs a cell, where await can be used outside a function: compiled with Python's own
    /// flag for that, and run on an event loop when it awaits, or as plain code when it does not.
    /// </summary>
    private const string RunnerSource = """
        # Written by FixFinder: runs a notebook's code as Jupyter runs a cell, where await can be used outside a function.

        import ast
        import asyncio
        import inspect
        import sys
        import types

        script = sys.argv[1]
        sys.argv = sys.argv[1:]

        with open(script, "rb") as source:
            code = compile(source.read(), script, "exec", flags=ast.PyCF_ALLOW_TOP_LEVEL_AWAIT, dont_inherit=True)

        main = types.ModuleType("__main__")
        sys.modules["__main__"] = main

        if code.co_flags & inspect.CO_COROUTINE:
            asyncio.run(eval(code, main.__dict__))
        else:
            exec(code, main.__dict__)
        """;

    /// <summary>Writes the runner where every Python can read it, named for its own text, and says where.</summary>
    public static string WriteRunner()
    {
        var name = $"{RunnerPrefix}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(RunnerSource)))[..12].ToLowerInvariant()}.py";
        var runner = Path.Combine(Path.GetTempPath(), "FixFinder-notebooks", name);

        if (!File.Exists(runner))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(runner)!);
            File.WriteAllText(runner, RunnerSource, new UTF8Encoding(false));
        }

        return runner;
    }

    /// <summary>
    /// An IPython command that changes what the cells after it see, as the Python that does the same - %cd, %env and a plain
    /// %run - or null for any other.
    /// </summary>
    private static string? InPython(string command)
    {
        if (ChangeFolder().Match(command) is { Success: true } folder)
            return $"__import__(\"os\").chdir(__import__(\"os\").path.expanduser({Literal(folder.Groups["folder"].Value)}))";

        if (SetVariable().Match(command) is { Success: true } variable)
            return $"__import__(\"os\").environ[{Literal(variable.Groups["name"].Value)}] = {Literal(variable.Groups["value"].Value)}";

        // What the script defines is kept, as %run keeps it - all but the names Python gives every module of its own.
        if (RunScript().Match(command) is { Success: true } run)
        {
            return "globals().update({name: value for name, value in __import__(\"runpy\").run_path(" +
                   $"{Literal(run.Groups["script"].Value)}, run_name=\"__main__\").items() if not name.startswith(\"__\")}})";
        }

        return null;
    }

    private static bool ChangesNothingRead(string command)
    {
        if (command.StartsWith('!'))
        {
            var words = command[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length > 0 && (ShellCommandsThatChangeNothingRead.Contains(words[0]) ||
                                        (words is [_, "-m", "pip", ..] && words[0].StartsWith("python", StringComparison.OrdinalIgnoreCase)));
        }

        var name = command[1..].Split(' ', 2)[0];
        return CommandsThatChangeNothingRead.Contains(name);
    }

    /// <summary>Text as a Python string, taking off the quotes it was written in, if any.</summary>
    private static string Literal(string text)
    {
        var unquoted = text.Length >= 2 && text[0] is '"' or '\'' && text[^1] == text[0] ? text[1..^1] : text;
        return "\"" + unquoted.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    /// <summary>
    /// Where the last statement at a cell's top level starts: the last line at the left margin that is code, not a
    /// comment, and not the closing bracket of a statement begun above it. Null for a cell with none.
    /// </summary>
    private static int? LastStatementStart(IReadOnlyList<string> lines)
    {
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var line = lines[index];
            if (line.Length == 0 || char.IsWhiteSpace(line[0]) || line.StartsWith('#') || line[0] is ')' or ']' or '}') continue;
            return index;
        }

        return null;
    }

    /// <summary>
    /// A place in the temp folder for one notebook's script, in a folder of the notebook's path's own and named after the
    /// notebook with .py after it.
    /// </summary>
    private static string ScriptPathFor(string notebook)
    {
        var folder = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notebook.ToUpperInvariant())))[..16].ToLowerInvariant();
        return Path.Combine(Path.GetTempPath(), "FixFinder-notebooks", folder, Path.GetFileName(notebook) + ".py");
    }
}
