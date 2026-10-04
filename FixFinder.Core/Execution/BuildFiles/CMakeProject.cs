using System.Text;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>The compiler a build is read for: CMake files ask about it - if(MSVC), $&lt;C_COMPILER_ID:GNU&gt; - and choose by it.</summary>
internal enum NativeCompilerKind
{
    Gnu,
    Clang,
    Msvc,
    None,
}

/// <summary>
/// The programs a CMake project builds, found by running its CMakeLists.txt files the way CMake runs them while it
/// configures - variables, lists, conditions, loops, functions, subdirectories, targets - without running CMake, any
/// command they would run, or anything that downloads.
/// </summary>
/// <remarks>
/// It is read as an IDE configures a project for debugging - CMAKE_BUILD_TYPE Debug, with the compiler FixFinder builds
/// with - because that is how FixFinder builds: with debugging information and without optimisation. What only running
/// something could say - execute_process, the folder CMake builds in, the version of the CMake that would run it, a
/// package find_package looks for - is kept as not known rather than guessed, and named wherever it matters to a program.
/// </remarks>
internal sealed partial class CMakeProject
{
    private const int MostDirectories = 100;
    private const int MostCallDepth = 50;
    private const int MostLoopTurns = 10_000;
    private const int MostCommandsRun = 500_000;

    /// <summary>The folder CMake would build in, which FixFinder does not make.</summary>
    private static readonly string BuildFolder = Unknowable.Mark("the folder CMake builds in");

    /// <summary>Which CMake would run the project - and so its version - is not known without running one.</summary>
    private static readonly string CMakeVersion = Unknowable.Mark("the version of CMake");

    private readonly NativeCompilerKind _compiler;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CMakeTarget> _targets = new(StringComparer.Ordinal);
    private readonly List<CMakeTarget> _targetsInOrder = [];
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommandDefinition> _definedCommands = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _testPrograms = new(StringComparer.Ordinal);
    private readonly HashSet<string> _generatedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _filesRead = [];
    private readonly List<string> _notFollowed = [];
    private readonly HashSet<string> _directoriesRead = new(StringComparer.OrdinalIgnoreCase);
    private int _commandsRun;
    private string? _stoppedBecause;

    private CMakeProject(NativeCompilerKind compiler) => _compiler = compiler;

    /// <summary>The CMake files run - the CMakeLists.txt files and those they include - so a change to any of them is noticed.</summary>
    public IReadOnlyList<string> FilesRead => _filesRead;

    /// <summary>Why CMake would stop before it finished configuring the project - a message(FATAL_ERROR) - if it would.</summary>
    public string? StoppedBecause => _stoppedBecause;

    public static CMakeProject Read(string cmakeLists, NativeCompilerKind compiler)
    {
        var path = Path.GetFullPath(cmakeLists);
        var folder = Path.GetDirectoryName(path)!;
        var project = new CMakeProject(compiler);

        var directory = new CMakeDirectory { SourceFolder = folder, ExcludedFromAll = false };
        var frame = new Frame { Variables = project.StartingVariables(folder), Directory = directory };

        project._directoriesRead.Add(folder);
        project.RunFile(path, frame);
        return project;
    }

    /// <summary>Where variables live while CMake files run: a folder's own copy, or a function's.</summary>
    private sealed class Frame
    {
        public required Dictionary<string, string> Variables { get; init; }

        public required CMakeDirectory Directory { get; init; }

        /// <summary>The frame set(... PARENT_SCOPE) writes to: the folder or function this one was entered from.</summary>
        public Frame? Parent { get; init; }

        public int CallDepth { get; init; }

        public string SourceFolder => Variables.TryGetValue("CMAKE_CURRENT_SOURCE_DIR", out var folder) ? folder : Directory.SourceFolder;
    }

    /// <summary>One folder of the project, with what its CMakeLists.txt gives every target in it and in the folders below.</summary>
    private sealed class CMakeDirectory
    {
        public required string SourceFolder { get; init; }

        public required bool ExcludedFromAll { get; init; }

        public List<string> IncludeDirectories { get; init; } = [];

        public List<string> Definitions { get; init; } = [];

        public List<string> CompileOptions { get; init; } = [];

        public List<string> LinkLibraries { get; init; } = [];

        public List<string> LinkDirectories { get; init; } = [];
    }

