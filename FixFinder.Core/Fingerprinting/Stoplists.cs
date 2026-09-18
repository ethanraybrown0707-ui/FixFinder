namespace FixFinder.Core.Fingerprinting;

/// <summary>Words that appear in almost every error message and therefore distinguish none of them.</summary>
public static class Stoplists
{
    public static IReadOnlySet<string> ErrorBoilerplate { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "error", "errors", "exception", "exceptions", "failed", "failure", "failing", "fault",
        "unable", "cannot", "can", "could", "couldnt", "didnt", "doesnt", "wasnt", "isnt",
        "invalid", "unexpected", "unhandled", "uncaught", "occurred", "occurs", "thrown",
        "throws", "raise", "raised", "raising", "caused", "caught", "abort", "aborted",
        "aborting", "crash", "crashed", "fatal", "critical", "warning", "warn", "problem",
        "issue", "wrong", "bad", "message", "detail", "details", "info", "trace", "traceback",
        "stack", "stacktrace", "backtrace", "frame", "frames", "inner", "outer", "root",

        "a", "an", "the", "and", "or", "but", "if", "then", "than", "that", "this", "these",
        "those", "there", "here", "it", "its", "is", "are", "was", "were", "be", "been", "being",
        "am", "do", "does", "did", "done", "have", "has", "had", "will", "would", "shall",
        "should", "may", "might", "must", "can", "at", "by", "for", "from", "in", "into", "of",
        "on", "onto", "to", "with", "within", "without", "while", "during", "after", "before",
        "when", "where", "which", "who", "whom", "whose", "what", "why", "how", "all", "any",
        "some", "no", "not", "nor", "only", "own", "same", "so", "too", "very", "just", "also",

        "expected", "actual", "found", "given", "provided", "required", "requires", "missing",
        "present", "available", "unavailable", "supported", "unsupported", "specified",
        "attempt", "attempted", "attempting", "trying", "tried", "call", "called", "calling",
        "run", "running", "ran", "start", "started", "starting", "stop", "stopped", "please",
        "see", "check", "ensure", "make", "sure", "note", "notes", "more", "less", "first",
        "last", "next", "previous", "current", "new", "old", "one", "two", "three",
    };

    public static double Weight(string token)
    {
        if (ErrorBoilerplate.Contains(token)) return 0;
        if (token.Length < 3) return 0;

        var weight = token.Length >= 7 ? 2.0 : 1.0;
        if (LooksLikeIdentifier(token)) weight *= 1.5;

        return weight;
    }

    private static bool LooksLikeIdentifier(string token)
    {
        if (token.Contains('_') || token.Contains('.') || token.Contains("::", StringComparison.Ordinal)) return true;

        var hasLower = false;
        var hasUpperAfterLower = false;

        foreach (var c in token)
        {
            if (char.IsLower(c)) { hasLower = true; continue; }
            if (char.IsUpper(c) && hasLower) hasUpperAfterLower = true;
        }

        return hasUpperAfterLower;
    }
}
