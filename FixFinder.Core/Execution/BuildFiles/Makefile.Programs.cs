namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>The programs a Makefile builds: its link commands, and each object file followed back to its source.</summary>
internal sealed partial class Makefile
{
    /// <summary>GNU make's own rules for making an object file from a source, used when the Makefile has no rule of its own.</summary>
    private static readonly (string SourceExtension, string Recipe)[] BuiltInCompileRules =
    [
        (".c", "$(COMPILE.c) $(OUTPUT_OPTION) $<"),
        (".cc", "$(COMPILE.cc) $(OUTPUT_OPTION) $<"),
        (".cpp", "$(COMPILE.cpp) $(OUTPUT_OPTION) $<"),
    ];

    /// <summary>GNU make's own rules for linking a program named like its object file or its source: app from app.o or app.c.</summary>
    private static readonly (string Extension, string Recipe)[] BuiltInLinkRules =
    [
        (".o", "$(LINK.o) $^ $(LOADLIBES) $(LDLIBS) -o $@"),
        (".c", "$(LINK.c) $^ $(LOADLIBES) $(LDLIBS) -o $@"),
        (".cc", "$(LINK.cc) $^ $(LOADLIBES) $(LDLIBS) -o $@"),
        (".cpp", "$(LINK.cpp) $^ $(LOADLIBES) $(LDLIBS) -o $@"),
    ];

    /// <summary>A compiler command a recipe runs, and the folder it runs in - the Makefile's, or the one a cd before it went to.</summary>
    private sealed record RanCommand(CompilerCommand Compiler, string Folder);

    /// <summary>Every program the Makefile links, in the order its targets are written.</summary>
    public IReadOnlyList<BuiltProgram> Programs()
    {
        var madeByDefault = TargetsMadeByDefault();
        var programs = new List<BuiltProgram>();

        foreach (var target in _targetsInOrder.ToList())
        {
            if (target.Name.StartsWith('.') && !target.Name.Contains('/')) continue;

            var commands = target.Recipe is { Count: > 0 } recipe
                ? CompilerCommands(recipe, AutomaticFor(target), target)
                : _phony.Contains(target.Name) ? [] : MadeByMakesOwnRules(target);

            foreach (var link in commands.Where(command => command.Compiler is { CompilesOnly: false, MakesALibrary: false } compiler &&
                                                           compiler.Inputs.Count + compiler.NotKnown.Count > 0))
            {
                programs.Add(ProgramFrom(link, target, madeByDefault.Contains(target.Name)));
            }
        }

        return programs;
    }

    /// <summary>The compiler commands a recipe runs, each line expanded the way make expands it for the target.</summary>
    private List<RanCommand> CompilerCommands(IEnumerable<string> recipe, Automatic automatic, MakeTarget? target)
    {
        var scope = new Scope(target, automatic, null, null, 0);
        var commands = new List<RanCommand>();

        foreach (var line in recipe)
        {
            foreach (var expandedLine in Expand(line, scope).Split('\n'))
            {
                // Each line runs in a shell of its own, starting in the Makefile's folder; a cd changes it for the rest of that line.
                var folder = _folder;

                foreach (var words in ShellWords.Commands(expandedLine.TrimStart(' ', '\t', '@', '-', '+')))
                {
                    if (words[0] == "cd")
                    {
                        if (words.Count > 1 && !Unknowable.IsIn(words[1])) folder = Full(words[1], folder);
                        continue;
                    }

                    if (CompilerCommand.Read(words) is { } compiler) commands.Add(new RanCommand(compiler, folder));
                }
            }
        }

        return commands;
    }

    private Automatic AutomaticFor(MakeTarget target) =>
        new(target.Name, target.Prerequisites.Select(Found).ToList(), target.OrderOnly.Select(Found).ToList(), target.Stem ?? StemBySuffix(target.Name));

    /// <summary>$* for a target no pattern made: its name without a suffix make knows, as make sets it.</summary>
    private string? StemBySuffix(string name) =>
        Path.GetExtension(name) is { Length: > 0 } suffix && _suffixes.Contains(suffix) ? name[..^suffix.Length] : null;

