using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Ranking;

namespace FixFinder.Core.Sources;

/// <summary>
/// Turns "No module named 'yaml'" into the command that installs it.
/// </summary>
/// <remarks>
/// One of the commonest errors there is, and the fix is not a patch to anything - the code is
/// right, the environment is short of a package. <see cref="FixTier.Dependency"/> was reserved
/// from the start for exactly this shape of answer.
/// <para>
/// <b>The import name is not the package name</b> often enough to matter. <c>import yaml</c>
/// comes from <c>pyyaml</c>, <c>import cv2</c> from <c>opencv-python</c>, <c>import PIL</c> from
/// <c>pillow</c>. Telling someone to run <c>pip install yaml</c> sends them to a package that is
/// either missing or, worse, squatted - so the mismatches are listed rather than guessed.
/// </para>
/// <para>
/// <b>The module name arrives from untrusted output.</b> It is read out of whatever the program
/// printed, and a program can print anything it likes - including
/// <c>No module named '--index-url=http://evil'</c>. So the name is validated against a strict
/// identifier pattern before it goes anywhere near a command line, it is passed as its own
/// argument rather than through a shell, and the exact command is shown and confirmed before it
/// runs. A name that is not a plain Python identifier is refused outright.
/// </para>
/// </remarks>
public static partial class MissingModule
{
    /// <summary>
    /// The name, taken whole from between the quotes rather than up to the first space.
    /// </summary>
    /// <remarks>
    /// Stopping at whitespace looked equivalent and was not: <c>No module named
    /// 'requests &amp;&amp; curl evil.invalid'</c> would have been read as <c>requests</c> and
    /// offered as a perfectly ordinary install, quietly turning a line a program made up into a
    /// different, plausible one. Taking everything inside the quotes means the validation below
    /// sees exactly what was printed and refuses it.
    /// </remarks>
    [GeneratedRegex(@"No module named\s+(?:'(?<name>[^']*)'|""(?<name>[^""]*)""|(?<name>\S+))")]
    private static partial Regex MissingPattern();

    /// <summary>
    /// A module name and nothing else: letters, digits, underscores and dots.
    /// </summary>
    /// <remarks>
    /// The security boundary of this whole file. Anchored at both ends, no leading dash, no
    /// slashes, no spaces - so nothing read out of a program's output can turn into a switch, a
    /// path or a second command.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex SafeName();