    /// <summary>A function or macro the project defines: its parameters and the commands it runs.</summary>
    private sealed record CommandDefinition(IReadOnlyList<string> Parameters, IReadOnlyList<CMakeCommand> Body, bool IsMacro);

    /// <summary>What running a stretch of commands ended with: going on, or a break(), continue() or return().</summary>
    private enum Flow
    {
        Next,
        Break,
        Continue,
        Return,
    }

    private Dictionary<string, string> StartingVariables(string folder)
    {
        var top = Slashed(folder);
        var compilerId = _compiler switch
        {
            NativeCompilerKind.Gnu => "GNU",
            NativeCompilerKind.Clang => "Clang",
            NativeCompilerKind.Msvc => "MSVC",
            _ => "",
        };

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CMAKE_SOURCE_DIR"] = top,
            ["CMAKE_CURRENT_SOURCE_DIR"] = top,
            ["CMAKE_CURRENT_LIST_DIR"] = top,
            ["CMAKE_CURRENT_LIST_FILE"] = top + "/CMakeLists.txt",
            ["CMAKE_BINARY_DIR"] = BuildFolder,
            ["CMAKE_CURRENT_BINARY_DIR"] = BuildFolder,
            ["PROJECT_BINARY_DIR"] = BuildFolder,
            ["WIN32"] = "1",
            ["CMAKE_HOST_WIN32"] = "1",
            ["CMAKE_SYSTEM_NAME"] = "Windows",
            ["CMAKE_HOST_SYSTEM_NAME"] = "Windows",
            ["CMAKE_SYSTEM_PROCESSOR"] = "AMD64",
            ["CMAKE_SIZEOF_VOID_P"] = "8",
            ["CMAKE_BUILD_TYPE"] = "Debug",
            ["CMAKE_VERSION"] = CMakeVersion,
            ["CMAKE_MAJOR_VERSION"] = CMakeVersion,
            ["CMAKE_MINOR_VERSION"] = CMakeVersion,
            ["CMAKE_PATCH_VERSION"] = CMakeVersion,
            ["CMAKE_C_COMPILER_ID"] = compilerId,
            ["CMAKE_CXX_COMPILER_ID"] = compilerId,
            ["CMAKE_C_COMPILER_VERSION"] = Unknowable.Mark("the compiler's version"),
            ["CMAKE_CXX_COMPILER_VERSION"] = Unknowable.Mark("the compiler's version"),
            ["CMAKE_EXECUTABLE_SUFFIX"] = ".exe",
            ["CMAKE_STATIC_LIBRARY_PREFIX"] = _compiler == NativeCompilerKind.Msvc ? "" : "lib",
            ["CMAKE_STATIC_LIBRARY_SUFFIX"] = _compiler == NativeCompilerKind.Msvc ? ".lib" : ".a",
        };

        if (_compiler == NativeCompilerKind.Msvc) variables["MSVC"] = "1";

        if (_compiler == NativeCompilerKind.Gnu)
        {
            variables["MINGW"] = "1";
            variables["CMAKE_COMPILER_IS_GNUCC"] = "1";
            variables["CMAKE_COMPILER_IS_GNUCXX"] = "1";
        }

