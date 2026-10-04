using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>
/// Targets - add_executable, add_library and what target_* commands give them - and the program each executable is:
/// its own sources and those of the libraries it links, with the include folders, definitions and standard that reach it.
/// </summary>
internal sealed partial class CMakeProject
{
    /// <summary>Stands for $&lt;TARGET_OBJECTS:library&gt; - the object files of an object library - followed by the library's name.</summary>
    private const char ObjectsOfTarget = '\u0004';

    /// <summary>The words add_executable and add_library take before their sources.</summary>
    private static readonly HashSet<string> TargetKeywords = new(StringComparer.Ordinal)
    {
        "WIN32", "MACOSX_BUNDLE", "EXCLUDE_FROM_ALL", "STATIC", "SHARED", "MODULE", "OBJECT", "INTERFACE", "UNKNOWN", "GLOBAL",
    };

    /// <summary>The variables that, set when a target is made, give it its C or C++ standard.</summary>
    private static readonly (string Variable, string Property)[] PropertiesFromVariables =
    [
        ("CMAKE_C_STANDARD", "C_STANDARD"),
        ("CMAKE_CXX_STANDARD", "CXX_STANDARD"),
        ("CMAKE_C_EXTENSIONS", "C_EXTENSIONS"),
        ("CMAKE_CXX_EXTENSIONS", "CXX_EXTENSIONS"),
    ];

    [GeneratedRegex(@"^(?<language>c|cxx)_std_(?<level>\d+)$")]
    private static partial Regex StandardFeature();

    [GeneratedRegex(@"^[A-Za-z0-9_.+\-]+$")]
    private static partial Regex LibraryName();

    private enum TargetKind
    {
        Executable,
        Library,
        InterfaceLibrary,
        Imported,
    }

    private sealed class CMakeTarget
    {
        private readonly Dictionary<string, List<string>> _properties = new(StringComparer.Ordinal);

        public required string Name { get; init; }

        public required TargetKind Kind { get; init; }

        public required string SourceFolder { get; init; }

        public CMakeDirectory? Directory { get; init; }

        public bool ExcludedFromAll { get; init; }

        /// <summary>Whether it is Threads::Threads, which is the threads the compiler has.</summary>
        public bool IsThreads { get; init; }

        /// <summary>One of its properties as a list: SOURCES, INCLUDE_DIRECTORIES, INTERFACE_INCLUDE_DIRECTORIES, CXX_STANDARD.</summary>
        public List<string> this[string property]
        {
            get
            {
                if (!_properties.TryGetValue(property, out var values)) _properties[property] = values = [];
                return values;
            }
        }

        public bool Has(string property) => _properties.TryGetValue(property, out var values) && values.Count > 0;
    }

    private bool RunTargetCommand(string name, List<string> arguments, Frame frame)
    {
        switch (name)
        {
            case "add_executable":
                AddTarget(arguments, frame, executable: true);
                return true;
            case "add_library":
                AddTarget(arguments, frame, executable: false);
                return true;
            case "target_sources":
                TargetSources(arguments, frame);
                return true;
            case "target_include_directories":
                TargetItems(arguments, "INCLUDE_DIRECTORIES", folder => Full(folder, frame.SourceFolder));
                return true;
            case "target_compile_definitions":
                TargetItems(arguments, "COMPILE_DEFINITIONS", WithoutDefinePrefix);
                return true;
            case "target_compile_options":
                TargetItems(arguments, "COMPILE_OPTIONS", option => option);
                return true;
            case "target_compile_features":
                TargetItems(arguments, "COMPILE_FEATURES", feature => feature);
                return true;
            case "target_link_directories":
                TargetItems(arguments, "LINK_DIRECTORIES", folder => Full(folder, frame.SourceFolder));
                return true;
            case "target_link_libraries":
                TargetLinkLibraries(arguments);
                return true;
            case "include_directories":
                ForTheFolder(frame, folderList: frame.Directory.IncludeDirectories, property: "INCLUDE_DIRECTORIES",
                    arguments.Where(argument => argument is not ("AFTER" or "BEFORE" or "SYSTEM")).Select(folder => Full(folder, frame.SourceFolder)));
                return true;
            case "add_compile_definitions":
                ForTheFolder(frame, frame.Directory.Definitions, "COMPILE_DEFINITIONS", arguments.Select(WithoutDefinePrefix));
                return true;
            case "add_definitions":
                ForTheFolder(frame, frame.Directory.Definitions, "COMPILE_DEFINITIONS", arguments.Where(IsDefineFlag).Select(WithoutDefinePrefix));
                ForTheFolder(frame, frame.Directory.CompileOptions, "COMPILE_OPTIONS", arguments.Where(argument => !IsDefineFlag(argument)));
                return true;
            case "remove_definitions":
                RemoveDefinitions(arguments, frame);
                return true;
            case "add_compile_options":
                frame.Directory.CompileOptions.AddRange(arguments);
                return true;
            case "link_libraries":
                frame.Directory.LinkLibraries.AddRange(arguments);
                return true;
            case "link_directories":
                frame.Directory.LinkDirectories.AddRange(arguments.Where(argument => argument is not ("AFTER" or "BEFORE")).Select(folder => Full(folder, frame.SourceFolder)));
                return true;
            case "set_target_properties":
                SetTargetProperties(arguments);
                return true;
            case "set_property":
                SetProperty(arguments);
                return true;
            case "get_target_property":
                GetTargetProperty(arguments, frame);
                return true;
            default:
                return false;
        }
    }

