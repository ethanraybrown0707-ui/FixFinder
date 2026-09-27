using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Taint: text the person running the program controls, followed to where it becomes code, a shell command or SQL -
/// through assignments, formatting, and the program's own functions in both directions. Each is checked beside its safe
/// form, which must draw nothing: a query given its values separately, a command given as a list, a number parsed first.
/// </summary>
public class TaintTests(ITestOutputHelper output) : IDisposable
{
    private static readonly string[] TaintChecks = [Taint.CodeRule, Taint.CommandRule, Taint.SqlRule];

    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Python(string code)
    {
        if (PythonFrontend.FindInterpreter() is not { } python) return null;

        var path = Path.Combine(_temp.Path, "tainted.py");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Tainted(AbstractChecks.Run(await PythonFrontend.ReadAsync([path], python), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Java(string code)
    {
        if (JavaFrontend.FindTools() is not { } tools) return null;

        var path = Path.Combine(_temp.Path, code.Split("public class ")[1].Split(' ', '{')[0] + ".java");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Tainted(AbstractChecks.Run(await JavaFrontend.ReadAsync([path], tools.Javac, tools.Java), new SourceText()));
    }

    private async Task<IReadOnlyList<AnalysisFinding>> CSharp(string code)
    {
        var path = Path.Combine(_temp.Path, "Program.cs");
        await File.WriteAllTextAsync(path, code.ReplaceLineEndings("\n"));
        return Tainted(AbstractChecks.Run(await CSharpFrontend.ReadAsync([path]), new SourceText()));
    }

    private List<AnalysisFinding> Tainted(IEnumerable<AnalysisFinding> findings)
    {
        var found = findings.Where(f => TaintChecks.Contains(f.CheckId)).ToList();
        foreach (var finding in found) output.WriteLine($"line {finding.Span.Line} {finding.CheckId}: {finding.Message}");
        return found;
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) => string.Join("; ", findings.Select(f => $"line {f.Span.Line}: {f.Message}"));

    private static void Says(string expected, AnalysisFinding finding) =>
        Assert.True(finding.Message.Contains(expected, StringComparison.Ordinal), $"expected \"{expected}\" in: {finding.Message}");

    [Fact]
    public async Task WhatIsTypedRunAsCodeByEvalIsInjection()
    {
        const string code = """
            expression = input("Sum: ")
            print(eval(expression))
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Taint.CodeRule, found.CheckId);
        Assert.Equal(2, found.Span.Line);
        Says("runs, as Python code, text the user typed at line 1", found);
    }

    /// <summary>int() turns the text into a number: whatever was typed, what eval gets is digits.</summary>
    [Fact]
    public async Task TextTurnedIntoANumberFirstIsNoLongerTainted()
    {
        const string code = """
            count = int(input("How many? "))
            print(eval(str(count) + " * 2"))
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    private const string Orders = """
        import sqlite3

        connection = sqlite3.connect("shop.db")
        name = input("Customer: ")
        query = f"SELECT * FROM orders WHERE customer = '{name}'"
        connection.execute(query)
        """;

    [Fact]
    public async Task AQueryFormattedFromWhatIsTypedIsSqlInjection()
    {
        if (await Python(Orders) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Taint.SqlRule, found.CheckId);
        Assert.Equal(6, found.Span.Line);
        Says("runs, as SQL, text the user typed at line 4", found);
        Says("(SQL injection)", found);
    }

    /// <summary>The values passed separately never become SQL, however they were typed.</summary>
    [Fact]
    public async Task AQueryGivenItsValuesSeparatelyIsSafe()
    {
        var safe = Orders.ReplaceLineEndings("\n")
            .Replace("query = f\"SELECT * FROM orders WHERE customer = '{name}'\"\nconnection.execute(query)",
                "connection.execute(\"SELECT * FROM orders WHERE customer = ?\", (name,))", StringComparison.Ordinal);
        Assert.NotEqual(Orders.ReplaceLineEndings("\n"), safe);

        if (await Python(safe) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    /// <summary>The shell command is run inside run(); what the call passes it is what reaches the shell.</summary>
    [Fact]
    public async Task TextPassedToAFunctionThatRunsItAsACommandIsInjection()
    {
        const string code = """
            import os

            def run(command):
                os.system(command)

            run("ls " + input("Folder: "))
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Taint.CommandRule, found.CheckId);
        Assert.Equal(6, found.Span.Line);
        Says("passes text the user typed at line 6 to `run`, which runs it as a shell command at line 4", found);
    }

    /// <summary>ask() returns what was typed, so its caller's eval runs typed text.</summary>
    [Fact]
    public async Task TextReturnedByAFunctionCarriesItsTaint()
    {
        const string code = """
            def ask(prompt):
                return input(prompt)

            eval(ask("? "))
            """;
        if (await Python(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(4, found.Span.Line);
        Says("text the user typed at line 2", found);
    }

    /// <summary>A list of arguments goes to the program itself, never through a shell, so ; means nothing to it.</summary>
    [Fact]
    public async Task ACommandGivenAsAListWithoutAShellIsSafe()
    {
        const string code = """
            import subprocess

            subprocess.run(["ls", input("Folder: ")])
            """;
        if (await Python(code) is not { } findings) return;

        Assert.True(findings.Count == 0, Summary(findings));
    }

    [Fact]
    public async Task InJavaAStatementBuiltFromWhatIsReadIsSqlInjection()
    {
        const string code = """
            import java.sql.*;
            import java.util.Scanner;

            public class Orders {
                public static void main(String[] args) throws SQLException {
                    Scanner in = new Scanner(System.in);
                    String customer = in.nextLine();
                    Connection connection = DriverManager.getConnection("jdbc:sqlite:shop.db");
                    Statement statement = connection.createStatement();
                    ResultSet rows = statement.executeQuery("SELECT * FROM orders WHERE customer = '" + customer + "'");
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Taint.SqlRule, found.CheckId);
        Assert.Equal(10, found.Span.Line);
        Says("text read in at line 7", found);
        Says("PreparedStatement", found);
    }

    [Fact]
    public async Task InJavaTheProgramsArgumentsRunAsACommandAreInjection()
    {
        const string code = """
            public class Run {
                public static void main(String[] args) throws Exception {
                    Runtime.getRuntime().exec("ls " + args[0]);
                }
            }
            """;
        if (await Java(code) is not { } findings) return;

        var found = Assert.Single(findings);
        Assert.Equal(Taint.CommandRule, found.CheckId);
        Says("runs, as a command, text from the program's arguments", found);
        Says("can add arguments of its own choosing", found);
    }

    [Fact]
    public async Task InCSharpACommandMadeFromWhatIsTypedIsSqlInjection()
    {
        const string code = """
            using System;
            using Microsoft.Data.SqlClient;

            class Program
            {
                static void Main()
                {
                    var name = Console.ReadLine();
                    var command = new SqlCommand($"SELECT * FROM Orders WHERE Customer = '{name}'");
                }
            }
            """;

        var found = Assert.Single(await CSharp(code));
        Assert.Equal(Taint.SqlRule, found.CheckId);
        Assert.Equal(9, found.Span.Line);
        Says("makes a SqlCommand from text the user typed at line 8", found);
        Says("AddWithValue", found);
    }
}