    /// <summary>The link command for a target with no recipe: a pattern rule of the Makefile's own, or one of make's built-in ones.</summary>
    private List<RanCommand> MadeByMakesOwnRules(MakeTarget target)
    {
        if (PatternRuleFor(target.Name, target, chained: 0) is { } own) return CompilerCommands(own.Rule.Recipe, own.Automatic, target);
        if (!BuiltInRulesApply) return [];

        foreach (var (extension, recipe) in BuiltInLinkRules)
        {
            var prerequisite = target.Name + extension;

            var applies = extension == ".o"
                ? target.Prerequisites.Any(written => string.Equals(Normalised(written), prerequisite, StringComparison.OrdinalIgnoreCase)) || _targets.ContainsKey(prerequisite)
                : File.Exists(Full(Found(prerequisite), _folder));

            if (!applies) continue;

            var automatic = new Automatic(target.Name, [Found(prerequisite), .. target.Prerequisites.Select(Found)], target.OrderOnly, target.Name);
            return CompilerCommands([recipe], automatic, target);
        }

        return [];
    }

    /// <summary>The MAKEFLAGS a Makefile turns make's own rules off with.</summary>
    private static readonly string[] NoBuiltInRules = ["-r", "-R", "--no-builtin-rules", "--no-builtin-variables"];

    /// <summary>Whether make's own rules are there to use: not when the Makefile turns them off in MAKEFLAGS or with an empty .SUFFIXES.</summary>
    private bool BuiltInRulesApply =>
        _suffixes.Contains(".o") && !Words(Value("MAKEFLAGS", Scope.Everywhere)).Any(flag => NoBuiltInRules.Contains(flag, StringComparer.Ordinal));

    /// <summary>
    /// The pattern rule of the Makefile's own that makes a file: the one whose first prerequisite is there - or, once, can
    /// itself be made - with the shortest stem, and the first written of those, as GNU make chooses.
    /// </summary>
    private (PatternRule Rule, Automatic Automatic)? PatternRuleFor(string name, MakeTarget? target, int chained)
    {
        var candidates = new List<(PatternRule Rule, string Stem, List<string> Prerequisites, int Order)>();

        foreach (var (rule, order) in _patternRules.Select((rule, order) => (rule, order)))
        {
            if (rule.Recipe.Count == 0) continue;

            foreach (var pattern in rule.Targets)
            {
                if (PatternMatch(pattern, name) is not { } match) continue;

                var prerequisites = rule.Prerequisites.Select(prerequisite => prerequisite.Contains('%') ? match.Folder + WithStem(prerequisite, match.Stem) : prerequisite).ToList();
                if (prerequisites.Count > 0 && !CanBeMade(prerequisites[0], chained)) continue;

                candidates.Add((rule, match.Folder + match.Stem, prerequisites, order));
                break;
            }
        }

        if (candidates.Count == 0) return null;

        var chosen = candidates.OrderBy(candidate => candidate.Stem.Length).ThenBy(candidate => candidate.Order).First();
        IEnumerable<string> explicitPrerequisites = target?.Prerequisites ?? [];

        return (chosen.Rule, new Automatic(name, [.. chosen.Prerequisites.Select(Found), .. explicitPrerequisites.Select(Found)], target?.OrderOnly ?? [], chosen.Stem));
    }

    /// <summary>
    /// How a file name matches a target pattern. A pattern without a / is matched against the name without its folder,
    /// and the folder is put back in front of the prerequisites - so %.o: %.c makes src/list.o from src/list.c.
    /// </summary>
    private static (string Stem, string Folder)? PatternMatch(string pattern, string name)
    {
        if (pattern.Contains('/')) return StemOf(pattern, name) is { } stem ? (stem, "") : null;

        var slash = name.LastIndexOf('/');
        var folder = slash < 0 ? "" : name[..(slash + 1)];

        return StemOf(pattern, name[folder.Length..]) is { } nameStem ? (nameStem, folder) : null;
    }

