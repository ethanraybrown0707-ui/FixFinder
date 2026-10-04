using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>
/// The Java of 21 to 25 read by javac's own parser into the IR: a pattern's case is one that may match, never another
/// default; the variables a pattern binds are declared where they can be used; the unnamed _ names nothing; a compact
/// source file is the class javac makes of it. Each is read with a JDK of the Java that has it - the newest here - and is
/// skipped where there is none, as on a computer with only an older JDK.
/// </summary>
public class JavaNewerSyntaxTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>The file read with the newest JDK of at least this Java, or null when there is none.</summary>
    private async Task<IrProgram?> Read(int needs, string fileName, string code)
    {
        if (Jdks.AtLeast(needs) is not { } jdk) return null;

        var path = Path.Combine(_temp.Path, fileName);
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));

        var program = await JavaFrontend.ReadAsync([path], jdk.Javac, jdk.Java);
        Assert.Empty(program.Problems);
        return program;
    }

    private static IrFunction Method(IrProgram program, string name) => program.Classes.SelectMany(type => type.Methods).Single(method => method.Name == name);

    private static T Single<T>(IrFunction function) => IrWalk.Statements(function.Body).OfType<T>().Single();

    private const string Describe = """
        public class Shapes {
            static String describe(Object value) {
                switch (value) {
                    case Integer number -> { return "int " + number; }
                    case String text when text.isEmpty() -> { return "empty"; }
                    case String text -> { return "text " + text; }
                    default -> { return "other"; }
                }
            }

            public static void main(String[] args) {
                System.out.println(describe(4));
                System.out.println(describe(""));
                System.out.println(describe(2.5));
            }
        }
        """;

    /// <summary>
    /// javac gives a pattern's case no constant, so read by its constants alone every pattern case looked like default - and
    /// the real default, after them, could never be reached.
    /// </summary>
    [Fact]
    public async Task APatternsCaseMayMatchAndOnlyDefaultIsTheDefault()
    {
        if (await Read(21, "Shapes.java", Describe) is not { } program) return;

        var cases = Single<Switch>(Method(program, "describe")).Cases;

        Assert.Equal(4, cases.Count);
        Assert.All(cases.Take(3), patternCase => Assert.Equal("pattern", Assert.IsType<Opaque>(Assert.Single(patternCase.Labels)).What));
        Assert.Empty(cases[3].Labels);
        Assert.All(cases, eachCase => Assert.False(eachCase.FallsThrough));
    }

    [Fact]
    public async Task APatternsVariableIsDeclaredAtTheStartOfItsCaseAndItsGuardHoldsOverIt()
    {
        if (await Read(21, "Shapes.java", Describe) is not { } program) return;

        var cases = Single<Switch>(Method(program, "describe")).Cases;

        Assert.Equal("number", Assert.IsType<Declare>(cases[0].Body[0]).Variable);
        Assert.Equal("text", Assert.IsType<Declare>(cases[1].Body[0]).Variable);
        Assert.IsType<If>(cases[1].Body[1]);
    }

    [Fact]
    public async Task APatternSwitchWithADefaultRaisesNoFalseAlarm()
    {
        if (await Read(21, "Shapes.java", Describe) is not { } program) return;

        Assert.Empty(AbstractChecks.Run(program, new SourceText()));
    }

    [Fact]
    public async Task ARecordPatternsVariablesAreDeclaredAndTheUnnamedOneIsNot()
    {
        const string code = """
            public class Pairs {
                record Pair(Object left, Object right) {}

                static int width(Object value) {
                    if (value instanceof Pair(String first, _)) {
                        return first.length();
                    }
                    return 0;
                }
            }
            """;
        if (await Read(22, "Pairs.java", code) is not { } program) return;

        var width = Method(program, "width");
        var declared = IrWalk.Statements(width.Body).OfType<Declare>().Select(declare => declare.Variable).ToList();

        Assert.Equal(["first"], declared);
        Assert.IsType<Declare>(width.Body[0]);
        Assert.IsType<If>(width.Body[1]);
    }

    [Fact]
    public async Task AnInstanceofPatternsVariableIsDeclaredBeforeTheStatementThatTestsIt()
    {
        const string code = """
            public class Texts {
                static int size(Object value) {
                    if (value instanceof String text && !text.isEmpty()) {
                        return text.length();
                    }
                    return -1;
                }
            }
            """;
        if (await Read(17, "Texts.java", code) is not { } program) return;

        var size = Method(program, "size");

        Assert.Equal("text", Assert.IsType<Declare>(size.Body[0]).Variable);
        Assert.IsType<If>(size.Body[1]);
        Assert.Empty(AbstractChecks.Run(program, new SourceText()));
    }

    /// <summary>Java 22's unnamed variables name nothing: no variable called _ - or with no name at all - is declared or caught.</summary>
    [Fact]
    public async Task UnnamedVariablesDeclareNothing()
    {
        const string code = """
            import java.util.List;
            import java.util.function.Function;

            public class Unused {
                static int count(List<String> names) {
                    int total = 0;
                    for (String _ : names) total++;
                    try { Integer.parseInt("x"); } catch (NumberFormatException _) { total--; }
                    var _ = names.size();
                    Function<String, Integer> one = _ -> 1;
                    return total + one.apply("a");
                }
            }
            """;
        if (await Read(22, "Unused.java", code) is not { } program) return;

        var count = Method(program, "count");
        var statements = IrWalk.Statements(count.Body).ToList();

        Assert.DoesNotContain(statements.OfType<Declare>(), declare => declare.Variable.Length == 0);
        Assert.Equal("_", Assert.IsType<Name>(statements.OfType<ForEach>().Single().Target).Identifier);
        Assert.Null(statements.OfType<Try>().Single().Handlers.Single().Variable);
        Assert.Contains(statements.OfType<Evaluate>(), evaluated => IrText.Of(evaluated.Value).Contains("names.size()", StringComparison.Ordinal));
        Assert.Equal("_", program.Functions.Single(function => function.Name.StartsWith("lambda", StringComparison.Ordinal)).Parameters.Single().Name);
    }

    /// <summary>A compact source file is read as the class javac makes of it - named for its file - with its main neither static nor given arguments.</summary>
    [Fact]
    public async Task ACompactSourceFileIsTheClassNamedForItsFile()
    {
        const string code = """
            int doubled(int value) {
                return value * 2;
            }

            void main() {
                IO.println(doubled(21));
            }
            """;
        if (await Read(25, "Hello.java", code) is not { } program) return;

        var hello = program.Classes.Single();
        var main = hello.Methods.Single(method => method.Name == "main");

        Assert.Equal("Hello", hello.Name);
        Assert.False(main.IsStatic);
        Assert.Empty(main.Parameters);
    }
}
