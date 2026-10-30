using FixFinder.Core;
using FixFinder.Core.Execution;
using FixFinder.Core.Teaching;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// A program that never finishes, given a run far shorter than its compiler takes: the time is the program's, not the
/// compiler's, so it is built in full, and then stopped as never finishing - not called a program that ran to the end and
/// printed nothing, as one was when its build was stopped at the run's ten seconds on a busy computer.
/// </summary>
public class SnippetRunnerLiveTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("C", "#include <stdio.h>\n\nint main(void) {\n    volatile long long passes = 0;\n    for (;;) {\n        passes++;\n    }\n    return 0;\n}\n")]
    [InlineData("Java", "public class Main {\n    public static void main(String[] args) {\n        long passes = 0;\n        while (true) {\n            passes++;\n        }\n    }\n}\n")]
    public async Task AProgramGivenLessTimeThanItsCompilerTakesIsBuiltAndThenStoppedAsNeverFinishing(string languageName, string code)
    {
        var language = CodeLanguage.All.Single(each => each.Name == languageName);

        var observed = await SnippetRunner.RunAsync(language, code, TimeSpan.FromMilliseconds(100));
        if (!observed.Ran || ApplicationControl.Refused(string.Join("\n", observed.Output) + "\n" + observed.WhyNotRun))
        {
            output.WriteLine($"Not run on this computer: {observed.WhyNotRun ?? "Windows refused to start it"}");
            return;
        }

        output.WriteLine(observed.Said(languageName));
        Assert.True(observed.Shows(Behaviour.Running()), observed.Said(languageName));
    }
}
