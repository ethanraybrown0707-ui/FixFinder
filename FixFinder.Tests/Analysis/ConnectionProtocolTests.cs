using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Tests;

/// <summary>
/// Database connections and sockets followed as state machines, as files are: made - connected, or not yet - then used, then
/// closed. Each failure is said with what Python 3.13, Java 21 and .NET 10 were seen to raise for it. A with block on a sqlite3
/// connection commits and leaves it open, as Python did, so it is not taken to close it; a server socket, which binds and
/// listens and never connects, is not taken to be sent on before connecting.
/// </summary>
public class ConnectionProtocolTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<IReadOnlyList<AnalysisFinding>?> Analyse(string language, string code)
    {
        switch (language)
        {
            case "Python":
                if (PythonFrontend.FindInterpreter() is not { } python) return null;
                var script = Path.Combine(_temp.Path, "sample.py");
                await File.WriteAllTextAsync(script, code);
                return AbstractChecks.Run(await PythonFrontend.ReadAsync([script], python), new SourceText());

            case "Java":
                if (JavaFrontend.FindTools() is not { } tools) return null;
                var source = Path.Combine(_temp.Path, "App.java");
                await File.WriteAllTextAsync(source, code);
                return AbstractChecks.Run(await JavaFrontend.ReadAsync([source], tools.Javac, tools.Java), new SourceText());

            default:
                var program = Path.Combine(_temp.Path, "Sample.cs");
                await File.WriteAllTextAsync(program, code);
                return AbstractChecks.Run(await CSharpFrontend.ReadAsync([program]), new SourceText());
        }
    }

    private static string Summary(IEnumerable<AnalysisFinding> findings) =>
        string.Join("; ", findings.Select(f => $"{f.CheckId} line {f.Span.Line}: {f.Message}"));

    [Theory]
    [InlineData("Python", "import sqlite3\n\ndef count():\n    conn = sqlite3.connect(':memory:')\n    conn.close()\n    return conn.execute('SELECT 1').fetchone()\n",
        "analysis-connection-used-after-close", 6, "`conn` was closed on line 5, so `conn.execute('SELECT 1')` fails with sqlite3.ProgrammingError: Cannot operate on a closed database.")]
    [InlineData("Python", "import socket\n\ndef ping():\n    connection = socket.socket()\n    connection.sendall(b'ping')\n    connection.close()\n",
        "analysis-used-before-connect", 5, "`connection` was made on line 4 and is not connected yet, so `connection.sendall(b'ping')` fails with OSError")]
    [InlineData("Python", "import socket\n\ndef ping():\n    with socket.socket() as connection:\n        connection.connect(('example.org', 80))\n    connection.sendall(b'ping')\n",
        "analysis-connection-used-after-close", 6, "`connection` was closed on line 4, so `connection.sendall(b'ping')` fails with OSError")]
    [InlineData("Python", "import sqlite3\n\ndef count():\n    with sqlite3.connect(':memory:') as conn:\n        return conn.execute('SELECT 1').fetchone()\n",
        "analysis-connection-not-closed", 4, "`conn`, opened here, is still open when the function returns on line 5")]
    [InlineData("Java", "import java.io.IOException;\nimport java.net.Socket;\n\npublic class App {\n    static void ping() throws IOException {\n        Socket socket = new Socket();\n        socket.getOutputStream().write(1);\n        socket.close();\n    }\n}\n",
        "analysis-used-before-connect", 7, "fails with a SocketException (Socket is not connected)")]
    [InlineData("Java", "import java.io.IOException;\nimport java.net.Socket;\n\npublic class App {\n    static void ping() throws IOException {\n        Socket socket = new Socket(\"example.org\", 80);\n        socket.close();\n        socket.getOutputStream().write(1);\n    }\n}\n",
        "analysis-connection-used-after-close", 8, "fails with a SocketException (Socket is closed)")]
    [InlineData("C#", "using System.Net.Sockets;\n\nclass Program\n{\n    static void Ping()\n    {\n        var client = new TcpClient();\n        client.GetStream().WriteByte(1);\n        client.Close();\n    }\n}\n",
        "analysis-used-before-connect", 8, "fails with an InvalidOperationException (The operation is not allowed on non-connected sockets.)")]
    [InlineData("C#", "using System.Net.Sockets;\n\nclass Program\n{\n    static void Ping()\n    {\n        var client = new TcpClient(\"example.org\", 80);\n        client.Close();\n        client.GetStream().WriteByte(1);\n    }\n}\n",
        "analysis-connection-used-after-close", 9, "fails with an ObjectDisposedException")]
    public async Task AConnectionOrSocketUsedOutOfTurnIsFound(string language, string code, string check, int line, string said)
    {
        if (await Analyse(language, code) is not { } findings) return;

        var finding = Assert.Single(findings, f => f.CheckId == check);
        Assert.Equal(line, finding.Span.Line);
        Assert.Contains(said, finding.Message);
    }

    [Theory]
    // A with block on a sqlite3 connection commits and leaves it open: using it afterwards is fine, and it is closed after.
    [InlineData("Python", "import sqlite3\n\ndef count():\n    with sqlite3.connect(':memory:') as conn:\n        conn.execute('CREATE TABLE t (x)')\n    total = conn.execute('SELECT COUNT(*) FROM t').fetchone()\n    conn.close()\n    return total\n")]
    [InlineData("Python", "import contextlib\nimport sqlite3\n\ndef count():\n    with contextlib.closing(sqlite3.connect(':memory:')) as conn:\n        return conn.execute('SELECT 1').fetchone()\n")]
    [InlineData("Python", "import socket\n\ndef ping():\n    with socket.socket() as connection:\n        connection.connect(('example.org', 80))\n        connection.sendall(b'ping')\n")]
    // A server socket binds, listens and accepts, and is never connected itself.
    [InlineData("Python", "import socket\n\ndef serve():\n    with socket.socket() as server:\n        server.bind(('127.0.0.1', 0))\n        server.listen()\n        client, address = server.accept()\n        client.close()\n")]
    [InlineData("Java", "import java.io.IOException;\nimport java.net.Socket;\n\npublic class App {\n    static void ping() throws IOException {\n        try (Socket socket = new Socket(\"example.org\", 80)) {\n            socket.getOutputStream().write(1);\n        }\n    }\n}\n")]
    [InlineData("C#", "using System.Net.Sockets;\n\nclass Program\n{\n    static void Ping()\n    {\n        using var client = new TcpClient();\n        client.Connect(\"example.org\", 80);\n        client.GetStream().WriteByte(1);\n    }\n}\n")]
    public async Task AConnectionOrSocketUsedInTurnIsLeftAlone(string language, string code)
    {
        if (await Analyse(language, code) is not { } findings) return;

        Assert.True(!findings.Any(f => f.CheckId is "analysis-connection-used-after-close" or "analysis-used-before-connect" or "analysis-connection-not-closed"),
            Summary(findings));
    }
}
