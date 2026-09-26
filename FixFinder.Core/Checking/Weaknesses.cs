using FixFinder.Core.Checking.Guides;

namespace FixFinder.Core.Checking;

/// <summary>
/// An entry in MITRE's Common Weakness Enumeration: the catalogue of software weaknesses that security tools, courses
/// and standards refer to by number. <paramref name="Title"/> is the entry's own title, word for word.
/// </summary>
public sealed record Weakness(int Id, string Title)
{
    public string Url => $"https://cwe.mitre.org/data/definitions/{Id}.html";

    public override string ToString() => $"CWE-{Id}: {Title}";
}

/// <summary>
/// Which CWE entry each of FixFinder's analyses finds an instance of - an authoritative, independent description of
/// the weakness, for a reader who wants more than FixFinder's own explanation.
/// </summary>
/// <remarks>
/// Unlike <see cref="Documentation"/>'s searches, these are links to particular pages - which is a claim that each page
/// exists and describes that weakness. Each was fetched on 2026-09-25 (CWE 4.20) and its title copied from it; CWE
/// numbers are permanent, so a page does not move. A rule is classified under an entry only where the entry's own
/// description fits everything the rule reports, in the language it reports it in:
/// <list type="bullet">
/// <item>CWE-584 is a return in a finally block, and the rule for it also reports break and continue.</item>
/// <item>CWE-129's description and examples are of an index that comes in from outside unchecked, and FixFinder's
/// index rule also reports one worked out wrongly inside the function, such as one step too far - which is CWE-193.
/// It does not tell the two apart, so the rule is not classified.</item>
/// <item>CWE-476 is a pointer that is NULL. Java's and C#'s null and Go's nil are its "null pointer exception" and
/// "nil pointer dereference"; Python's None and JavaScript's null are objects, not pointers, so they are not classified.</item>
/// </list>
/// </remarks>
public static class Weaknesses
{
    private static readonly Weakness SqlInjection = new(89, "Improper Neutralization of Special Elements used in an SQL Command ('SQL Injection')");
    private static readonly Weakness CommandInjection = new(78, "Improper Neutralization of Special Elements used in an OS Command ('OS Command Injection')");
    private static readonly Weakness EvalInjection = new(95, "Improper Neutralization of Directives in Dynamically Evaluated Code ('Eval Injection')");
    private static readonly Weakness InfiniteLoop = new(835, "Loop with Unreachable Exit Condition ('Infinite Loop')");
    private static readonly Weakness DivideByZero = new(369, "Divide By Zero");
    private static readonly Weakness NullDereference = new(476, "NULL Pointer Dereference");
    private static readonly Weakness RaceCondition = new(362, "Concurrent Execution using Shared Resource with Improper Synchronization ('Race Condition')");
    private static readonly Weakness MissingSynchronization = new(820, "Missing Synchronization");
    private static readonly Weakness Deadlock = new(833, "Deadlock");
    private static readonly Weakness LockedTwice = new(764, "Multiple Locks of a Critical Resource");
    private static readonly Weakness ImproperLocking = new(667, "Improper Locking");
    private static readonly Weakness RunInsteadOfStart = new(572, "Call to Thread run() instead of start()");
    private static readonly Weakness ResourceNotReleased = new(772, "Missing Release of Resource after Effective Lifetime");
    private static readonly Weakness UseAfterFree = new(416, "Use After Free");
    private static readonly Weakness DoubleFree = new(415, "Double Free");
    private static readonly Weakness MemoryLeak = new(401, "Missing Release of Memory after Effective Lifetime");
    private static readonly Weakness UninitializedVariable = new(457, "Use of Uninitialized Variable");
    private static readonly Weakness StackAddressReturned = new(562, "Return of Stack Variable Address");
    private static readonly Weakness AfterRelease = new(672, "Operation on a Resource after Expiration or Release");
    private static readonly Weakness AlwaysFalse = new(570, "Expression is Always False");
    private static readonly Weakness AlwaysTrue = new(571, "Expression is Always True");

    private static readonly Dictionary<string, Weakness> ByRule = new(StringComparer.Ordinal)
    {
        ["analysis-sql-injection"] = SqlInjection,
        ["analysis-command-injection"] = CommandInjection,
        ["analysis-code-injection"] = EvalInjection,
        ["analysis-loop-can-get-stuck"] = InfiniteLoop,
        ["analysis-loop-never-ends"] = InfiniteLoop,
        ["analysis-division-by-zero"] = DivideByZero,
        ["analysis-data-race"] = RaceCondition,
        ["analysis-lost-update"] = RaceCondition,
        ["analysis-read-before-join"] = RaceCondition,
        ["analysis-stale-read"] = MissingSynchronization,
        ["analysis-lock-order"] = Deadlock,
        ["analysis-lock-cycle"] = Deadlock,
        ["analysis-lock-reacquired"] = LockedTwice,
        ["analysis-lock-not-released"] = ImproperLocking,
        ["analysis-run-not-start"] = RunInsteadOfStart,
        ["analysis-resource-not-closed"] = ResourceNotReleased,
        ["analysis-use-after-free"] = UseAfterFree,
        ["analysis-double-free"] = DoubleFree,
        ["analysis-memory-leak"] = MemoryLeak,
        ["analysis-uninitialised-read"] = UninitializedVariable,
        ["analysis-unassigned-after-error"] = UninitializedVariable,
        ["analysis-dangling-pointer"] = StackAddressReturned,
        ["analysis-used-after-close"] = AfterRelease,
        ["analysis-never-true"] = AlwaysFalse,
        ["analysis-always-true"] = AlwaysTrue,
    };

    /// <summary>The languages whose "nothing" is a pointer to nothing, which is what CWE-476 is about.</summary>
    private static readonly HashSet<string> LanguagesWithNullPointers = new(StringComparer.Ordinal) { "C", "C++", "Java", "C#", "Go" };

    /// <summary>The CWE entry a finding is an instance of, or null when none fits exactly.</summary>
    public static Weakness? For(string ruleId, string file)
    {
        if (ruleId == "analysis-null-used")
            return LanguagesWithNullPointers.Contains(Guidebook.LanguageName(file)) ? NullDereference : null;

        return ByRule.GetValueOrDefault(ruleId);
    }

    /// <summary>Every entry any rule is classified under, for checks that hold of all of them.</summary>
    public static IEnumerable<Weakness> Every() => ByRule.Values.Append(NullDereference).Distinct();

    /// <summary>Every rule that is classified, in some language or all of them.</summary>
    public static IEnumerable<string> ClassifiedRules() => ByRule.Keys.Append("analysis-null-used");
}
