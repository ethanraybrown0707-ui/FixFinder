using System.Text.Json;
using FixFinder.Core.Execution;

namespace FixFinder.Core.Parsing.Parsers;

/// <summary>
/// Reads what rustc says when it builds a program, as FixFinder asks it to say it: one JSON message a line, each with the
/// message, its code - E0425, or a warning's name such as unused_variables - where it is, the text rustc would have printed
/// for a person, and the changes rustc suggests, each marked with how sure rustc is that it is right.
/// </summary>
/// <remarks>
/// <code>
/// {"$message_type":"diagnostic","message":"cannot find value `totl` in this scope","code":{"code":"E0425",...},
///  "level":"error","spans":[{"file_name":"errors.rs","line_start":5,"column_start":20,...,"is_primary":true}],
///  "children":[{"message":"a local variable with a similar name exists","level":"help","spans":[{...,
///  "suggested_replacement":"total","suggestion_applicability":"MaybeIncorrect"}]}],"rendered":"error[E0425]: ..."}
/// </code>
/// <para>
/// rustc 1.98's own messages, read as it wrote them. Its closing lines - "aborting due to 2 previous errors", "3 warnings
/// emitted", "For more information about this error, try rustc --explain E0425" - say nothing of the program, and are
/// passed over.
/// </para>
/// </remarks>
public sealed class RustCompileParser : IStackTraceParser, IMultiErrorParser
{
    private const string DiagnosticMark = "{\"$message_type\":\"diagnostic\"";

    public string LanguageId => "rust";

    public string DisplayName => "Rust compiler";

    public int Detect(IReadOnlyList<string> lines) => lines.Any(line => line.TrimStart().StartsWith(DiagnosticMark, StringComparison.Ordinal)) ? 95 : 0;

    public ParsedError? Parse(IReadOnlyList<CapturedLine> lines) => ParseAll(lines).FirstOrDefault();

    public IReadOnlyList<ParsedError> ParseAll(IReadOnlyList<CapturedLine> lines) => Read(lines, wantWarnings: false);

    /// <summary>The warnings rustc gave while building - each a message of its own, with its name as its code.</summary>
    public static IReadOnlyList<ParsedError> ParseWarnings(IReadOnlyList<CapturedLine> lines) => Read(lines, wantWarnings: true);

    private static IReadOnlyList<ParsedError> Read(IReadOnlyList<CapturedLine> lines, bool wantWarnings)
    {
        var found = new List<ParsedError>();

        foreach (var line in lines)
        {
            var text = line.Text.Trim();
            if (!text.StartsWith(DiagnosticMark, StringComparison.Ordinal)) continue;

            try
            {
                using var document = JsonDocument.Parse(text);
                if (Read(document.RootElement, line.Sequence, wantWarnings) is { } error) found.Add(error);
            }
            catch (JsonException)
            {
                // A line cut short or written by something else is not rustc's message.
            }
        }

        return found;
    }

    private static ParsedError? Read(JsonElement diagnostic, int sequence, bool wantWarnings)
    {
        var level = Text(diagnostic, "level");
        if (level != (wantWarnings ? "warning" : "error")) return null;

        var message = Text(diagnostic, "message") ?? "";
        var spans = diagnostic.TryGetProperty("spans", out var spanList) ? spanList.EnumerateArray().ToList() : [];
        if (spans.Count == 0) return null;

        var primary = spans.FirstOrDefault(span => span.TryGetProperty("is_primary", out var isPrimary) && isPrimary.GetBoolean());
        if (primary.ValueKind != JsonValueKind.Object) primary = spans[0];
        primary = InTheProgram(primary);

        var label = Text(primary, "label");
        var said = label is { Length: > 0 } && !message.Contains(label, StringComparison.OrdinalIgnoreCase) ? $"{message}: {label}" : message;
        var code = diagnostic.TryGetProperty("code", out var codeObject) && codeObject.ValueKind == JsonValueKind.Object ? Text(codeObject, "code") : null;

        return new ParsedError
        {
            LanguageId = "rust",
            Confidence = 95,
            RawText = (Text(diagnostic, "rendered") ?? said).Replace("\r\n", "\n").TrimEnd(),
            FirstLineSequence = sequence,
            ExceptionType = wantWarnings ? "compile warning" : "compile error",
            ErrorCode = code,
            Message = said,
            Frames =
            [
                new ErrorFrame
                {
                    Order = 0,
                    File = Text(primary, "file_name"),
                    Line = Number(primary, "line_start"),
                    Column = Number(primary, "column_start"),
                    RawLine = $"{Text(primary, "file_name")}:{Number(primary, "line_start")}:{Number(primary, "column_start")}",
                },
            ],
            Suggestions = SuggestionsIn(diagnostic),
        };
    }

    /// <summary>
    /// The place in the program a span stands for. A message about code a macro wrote - println!'s, say - has its span in
    /// Rust's own library, under /rustc/; the span's expansion names where the program used the macro, and that is followed
    /// until it is in a file of the program's.
    /// </summary>
    private static JsonElement InTheProgram(JsonElement span)
    {
        for (var depth = 0; depth < 10 && IsRustsOwn(Text(span, "file_name")); depth++)
        {
            if (!span.TryGetProperty("expansion", out var expansion) || expansion.ValueKind != JsonValueKind.Object ||
                !expansion.TryGetProperty("span", out var calledFrom) || calledFrom.ValueKind != JsonValueKind.Object)
                break;

            span = calledFrom;
        }

        return span;
    }

    private static bool IsRustsOwn(string? file) =>
        file is null || file.StartsWith("/rustc/", StringComparison.Ordinal) || file.StartsWith('<');

    /// <summary>The changes rustc suggests with a message: each help under it that carries replacements.</summary>
    private static IReadOnlyList<SuggestedChange> SuggestionsIn(JsonElement diagnostic)
    {
        if (!diagnostic.TryGetProperty("children", out var children)) return [];

        var changes = new List<SuggestedChange>();

        foreach (var child in children.EnumerateArray())
        {
            if (!child.TryGetProperty("spans", out var spans)) continue;

            var edits = new List<SuggestedEdit>();
            var certainty = SuggestionCertainty.Unknown;

            foreach (var span in spans.EnumerateArray())
            {
                if (!span.TryGetProperty("suggested_replacement", out var replacement) || replacement.ValueKind != JsonValueKind.String) continue;

                edits.Add(new SuggestedEdit(
                    Text(span, "file_name") ?? "", Number(span, "line_start") ?? 0, Number(span, "column_start") ?? 0,
                    Number(span, "line_end") ?? 0, Number(span, "column_end") ?? 0, replacement.GetString() ?? ""));
                certainty = Text(span, "suggestion_applicability") switch
                {
                    "MachineApplicable" => SuggestionCertainty.Certain,
                    "MaybeIncorrect" => SuggestionCertainty.Possible,
                    "HasPlaceholders" => SuggestionCertainty.HasPlaceholders,
                    _ => SuggestionCertainty.Unknown,
                };
            }

            if (edits.Count > 0) changes.Add(new SuggestedChange(Text(child, "message") ?? "", certainty, edits));
        }

        return changes;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
