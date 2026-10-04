using System.Globalization;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>The commands that set variables, read folders and files, and bring in other CMake files.</summary>
internal sealed partial class CMakeProject
{
    /// <summary>Commands that would download something - which FixFinder never does - with what they are named in a note.</summary>
    private static readonly HashSet<string> Downloads = new(StringComparer.Ordinal)
    {
        "fetchcontent_declare", "fetchcontent_makeavailable", "fetchcontent_populate", "externalproject_add", "cpmaddpackage", "cpmfindpackage",
    };

    /// <summary>Commands a test framework's CMake module gives, whose first argument is the target of a test program.</summary>
    private static readonly HashSet<string> TestDiscovery = new(StringComparer.Ordinal)
    {
        "gtest_discover_tests", "gtest_add_tests", "catch_discover_tests", "doctest_discover_tests",
    };

    private void RunCommand(CMakeCommand command, Frame frame)
    {
        if (_definedCommands.TryGetValue(command.Name, out var defined))
        {
            RunDefined(defined, Arguments(command, frame), frame);
            return;
        }

        var arguments = Arguments(command, frame);

        switch (command.Name)
        {
            case "set": Set(arguments, frame); break;
            case "unset": Unset(arguments, frame); break;
            case "option": Option(arguments); break;
            case "list": ListCommand(arguments, frame); break;
            case "string": StringCommand(arguments, frame); break;
            case "file": FileCommand(arguments, frame); break;
            case "math": MathCommand(arguments, frame); break;
            case "get_filename_component": FileNameComponent(arguments, frame); break;
            case "cmake_path": PathCommand(arguments, frame); break;
            case "aux_source_directory": SourcesInFolder(arguments, frame); break;
            case "cmake_parse_arguments": ParseArguments(arguments, frame); break;
            case "include": Include(arguments, frame); break;
            case "add_subdirectory": AddSubdirectory(arguments, frame); break;
            case "project": Project(arguments, frame); break;
            case "find_package": FindPackage(arguments, frame); break;
            case "find_library": FindFile(arguments, frame, library: true); break;
            case "find_path" or "find_file": FindFile(arguments, frame, library: false, folderOnly: command.Name == "find_path"); break;
            case "find_program": if (arguments.Count > 0) SetVariable(arguments[0], $"{arguments[0]}-NOTFOUND", frame); break;
            case "execute_process": ExecuteProcess(arguments, frame); break;
            case "configure_file": ConfigureFile(arguments, frame); break;
            case "message": Message(arguments, command); break;
            case "add_custom_command": CustomCommand(arguments); break;
            case "enable_testing": break;
            case "add_test": AddTest(arguments); break;
            default:
                if (RunTargetCommand(command.Name, arguments, frame)) break;
                if (Downloads.Contains(command.Name) && arguments.Count > 0) NotFollowed($"it downloads {arguments[0]} with {command.Name}(), which FixFinder never does");
                if (TestDiscovery.Contains(command.Name) && arguments.Count > 0) _testPrograms.Add(arguments[0]);
                break;
        }
    }

    private void Set(List<string> arguments, Frame frame)
    {
        if (arguments.Count == 0) return;

        var name = arguments[0];

        if (name.StartsWith("ENV{", StringComparison.Ordinal)) return;

        var cache = arguments.IndexOf("CACHE");

        if (cache > 0)
        {
            var value = JoinList(arguments.Skip(1).Take(cache - 1));
            var force = arguments[^1] == "FORCE";
            if (force || !_cache.ContainsKey(name)) _cache[name] = value;
            return;
        }

        if (arguments.Count > 1 && arguments[^1] == "PARENT_SCOPE")
        {
            var parent = frame.Parent?.Variables;
            if (parent is null) return;

            if (arguments.Count == 2) parent.Remove(name);
            else parent[name] = JoinList(arguments.Skip(1).SkipLast(1));
            return;
        }

        if (arguments.Count == 1) frame.Variables.Remove(name);
        else frame.Variables[name] = JoinList(arguments.Skip(1));
    }

    private void Unset(List<string> arguments, Frame frame)
    {
        if (arguments.Count == 0) return;

        if (arguments.Contains("CACHE")) _cache.Remove(arguments[0]);
        else if (arguments.Contains("PARENT_SCOPE")) frame.Parent?.Variables.Remove(arguments[0]);
        else frame.Variables.Remove(arguments[0]);
    }