        return variables;
    }

    private void RunFile(string path, Frame frame)
    {
        string text;

        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _filesRead.Add(path);
        Run(CMakeScript.Commands(text, path), frame);
    }

    private Flow Run(IReadOnlyList<CMakeCommand> commands, Frame frame)
    {
        for (var index = 0; index < commands.Count; index++)
        {
            if (_stoppedBecause is not null) return Flow.Return;

            if (++_commandsRun > MostCommandsRun)
            {
                _stoppedBecause = "it runs more commands than FixFinder follows - a loop that does not end, perhaps";
                return Flow.Return;
            }

            var command = commands[index];

            switch (command.Name)
            {
                case "if":
                {
                    var end = BlockEnd(commands, index, "if", "endif");
                    var flow = RunIf(commands, index, end, frame);
                    index = end;
                    if (flow != Flow.Next) return flow;
                    continue;
                }

                case "foreach":
                {
                    var end = BlockEnd(commands, index, "foreach", "endforeach");
                    var flow = RunForeach(command, Between(commands, index, end), frame);
                    index = end;
                    if (flow == Flow.Return) return flow;
                    continue;
                }

                case "while":
                {
                    var end = BlockEnd(commands, index, "while", "endwhile");
                    var flow = RunWhile(command, Between(commands, index, end), frame);
                    index = end;
                    if (flow == Flow.Return) return flow;
                    continue;
                }

                case "function" or "macro":
                {
                    var end = BlockEnd(commands, index, command.Name, "end" + command.Name);
                    var signature = Arguments(command, frame);
                    if (signature.Count > 0) _definedCommands[signature[0]] = new CommandDefinition(signature.Skip(1).ToList(), Between(commands, index, end), command.Name == "macro");
                    index = end;
                    continue;
                }

                case "block":
                {
                    var end = BlockEnd(commands, index, "block", "endblock");
                    var inner = new Frame { Variables = new Dictionary<string, string>(frame.Variables, StringComparer.Ordinal), Directory = frame.Directory, Parent = frame, CallDepth = frame.CallDepth };
                    var flow = Run(Between(commands, index, end), inner);
                    index = end;
                    if (flow != Flow.Next) return flow;
                    continue;
                }

                case "break":
                    return Flow.Break;

                case "continue":
                    return Flow.Continue;

                case "return":
                    return Flow.Return;

                default:
                    RunCommand(command, frame);
                    continue;
            }
        }

        return Flow.Next;
    }

    /// <summary>Where the block opened at <paramref name="start"/> ends, counting the blocks of its own kind inside it.</summary>
    private static int BlockEnd(IReadOnlyList<CMakeCommand> commands, int start, string opening, string closing)
    {
        var depth = 0;

        for (var index = start; index < commands.Count; index++)
        {
            if (commands[index].Name == opening) depth++;
            else if (commands[index].Name == closing && --depth == 0) return index;
        }

        return commands.Count;
    }

    private static List<CMakeCommand> Between(IReadOnlyList<CMakeCommand> commands, int opening, int closing) =>
        commands.Skip(opening + 1).Take(Math.Max(0, Math.Min(closing, commands.Count) - opening - 1)).ToList();

    private Flow RunIf(IReadOnlyList<CMakeCommand> commands, int start, int end, Frame frame)
    {
        // The if and each elseif and else at its own level, each with the commands up to the next.
        var branches = new List<int> { start };
        var depth = 0;

        for (var index = start + 1; index < Math.Min(end, commands.Count); index++)
        {
            var name = commands[index].Name;

            if (name == "if") depth++;
            else if (name == "endif") depth--;
            else if (depth == 0 && name is "elseif" or "else") branches.Add(index);
        }

        branches.Add(end);

        for (var branch = 0; branch < branches.Count - 1; branch++)
        {
            var opening = commands[branches[branch]];
            var holds = opening.Name == "else" || Condition(ConditionArguments(opening, frame), frame, opening);

            if (holds) return Run(Between(commands, branches[branch], branches[branch + 1]), frame);
        }

        return Flow.Next;
    }

    private Flow RunForeach(CMakeCommand command, IReadOnlyList<CMakeCommand> body, Frame frame)
    {
        var arguments = Arguments(command, frame);
        if (arguments.Count == 0) return Flow.Next;

        var loopVariable = arguments[0];
        var items = new List<string>();

        if (arguments.Count > 1 && arguments[1] == "RANGE")
        {
            var numbers = arguments.Skip(2).Select(text => int.TryParse(text, out var number) ? number : 0).ToList();
            var (first, last, step) = numbers.Count switch
            {
                1 => (0, numbers[0], 1),
                2 => (numbers[0], numbers[1], 1),
                >= 3 => (numbers[0], numbers[1], Math.Max(1, numbers[2])),
                _ => (0, -1, 1),
            };

            for (var number = first; number <= last && items.Count < MostLoopTurns; number += step) items.Add(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else if (arguments.Count > 1 && arguments[1] == "IN")
        {
            var mode = "";

            foreach (var argument in arguments.Skip(2))
            {
                if (argument is "LISTS" or "ITEMS" or "ZIP_LISTS")
                {
                    mode = argument;
                    continue;
                }

                if (mode == "LISTS") items.AddRange(SplitList(Variable(argument, frame)));
                else if (mode == "ITEMS") items.Add(argument);
            }
        }
        else
        {
            items.AddRange(arguments.Skip(1));
        }

        frame.Variables.TryGetValue(loopVariable, out var before);

        try
        {
            foreach (var item in items)
            {
                frame.Variables[loopVariable] = item;

                var flow = Run(body, frame);
                if (flow == Flow.Break) break;
                if (flow == Flow.Return) return flow;
            }
        }
        finally
        {
            if (before is null) frame.Variables.Remove(loopVariable);
            else frame.Variables[loopVariable] = before;
        }

        return Flow.Next;
    }

    private Flow RunWhile(CMakeCommand command, IReadOnlyList<CMakeCommand> body, Frame frame)
    {
        for (var turn = 0; Condition(ConditionArguments(command, frame), frame, command); turn++)
        {
            if (turn >= MostLoopTurns)
            {
                NotFollowed($"its while() at line {command.Line} of {Path.GetFileName(command.File)} goes round more than {MostLoopTurns} times, and FixFinder stopped following it");
                break;
            }

            var flow = Run(body, frame);
            if (flow == Flow.Break) break;
            if (flow == Flow.Return) return flow;
        }

        return Flow.Next;
    }

    /// <summary>A function or macro of the project's own: a function runs with variables of its own, a macro in the caller's.</summary>
    private void RunDefined(CommandDefinition definition, List<string> arguments, Frame frame)
    {
        if (frame.CallDepth >= MostCallDepth) return;

        var given = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ARGC"] = arguments.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ARGV"] = string.Join(";", arguments),
            ["ARGN"] = string.Join(";", arguments.Skip(definition.Parameters.Count)),
        };

        for (var position = 0; position < arguments.Count; position++) given[$"ARGV{position}"] = arguments[position];
        for (var position = 0; position < definition.Parameters.Count; position++) given[definition.Parameters[position]] = position < arguments.Count ? arguments[position] : "";

        if (!definition.IsMacro)
        {
            var called = new Frame
            {
                Variables = new Dictionary<string, string>(frame.Variables, StringComparer.Ordinal),
                Directory = frame.Directory,
                Parent = frame,
                CallDepth = frame.CallDepth + 1,
            };

            foreach (var (name, value) in given) called.Variables[name] = value;

            Run(definition.Body, called);
            return;
        }

        // A macro's parameters stand for the text it was given; here they are set while it runs and put back after.
        var before = given.Keys.ToDictionary(name => name, name => frame.Variables.TryGetValue(name, out var value) ? value : null, StringComparer.Ordinal);
        foreach (var (name, value) in given) frame.Variables[name] = value;

        try
        {
            Run(definition.Body, new Frame { Variables = frame.Variables, Directory = frame.Directory, Parent = frame.Parent, CallDepth = frame.CallDepth + 1 });
        }
        finally
        {
            foreach (var (name, value) in before)
            {
                if (value is null) frame.Variables.Remove(name);
                else frame.Variables[name] = value;
            }
        }
    }

    /// <summary>The arguments a command is given: each unquoted one expanded and split into its list, each quoted one expanded whole.</summary>
    private List<string> Arguments(CMakeCommand command, Frame frame)
    {
        var arguments = new List<string>();

        foreach (var argument in command.Arguments)
        {
            switch (argument.Kind)
            {
                case CMakeArgumentKind.Bracket:
                    arguments.Add(argument.Text);
                    break;
                case CMakeArgumentKind.Quoted:
                    arguments.Add(Expand(argument.Text, frame, quoted: true));
                    break;
                default:
                    arguments.AddRange(SplitList(Expand(argument.Text, frame, quoted: false)));
                    break;
            }
        }

        return arguments;
    }

    /// <summary>The arguments of if(), while() or elseif(), each with whether it was quoted - which decides whether it names a variable.</summary>
    private List<(string Text, bool Quoted)> ConditionArguments(CMakeCommand command, Frame frame)
    {
        var arguments = new List<(string, bool)>();

        foreach (var argument in command.Arguments)
        {
            switch (argument.Kind)
            {
                case CMakeArgumentKind.Bracket:
                    arguments.Add((argument.Text, true));
                    break;
                case CMakeArgumentKind.Quoted:
                    arguments.Add((Expand(argument.Text, frame, quoted: true), true));
                    break;
                default:
                    arguments.AddRange(SplitList(Expand(argument.Text, frame, quoted: false)).Select(item => (item, false)));
                    break;
            }
        }

        return arguments;
    }

    /// <summary>Stands for a ; written \; in an unquoted argument, which keeps it from dividing the list there.</summary>
    private const char KeptSemicolon = '\u0003';

    /// <summary>An argument with its escapes worked out and each ${VARIABLE}, $ENV{VARIABLE} and $CACHE{VARIABLE} replaced by its value.</summary>
    private string Expand(string text, Frame frame, bool quoted)
    {
        if (text.IndexOfAny(['$', '\\']) < 0) return text;

        var expanded = new StringBuilder();

        for (var at = 0; at < text.Length; at++)
        {
            var character = text[at];

            if (character == '\\' && at + 1 < text.Length)
            {
                var escaped = text[++at];

                switch (escaped)
                {
                    case '\n' when quoted:
                        break;
                    case 't':
                        expanded.Append('\t');
                        break;
                    case 'n':
                        expanded.Append('\n');
                        break;
                    case 'r':
                        expanded.Append('\r');
                        break;
                    case ';':
                        expanded.Append(quoted ? ';' : KeptSemicolon);
                        break;
                    default:
                        if (char.IsAsciiLetterOrDigit(escaped)) expanded.Append('\\');
                        expanded.Append(escaped);
                        break;
                }

                continue;
            }

            if (character == '$' && Reference(text, at) is { } reference)
            {
                var name = Expand(reference.Name, frame, quoted: true);

                expanded.Append(reference.Kind switch
                {
                    "ENV" => Environment.GetEnvironmentVariable(name) ?? "",
                    "CACHE" => _cache.TryGetValue(name, out var cached) ? cached : "",
                    _ => Variable(name, frame),
                });

                at = reference.End;
                continue;
            }

            expanded.Append(character);
        }

        return expanded.ToString();
    }

    /// <summary>The reference starting at <paramref name="at"/> - ${NAME}, $ENV{NAME} or $CACHE{NAME} - with the name as written and where it ends.</summary>
    private static (string Kind, string Name, int End)? Reference(string text, int at)
    {
        var kind = "";
        var opening = at + 1;

        foreach (var prefix in new[] { "ENV", "CACHE" })
        {
            if (string.CompareOrdinal(text, opening, prefix + "{", 0, prefix.Length + 1) == 0)
            {
                kind = prefix;
                opening += prefix.Length;
                break;
            }
        }

        if (opening >= text.Length || text[opening] != '{') return null;

        var depth = 0;

        for (var position = opening; position < text.Length; position++)
        {
            if (text[position] == '{') depth++;
            else if (text[position] == '}' && --depth == 0) return (kind, text[(opening + 1)..position], position);
        }

        return null;
    }

    private string Variable(string name, Frame frame) =>
        frame.Variables.TryGetValue(name, out var value) ? value : _cache.TryGetValue(name, out var cached) ? cached : "";

    private bool IsDefined(string name, Frame frame) => frame.Variables.ContainsKey(name) || _cache.ContainsKey(name);

    /// <summary>A CMake list - text divided at each ; - as its items, the empty ones left out as command arguments leave them out.</summary>
    private static List<string> SplitList(string text)
    {
        if (text.Length == 0) return [];

        var items = new List<string>();
        var depth = 0;
        var start = 0;

        for (var at = 0; at < text.Length; at++)
        {
            switch (text[at])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ';' when depth == 0:
                    Add(text[start..at]);
                    start = at + 1;
                    break;
            }
        }

        Add(text[start..]);
        return items;

        void Add(string item)
        {
            if (item.Length > 0) items.Add(item.Replace(KeptSemicolon, ';'));
        }
    }

    private static string JoinList(IEnumerable<string> items) => string.Join(";", items);

    private void SetVariable(string name, string value, Frame frame) => frame.Variables[name] = value;

    private void NotFollowed(string what)
    {
        if (!_notFollowed.Contains(what, StringComparer.Ordinal)) _notFollowed.Add(what);
    }

    /// <summary>A path as CMake writes one: with / between its folders.</summary>
    private static string Slashed(string path) => path.Replace('\\', '/');

    /// <summary>A path taken from the folder a command runs in, when it is not a full path already.</summary>
    private static string Full(string path, string folder)
    {
        if (Unknowable.IsIn(path) || path.StartsWith("$<", StringComparison.Ordinal)) return path;

        try
        {
            return Slashed(Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(folder, path)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
