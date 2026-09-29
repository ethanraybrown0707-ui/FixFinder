using System.Net.NetworkInformation;
using FixFinder.Core;
using FixFinder.Core.Checking;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Covers a program that stops because a connection it made was refused - its database or server not running - which is
/// said as that, from what the runtime itself said, rather than as a mistake in the code.
/// </summary>
public class RefusedConnectionTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n"));
        return path;
    }

    private async Task<CheckReport> CheckAsync(string file, CodeLanguage language)
    {
        using var http = new FixFinderHttpClient();
        var launch = TargetFactory.FromFile(file);
        Assert.True(launch.Ok, launch.Problem);

        var report = await new ProgramChecker(http, new FixSourceRegistry()) { Language = language }.CheckAsync(launch);

        output.WriteLine($"syntax: {report.SyntaxSummary}");
        foreach (var finding in report.Findings)
            output.WriteLine($"[{finding.Severity}/{finding.Confidence}/{finding.Kind}] {finding.RuleId}: {finding.Title}\n    {finding.Explanation}\n    fix: {finding.SuggestedFix}");

        return report;
    }

    /// <summary>A port nothing on this computer is listening on, so a connection to it is refused.</summary>
    private static int PortNothingListensOn()
    {
        var listening = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(endpoint => endpoint.Port).ToHashSet();
        return Enumerable.Range(0, 1000).Select(step => 59999 - step).First(port => !listening.Contains(port));
    }

    private static bool Listening(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);

    private static Finding RefusalIn(CheckReport report)
    {
        var refused = Assert.Single(report.Findings, finding => finding.Kind == FindingKind.Runtime);
        Assert.Equal("connection-refused", refused.RuleId);
        Assert.Equal(Severity.Warning, refused.Severity);
        return refused;
    }

    [Fact]
    public async Task APythonProgramWhoseConnectionIsRefusedIsSaidToHaveHadNothingToConnectTo()
    {
        if (!LocalFixLiveTests.Available("python")) return;

        var file = Write("client.py", $"import socket\n\nsocket.create_connection((\"localhost\", {PortNothingListensOn()}))\n");

        var report = await CheckAsync(file, CodeLanguage.Python);

        // Python's error names no address, so none is given.
        var refused = RefusalIn(report);
        Assert.Equal(Confidence.Certain, refused.Confidence);
        Assert.Equal("It could not connect: the connection was refused", refused.Title);
        Assert.StartsWith("The program made a connection - to a database, a server or a service - and it was refused", refused.Explanation, StringComparison.Ordinal);
        Assert.Contains("That is not a mistake in the code", refused.Explanation, StringComparison.Ordinal);
        Assert.Equal("No syntax errors, but it could not connect when run", report.SyntaxSummary);
    }

    [Fact]
    public async Task AJavaSocketWhoseConnectionIsRefusedIsSaidToHaveHadNothingToConnectTo()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var file = Write("Client.java", $$"""
            public class Client {
                public static void main(String[] args) throws Exception {
                    new java.net.Socket("localhost", {{PortNothingListensOn()}});
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        var refused = RefusalIn(report);
        Assert.Equal(Confidence.Certain, refused.Confidence);
        Assert.Equal("It could not connect: the connection was refused", refused.Title);
    }

    [Fact]
    public async Task JavasHttpClientSayingOnlyConnectExceptionIsSaidToBeLikelyNotCertain()
    {
        if (!LocalFixLiveTests.Available("java")) return;

        var file = Write("Fetch.java", $$"""
            public class Fetch {
                public static void main(String[] args) throws Exception {
                    var client = java.net.http.HttpClient.newHttpClient();
                    var request = java.net.http.HttpRequest.newBuilder(java.net.URI.create("http://localhost:{{PortNothingListensOn()}}/marks")).build();
                    client.send(request, java.net.http.HttpResponse.BodyHandlers.ofString());
                }
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Java);

        // Java gives no reason with it, so that the connection was refused is what is typical, not what was said.
        var refused = RefusalIn(report);
        Assert.Equal(Confidence.Likely, refused.Confidence);
        Assert.Equal("It could not connect: ConnectException", refused.Title);
        Assert.StartsWith("Java could not make a connection the program asked for, and gave a ConnectException with no reason", refused.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACSharpProgramIsToldTheAddressItsRuntimeNames()
    {
        if (!LocalFixLiveTests.Available("dotnet")) return;

        var port = PortNothingListensOn();
        var file = Write(@"client\Program.cs", $"using var client = new System.Net.Sockets.TcpClient(\"localhost\", {port});\n");

        var report = await CheckAsync(file, CodeLanguage.CSharp);

        // Which of localhost's addresses is named - [::1] or [::ffff:127.0.0.1] - is .NET's to say.
        var refused = RefusalIn(report);
        Assert.StartsWith("It could not connect to [", refused.Title, StringComparison.Ordinal);
        Assert.EndsWith($"]:{port}: the connection was refused", refused.Title, StringComparison.Ordinal);
        Assert.Contains(" - this computer - and was refused: nothing was listening on that port when it ran.", refused.Explanation, StringComparison.Ordinal);
        Assert.StartsWith($"Start what the program connects to so that it is listening on port {port} of this computer", refused.SuggestedFix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGoProgramConnectingToPostgreSqlsUsualPortIsToldWhatUsuallyListensThere()
    {
        // Only when nothing is listening on PostgreSQL's port here - a computer running PostgreSQL has nothing to refuse.
        if (!LocalFixLiveTests.Available("go") || Listening(5432)) return;

        var file = Write(@"marks\main.go", """
            package main

            import "net"

            func main() {
            	if _, err := net.Dial("tcp", "localhost:5432"); err != nil {
            		panic(err)
            	}
            }
            """);

        var report = await CheckAsync(file, CodeLanguage.Go);

        var refused = RefusalIn(report);
        Assert.EndsWith(":5432: the connection was refused", refused.Title, StringComparison.Ordinal);
        Assert.Contains("5432 is the port PostgreSQL listens on unless it is set up otherwise.", refused.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANodeProgramIsToldTheAddressNodeNames()
    {
        if (!LocalFixLiveTests.Available("node")) return;

        var port = PortNothingListensOn();
        var file = Write("client.js", $"const net = require(\"net\");\nnet.connect({port}, \"localhost\");\n");

        var report = await CheckAsync(file, CodeLanguage.JavaScript);

        var refused = RefusalIn(report);
        Assert.EndsWith($":{port}: the connection was refused", refused.Title, StringComparison.Ordinal);
    }

    private static ParsedError Error(string type, string message, string language = "python") => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = $"{type}: {message}",
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = [],
    };

    [Fact]
    public void NodesAddressIsReadWithItsIPv6HostAsNodeWritesIt()
    {
        // Node writes an IPv6 host without brackets, so its port is what follows the last colon.
        var refusal = RefusedConnection.In(new ParsedError
        {
            LanguageId = "node",
            Confidence = 88,
            RawText = "AggregateError [ECONNREFUSED]: \n  [errors]: [\n    Error: connect ECONNREFUSED ::1:3000",
            FirstLineSequence = 0,
            ExceptionType = "AggregateError",
            ErrorCode = "ECONNREFUSED",
            Frames = [],
        });

        Assert.Equal(("::1", 3000), (refusal?.Host, refusal?.Port));
        Assert.True(refusal!.OnThisComputer);
    }

    [Fact]
    public void RequestsGivesTheAddressInUrllib3sWords()
    {
        // urllib3's words: a pool is HTTPConnectionPool(host='localhost', port=5000), and a MaxRetryError says what it tried
        // and why - here, what Windows says of a refused connection.
        var error = Error("requests.exceptions.ConnectionError",
            "HTTPConnectionPool(host='localhost', port=5000): Max retries exceeded with url: /api/marks (Caused by " +
            "NewConnectionError('<urllib3.connection.HTTPConnection object at 0x000001D2C3B4A5E0>: Failed to establish a new connection: " +
            "[WinError 10061] No connection could be made because the target machine actively refused it'))");

        var refusal = RefusedConnection.In(error);

        Assert.Equal(("localhost", 5000), (refusal?.Host, refusal?.Port));
        Assert.Equal("It could not connect to localhost:5000: the connection was refused", RefusedConnection.TitleOf(refusal!, error));
    }

    [Fact]
    public void PsycopgGivesTheAddressInLibpqsWords()
    {
        // libpq's words: connection to server at "host" (address), port N failed: and the system's reason.
        var refusal = RefusedConnection.In(Error("psycopg2.OperationalError",
            "connection to server at \"localhost\" (::1), port 5432 failed: Connection refused"));

        Assert.Equal(("localhost", 5432, "PostgreSQL"), (refusal?.Host, refusal?.Port, refusal?.UsualListener));
    }

    [Fact]
    public void PyMySqlNamesTheServerAndTheHostButNoPort()
    {
        // PyMySQL's words: Can't connect to MySQL server on 'host' (and the system's reason).
        var refusal = RefusedConnection.In(Error("pymysql.err.OperationalError",
            "(2003, \"Can't connect to MySQL server on 'localhost' ([WinError 10061] No connection could be made because the target machine actively refused it)\")"));

        Assert.Equal(("localhost", (int?)null, "MySQL"), (refusal?.Host, refusal?.Port, refusal?.NamedServer));
        Assert.Contains("The error says it was connecting to a MySQL server.", RefusedConnection.ExplanationOf(refusal!), StringComparison.Ordinal);
        Assert.StartsWith("Start the MySQL server, and check the program again", RefusedConnection.FixOf(refusal!), StringComparison.Ordinal);
    }

    [Fact]
    public void AConnectionRefusedByAnotherMachineIsNotSaidToBeThisComputers()
    {
        var refusal = RefusedConnection.In(Error("panic", "dial tcp 10.0.0.12:3306: connectex: No connection could be made because the target machine actively refused it.", "go"));

        Assert.False(refusal!.OnThisComputer);
        Assert.StartsWith("The program connected to 10.0.0.12:3306 and was refused: that machine had nothing listening on that port, or turned the connection away.",
            RefusedConnection.ExplanationOf(refusal), StringComparison.Ordinal);
        Assert.StartsWith("Check that 10.0.0.12 is running what the program connects to, on port 3306", RefusedConnection.FixOf(refusal), StringComparison.Ordinal);
    }

    [Fact]
    public void WordsAboutAConnectionInTheProgramsOwnLinesAreNotTheRuntimesOwn()
    {
        // A traceback shows the lines it went through; one of the program's own mentioning a refused connection says nothing
        // about why it stopped.
        var withItsLines = new ParsedError
        {
            LanguageId = "python",
            Confidence = 90,
            RawText = "Traceback (most recent call last):\n  File \"report.py\", line 4, in <module>\n    print(\"Connection refused:\", 1 / 0)\nZeroDivisionError: division by zero",
            FirstLineSequence = 0,
            ExceptionType = "ZeroDivisionError",
            Message = "division by zero",
            Frames = [],
        };

        Assert.Null(RefusedConnection.In(withItsLines));
    }
}