    private void AddTarget(List<string> arguments, Frame frame, bool executable)
    {
        if (arguments.Count == 0) return;

        var name = arguments[0];
        var rest = arguments.Skip(1).ToList();

        if (rest.IndexOf("ALIAS") is var alias and >= 0)
        {
            if (alias + 1 < rest.Count) _aliases[name] = rest[alias + 1];
            return;
        }

        if (rest.Contains("IMPORTED"))
        {
            DefineImported(name, threads: false);
            return;
        }

        var kind = executable ? TargetKind.Executable : rest.Contains("INTERFACE") ? TargetKind.InterfaceLibrary : TargetKind.Library;
        var target = new CMakeTarget
        {
            Name = name,
            Kind = kind,
            SourceFolder = frame.SourceFolder,
            Directory = frame.Directory,
            ExcludedFromAll = frame.Directory.ExcludedFromAll || rest.Contains("EXCLUDE_FROM_ALL"),
        };

        target["SOURCES"].AddRange(rest.Where(argument => !TargetKeywords.Contains(argument)).Select(source => Full(source, frame.SourceFolder)));

        // What the folder gives every target made in it.
        target["INCLUDE_DIRECTORIES"].AddRange(frame.Directory.IncludeDirectories);
        target["COMPILE_DEFINITIONS"].AddRange(frame.Directory.Definitions);
        target["COMPILE_OPTIONS"].AddRange(frame.Directory.CompileOptions);
        target["LINK_LIBRARIES"].AddRange(frame.Directory.LinkLibraries);
        target["LINK_DIRECTORIES"].AddRange(frame.Directory.LinkDirectories);

        foreach (var (variable, property) in PropertiesFromVariables)
        {
            if (IsDefined(variable, frame)) target[property].Add(Variable(variable, frame));
        }

        _targets[name] = target;
        _targetsInOrder.Add(target);
    }

    private void DefineImported(string name, bool threads)
    {
        if (_targets.ContainsKey(name)) return;

        _targets[name] = new CMakeTarget { Name = name, Kind = TargetKind.Imported, SourceFolder = "", IsThreads = threads };
    }

    private CMakeTarget? TargetNamed(string name) =>
        _targets.TryGetValue(name, out var target) ? target
        : _aliases.TryGetValue(name, out var aliased) && _targets.TryGetValue(aliased, out var throughAlias) ? throughAlias
        : null;