    /// <summary>
    /// Import names whose package is called something else.
    /// </summary>
    /// <remarks>
    /// Hand-written and deliberately short. Every entry is a case where installing the obvious
    /// name gets you the wrong thing or nothing at all; anything not listed falls back to the
    /// import name, which is right far more often than not.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownPackages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["yaml"] = "pyyaml",
        ["cv2"] = "opencv-python",
        ["PIL"] = "pillow",
        ["sklearn"] = "scikit-learn",
        ["bs4"] = "beautifulsoup4",
        ["serial"] = "pyserial",
        ["dateutil"] = "python-dateutil",
        ["dotenv"] = "python-dotenv",
        ["jwt"] = "pyjwt",
        ["OpenSSL"] = "pyopenssl",
        ["win32api"] = "pywin32",
        ["win32com"] = "pywin32",
        ["pkg_resources"] = "setuptools",
        ["attr"] = "attrs",
        ["google"] = "protobuf",
        ["magic"] = "python-magic",
        ["fitz"] = "pymupdf",
        ["docx"] = "python-docx",
        ["pptx"] = "python-pptx",
        ["Crypto"] = "pycryptodome",
    };

    /// <summary>What is missing and what installs it.</summary>
    /// <param name="Module">The name as the program imported it.</param>
    /// <param name="Package">The name pip knows it by, which is often the same.</param>
    public sealed record Missing(string Module, string Package);

    /// <summary>Reads the missing module out of an error, or returns null.</summary>
    public static Missing? Read(ParsedError error)
    {
        if (error.ExceptionType is not { Length: > 0 } type) return null;

        if (!type.EndsWith("ModuleNotFoundError", StringComparison.Ordinal) &&
            !type.EndsWith("ImportError", StringComparison.Ordinal))
        {
            return null;
        }

        var match = MissingPattern().Match(error.Message ?? "");
        if (!match.Success) return null;

        var module = match.Groups["name"].Value;

        // Refused rather than sanitised. Anything that is not a plain import name did not come
        // from a real ModuleNotFoundError, and the interesting question is not how to clean it
        // up but why the program printed it.
        if (!SafeName().IsMatch(module)) return null;

        // A failed submodule import means the top-level distribution is missing.
        var top = module.Split('.')[0];

        return new Missing(module, KnownPackages.TryGetValue(top, out var package) ? package : top);
    }

    /// <summary>
    /// Builds the candidate offering the install, or null when there is nothing to offer.
    /// </summary>
    /// <remarks>
    /// Installed with the interpreter that actually crashed - <c>&lt;that python&gt; -m pip</c>
    /// rather than whichever <c>pip</c> is first on PATH. On a machine with several Pythons those
    /// are different environments, and installing into the wrong one produces the most confusing
    /// possible outcome: a successful install and an unchanged error.
    /// </remarks>
    public static FixCandidate? For(ParsedError error, TargetSpec? spec)
    {
        if (Read(error) is not { } missing) return null;

        var interpreter = Interpreter(spec);
        if (interpreter is null) return null;

        // A misspelt standard module is not a package to install. `import maths` using maths.sqrt
        // means math, and pip install maths would download somebody else's package to fix a typo.
        // The local fix offers the corrected import instead.
        var lines = FixFinder.Core.LocalFixes.SourceFile.Read(FixFinder.Core.LocalFixes.LocalFixContext.OwnFrame(error)?.File)?.Lines;
        if (FixFinder.Core.LocalFixes.Rules.PythonStandardLibrary.TypoOf(interpreter, missing.Module, lines) is not null) return null;

        var command = $"\"{interpreter}\" -m pip install {missing.Package}";

        var body =
            $"`{missing.Module}` is not installed for the interpreter that ran this program.\n\n" +
            (string.Equals(missing.Module, missing.Package, StringComparison.OrdinalIgnoreCase)
                ? ""
                : $"It is imported as `{missing.Module}` but published as `{missing.Package}`, " +
                  "so the obvious name would install the wrong thing.\n\n") +
            $"    {command}\n";

        var candidate = new FixCandidate
        {
            SourceName = "Your Python environment",
            Id = $"pip:{missing.Package}",
            Title = $"Install {missing.Package}, which provides {missing.Module}",
            Url = $"https://pypi.org/project/{missing.Package}/",
            BodyText = body,
            RawBody = body,
            RawBodyIsHtml = false,
            Tier = FixTier.Dependency,
            Command = command,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            AnswerNoun = "suggestions",
        };

        // Not ranked against the search results for the same reason the runtime's own correction
        // is not: this did not come from anybody's opinion about what the error resembles.
        candidate.Score = 100;
        candidate.ScoreComponents =
        [
            new ScoreComponent(
                "What the import actually needs", 1.0, 1.0,
                $"the interpreter reported {missing.Module} missing, and {missing.Package} is the " +
                "package that provides it - this is not a search result"),
        ];

        return candidate;
    }

    /// <summary>
    /// The python that ran the program, when that is what ran it.
    /// </summary>
    /// <remarks>
    /// Returns null for anything else. A Java program can raise its own kind of missing-dependency
    /// error, and offering to run pip at it would be worse than offering nothing.
    /// </remarks>
    private static string? Interpreter(TargetSpec? spec)
    {
        if (spec?.ExecutablePath is not { Length: > 0 } path) return null;

        var name = Path.GetFileNameWithoutExtension(path);

        return name.StartsWith("python", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("py", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }
}
