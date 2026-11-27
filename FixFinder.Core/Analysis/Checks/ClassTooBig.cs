using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;

namespace FixFinder.Core.Analysis.Checks;

/// <summary>
/// A class too big: one with <see cref="MostMethods"/> methods or more. A class that size is usually doing the work of
/// several, and each part of it is harder to find, change and test on its own. The number is the one FixFinder's owner
/// chose for it, not worked out from any study of programs.
/// </summary>
/// <remarks>
/// A class's methods are counted as its language's reader reads them, leaving out its constructors and its destructor,
/// and in C# a property's or an indexer's accessors and an operator, which C# declares apart from its methods - so each
/// one counted is a method as the class's own language calls one. A C++ method is counted when it is written inside its
/// class, not when it is only declared there and written after it, and a class is counted in each file it is written in:
/// a C# partial class written across several files is counted part by part, which can only count it as smaller than it
/// is, never as bigger.
/// </remarks>
public static class ClassTooBig
{
    public const string CheckId = "analysis-class-too-big";

    public const string FoundBy = "counting each class's methods";

    /// <summary>How many methods make a class too big: 250 or more.</summary>
    public const int MostMethods = 250;

    /// <summary>A finding for each class of 250 methods or more in <paramref name="chosenFile"/>; in every file when no file is named.</summary>
    public static IReadOnlyList<AnalysisFinding> Find(IrProgram program, string? chosenFile)
    {
        var findings = new List<AnalysisFinding>();

        foreach (var type in program.Classes)
        {
            if (chosenFile is not null && !string.Equals(Path.GetFullPath(type.Span.File), Path.GetFullPath(chosenFile), StringComparison.OrdinalIgnoreCase)) continue;

            var methods = MethodsOf(type);
            if (methods < MostMethods) continue;

            findings.Add(new AnalysisFinding(CheckId, new SourceSpan(type.Span.File, type.Span.Line),
                $"`{type.Name}` has {methods} methods, and {MostMethods} or more make a class too big: a class that size is usually doing " +
                "the work of several, and each part of it is harder to find, change and test on its own",
                Severity.Suggestion, Confidence.Certain, FindingKind.Logic, FoundBy));
        }

        return findings;
    }

    /// <summary>
    /// How many methods a class has, as they are counted here: its constructors left out - and, as C++'s reader does not
    /// mark a constructor as one, the method named as its class, which is a C++ destructor's name too - and a C#
    /// destructor, ~ before that name; and in C#, the accessors and operators C# declares apart from its methods.
    /// </summary>
    public static int MethodsOf(IrClass type)
    {
        var writtenInCSharp = string.Equals(Path.GetExtension(type.Span.File), ".cs", StringComparison.OrdinalIgnoreCase);

        return type.Methods.Count(method => !method.IsConstructor && method.Name != IrFunction.ModuleBody &&
                                            method.Name != type.Name && method.Name != "~" + type.Name &&
                                            !(writtenInCSharp && DeclaredApartFromMethods(method.Name)));
    }

    /// <summary>
    /// Whether a C# member is one C# declares apart from its methods, by the name its reader gives it: a property's or an
    /// indexer's accessor - get Count, set Count, init Count, get this[] - or an operator, operator + or operator int.
    /// A method's own name never holds a space.
    /// </summary>
    private static bool DeclaredApartFromMethods(string name) =>
        name.StartsWith("get ", StringComparison.Ordinal) || name.StartsWith("set ", StringComparison.Ordinal) ||
        name.StartsWith("init ", StringComparison.Ordinal) || name.StartsWith("operator ", StringComparison.Ordinal);
}