    /// <summary>target_include_directories and the rest: each item for the target itself (PRIVATE), for what links it (INTERFACE), or both (PUBLIC).</summary>
    private void TargetItems(List<string> arguments, string property, Func<string, string> asWritten)
    {
        if (arguments.Count == 0 || TargetNamed(arguments[0]) is not { } target) return;

        var scope = "PRIVATE";

        foreach (var argument in arguments.Skip(1))
        {
            switch (argument)
            {
                case "SYSTEM" or "AFTER" or "BEFORE":
                    continue;
                case "PRIVATE" or "PUBLIC" or "INTERFACE":
                    scope = argument;
                    continue;
            }

            var item = asWritten(argument);
            if (scope != "INTERFACE") target[property].Add(item);
            if (scope != "PRIVATE") target["INTERFACE_" + property].Add(item);
        }
    }

    /// <summary>target_sources: more sources, or a FILE_SET of headers whose BASE_DIRS are folders its headers are found in.</summary>
    private void TargetSources(List<string> arguments, Frame frame)
    {
        if (arguments.Count == 0 || TargetNamed(arguments[0]) is not { } target) return;

        var scope = "PRIVATE";
        string? fileSetPart = null;
        var isHeaderSet = false;
        var baseFolders = new List<string>();

        void EndFileSet()
        {
            if (isHeaderSet)
            {
                var folders = baseFolders.Count > 0 ? baseFolders : [frame.SourceFolder];
                if (scope != "INTERFACE") target["INCLUDE_DIRECTORIES"].AddRange(folders);
                if (scope != "PRIVATE") target["INTERFACE_INCLUDE_DIRECTORIES"].AddRange(folders);
            }

            fileSetPart = null;
            isHeaderSet = false;
            baseFolders = [];
        }

        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];

            switch (argument)
            {
                case "PRIVATE" or "PUBLIC" or "INTERFACE":
                    EndFileSet();
                    scope = argument;
                    continue;
                case "FILE_SET" when index + 1 < arguments.Count:
                    EndFileSet();
                    fileSetPart = "NAME";
                    isHeaderSet = arguments[++index] == "HEADERS";
                    continue;
                case "TYPE" or "BASE_DIRS" or "FILES" when fileSetPart is not null:
                    fileSetPart = argument;
                    continue;
            }

