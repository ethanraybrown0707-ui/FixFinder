using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution;

/// <summary>
/// What a Rust program is: the file rustc builds it from - its crate root - the folder it is built and run in, the edition
/// of Rust it is written in, and the files its <c>mod</c> lines bring in.
/// </summary>
/// <remarks>
/// In a Cargo package - a folder with a Cargo.toml - the program is src/main.rs, or the file chosen under src/bin, and it
/// runs from the package's folder, as cargo run runs it; its edition is the one Cargo.toml's [package] gives. FixFinder
/// builds it with rustc, never with cargo, which would download the crates Cargo.toml names - so those crates are named
/// in how it ran instead. Anywhere else, the program is the chosen file, in the newest edition the rustc here builds.
/// </remarks>
public static partial class RustProgram
{
    /// <param name="Root">The file rustc is given: src/main.rs in a package, or the chosen file.</param>
    /// <param name="Folder">Where it is built and run: the package's folder, or the chosen file's.</param>
    /// <param name="Edition">The edition it is built as, or null for the newest the rustc here builds.</param>
    /// <param name="CargoFile">The package's Cargo.toml, or null.</param>
    /// <param name="Crates">The crates Cargo.toml's [dependencies] name, which are not built with it.</param>
    public sealed record Layout(string Root, string Folder, string? Edition, string? CargoFile, IReadOnlyList<string> Crates)
    {
        /// <summary>The program's Rust files: the root and those its mod lines bring in, each after the file that names it.</summary>
        public IReadOnlyList<string> Files => FilesOf(Root);
    }

    /// <summary>How many folders above the chosen file a Cargo.toml is looked for in.</summary>
    private const int FoldersLookedUp = 4;

    public static bool IsRust(string file) => Path.GetExtension(file).Equals(".rs", StringComparison.OrdinalIgnoreCase);

    public static Layout Of(string chosen)
    {
        var file = Path.GetFullPath(chosen);
        var folder = Path.GetDirectoryName(file)!;

        if (CargoFileAbove(folder) is not { } cargo) return new Layout(file, folder, null, null, []);

        var package = Path.GetDirectoryName(cargo)!;
        var text = Read(cargo);
        var main = Path.Combine(package, "src", "main.rs");
        var underBin = file.StartsWith(Path.Combine(package, "src", "bin") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        var root = underBin || !File.Exists(main) ? file : main;

        return new Layout(root, package, EditionIn(text), cargo, CratesIn(text));
    }

    private static string? CargoFileAbove(string folder)
    {
        var looking = new DirectoryInfo(folder);

        for (var level = 0; looking is not null && level <= FoldersLookedUp; level++, looking = looking.Parent)
        {
            var cargo = Path.Combine(looking.FullName, "Cargo.toml");
            if (File.Exists(cargo)) return cargo;
        }

        return null;
    }

    private static string Read(string file)
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

    [GeneratedRegex(@"^\s*\[(?<name>[^\]]+)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex TableHeading();

    [GeneratedRegex(@"^\s*edition\s*=\s*""(?<edition>\d{4})""", RegexOptions.Multiline)]
    private static partial Regex EditionLine();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z0-9_-]+)\s*=", RegexOptions.Multiline)]
    private static partial Regex KeyLine();

    /// <summary>The text of one table of a Cargo.toml - [package], [dependencies] - up to the next table.</summary>
    private static string Table(string toml, string name)
    {
        var headings = TableHeading().Matches(toml).ToList();
        var heading = headings.FirstOrDefault(each => each.Groups["name"].Value.Trim() == name);
        if (heading is null) return "";

        var next = headings.FirstOrDefault(each => each.Index > heading.Index);
        var start = heading.Index + heading.Length;
        return toml[start..(next?.Index ?? toml.Length)];
    }

    /// <summary>The edition [package] gives, or null when it gives none - which cargo takes as 2015.</summary>
    public static string? EditionIn(string toml) =>
        EditionLine().Match(Table(toml, "package")) is { Success: true } edition ? edition.Groups["edition"].Value : null;

    /// <summary>The crates [dependencies] names.</summary>
    public static IReadOnlyList<string> CratesIn(string toml) =>
        KeyLine().Matches(Table(toml, "dependencies")).Select(key => key.Groups["name"].Value).Distinct().ToList();

    [GeneratedRegex(@"^\s*(?:pub(?:\([^)]*\))?\s+)?mod\s+(?<name>[A-Za-z_]\w*)\s*;", RegexOptions.Multiline)]
    private static partial Regex ModLine();

    /// <summary>
    /// A file and the files its <c>mod name;</c> lines bring in - name.rs beside it, or name/mod.rs - and theirs in turn,
    /// as rustc finds them. A module of a file that is not a crate root or a mod.rs is looked for in the folder named after
    /// that file, as rustc looks.
    /// </summary>
    private static IReadOnlyList<string> FilesOf(string root)
    {
        var files = new List<string>();
        var waiting = new Queue<(string File, bool OwnsFolder)>([(root, true)]);

        while (waiting.Count > 0 && files.Count < 200)
        {
            var (file, ownsFolder) = waiting.Dequeue();
            if (files.Contains(file, StringComparer.OrdinalIgnoreCase) || !File.Exists(file)) continue;
            files.Add(file);

            var folder = Path.GetDirectoryName(file)!;
            var modulesFolder = ownsFolder || Path.GetFileName(file).Equals("mod.rs", StringComparison.OrdinalIgnoreCase)
                ? folder
                : Path.Combine(folder, Path.GetFileNameWithoutExtension(file));

            foreach (Match mod in ModLine().Matches(Read(file)))
            {
                var name = mod.Groups["name"].Value;
                var beside = Path.Combine(modulesFolder, name + ".rs");
                var inFolder = Path.Combine(modulesFolder, name, "mod.rs");

                if (File.Exists(beside)) waiting.Enqueue((beside, false));
                else if (File.Exists(inFolder)) waiting.Enqueue((inFolder, true));
            }
        }

        return files;
    }
}