    private void Option(List<string> arguments)
    {
        if (arguments.Count == 0 || _cache.ContainsKey(arguments[0])) return;

        _cache[arguments[0]] = arguments.Count > 2 ? arguments[2] : "OFF";
    }

    private void ListCommand(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 2) return;

        var operation = arguments[0];
        var name = arguments[1];
        var items = SplitList(Variable(name, frame));
        var given = arguments.Skip(2).ToList();

        switch (operation)
        {
            case "APPEND":
                SetVariable(name, JoinList(items.Concat(given)), frame);
                break;
            case "PREPEND":
                SetVariable(name, JoinList(given.Concat(items)), frame);
                break;
            case "INSERT" when given.Count > 0 && int.TryParse(given[0], out var position):
                items.InsertRange(Math.Clamp(position < 0 ? items.Count + position : position, 0, items.Count), given.Skip(1));
                SetVariable(name, JoinList(items), frame);
                break;
            case "REMOVE_ITEM":
                SetVariable(name, JoinList(items.Where(item => !given.Contains(item))), frame);
                break;
            case "REMOVE_AT":
            {
                var removed = given.Select(text => int.TryParse(text, out var index) ? (index < 0 ? items.Count + index : index) : -1).ToHashSet();
                SetVariable(name, JoinList(items.Where((_, index) => !removed.Contains(index))), frame);
                break;
            }
            case "REMOVE_DUPLICATES":
                SetVariable(name, JoinList(items.Distinct(StringComparer.Ordinal)), frame);
                break;
            case "REVERSE":
                items.Reverse();
                SetVariable(name, JoinList(items), frame);
                break;
            case "SORT":
                SetVariable(name, JoinList(items.Order(StringComparer.Ordinal)), frame);
                break;
            case "LENGTH" when given.Count > 0:
                SetVariable(given[0], items.Count.ToString(CultureInfo.InvariantCulture), frame);
                break;
            case "GET" when given.Count > 1:
            {
                var picked = given.SkipLast(1).Select(text => int.TryParse(text, out var index) ? (index < 0 ? items.Count + index : index) : -1)
                    .Where(index => index >= 0 && index < items.Count).Select(index => items[index]);
                SetVariable(given[^1], JoinList(picked), frame);
                break;
            }
            case "JOIN" when given.Count > 1:
                SetVariable(given[1], string.Join(given[0], items), frame);
                break;
            case "FIND" when given.Count > 1:
                SetVariable(given[1], items.IndexOf(given[0]).ToString(CultureInfo.InvariantCulture), frame);
                break;
            case "SUBLIST" when given.Count > 2 && int.TryParse(given[0], out var begin) && int.TryParse(given[1], out var length):
                SetVariable(given[2], JoinList(items.Skip(begin).Take(length < 0 ? int.MaxValue : length)), frame);
                break;
            case "POP_BACK" or "POP_FRONT":
            {
                var front = operation == "POP_FRONT";
                var outputs = given.Count == 0 ? 1 : given.Count;
                var taken = front ? items.Take(outputs).ToList() : items.TakeLast(outputs).Reverse().ToList();

                for (var output = 0; output < given.Count; output++) SetVariable(given[output], output < taken.Count ? taken[output] : "", frame);
                SetVariable(name, JoinList(front ? items.Skip(outputs) : items.SkipLast(outputs)), frame);
                break;
            }
            case "FILTER" when given.Count > 2 && given[1] == "REGEX":
            {
                var pattern = CMakeRegex(given[2]);
                var keep = given[0] == "INCLUDE";
                SetVariable(name, JoinList(items.Where(item => pattern?.IsMatch(item) == keep)), frame);
                break;
            }
            case "TRANSFORM" when given.Count > 0:
                SetVariable(OutputOf(given, name), JoinList(Transformed(items, given)), frame);
                break;
            default:
                // A list operation not followed leaves its list unknown rather than wrong.
                SetVariable(name, Unknowable.Mark($"list({operation} {name} ...)"), frame);
                break;
        }
    }

    /// <summary>The variable list(TRANSFORM ...) writes to: the one OUTPUT_VARIABLE names, or the list itself.</summary>
    private static string OutputOf(List<string> given, string name)
    {
        var output = given.IndexOf("OUTPUT_VARIABLE");
        return output >= 0 && output + 1 < given.Count ? given[output + 1] : name;
    }

    private static IEnumerable<string> Transformed(List<string> items, List<string> given)
    {
        var argument = given.Count > 1 ? given[1] : "";

        return given[0] switch
        {
            "APPEND" => items.Select(item => item + argument),
            "PREPEND" => items.Select(item => argument + item),
            "TOLOWER" => items.Select(item => item.ToLowerInvariant()),
            "TOUPPER" => items.Select(item => item.ToUpperInvariant()),
            "STRIP" => items.Select(item => item.Trim()),
            "REPLACE" when given.Count > 2 && CMakeRegex(given[1]) is { } pattern => items.Select(item => pattern.Replace(item, CMakeReplacement(given[2]))),
            _ => items.Select(_ => Unknowable.Mark($"list(TRANSFORM {given[0]} ...)")),
        };
    }

    private void StringCommand(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 2) return;

        var operation = arguments[0];
        var rest = arguments.Skip(1).ToList();

        switch (operation)
        {
            case "REPLACE" when rest.Count >= 3:
                SetVariable(rest[2], string.Concat(rest.Skip(3)).Replace(rest[0], rest[1], StringComparison.Ordinal), frame);
                break;
            case "REGEX" when rest.Count >= 3:
                RegexCommand(rest, frame);
                break;
            case "APPEND":
                SetVariable(rest[0], Variable(rest[0], frame) + string.Concat(rest.Skip(1)), frame);
                break;
            case "PREPEND":
                SetVariable(rest[0], string.Concat(rest.Skip(1)) + Variable(rest[0], frame), frame);
                break;
            case "CONCAT":
                SetVariable(rest[0], string.Concat(rest.Skip(1)), frame);
                break;
            case "JOIN" when rest.Count >= 2:
                SetVariable(rest[1], string.Join(rest[0], rest.Skip(2)), frame);
                break;
            case "TOLOWER" when rest.Count >= 2:
                SetVariable(rest[1], rest[0].ToLowerInvariant(), frame);
                break;
            case "TOUPPER" when rest.Count >= 2:
                SetVariable(rest[1], rest[0].ToUpperInvariant(), frame);
                break;
            case "STRIP" when rest.Count >= 2:
                SetVariable(rest[1], rest[0].Trim(), frame);
                break;
            case "LENGTH" when rest.Count >= 2:
                SetVariable(rest[1], rest[0].Length.ToString(CultureInfo.InvariantCulture), frame);
                break;
            case "SUBSTRING" when rest.Count >= 4 && int.TryParse(rest[1], out var begin) && int.TryParse(rest[2], out var length):
                SetVariable(rest[3], begin >= 0 && begin <= rest[0].Length ? rest[0].Substring(begin, length < 0 ? rest[0].Length - begin : Math.Min(length, rest[0].Length - begin)) : "", frame);
                break;
            case "FIND" when rest.Count >= 3:
                SetVariable(rest[2], (rest.Count > 3 && rest[3] == "REVERSE" ? rest[0].LastIndexOf(rest[1], StringComparison.Ordinal) : rest[0].IndexOf(rest[1], StringComparison.Ordinal)).ToString(CultureInfo.InvariantCulture), frame);
                break;
            case "MAKE_C_IDENTIFIER" when rest.Count >= 2:
                SetVariable(rest[1], Regex.Replace(char.IsAsciiDigit(rest[0].FirstOrDefault()) ? "_" + rest[0] : rest[0], @"[^A-Za-z0-9_]", "_"), frame);
                break;
            case "COMPARE" when rest.Count >= 4:
                SetVariable(rest[3], Compared(rest[0], rest[1], rest[2]) ? "1" : "0", frame);
                break;
            case "GENEX_STRIP" when rest.Count >= 2:
                SetVariable(rest[1], Regex.Replace(rest[0], @"\$<[^<>]*>", ""), frame);
                break;
            default:
            {
                // A form not followed leaves the variable it writes unknown: the first after the form's name for these, the last for the rest.
                var written = operation is "TIMESTAMP" or "UUID" or "JSON" or "MD5" or "SHA1" or "SHA224" or "SHA256" or "SHA384" or "SHA512" or
                    "SHA3_224" or "SHA3_256" or "SHA3_384" or "SHA3_512"
                    ? rest[0]
                    : arguments[^1];
                SetVariable(written, Unknowable.Mark($"string({operation} ...)"), frame);
                break;
            }
        }
    }

    private static bool Compared(string how, string left, string right) => how switch
    {
        "EQUAL" => string.Equals(left, right, StringComparison.Ordinal),
        "NOTEQUAL" => !string.Equals(left, right, StringComparison.Ordinal),
        "LESS" => string.CompareOrdinal(left, right) < 0,
        "GREATER" => string.CompareOrdinal(left, right) > 0,
        "LESS_EQUAL" => string.CompareOrdinal(left, right) <= 0,
        "GREATER_EQUAL" => string.CompareOrdinal(left, right) >= 0,
        _ => false,
    };

    private void RegexCommand(List<string> rest, Frame frame)
    {
        var mode = rest[0];
        if (CMakeRegex(rest[1]) is not { } pattern)
        {
            var written = mode == "REPLACE" && rest.Count >= 4 ? rest[3] : rest[2];
            SetVariable(written, Unknowable.Mark($"string(REGEX {mode} ...)"), frame);
            return;
        }

        switch (mode)
        {
            case "REPLACE" when rest.Count >= 4:
                SetVariable(rest[3], pattern.Replace(string.Concat(rest.Skip(4)), CMakeReplacement(rest[2])), frame);
                break;
            case "MATCH":
                SetVariable(rest[2], pattern.Match(string.Concat(rest.Skip(3))) is { Success: true } match ? match.Value : "", frame);
                break;
            case "MATCHALL":
                SetVariable(rest[2], JoinList(pattern.Matches(string.Concat(rest.Skip(3))).Select(match => match.Value)), frame);
                break;
        }
    }

    /// <summary>A CMake regular expression as .NET reads one - they agree on what CMake files use - or null for one .NET cannot read.</summary>
    private static Regex? CMakeRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>CMake's replacement text - \1 for the first group - as .NET's: $1.</summary>
    private static string CMakeReplacement(string replacement) =>
        Regex.Replace(replacement.Replace("$", "$$", StringComparison.Ordinal), @"\\(\d)", "$${$1}");

    private void MathCommand(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 3 || arguments[0] != "EXPR") return;

        SetVariable(arguments[1], Unknowable.Mark($"math(EXPR {arguments[1]} {arguments[2]})"), frame);
    }

    private void FileCommand(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 2) return;

        switch (arguments[0])
        {
            case "GLOB" or "GLOB_RECURSE":
                Glob(arguments, frame);
                break;
            case "TO_CMAKE_PATH" when arguments.Count >= 3:
                SetVariable(arguments[2], Slashed(arguments[1]), frame);
                break;
            case "TO_NATIVE_PATH" when arguments.Count >= 3:
                SetVariable(arguments[2], arguments[1].Replace('/', '\\'), frame);
                break;
            case "REAL_PATH" when arguments.Count >= 3:
                SetVariable(arguments[2], Full(arguments[1], frame.SourceFolder), frame);
                break;
            case "RELATIVE_PATH" when arguments.Count >= 4:
                SetVariable(arguments[1], Slashed(Path.GetRelativePath(arguments[2], arguments[3])), frame);
                break;
            case "READ" or "STRINGS" when arguments.Count >= 3:
                SetVariable(arguments[2], Unknowable.Mark($"file({arguments[0]} {arguments[1]})"), frame);
                break;
            case "DOWNLOAD":
                NotFollowed($"it downloads {arguments[1]} with file(DOWNLOAD), which FixFinder never does");
                break;
        }
    }

    /// <summary>file(GLOB ...) and file(GLOB_RECURSE ...): the files their patterns name, as full paths in the order of their names.</summary>
    private void Glob(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 2) return;

        var recursive = arguments[0] == "GLOB_RECURSE";
        var variable = arguments[1];
        var listDirectories = !recursive;
        string? relativeTo = null;
        var found = new List<string>();

        for (var index = 2; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "CONFIGURE_DEPENDS" or "FOLLOW_SYMLINKS":
                    continue;
                case "LIST_DIRECTORIES" when index + 1 < arguments.Count:
                    listDirectories = IsTrueConstant(arguments[++index]);
                    continue;
                case "RELATIVE" when index + 1 < arguments.Count:
                    relativeTo = arguments[++index];
                    continue;
            }

            var pattern = arguments[index];

            if (Unknowable.IsIn(pattern))
            {
                found.Add(pattern);
                continue;
            }

            var matches = recursive
                ? PathPattern.RecursiveMatches(frame.SourceFolder, pattern)
                : PathPattern.Matches(frame.SourceFolder, pattern).Select(match => Full(match, frame.SourceFolder));

            found.AddRange(matches.Select(Slashed).Where(match => listDirectories || !Directory.Exists(match)));
        }

        var listed = found.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)
            .Select(path => relativeTo is null || Unknowable.IsIn(path) ? path : Slashed(Path.GetRelativePath(relativeTo, path)));

        SetVariable(variable, JoinList(listed), frame);
    }

    private void FileNameComponent(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 3) return;

        var path = arguments[1];
        var baseFolder = arguments.IndexOf("BASE_DIR") is var at and >= 0 && at + 1 < arguments.Count ? arguments[at + 1] : frame.SourceFolder;
        var name = path.Replace('\\', '/')[(path.Replace('\\', '/').LastIndexOf('/') + 1)..];

        var component = arguments[2] switch
        {
            "DIRECTORY" or "PATH" => path.Replace('\\', '/').LastIndexOf('/') is var slash and >= 0 ? path[..slash] : "",
            "NAME" => name,
            "EXT" => name.IndexOf('.') is var dot and >= 0 ? name[dot..] : "",
            "NAME_WE" => name.IndexOf('.') is var dot and >= 0 ? name[..dot] : name,
            "LAST_EXT" => Path.GetExtension(name),
            "NAME_WLE" => Path.GetFileNameWithoutExtension(name),
            "ABSOLUTE" or "REALPATH" => Full(path, baseFolder),
            _ => Unknowable.Mark($"get_filename_component({arguments[2]})"),
        };

        SetVariable(arguments[0], component, frame);
    }

    /// <summary>cmake_path(GET ...), (SET ...) and (APPEND ...): the parts of a path a CMakeLists.txt takes apart.</summary>
    private void PathCommand(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 3) return;

        switch (arguments[0])
        {
            case "SET":
                SetVariable(arguments[1], Slashed(arguments[2]), frame);
                break;
            case "APPEND":
                SetVariable(arguments[1], string.Join("/", new[] { Variable(arguments[1], frame) }.Concat(arguments.Skip(2).TakeWhile(part => part != "OUTPUT_VARIABLE")).Where(part => part.Length > 0)), frame);
                break;
            case "GET" when arguments.Count >= 4:
            {
                var path = Slashed(Variable(arguments[1], frame));
                var name = path[(path.LastIndexOf('/') + 1)..];
                var output = arguments[^1];

                SetVariable(output, arguments[2] switch
                {
                    "FILENAME" => name,
                    "STEM" => arguments.Contains("LAST_ONLY") ? Path.GetFileNameWithoutExtension(name) : name.IndexOf('.') is var dot and > 0 ? name[..dot] : name,
                    "EXTENSION" => arguments.Contains("LAST_ONLY") ? Path.GetExtension(name) : name.IndexOf('.') is var dot and > 0 ? name[dot..] : "",
                    "PARENT_PATH" => path.LastIndexOf('/') is var slash and >= 0 ? path[..slash] : "",
                    _ => Unknowable.Mark($"cmake_path(GET {arguments[2]})"),
                }, frame);
                break;
            }
        }
    }

    /// <summary>aux_source_directory(folder VARIABLE): the C and C++ files in a folder.</summary>
    private void SourcesInFolder(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 2) return;

        var folder = Full(arguments[0], frame.SourceFolder);
        IEnumerable<string> files;

        try
        {
            files = Directory.EnumerateFiles(folder).Where(NativeFileKinds.IsSource).Order(StringComparer.OrdinalIgnoreCase).Select(Slashed).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            files = [];
        }

        SetVariable(arguments[1], JoinList(SplitList(Variable(arguments[1], frame)).Concat(files)), frame);
    }

    /// <summary>cmake_parse_arguments: a function's arguments sorted under its keywords, as &lt;prefix&gt;_&lt;KEYWORD&gt; variables.</summary>
    private void ParseArguments(List<string> arguments, Frame frame)
    {
        List<string> parsed;
        int first;

        if (arguments.Count >= 5 && arguments[0] == "PARSE_ARGV" && int.TryParse(arguments[1], out var skipped))
        {
            parsed = SplitList(Variable("ARGV", frame)).Skip(skipped).ToList();
            first = 2;
        }
        else if (arguments.Count >= 4)
        {
            parsed = arguments.Skip(4).ToList();
            first = 0;
        }
        else
        {
            return;
        }

        var prefix = arguments[first];
        var options = SplitList(arguments[first + 1]);
        var single = SplitList(arguments[first + 2]);
        var multiple = SplitList(arguments[first + 3]);
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var unparsed = new List<string>();
        string? current = null;

        foreach (var argument in parsed)
        {
            if (options.Contains(argument))
            {
                values[argument] = ["TRUE"];
                current = null;
            }
            else if (single.Contains(argument) || multiple.Contains(argument))
            {
                values[argument] = [];
                current = argument;
            }
            else if (current is not null)
            {
                values[current].Add(argument);
                if (single.Contains(current)) current = null;
            }
            else
            {
                unparsed.Add(argument);
            }
        }

        foreach (var option in options) SetVariable($"{prefix}_{option}", values.ContainsKey(option) ? "TRUE" : "FALSE", frame);

        foreach (var keyword in single.Concat(multiple))
        {
            if (values.TryGetValue(keyword, out var given) && given.Count > 0) SetVariable($"{prefix}_{keyword}", JoinList(given), frame);
            else frame.Variables.Remove($"{prefix}_{keyword}");
        }

        if (unparsed.Count > 0) SetVariable($"{prefix}_UNPARSED_ARGUMENTS", JoinList(unparsed), frame);
        else frame.Variables.Remove($"{prefix}_UNPARSED_ARGUMENTS");
    }

    /// <summary>include(): a .cmake file of the project's own is run where it is included; CMake's own modules give nothing to follow.</summary>
    private void Include(List<string> arguments, Frame frame)
    {
        if (arguments.Count == 0) return;

        var name = arguments[0];

        if (name == "CTest" && !_cache.ContainsKey("BUILD_TESTING")) _cache["BUILD_TESTING"] = "ON";

        var path = name.EndsWith(".cmake", StringComparison.OrdinalIgnoreCase) || name.Contains('/')
            ? Full(name, frame.SourceFolder)
            : SplitList(Variable("CMAKE_MODULE_PATH", frame))
                .Select(folder => Full(Path.Combine(folder, name + ".cmake"), frame.SourceFolder))
                .FirstOrDefault(File.Exists);

        if (path is null || !File.Exists(path) || _filesRead.Count > MostDirectories * 4) return;

        var listDirectory = Variable("CMAKE_CURRENT_LIST_DIR", frame);
        var listFile = Variable("CMAKE_CURRENT_LIST_FILE", frame);

        SetVariable("CMAKE_CURRENT_LIST_DIR", Slashed(Path.GetDirectoryName(path)!), frame);
        SetVariable("CMAKE_CURRENT_LIST_FILE", Slashed(path), frame);

        RunFile(path, frame);

        SetVariable("CMAKE_CURRENT_LIST_DIR", listDirectory, frame);
        SetVariable("CMAKE_CURRENT_LIST_FILE", listFile, frame);
    }

    private void AddSubdirectory(List<string> arguments, Frame frame)
    {
        if (arguments.Count == 0) return;

        if (Unknowable.IsIn(arguments[0])) return;

        var folder = Path.GetFullPath(Full(arguments[0], frame.SourceFolder));
        var file = Path.Combine(folder, "CMakeLists.txt");

        if (!File.Exists(file) || _directoriesRead.Count >= MostDirectories || !_directoriesRead.Add(folder)) return;

        var parent = frame.Directory;
        var directory = new CMakeDirectory
        {
            SourceFolder = folder,
            ExcludedFromAll = parent.ExcludedFromAll || arguments.Contains("EXCLUDE_FROM_ALL"),
            IncludeDirectories = [.. parent.IncludeDirectories],
            Definitions = [.. parent.Definitions],
            CompileOptions = [.. parent.CompileOptions],
            LinkLibraries = [.. parent.LinkLibraries],
            LinkDirectories = [.. parent.LinkDirectories],
        };

        var variables = new Dictionary<string, string>(frame.Variables, StringComparer.Ordinal)
        {
            ["CMAKE_CURRENT_SOURCE_DIR"] = Slashed(folder),
            ["CMAKE_CURRENT_LIST_DIR"] = Slashed(folder),
            ["CMAKE_CURRENT_LIST_FILE"] = Slashed(file),
            ["CMAKE_CURRENT_BINARY_DIR"] = BuildFolder,
            ["CMAKE_PARENT_LIST_FILE"] = Variable("CMAKE_CURRENT_LIST_FILE", frame),
        };

        RunFile(file, new Frame { Variables = variables, Directory = directory, Parent = frame, CallDepth = frame.CallDepth });
    }

    private void Project(List<string> arguments, Frame frame)
    {
        if (arguments.Count == 0) return;

        var name = arguments[0];
        var folder = frame.SourceFolder;
        var topLevel = !IsDefined("CMAKE_PROJECT_NAME", frame);

        SetVariable("PROJECT_NAME", name, frame);
        SetVariable("PROJECT_SOURCE_DIR", folder, frame);
        SetVariable($"{name}_SOURCE_DIR", folder, frame);
        SetVariable("PROJECT_BINARY_DIR", BuildFolder, frame);
        SetVariable($"{name}_BINARY_DIR", BuildFolder, frame);
        SetVariable("PROJECT_IS_TOP_LEVEL", topLevel ? "ON" : "OFF", frame);
        SetVariable($"{name}_IS_TOP_LEVEL", topLevel ? "ON" : "OFF", frame);

        if (topLevel)
        {
            SetVariable("CMAKE_PROJECT_NAME", name, frame);
            _cache["CMAKE_PROJECT_NAME"] = name;
        }

        if (arguments.IndexOf("VERSION") is var at and > 0 && at + 1 < arguments.Count)
        {
            var version = arguments[at + 1];
            var parts = version.Split('.');

            SetVariable("PROJECT_VERSION", version, frame);
            SetVariable($"{name}_VERSION", version, frame);
            SetVariable("PROJECT_VERSION_MAJOR", parts[0], frame);
            SetVariable("PROJECT_VERSION_MINOR", parts.Length > 1 ? parts[1] : "", frame);
            SetVariable("PROJECT_VERSION_PATCH", parts.Length > 2 ? parts[2] : "", frame);
        }
    }

    /// <summary>
    /// find_package(Threads) is the threads the compiler has; any other package is one FixFinder does not look for -
    /// left not found, as CMake leaves a package it cannot find, and named if the project needs it.
    /// </summary>
    private void FindPackage(List<string> arguments, Frame frame)
    {
        if (arguments.Count == 0) return;

        var name = arguments[0];

        if (name == "Threads")
        {
            DefineImported("Threads::Threads", threads: true);
            SetVariable("Threads_FOUND", "TRUE", frame);
            SetVariable("CMAKE_THREAD_LIBS_INIT", _compiler is NativeCompilerKind.Gnu or NativeCompilerKind.Clang ? "-pthread" : "", frame);
            SetVariable(_compiler == NativeCompilerKind.Msvc ? "CMAKE_USE_WIN32_THREADS_INIT" : "CMAKE_USE_PTHREADS_INIT", "1", frame);
            return;
        }

        if (arguments.Contains("REQUIRED")) NotFollowed($"it needs the package {name} (find_package({name} REQUIRED)), which FixFinder does not look for");
    }

    /// <summary>find_library, find_path and find_file, looking only in the folders the command itself names - never across the computer.</summary>
    private void FindFile(List<string> arguments, Frame frame, bool library, bool folderOnly = false)
    {
        if (arguments.Count < 2) return;

        var variable = arguments[0];
        if (IsDefined(variable, frame) && !Variable(variable, frame).EndsWith("NOTFOUND", StringComparison.Ordinal)) return;

        var keywords = new HashSet<string>(StringComparer.Ordinal) { "NAMES", "HINTS", "PATHS", "PATH_SUFFIXES", "DOC", "NO_DEFAULT_PATH", "REQUIRED", "NO_CACHE", "NAMES_PER_DIR", "ENV" };
        var names = new List<string>();
        var folders = new List<string>();
        var suffixes = new List<string> { "" };
        var section = arguments.Contains("NAMES") ? "" : "NAMES";

        foreach (var argument in arguments.Skip(1))
        {
            if (keywords.Contains(argument))
            {
                section = argument;
                continue;
            }

            switch (section)
            {
                case "NAMES": names.Add(argument); if (!arguments.Contains("NAMES")) section = "PATHS"; break;
                case "HINTS" or "PATHS": folders.Add(Full(argument, frame.SourceFolder)); break;
                case "PATH_SUFFIXES": suffixes.Add(argument); break;
            }
        }

        var candidates = names.SelectMany(name => library
            ? new[] { $"lib{name}.a", $"{name}.lib", $"lib{name}.dll.a", $"{name}.a", name }
            : [name]);

        foreach (var folder in folders.Where(folder => !Unknowable.IsIn(folder)))
        {
            foreach (var suffix in suffixes)
            {
                foreach (var candidate in candidates)
                {
                    var path = Path.Combine(folder, suffix, candidate);
                    if (!File.Exists(path)) continue;

                    var found = Slashed(folderOnly ? Path.GetDirectoryName(Path.GetFullPath(path))! : Path.GetFullPath(path));
                    SetVariable(variable, found, frame);
                    _cache[variable] = found;
                    return;
                }
            }
        }

        SetVariable(variable, $"{variable}-NOTFOUND", frame);
    }

    private void ExecuteProcess(List<string> arguments, Frame frame)
    {
        var command = arguments.IndexOf("COMMAND") is var at and >= 0 && at + 1 < arguments.Count ? arguments[at + 1] : "a command";

        foreach (var output in new[] { "OUTPUT_VARIABLE", "ERROR_VARIABLE", "RESULT_VARIABLE", "RESULTS_VARIABLE" })
        {
            if (arguments.IndexOf(output) is var position and >= 0 && position + 1 < arguments.Count)
                SetVariable(arguments[position + 1], Unknowable.Mark($"what execute_process gets from {command}"), frame);
        }
    }

    /// <summary>configure_file(): a file CMake writes while it configures - often a header the program includes - which FixFinder does not write.</summary>
    private void ConfigureFile(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 2) return;

        var output = Variable("CMAKE_CURRENT_BINARY_DIR", frame) is var buildFolder && !Path.IsPathRooted(arguments[1]) && !Unknowable.IsIn(arguments[1])
            ? $"{buildFolder}/{arguments[1]}"
            : arguments[1];

        if (Unknowable.IsIn(output) || !File.Exists(output))
            _generatedFiles.Add(Path.GetFileName(arguments[1].Replace('\\', '/')));
    }

    /// <summary>The words add_custom_command(OUTPUT ...) goes on with after the files it makes.</summary>
    private static readonly HashSet<string> CustomCommandKeywords = new(StringComparer.Ordinal)
    {
        "COMMAND", "MAIN_DEPENDENCY", "DEPENDS", "BYPRODUCTS", "IMPLICIT_DEPENDS", "WORKING_DIRECTORY", "COMMENT", "DEPFILE",
        "JOB_POOL", "JOB_SERVER_AWARE", "VERBATIM", "APPEND", "USES_TERMINAL", "COMMAND_EXPAND_LISTS", "DEPENDS_EXPLICIT_ONLY", "CODEGEN",
    };

    /// <summary>add_custom_command(OUTPUT ...): files a command makes while the project builds, which FixFinder does not run.</summary>
    private void CustomCommand(List<string> arguments)
    {
        if (arguments.FirstOrDefault() != "OUTPUT") return;

        foreach (var output in arguments.Skip(1).TakeWhile(argument => !CustomCommandKeywords.Contains(argument)))
            _generatedFiles.Add(Path.GetFileName(output.Replace('\\', '/')));
    }

    /// <summary>message(FATAL_ERROR ...) stops CMake there, and so stops the reading of the project.</summary>
    private void Message(List<string> arguments, CMakeCommand command)
    {
        if (arguments.Count == 0 || arguments[0] is not ("FATAL_ERROR" or "SEND_ERROR")) return;

        var text = string.Concat(arguments.Skip(1)).Trim();
        _stoppedBecause = $"CMake would stop at line {command.Line} of {Path.GetFileName(command.File)}, with message(FATAL_ERROR \"{(text.Length > 200 ? text[..200] + "..." : text)}\")";
    }

    /// <summary>add_test(): the program it runs is a test program.</summary>
    private void AddTest(List<string> arguments)
    {
        if (arguments.Count >= 2 && arguments[0] == "NAME")
        {
            var command = arguments.IndexOf("COMMAND");
            if (command >= 0 && command + 1 < arguments.Count) _testPrograms.Add(arguments[command + 1]);
        }
        else if (arguments.Count >= 2)
        {
            _testPrograms.Add(arguments[1]);
        }
    }
}