            if (fileSetPart == "TYPE") isHeaderSet = argument == "HEADERS";
            else if (fileSetPart == "BASE_DIRS") baseFolders.Add(Full(argument, frame.SourceFolder));
            else if (fileSetPart is null)
            {
                var source = Full(argument, frame.SourceFolder);
                if (scope != "INTERFACE") target["SOURCES"].Add(source);
                if (scope != "PRIVATE") target["INTERFACE_SOURCES"].Add(source);
            }
        }

        EndFileSet();
    }

    /// <summary>
    /// target_link_libraries, in its forms: PRIVATE, PUBLIC and INTERFACE; the older LINK_PRIVATE and LINK_PUBLIC; and plain,
    /// which links the items and passes them on. debug and optimized pick items for one configuration: this reads Debug's.
    /// </summary>
    private void TargetLinkLibraries(List<string> arguments)
    {
        if (arguments.Count == 0 || TargetNamed(arguments[0]) is not { } target) return;

        var scope = "PUBLIC";
        var skipNext = false;
        var keepNext = false;

        foreach (var argument in arguments.Skip(1))
        {
            switch (argument)
            {
                case "PRIVATE" or "LINK_PRIVATE":
                    scope = "PRIVATE";
                    continue;
                case "PUBLIC" or "LINK_PUBLIC":
                    scope = "PUBLIC";
                    continue;
                case "INTERFACE" or "LINK_INTERFACE_LIBRARIES":
                    scope = "INTERFACE";
                    continue;
                case "debug" or "general" when !keepNext && !skipNext:
                    keepNext = true;
                    continue;
                case "optimized" when !keepNext && !skipNext:
                    skipNext = true;
                    continue;
            }

            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            keepNext = false;

            if (scope != "INTERFACE") target["LINK_LIBRARIES"].Add(argument);
            if (scope != "PRIVATE") target["INTERFACE_LINK_LIBRARIES"].Add(argument);
        }
    }

    /// <summary>include_directories and add_compile_definitions: for the folder, so for every target in it - those made before as well - and in the folders it adds after.</summary>
    private void ForTheFolder(Frame frame, List<string> folderList, string property, IEnumerable<string> items)
    {
        var added = items.ToList();
        folderList.AddRange(added);

        foreach (var target in _targetsInOrder.Where(target => target.Directory == frame.Directory)) target[property].AddRange(added);
    }

    private void RemoveDefinitions(List<string> arguments, Frame frame)
    {
        var removed = arguments.Where(IsDefineFlag).Select(WithoutDefinePrefix).ToHashSet(StringComparer.Ordinal);

        frame.Directory.Definitions.RemoveAll(removed.Contains);
        foreach (var target in _targetsInOrder.Where(target => target.Directory == frame.Directory)) target["COMPILE_DEFINITIONS"].RemoveAll(removed.Contains);
    }

    private static bool IsDefineFlag(string flag) => flag.StartsWith("-D", StringComparison.Ordinal) || flag.StartsWith("/D", StringComparison.Ordinal);

    private static string WithoutDefinePrefix(string definition) => IsDefineFlag(definition) ? definition[2..] : definition;

    private void SetTargetProperties(List<string> arguments)
    {
        var properties = arguments.IndexOf("PROPERTIES");
        if (properties < 0) return;

        foreach (var target in arguments.Take(properties).Select(TargetNamed).OfType<CMakeTarget>())
        {
            for (var index = properties + 1; index + 1 < arguments.Count; index += 2)
            {
                target[arguments[index]].Clear();
                target[arguments[index]].AddRange(SplitList(arguments[index + 1]));
            }
        }
    }

    /// <summary>set_property(TARGET ... PROPERTY ...): the target properties; other kinds of property give a program nothing FixFinder builds with.</summary>
    private void SetProperty(List<string> arguments)
    {
        if (arguments.FirstOrDefault() != "TARGET") return;

        var property = arguments.IndexOf("PROPERTY");
        if (property < 0 || property + 1 >= arguments.Count) return;

        var append = arguments.Take(property).Contains("APPEND") || arguments.Take(property).Contains("APPEND_STRING");
        var name = arguments[property + 1];
        var values = arguments.Skip(property + 2).ToList();

        foreach (var target in arguments.Skip(1).Take(property - 1).Where(word => word is not ("APPEND" or "APPEND_STRING")).Select(TargetNamed).OfType<CMakeTarget>())
        {
            if (!append) target[name].Clear();
            target[name].AddRange(values);
        }
    }

    private void GetTargetProperty(List<string> arguments, Frame frame)
    {
        if (arguments.Count < 3) return;

        var value = TargetNamed(arguments[1]) is { } target && target.Has(arguments[2]) ? JoinList(target[arguments[2]]) : $"{arguments[0]}-NOTFOUND";
        SetVariable(arguments[0], value, frame);
    }

    /// <summary>Every program the project builds, in the order its add_executable commands run.</summary>
    public IReadOnlyList<BuiltProgram> Programs() =>
        _targetsInOrder.Where(target => target.Kind == TargetKind.Executable).Select(ProgramOf).ToList();

    /// <summary>
    /// A program: its own sources and those of every library it links, however indirectly; its own include folders,
    /// definitions and options, and those the libraries it links pass on (PUBLIC and INTERFACE); what it links that is not
    /// a target of the project; and its standard - from C_STANDARD or CXX_STANDARD, raised by a compile feature that asks
    /// for more, and an -std= of its own options last.
    /// </summary>
    private BuiltProgram ProgramOf(CMakeTarget program)
    {
        var linked = new List<CMakeTarget>();
        var passedOnBy = new List<CMakeTarget>();
        var linkedSet = new HashSet<CMakeTarget>();
        var passedOnSet = new HashSet<CMakeTarget>();
        var linkItems = new List<string>();
        var notFollowed = new List<string>(_notFollowed);
        var threads = false;

        void Link(string item, bool takesWhatItPassesOn, int depth)
        {
            if (depth > MostCallDepth) return;

            foreach (var evaluated in Evaluated(item, cpp: null))
            {
                if (TargetNamed(evaluated) is not { } library)
                {
                    linkItems.Add(evaluated);
                    continue;
                }

                threads |= library.IsThreads;

                var passesOnNow = takesWhatItPassesOn && passedOnSet.Add(library);
                var linkedNow = linkedSet.Add(library);

                if (passesOnNow) passedOnBy.Add(library);

                if (linkedNow && library.Kind is TargetKind.Library or TargetKind.Imported)
                {
                    linked.Add(library);
                    foreach (var privateItem in library["LINK_LIBRARIES"]) Link(privateItem, takesWhatItPassesOn: false, depth + 1);
                }

                if (passesOnNow || linkedNow)
                {
                    foreach (var passedOnItem in library["INTERFACE_LINK_LIBRARIES"]) Link(passedOnItem, takesWhatItPassesOn, depth + 1);
                }
            }
        }

        foreach (var item in program["LINK_LIBRARIES"]) Link(item, takesWhatItPassesOn: true, depth: 0);

        var (sources, cannotBeBuilt) = SourcesOf(program, linked, passedOnBy);
        var cpp = sources.Any(NativeFileKinds.IsCpp);

        if (sources.Count == 0) cannotBeBuilt ??= $"no C or C++ file is among what CMakeLists.txt builds {program.Name} from";
        if (cpp && sources.Any(NativeFileKinds.IsC)) cannotBeBuilt ??= "it is built from C and C++ files together, which FixFinder does not build as one program";

        // What reaches the compiler: the program's own, then what each library it links passes on.
        IEnumerable<string> Reaching(string property) =>
            program[property].Concat(passedOnBy.SelectMany(library => library["INTERFACE_" + property])).SelectMany(item => Evaluated(item, cpp));

        var options = Reaching("COMPILE_OPTIONS").ToList();
        var includeFolders = new List<string>();

        foreach (var folder in Reaching("INCLUDE_DIRECTORIES").Concat(options.Where(option => option.StartsWith("-I", StringComparison.Ordinal) && option.Length > 2).Select(option => option[2..])))
        {
            if (Unknowable.IsIn(folder))
            {
                notFollowed.Add(_generatedFiles.Count > 0
                    ? $"it looks for headers in the folder CMake builds in as well, where CMake writes {string.Join(" and ", _generatedFiles.Order(StringComparer.OrdinalIgnoreCase))} - which FixFinder does not write"
                    : "it looks for headers in the folder CMake builds in as well, which FixFinder does not make");
                continue;
            }

            var full = Full(folder, program.SourceFolder);
            if (Directory.Exists(full)) includeFolders.Add(Path.GetFullPath(full));
        }

        var definitions = Reaching("COMPILE_DEFINITIONS")
            .Concat(options.Where(option => option.StartsWith("-D", StringComparison.Ordinal) && option.Length > 2).Select(option => option[2..]))
            .Where(definition => !Unknowable.IsIn(definition))
            .ToList();

        var (libraries, libraryFolders, linkedFiles) = Linking(program, linked, passedOnBy, linkItems, notFollowed, ref threads);
        threads |= options.Contains("-pthread");

        var standard = Standard(program, cpp, Reaching("COMPILE_FEATURES"), options);
        if (_stoppedBecause is not null) notFollowed.Insert(0, _stoppedBecause);

        return new BuiltProgram(
            Name: program.Has("OUTPUT_NAME") ? program["OUTPUT_NAME"][^1] : program.Name,
            Sources: sources,
            IncludeFolders: includeFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Definitions: definitions.Distinct(StringComparer.Ordinal).ToList(),
            Libraries: libraries,
            LibraryFolders: libraryFolders,
            LinkedFiles: linkedFiles,
            Threads: threads,
            Standards: standard is null ? new Dictionary<string, string>() : sources.ToDictionary(source => source, _ => standard, StringComparer.OrdinalIgnoreCase),
            MadeByDefault: !program.ExcludedFromAll,
            IsATest: _testPrograms.Contains(program.Name),
            NotFollowed: notFollowed.Distinct(StringComparer.Ordinal).ToList(),
            CannotBeBuilt: cannotBeBuilt);
    }

    /// <summary>The program's C and C++ files - its own, then each linked library's - and why it cannot be built, if one is missing.</summary>
    private (List<string> Sources, string? CannotBeBuilt) SourcesOf(CMakeTarget program, List<CMakeTarget> linked, List<CMakeTarget> passedOnBy)
    {
        var sources = new List<string>();
        string? cannotBeBuilt = null;

        var written = program["SOURCES"].Select(item => (Item: item, Owner: program))
            .Concat(linked.SelectMany(library => library["SOURCES"].Select(item => (Item: item, Owner: library))))
            .Concat(passedOnBy.SelectMany(library => library["INTERFACE_SOURCES"].Select(item => (Item: item, Owner: library))));

        var pending = new Queue<(string Item, CMakeTarget Owner)>(written);
        var objectLibrariesTaken = new HashSet<string>(StringComparer.Ordinal);

        while (pending.TryDequeue(out var entry))
        {
            foreach (var evaluated in Evaluated(entry.Item, cpp: null))
            {
                if (evaluated.StartsWith(ObjectsOfTarget) && TargetNamed(evaluated[1..]) is { } objectLibrary)
                {
                    if (objectLibrariesTaken.Add(objectLibrary.Name))
                        foreach (var item in objectLibrary["SOURCES"]) pending.Enqueue((item, objectLibrary));
                    continue;
                }

                if (Unknowable.IsIn(evaluated))
                {
                    cannotBeBuilt ??= $"it builds {program.Name} from {string.Join(" and ", Unknowable.SourcesIn(evaluated))}, which FixFinder does not know";
                    continue;
                }

                var path = Full(evaluated, entry.Owner.SourceFolder);

                if (NativeFileKinds.IsAssembly(path))
                {
                    cannotBeBuilt ??= $"it is built from assembly as well ({Path.GetFileName(path)}), which FixFinder does not build";
                }
                else if (NativeFileKinds.IsSource(path))
                {
                    if (File.Exists(path))
                    {
                        var full = Path.GetFullPath(path);
                        if (!sources.Contains(full, StringComparer.OrdinalIgnoreCase)) sources.Add(full);
                    }
                    else
                    {
                        cannotBeBuilt ??= _generatedFiles.Contains(Path.GetFileName(path))
                            ? $"it builds {program.Name} from {Path.GetFileName(path)}, which CMake makes while it builds, and FixFinder does not"
                            : $"{Path.GetFileName(path)}, which CMakeLists.txt builds {program.Name} from, is not there";
                    }
                }
            }
        }

        return (sources, cannotBeBuilt);
    }

    /// <summary>What the program links that is not built from the project's own sources: libraries by name, folders for them, and files.</summary>
    private (List<string> Libraries, List<string> LibraryFolders, List<string> LinkedFiles) Linking(
        CMakeTarget program, List<CMakeTarget> linked, List<CMakeTarget> passedOnBy, List<string> linkItems, List<string> notFollowed, ref bool threads)
    {
        var libraries = new List<string>();
        var libraryFolders = program["LINK_DIRECTORIES"].Concat(passedOnBy.SelectMany(library => library["INTERFACE_LINK_DIRECTORIES"]))
            .Where(folder => !Unknowable.IsIn(folder) && Directory.Exists(folder)).Select(Path.GetFullPath).ToList();
        var linkedFiles = new List<string>();

        foreach (var imported in linked.Where(library => library.Kind == TargetKind.Imported && !library.IsThreads))
        {
            var location = new[] { "IMPORTED_LOCATION_DEBUG", "IMPORTED_LOCATION", "IMPORTED_IMPLIB_DEBUG", "IMPORTED_IMPLIB" }
                .Where(imported.Has).Select(property => imported[property][^1]).FirstOrDefault();

            if (location is not null && File.Exists(location)) linkedFiles.Add(Path.GetFullPath(location));
            else if (!imported.Has("INTERFACE_INCLUDE_DIRECTORIES") && !imported.Has("INTERFACE_LINK_LIBRARIES"))
                notFollowed.Add($"it links {imported.Name}, which a package or a library outside the project gives, and FixFinder does not look for it");
        }

        foreach (var item in linkItems)
        {
            if (Unknowable.IsIn(item))
            {
                notFollowed.Add($"it also links {string.Join(" and ", Unknowable.SourcesIn(item))}, which FixFinder does not know");
            }
            else if (item == "-pthread")
            {
                threads = true;
            }
            else if (item.StartsWith("-l", StringComparison.Ordinal) && item.Length > 2)
            {
                libraries.Add(item[2..]);
            }
            else if (item.StartsWith("-L", StringComparison.Ordinal) && item.Length > 2)
            {
                if (Directory.Exists(item[2..])) libraryFolders.Add(Path.GetFullPath(item[2..]));
            }
            else if (item.StartsWith('-'))
            {
                // A linker flag - -static, -Wl,... - which changes how it links rather than what.
            }
            else if (item.Contains("::", StringComparison.Ordinal))
            {
                notFollowed.Add($"it links {item}, which a package FixFinder does not look for would give");
            }
            else if (item.Contains('/') || item.Contains('\\') || NativeFileKinds.IsArchive(item) || item.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var path = Full(item, program.SourceFolder);
                if (File.Exists(path)) linkedFiles.Add(Path.GetFullPath(path));
                else notFollowed.Add($"it links {item}, which is not there");
            }
            else if (LibraryName().IsMatch(item))
            {
                libraries.Add(item);
            }
        }

        return (libraries.Distinct(StringComparer.Ordinal).ToList(), libraryFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), linkedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// The -std= the program is built with. C_STANDARD or CXX_STANDARD always gives one - gnu11 rather than c11 unless
    /// C_EXTENSIONS is OFF, as CMake gives gcc - raised to what a compile feature asks for if that is more. A compile
    /// feature alone gives one only when it asks for more than gcc's own default from GCC 11, C17 and C++17, as CMake then
    /// leaves the flag out. An -std= in the program's options comes after either and so is the one gcc uses.
    /// </summary>
    private static string? Standard(CMakeTarget program, bool cpp, IEnumerable<string> features, List<string> options)
    {
        var language = cpp ? "cxx" : "c";
        var featureYears = features
            .Select(feature => StandardFeature().Match(feature))
            .Where(match => match.Success && match.Groups["language"].Value == language)
            .Select(match => StandardYear(match.Groups["level"].Value, cpp))
            .Where(year => year > 0)
            .ToList();

        var propertyYear = program.Has(cpp ? "CXX_STANDARD" : "C_STANDARD") ? StandardYear(program[cpp ? "CXX_STANDARD" : "C_STANDARD"][^1], cpp) : 0;
        var extensions = !program.Has(cpp ? "CXX_EXTENSIONS" : "C_EXTENSIONS") || IsTrueConstant(program[cpp ? "CXX_EXTENSIONS" : "C_EXTENSIONS"][^1]);
        var asked = featureYears.Count > 0 ? featureYears.Max() : 0;

        var standard = propertyYear > 0 ? StandardName(Math.Max(propertyYear, asked), cpp, extensions)
            : asked > 2017 ? StandardName(asked, cpp, extensions)
            : null;

        return options.LastOrDefault(option => option.StartsWith("-std=", StringComparison.Ordinal)) is { } own ? own[5..] : standard;
    }

    /// <summary>The year of a standard as CMake numbers it - 11 is C11 or C++11, 98 is C++98, 90 is C90.</summary>
    private static int StandardYear(string level, bool cpp) => (level, cpp) switch
    {
        ("90", false) => 1990,
        ("99", false) => 1999,
        ("98", true) => 1998,
        ("11", _) => 2011,
        ("14", true) => 2014,
        ("17", _) => 2017,
        ("20", true) => 2020,
        ("23", _) => 2023,
        ("26", true) => 2026,
        _ => 0,
    };

    /// <summary>The -std= name gcc and clang take for a standard; C23 as c2x, the name every gcc from 9 accepts.</summary>
    private static string StandardName(int year, bool cpp, bool extensions)
    {
        var level = year switch
        {
            1990 => "90",
            1998 => "98",
            1999 => "99",
            2023 when !cpp => "2x",
            _ => (year % 100).ToString("00", CultureInfo.InvariantCulture),
        };

        return (extensions ? "gnu" : "c") + (cpp ? "++" : "") + level;
    }

    /// <summary>An item with its generator expressions worked out, as the list of items it becomes.</summary>
    private List<string> Evaluated(string item, bool? cpp) => item.Contains("$<", StringComparison.Ordinal) ? SplitList(Generators(item, cpp)) : [item];

    private string Generators(string text, bool? cpp)
    {
        var evaluated = new StringBuilder();

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] == '$' && at + 1 < text.Length && text[at + 1] == '<' && GeneratorEnd(text, at) is var closing and >= 0)
            {
                evaluated.Append(Generator(Generators(text[(at + 2)..closing], cpp), cpp));
                at = closing;
                continue;
            }

            evaluated.Append(text[at]);
        }

        return evaluated.ToString();
    }

    private static int GeneratorEnd(string text, int opening)
    {
        var depth = 0;

        for (var at = opening; at < text.Length; at++)
        {
            if (text[at] == '$' && at + 1 < text.Length && text[at + 1] == '<')
            {
                depth++;
                at++;
            }
            else if (text[at] == '>' && --depth == 0)
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>
    /// One generator expression, its inner ones already worked out: those that choose by the configuration, the platform,
    /// the compiler and the language, and those that only pass their text on. Any other is not known.
    /// </summary>
    private string Generator(string inside, bool? cpp)
    {
        var colon = inside.IndexOf(':');
        var name = colon < 0 ? inside : inside[..colon];
        var argument = colon < 0 ? null : inside[(colon + 1)..];
        var parts = (argument ?? "").Split(',');
        var compilerId = _compiler switch
        {
            NativeCompilerKind.Gnu => "GNU",
            NativeCompilerKind.Clang => "Clang",
            NativeCompilerKind.Msvc => "MSVC",
            _ => "",
        };
        var language = cpp is null ? null : cpp.Value ? "CXX" : "C";

        static string Flag(bool holds) => holds ? "1" : "0";

        return name switch
        {
            "0" => "",
            "1" => argument ?? "",
            "BUILD_INTERFACE" or "BUILD_LOCAL_INTERFACE" or "LINK_ONLY" or "HOST_LINK" => argument ?? "",
            "INSTALL_INTERFACE" or "DEVICE_LINK" => "",
            "BOOL" => Flag(!IsFalseConstant(argument ?? "")),
            "NOT" => Flag(argument == "0"),
            "AND" => Flag(parts.All(part => part == "1")),
            "OR" => Flag(parts.Any(part => part == "1")),
            "IF" when parts.Length == 3 => parts[0] == "1" ? parts[1] : parts[2],
            "STREQUAL" when parts.Length == 2 => Flag(parts[0] == parts[1]),
            "EQUAL" when parts.Length == 2 => Flag(parts[0].Trim() == parts[1].Trim()),
            "IN_LIST" when parts.Length == 2 => Flag(SplitList(parts[1]).Contains(parts[0])),
            "CONFIG" => argument is null ? "Debug" : Flag(parts.Any(config => config.Equals("Debug", StringComparison.OrdinalIgnoreCase))),
            "PLATFORM_ID" => argument is null ? "Windows" : Flag(parts.Contains("Windows")),
            "C_COMPILER_ID" or "CXX_COMPILER_ID" => argument is null ? compilerId : Flag(parts.Contains(compilerId)),
            "COMPILE_LANGUAGE" or "LINK_LANGUAGE" when language is not null => argument is null ? language : Flag(parts.Contains(language)),
            "COMPILE_LANG_AND_ID" when language is not null && parts.Length >= 2 => Flag(parts[0] == language && parts.Skip(1).Contains(compilerId)),
            "TARGET_OBJECTS" => ObjectsOfTarget + (argument ?? ""),
            "TARGET_EXISTS" => Flag(argument is not null && TargetNamed(argument) is not null),
            "ANGLE-R" => ">",
            "COMMA" => ",",
            "SEMICOLON" => ";",
            "LOWER_CASE" => (argument ?? "").ToLowerInvariant(),
            "UPPER_CASE" => (argument ?? "").ToUpperInvariant(),
            _ => Unknowable.Mark($"$<{inside}>"),
        };
    }
}