    private bool CanBeMade(string prerequisite, int chained) =>
        File.Exists(Full(Found(prerequisite), _folder)) ||
        _targets.ContainsKey(Normalised(prerequisite)) ||
        (chained == 0 && NativeFileKinds.IsObject(prerequisite) && ObjectSource(Normalised(prerequisite), chained: 1) is not null);

    /// <summary>
    /// Where make finds a prerequisite that is not where it is named: the folders vpath and VPATH give it. A file that is
    /// there, or that a rule makes, is used as it is named.
    /// </summary>
    private string Found(string prerequisite)
    {
        if (File.Exists(Full(prerequisite, _folder)) || _targets.ContainsKey(Normalised(prerequisite))) return prerequisite;

        var searched = _searchFolders
            .Where(entry => StemOf(entry.NamePattern, prerequisite) is not null)
            .SelectMany(entry => entry.Folders)
            .Concat(Value("VPATH", Scope.Everywhere).Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries));

        foreach (var folder in searched)
        {
            var candidate = $"{folder.TrimEnd('/', '\\')}/{prerequisite}";
            if (File.Exists(Full(candidate, _folder))) return candidate;
        }

        return prerequisite;
    }

    /// <summary>The source an object file is compiled from, and the command that compiles it - by the Makefile's rule for it, its pattern rules, or make's own.</summary>
    private (string Source, RanCommand Command)? ObjectSource(string objectName, int chained)
    {
        _targets.TryGetValue(objectName, out var target);

        List<RanCommand> commands;

        if (target?.Recipe is { Count: > 0 } recipe)
        {
            commands = CompilerCommands(recipe, AutomaticFor(target), target);
        }
        else if (PatternRuleFor(objectName, target, chained) is { } pattern)
        {
            commands = CompilerCommands(pattern.Rule.Recipe, pattern.Automatic, target);
        }
        else if (BuiltInRulesApply && objectName.EndsWith(".o", StringComparison.OrdinalIgnoreCase) &&
                 BuiltInCompileRules.FirstOrDefault(rule => File.Exists(Full(Found(objectName[..^2] + rule.SourceExtension), _folder))) is { Recipe: not null } builtIn)
        {
            var source = Found(objectName[..^2] + builtIn.SourceExtension);
            IEnumerable<string> explicitPrerequisites = target?.Prerequisites ?? [];
            commands = CompilerCommands([builtIn.Recipe], new Automatic(objectName, [source, .. explicitPrerequisites.Select(Found)], [], objectName[..^2]), target);
        }
        else
        {
            return null;
        }

        foreach (var command in commands.Where(command => command.Compiler.CompilesOnly))
        {
            if (command.Compiler.Inputs.FirstOrDefault(NativeFileKinds.IsSource) is { } source) return (Full(source, command.Folder), command);
        }

        return null;
    }

    /// <summary>A program from its link command: each file it is given followed back to a source, and the flags they are compiled with.</summary>
    private BuiltProgram ProgramFrom(RanCommand link, MakeTarget target, bool madeByDefault)
    {
        var sources = new List<string>();
        var compiledBy = new List<RanCommand>();
        var linkedFiles = new List<string>();
        var standards = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notFollowed = new List<string>(_notFollowed);
        string? cannotBeBuilt = null;

        void AddSource(string path, RanCommand command)
        {
            var full = Full(path, command.Folder);

            if (!File.Exists(full))
            {
                cannotBeBuilt ??= $"{Shown(full)}, which it builds from, is not there";
                return;
            }

            if (sources.Contains(full, StringComparer.OrdinalIgnoreCase)) return;

            sources.Add(full);
            compiledBy.Add(command);
            if (command.Compiler.Standard is { } standard) standards[full] = standard;
        }

        void AddObject(string path, string folder)
        {
            var objectName = Normalised(Path.GetRelativePath(_folder, Full(path, folder)));

            if (ObjectSource(objectName, chained: 0) is { } made) AddSource(made.Source, made.Command);
            else if (File.Exists(Full(path, folder))) linkedFiles.Add(Full(path, folder));
            else cannotBeBuilt ??= $"no rule says how {path} is made";
        }

        foreach (var input in link.Compiler.Inputs)
        {
            foreach (var path in PathPattern.HasWildcards(input) ? PathPattern.Matches(link.Folder, input) : [input])
            {
                if (NativeFileKinds.IsSource(path))
                {
                    AddSource(path, link);
                }
                else if (NativeFileKinds.IsObject(path))
                {
                    AddObject(path, link.Folder);
                }
                else if (NativeFileKinds.IsArchive(path))
                {
                    var archiveName = Normalised(Path.GetRelativePath(_folder, Full(path, link.Folder)));

                    if (_targets.TryGetValue(archiveName, out var archive) && archive.Prerequisites.Any(NativeFileKinds.IsObject))
                    {
                        foreach (var member in archive.Prerequisites.Where(NativeFileKinds.IsObject)) AddObject(Found(member), _folder);
                    }
                    else if (File.Exists(Full(path, link.Folder)))
                    {
                        linkedFiles.Add(Full(path, link.Folder));
                    }
                    else
                    {
                        cannotBeBuilt ??= $"no rule says how {path} is made";
                    }
                }
                else if (NativeFileKinds.IsAssembly(path))
                {
                    cannotBeBuilt ??= $"it is built from assembly as well ({path}), which FixFinder does not build";
                }
            }
        }

        var commands = compiledBy.Append(link).Distinct().ToList();

        foreach (var notKnown in commands.SelectMany(command => command.Compiler.NotKnown).SelectMany(Unknowable.SourcesIn).Distinct(StringComparer.Ordinal))
            notFollowed.Add($"it also gives the compiler {notKnown}, which FixFinder does not run");

        if (sources.Count == 0)
            cannotBeBuilt ??= link.Compiler.NotKnown.Count > 0
                ? $"it names its files with {string.Join(" and ", link.Compiler.NotKnown.SelectMany(Unknowable.SourcesIn).Distinct(StringComparer.Ordinal))}, which FixFinder does not run"
                : "no C or C++ file is among what it is built from";

        if (sources.Any(NativeFileKinds.IsC) && sources.Any(NativeFileKinds.IsCpp))
            cannotBeBuilt ??= "it is built from C and C++ files together, which FixFinder does not build as one program";

        return new BuiltProgram(
            Name: Path.GetFileNameWithoutExtension(link.Compiler.Output ?? target.Name),
            Sources: sources,
            IncludeFolders: commands.SelectMany(command => command.Compiler.IncludeFolders.Select(folder => Full(folder, command.Folder)))
                .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Definitions: commands.SelectMany(command => command.Compiler.Definitions).DistinctBy(DefinitionName, StringComparer.Ordinal).ToList(),
            Libraries: link.Compiler.Libraries.Distinct(StringComparer.Ordinal).ToList(),
            LibraryFolders: link.Compiler.LibraryFolders.Select(folder => Full(folder, link.Folder)).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            LinkedFiles: linkedFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Threads: commands.Any(command => command.Compiler.Threads),
            Standards: standards,
            MadeByDefault: madeByDefault,
            IsATest: false,
            NotFollowed: notFollowed.Distinct(StringComparer.Ordinal).ToList(),
            CannotBeBuilt: cannotBeBuilt);
    }

    private static string DefinitionName(string definition) => definition.Split('=')[0];

    /// <summary>A path as the Makefile's folder sees it: src/list.c.</summary>
    private string Shown(string path) => Path.GetRelativePath(_folder, path).Replace('\\', '/');

    /// <summary>The targets plain make builds: the first target, or .DEFAULT_GOAL, and all it needs.</summary>
    private HashSet<string> TargetsMadeByDefault()
    {
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var goal = Value(".DEFAULT_GOAL", Scope.Everywhere).Trim() is { Length: > 0 } named ? named : _firstTarget;
        if (goal is null) return reached;

        var pending = new Stack<string>([Normalised(goal)]);

        while (pending.TryPop(out var name))
        {
            if (!reached.Add(name) || !_targets.TryGetValue(name, out var target)) continue;

            foreach (var prerequisite in target.Prerequisites) pending.Push(Normalised(prerequisite));
        }

        return reached;
    }
}
