using System.Globalization;
using System.Text.RegularExpressions;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Checking;

/// <summary>
/// A program that stopped because a connection it made - to a database, a server, a web API - was refused, read from what
/// its runtime said. Nothing was listening where it connected: the program's code is not what is wrong, and nothing in it
/// can be changed to make the connection work.
/// </summary>
/// <remarks>
/// Each runtime says it in its own words: Windows' "actively refused", and its code 10061, in Python, Go and C#; POSIX's
/// "Connection refused" in Java and in Python elsewhere; Node's ECONNREFUSED. The address is taken only from the error, where
/// it names one, in the forms each runtime and the common database libraries write it.
/// </remarks>
public static partial class RefusedConnection
{
    /// <param name="Host">The host connected to, as the error names it; null when it names none.</param>
    /// <param name="Port">The port connected to, as the error names it; null when it names none.</param>
    /// <param name="SaidRefused">
    /// Whether the runtime said the connection was refused. Java's HTTP client gives only a ConnectException with no words,
    /// which Java gives when a connection cannot be made - typically because nothing is listening there.
    /// </param>
    /// <param name="NamedServer">The server the error itself names: "Can't connect to MySQL server" names MySQL.</param>
    /// <param name="UsualListener">What listens on the port unless it is set up otherwise: PostgreSQL on 5432.</param>
    public sealed record Refusal(string? Host, int? Port, bool SaidRefused, string? NamedServer, string? UsualListener)
    {
        /// <summary>The host and port as one address - localhost:5432 - or the host alone, or null when the error named neither.</summary>
        public string? Address => Host is null ? null : Port is null ? Host : $"{Host}:{Port}";

        /// <summary>Whether the host is this computer: localhost, or its loopback address.</summary>
        public bool OnThisComputer => Host?.Trim('[', ']').ToLowerInvariant() is "localhost" or "127.0.0.1" or "::1" or "::ffff:127.0.0.1";
    }

    [GeneratedRegex(@"actively refused|WinError 10061|Error 10061|\(10061\)|Connection refused|ECONNREFUSED", RegexOptions.IgnoreCase)]
    private static partial Regex RefusedWords();

    /// <summary>The ways an address is written in these errors, the most particular first.</summary>
    private static readonly Regex[] AddressForms =
    [
        // Node, whose IPv6 addresses have no brackets: connect ECONNREFUSED ::1:59999
        new(@"ECONNREFUSED (?<host>\S+):(?<port>\d{1,5})\b"),
        // urllib3, under requests: HTTPConnectionPool(host='localhost', port=5000)
        new(@"host='(?<host>[^']+)', port=(?<port>\d{1,5})"),
        // libpq, under psycopg: connection to server at "localhost" (::1), port 5432 failed
        new(@"connection to server at ""(?<host>[^""]+)""(?: \([^)]*\))?, port (?<port>\d{1,5}) failed"),
        // MySQL's Python drivers: Can't connect to MySQL server on 'localhost' / on 'localhost:3306'
        new(@"Can't connect to MySQL server on '(?<host>[^':]+)(?::(?<port>\d{1,5}))?'"),
        // Written out as host:port - C#'s [::ffff:127.0.0.1]:59999 and (localhost:59999), Go's dial tcp [::1]:5432, PostgreSQL's
        // JDBC driver's localhost:5432.
        new(@"(?<host>localhost|\d{1,3}(?:\.\d{1,3}){3}|\[[0-9A-Fa-f:.]+\]):(?<port>\d{1,5})\b"),
    ];

    /// <summary>What listens on a port unless it is set up otherwise.</summary>
    private static readonly Dictionary<int, string> UsualListeners = new()
    {
        [3306] = "MySQL",
        [5432] = "PostgreSQL",
        [1433] = "SQL Server",
        [1521] = "Oracle Database",
        [27017] = "MongoDB",
        [6379] = "Redis",
    };

    /// <summary>The refused connection an error is, or null when it is anything else.</summary>
    public static Refusal? In(ParsedError error)
    {
        // What the runtime said: the error's type and message, and those of the errors under it - not the program's own lines,
        // which a traceback shows too. Node prints what it knows of an error as an object, so all of that is what it said.
        var said = string.Join("\n", new[] { error }.Concat(error.Causes)
            .SelectMany(each => new[] { each.ExceptionType, each.Message })
            .Append(error.LanguageId == "node" ? error.RawText : null)
            .OfType<string>());
        var saidRefused = RefusedWords().IsMatch(said);

        // Java's HTTP client says only ConnectException, with no words of its own, and nothing under it says more.
        var wordlessConnectException = !saidRefused &&
            new[] { error }.Concat(error.Causes).Any(each => each.ExceptionType == "java.net.ConnectException" && string.IsNullOrWhiteSpace(each.Message));

        if (!saidRefused && !wordlessConnectException) return null;

        string? host = null;
        int? port = null;

        foreach (var form in AddressForms)
        {
            if (form.Match(said) is not { Success: true } address) continue;

            host = address.Groups["host"].Value;
            port = address.Groups["port"].Success && int.TryParse(address.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
            break;
        }

        var namedServer = said.Contains("MySQL server", StringComparison.Ordinal) ? "MySQL" : null;
        var usualListener = port is { } known && UsualListeners.TryGetValue(known, out var usual) ? usual : null;

        return new Refusal(host, port, saidRefused, namedServer, usualListener);
    }

    /// <summary>The finding's title: what the program could not do, and to where.</summary>
    public static string TitleOf(Refusal refusal, ParsedError error)
    {
        var to = refusal.Address is { } address ? $" to {address}" : "";

        return refusal.SaidRefused
            ? $"It could not connect{to}: the connection was refused"
            : $"It could not connect{to}: {error.ShortExceptionType ?? error.ExceptionType}";
    }

    /// <summary>What happened, in terms of what the error said and nothing more.</summary>
    public static string ExplanationOf(Refusal refusal)
    {
        var what = !refusal.SaidRefused
            ? "Java could not make a connection the program asked for, and gave a ConnectException with no reason - which Java gives when a " +
              "connection cannot be made, typically because nothing is listening at the address and port."
            : refusal.Address is not { } address
                ? "The program made a connection - to a database, a server or a service - and it was refused: nothing was listening where " +
                  "it connected when it ran."
                : refusal.OnThisComputer
                    ? $"The program connected to {address} - this computer - and was refused: nothing was listening on that port when it ran."
                    : $"The program connected to {address} and was refused: that machine had nothing listening on that port, or turned the " +
                      "connection away.";

        var whatListens = refusal switch
        {
            { NamedServer: { } server } => $" The error says it was connecting to a {server} server.",
            { UsualListener: { } usual, Port: { } port } => $" {port} is the port {usual} listens on unless it is set up otherwise.",
            _ => "",
        };

        return $"{what}{whatListens} That is not a mistake in the code: the program stops there because what it connects to was not there " +
               "to answer.";
    }

    /// <summary>What to do: start what the program connects to, or give the program the address it is really at.</summary>
    public static string FixOf(Refusal refusal)
    {
        var what = refusal.NamedServer is { } server ? $"the {server} server" : "what the program connects to";

        return refusal switch
        {
            { Port: { } port, OnThisComputer: true } =>
                $"Start {what} so that it is listening on port {port} of this computer, and check the program again. If it is meant to " +
                "connect somewhere else, give it that address instead.",
            { Host: { } host, Port: { } port } =>
                $"Check that {host} is running {what}, on port {port}, and lets this computer connect; or give the program the address it " +
                "is really at.",
            _ => $"Start {what}, and check the program again - or give the program the address it is really at.",
        };
    }
}
