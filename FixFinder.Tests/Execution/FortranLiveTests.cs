using System.Diagnostics;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Checking.Guides;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Fortran programs checked whole, as the window checks them: built by gfortran with -Wall and its run-time checks, run,
/// what gfortran says and what stops the program found at their lines - in a module the program uses too - with changes
/// checked in copies, and every example a Fortran guide shows built and run. A computer without gfortran passes over them.
/// </summary>
[Collection(BuildsRunAlone.Name)]
public class FortranLiveTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string? Gfortran()
    {
        if (FortranBuild.Gfortran() is { } gfortran)
        {
            output.WriteLine($"Checked with {FortranBuild.Version()}: {gfortran}");
            return gfortran;
        }

        output.WriteLine("Not run on this computer: no gfortran is here.");
        return null;
    }

    private string Write(string relative, string code)
    {
        var path = Path.Combine(_temp.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file)
    {
        var launch = TargetFactory.FromFile(file, LiveAllowance.For(file, TimeSpan.FromMinutes(3)));
        Assert.True(launch.Ok, launch.Problem);

        using var http = new FixFinderHttpClient();
        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = CodeLanguage.Fortran }.CheckAsync(launch);

        foreach (var note in report.Notes) output.WriteLine($"note: {note}");
        foreach (var finding in report.Findings)
        {
            output.WriteLine($"{Path.GetFileName(finding.File)}:{finding.Line}: {finding.Kind} {finding.Severity} {finding.RuleId}: {finding.Title} | {finding.Verified.Summary}");
            if (finding.Fix is { } fix) output.WriteLine($"  change at line {fix.StartLine} (removing {fix.RemoveCount}) to: {string.Join(" / ", fix.NewLines)}");
            if (finding.Error is { } error) output.WriteLine("  as gfortran said it:\n    " + error.RawText.Replace("\n", "\n    "));
        }

        foreach (var line in report.Run?.Run?.Lines ?? []) output.WriteLine($"  | {line.Text}");
        return report;
    }

    /// <summary>
    /// Builds Fortran code here as FixFinder does, runs it, and gives back what it printed - failing on any warning or error -
    /// or null when Windows refused to start the program it built, which says nothing about FixFinder.
    /// </summary>
    private async Task<string?> RunAsync(string gfortran, string code, string name)
    {
        var file = Write(Path.Combine("changed", name, "main.f90"), code);
        var folder = Path.GetDirectoryName(file)!;

        using var building = Process.Start(new ProcessStartInfo(gfortran, $"-g -Wall -fcheck=all -o program.exe main.f90")
            { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var built = await building.StandardOutput.ReadToEndAsync() + await building.StandardError.ReadToEndAsync();
        await building.WaitForExitAsync();
        Assert.True(building.ExitCode == 0 && built.Trim().Length == 0, $"{name}: gfortran said:\n{built}");

        Process started;

        try
        {
            started = Process.Start(new ProcessStartInfo(Path.Combine(folder, "program.exe"))
                { WorkingDirectory = folder, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        }
        catch (System.ComponentModel.Win32Exception refused) when (ApplicationControl.Refused(refused.Message))
        {
            output.WriteLine($"Windows refused to start the program built from {name}: {refused.Message}");
            return null;
        }

        using var running = started;
        var printed = await running.StandardOutput.ReadToEndAsync();
        var said = await running.StandardError.ReadToEndAsync();
        await running.WaitForExitAsync();

        Assert.True(running.ExitCode == 0 && said.Length == 0, $"{name}: the program said:\n{said}");
        return string.Join("\n", printed.ReplaceLineEndings("\n").Trim('\n').Split('\n').Select(line => line.Trim()));
    }

    private static string Changed(string code, Finding found)
    {
        var lines = code.TrimEnd('\n').Split('\n').ToList();
        lines.RemoveRange(found.Fix!.StartLine - 1, found.Fix.RemoveCount);
        lines.InsertRange(found.Fix.StartLine - 1, found.Fix.NewLines);
        return string.Join("\n", lines) + "\n";
    }

    private const string Head = "program marks\n  implicit none\n";

    [Theory]
    [InlineData(Head + "  integer :: total\n  total = 10\n  print *, totl + 1\nend program marks\n", "fortran-did-you-mean", 5, 5, "  print *, total + 1", "11")]
    [InlineData(Head + "  integer :: mark\n  mark = 70\n  if (mark = 40) then\n    print *, 'forty'\n  end if\n  print *, mark\nend program marks\n", "fortran-compare-with-double-equals", 5, 5, "  if (mark == 40) then", "70")]
    [InlineData(Head + "  integer :: mark\n  mark = 50\n  if (mark != 40) then\n    print *, 'not forty'\n  end if\nend program marks\n", "fortran-not-equal", 5, 5, "  if (mark /= 40) then", "not forty")]
    [InlineData(Head + "  integer :: mark\n  mark = 50\n  if (mark > 40)\n    print *, 'pass'\n  end if\nend program marks\n", "fortran-missing-then", 5, 5, "  if (mark > 40) then", "pass")]
    [InlineData(Head + "  integer :: i\n  do i = 1, 3\n    print *, i\nend program marks\n", "fortran-close-block", 6, 6, "  end do", "1\n2\n3")]
    [InlineData(Head + "  integer :: mark\n  mark = 50\n  if (mark > 40) then\n    print *, 'pass'\nend program marks\n", "fortran-close-block", 7, 7, "  end if", "pass")]
    [InlineData(Head + "  integer :: total\n  total = 5\n  call add_one(total)\n  print *, total\ncontains\n  subroutine add_one(n)\n    integer, intent(in) :: n\n    n = n + 1\n  end subroutine add_one\nend program marks\n", "fortran-intent-inout", 10, 9, "    integer, intent(inout) :: n", "6")]
    [InlineData(Head + "  integer, parameter :: pass_mark = 40\n  pass_mark = 50\n  print *, pass_mark\nend program marks\n", "fortran-parameter-changed", 4, 3, "  integer :: pass_mark = 40", "50")]
    [InlineData(Head + "  print *, \"hello\nend program marks\n", "fortran-close-text", 3, 3, "  print *, \"hello\"", "hello")]
    [InlineData(Head + "  print *, (1 + 2\nend program marks\n", "fortran-close-bracket", 3, 3, "  print *, (1 + 2)", "3")]
    [InlineData(Head + "  integer :: total\n  total = 150\n  print *, \"Total: \" // total\nend program marks\n", "fortran-concatenate-number", 5, 5, "  print *, \"Total: \", total", "Total:          150")]
    // gfortran names no line for a file that ends with no end line: the finding is where the change goes, after the last line.
    [InlineData(Head + "  print *, 70\n", "fortran-close-units", 4, 4, "end program marks", "70")]
    public async Task WhatGfortranRefusesIsFoundBeforeItRunsAndItsChangeBuildsAndRuns(string code, string rule, int line, int changedLine, string changedTo, string fixedPrints)
    {
        if (Gfortran() is not { } gfortran) return;

        var report = await CheckAsync(Write(Path.Combine($"refused{code.Length}", "main.f90"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == rule);
        Assert.NotEqual(FindingKind.Runtime, found.Kind);
        Assert.Equal(line, found.Line);
        Assert.Equal(changedLine, found.Fix?.StartLine);
        Assert.Equal(changedTo, found.Fix?.NewLines.Single());
        if (await RunAsync(gfortran, Changed(code, found), $"refused{code.Length}") is { } printed) Assert.Equal(fixedPrints, printed);
    }

    [Fact]
    public async Task AFileEndingWithASubroutineAndItsProgramOpenGetsBothEndLinesInnermostFirst()
    {
        if (Gfortran() is not { } gfortran) return;

        const string code = Head + "  call show(70)\ncontains\n  subroutine show(mark)\n    integer, intent(in) :: mark\n    print *, mark\n";
        var report = await CheckAsync(Write(Path.Combine("both-open", "main.f90"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == "fortran-close-units");
        Assert.Equal((8, 0), (found.Fix!.StartLine, found.Fix.RemoveCount));
        Assert.Equal(["  end subroutine show", "end program marks"], found.Fix.NewLines);
        if (await RunAsync(gfortran, Changed(code, found), "both-open") is { } printed) Assert.Equal("70", printed);
    }

    /// <summary>
    /// A loop that reads outside its array is found twice - by gfortran's warning before it runs and by the check that stops
    /// it - with the same change, so it is one finding, and the changed loop reads every mark.
    /// </summary>
    [Theory]
    [InlineData(Head + "  integer :: scores(3) = [70, 80, 90]\n  integer :: i\n  do i = 0, 3\n    print *, scores(i)\n  end do\nend program marks\n", "  do i = 1, 3")]
    [InlineData(Head + "  integer :: scores(3) = [70, 80, 90]\n  integer :: i\n  do i = 1, size(scores) + 1\n    print *, scores(i)\n  end do\nend program marks\n", "  do i = 1, size(scores)")]
    public async Task ALoopOutsideItsArrayIsOneFindingWithTheLoopPutRight(string code, string changedTo)
    {
        if (Gfortran() is not { } gfortran) return;

        var report = await CheckAsync(Write(Path.Combine($"bounds{code.Length}", "main.f90"), code));
        if (report.Run?.Run is { } run && ApplicationControl.Refused(run)) return;

        var loop = Assert.Single(report.Findings, finding => finding.RuleId == "fortran-loop-bounds");
        Assert.Equal(5, loop.Fix?.StartLine);
        Assert.Equal(changedTo, loop.Fix?.NewLines.Single());
        Assert.Contains(report.Run?.Run?.Lines ?? [], captured => captured.Text.StartsWith("Fortran runtime error: Index", StringComparison.Ordinal));
        if (await RunAsync(gfortran, Changed(code, loop), $"bounds{code.Length}") is { } printed) Assert.Equal("70\n80\n90", printed);
    }

    [Fact]
    public async Task AWholeNumberDividedByZeroStopsTheProgramWithNoLineNamed()
    {
        if (Gfortran() is null) return;

        var report = await CheckAsync(Write(Path.Combine("divided", "main.f90"),
            Head + "  integer :: total, count\n  total = 70\n  count = 0\n  print *, total / count\nend program marks\n"));
        if (report.Run?.Run is { } run && ApplicationControl.Refused(run)) return;

        var stopped = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal("SIGFPE", stopped.Error?.ExceptionType);
        Assert.Null(stopped.Line);
    }

    [Theory]
    [InlineData(Head + "  integer :: total, count\n  real :: average\n  total = 7\n  count = 2\n  average = total / count\n  print '(F0.1)', average\nend program marks\n", "logic-fortran-whole-number-division", 7, "3.5")]
    [InlineData(Head + "  integer :: scores(4) = [70, 80, 90, 65]\n  real :: average\n  average = sum(scores) / size(scores)\n  print '(F0.2)', average\nend program marks\n", "logic-fortran-whole-number-division", 5, "76.25")]
    [InlineData("program marks\n  integer :: total\n  total = 10\n  print *, total + 1\nend program marks\n", "logic-fortran-no-implicit-none", 1, "11")]
    [InlineData(Head + "  integer :: total, i\n  do i = 1, 3\n    total = total + i\n  end do\n  print *, total\nend program marks\n", "logic-fortran-total-not-started", 5, "6")]
    [InlineData(Head + "  double precision :: total\n  total = 0.1d0 + 0.2d0\n  if (total == 0.3d0) then\n    print *, 'equal'\n  else\n    print *, 'not equal'\n  end if\nend program marks\n", "logic-fortran-float-equality", 5, "equal")]
    [InlineData(Head + "  integer :: score, total\n  score = 45\n  total = 60\n  print *, score / total * 100\nend program marks\n", "logic-fortran-percent-divided-first", 6, "75")]
    public async Task ALogicMistakeGfortranSaysNothingOfIsFoundAndItsChangePrintsTheRightAnswer(string code, string check, int line, string fixedPrints)
    {
        if (Gfortran() is not { } gfortran) return;

        var report = await CheckAsync(Write(Path.Combine($"logic{code.Length}", "main.f90"), code));

        var found = Assert.Single(report.Findings, finding => finding.RuleId == check);
        Assert.Equal(line, found.Line);
        if (await RunAsync(gfortran, Changed(code, found), $"logic{code.Length}") is { } printed) Assert.Equal(fixedPrints, printed);
    }

    [Fact]
    public async Task AnErrorInAModuleTheProgramUsesIsFoundThere()
    {
        if (Gfortran() is null) return;

        var module = Write(Path.Combine("program", "stats.f90"),
            "module stats\n  implicit none\ncontains\n  integer function total_of(scores)\n    integer, intent(in) :: scores(:)\n    total_of = sum(scroes)\n  end function total_of\nend module stats\n");
        var main = Write(Path.Combine("program", "main.f90"), "program marks\n  use stats\n  implicit none\n  print *, total_of([70, 80])\nend program marks\n");

        var report = await CheckAsync(main);

        // gfortran also says main.f90 cannot open stats.mod - which the error in stats.f90 kept it from writing.
        var found = Assert.Single(report.Findings, finding => finding.RuleId == "fortran-did-you-mean");
        Assert.Equal((module, 6), (found.File, found.Line!.Value));
        Assert.Equal("    total_of = sum(scores)", found.Fix?.NewLines.Single());
        Assert.False(File.Exists(Path.Combine(_temp.Path, "program", "stats.mod")), "gfortran's .mod file was written beside the program");
    }

    /// <summary>Every whole program a Fortran guide shows builds with -Wall, warning of nothing, and runs with nothing but its output.</summary>
    [Fact]
    public async Task EveryExampleAFortranGuideShowsBuildsAndRunsClean()
    {
        if (Gfortran() is not { } gfortran) return;

        var examples = FortranGuides.All.Concat(FortranPatternGuides.All).Select(entry => entry.Guide.Example).Where(example => example.Length > 0).Distinct().ToList();

        foreach (var (example, number) in examples.Select((example, index) => (example, index + 1)))
            await RunAsync(gfortran, example + "\n", $"example{number}");

        output.WriteLine($"{examples.Count} examples built and run.");
    }
}
