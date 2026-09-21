using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Sources;

/// <summary>Turns "No module named 'yaml'" into the command that installs it.</summary>
public static partial class MissingModule
{
    [GeneratedRegex(@"No module named\s+(?:'(?<name>[^']*)'|""(?<name>[^""]*)""|(?<name>\S+))")]
    private static partial Regex MissingPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex SafeName();

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
    public sealed record Missing(string Module, string Package);

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

        if (!SafeName().IsMatch(module)) return null;

        var top = module.Split('.')[0];

        return new Missing(module, KnownPackages.TryGetValue(top, out var package) ? package : top);
    }

    public static FixCandidate? For(ParsedError error, TargetSpec? spec)
    {
        if (Read(error) is not { } missing) return null;

        var interpreter = Interpreter(spec);
        if (interpreter is null) return null;

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
