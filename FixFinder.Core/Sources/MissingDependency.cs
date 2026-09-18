using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;

namespace FixFinder.Core.Sources;

/// <summary>Turns "the thing you imported is not installed" into the line that installs it, in whichever ecosystem the program
/// was written in.</summary>
public static partial class MissingDependency
{
    [GeneratedRegex(@"Cannot find (?:module|package)\s+'(?<name>[^']*)'")]
    private static partial Regex NodeMissing();

    [GeneratedRegex(
        @"cannot load such file\s+--\s+(?<name>[^\r\n]+?)(?:\s+\([A-Za-z.:]*Error\))?[ \t\r]*$",
        RegexOptions.Multiline)]
    private static partial Regex RubyMissing();

    [GeneratedRegex(
        @"(?:no required module provides package|cannot find module providing package)\s+(?<name>[^;\r\n]+)" +
        @"|package\s+(?<name>[^;\r\n]+?)\s+is not in (?:GOROOT|std)")]
    private static partial Regex GoMissing();

    [GeneratedRegex(
        @"can't find crate for\s+[`'](?<name>[A-Za-z0-9_]+)[`']" +
        @"|use of undeclared crate or module\s+[`'](?<name>[A-Za-z0-9_]+)[`']" +
        @"|unresolved import\s+[`'](?<name>[A-Za-z0-9_]+)")]
    private static partial Regex RustMissing();

    [GeneratedRegex(@"Could not load file or assembly\s+'(?<name>[^',]*)")]
    private static partial Regex DotNetMissing();

    [GeneratedRegex(@"Can't locate\s+(?<name>[A-Za-z0-9_/]+\.pm)\s+in\s+@INC")]
    private static partial Regex PerlMissing();

    [GeneratedRegex(@"Class\s+[""'](?<name>[^""']*)[""']\s+not found")]
    private static partial Regex PhpMissing();

    [GeneratedRegex(@"Couldn't resolve the package\s+'(?<name>[^']*)'")]
    private static partial Regex DartMissing();

    [GeneratedRegex(@"module\s+'(?<name>[^']*)'\s+not found")]
    private static partial Regex LuaMissing();

    [GeneratedRegex(
        @"(?:ClassNotFoundException|NoClassDefFoundError)[:\s]+(?<name>[^\r\n]+?)[ \t\r]*$" +
        @"|^package\s+(?<name>[^\r\n]+?)\s+does not exist[ \t\r]*$",
        RegexOptions.Multiline)]
    private static partial Regex JavaMissing();

    [GeneratedRegex(@"^(?:@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex SafeNpm();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeGem();

    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_~-]*(\.[a-zA-Z0-9_~-]+)+(/[a-zA-Z0-9][a-zA-Z0-9._~-]*)+$")]
    private static partial Regex SafeGoModule();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex SafeCrate();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeNuGet();

    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)*$")]
    private static partial Regex SafeJavaClass();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(::[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex SafePerlModule();

    [GeneratedRegex(@"^\\?[A-Za-z_][A-Za-z0-9_]*(\\[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex SafePhpClass();

    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafePubPackage();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeRock();

    private static readonly HashSet<string> NodeBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "assert", "async_hooks", "buffer", "child_process", "cluster", "console", "constants",
        "crypto", "dgram", "diagnostics_channel", "dns", "domain", "events", "fs", "http", "http2",
        "https", "inspector", "module", "net", "os", "path", "perf_hooks", "process", "punycode",
        "querystring", "readline", "repl", "stream", "string_decoder", "sys", "timers", "tls",
        "trace_events", "tty", "url", "util", "v8", "vm", "wasi", "worker_threads", "zlib",
    };

    private static readonly HashSet<string> RustNonCrates = new(StringComparer.Ordinal)
    {
        "crate", "self", "super", "std", "core", "alloc",
    };

    private static readonly Dictionary<string, string> KnownGems = new(StringComparer.OrdinalIgnoreCase)
    {
        ["active_support"] = "activesupport",
        ["active_record"] = "activerecord",
        ["active_model"] = "activemodel",
        ["active_job"] = "activejob",
        ["active_storage"] = "activestorage",
        ["action_pack"] = "actionpack",
        ["action_view"] = "actionview",
        ["action_mailer"] = "actionmailer",
        ["action_cable"] = "actioncable",
        ["yaml"] = "psych",
        ["net/http"] = "net-http",
        ["open-uri"] = "open-uri",
    };

    private static readonly (string Prefix, string Coordinate)[] KnownArtifacts =
    [
        ("org.apache.commons.lang3", "org.apache.commons:commons-lang3"),
        ("org.apache.commons.io", "commons-io:commons-io"),
        ("org.apache.commons.codec", "commons-codec:commons-codec"),
        ("org.apache.commons.csv", "org.apache.commons:commons-csv"),
        ("org.apache.logging.log4j", "org.apache.logging.log4j:log4j-core"),
        ("org.apache.httpcomponents", "org.apache.httpcomponents:httpclient"),
        ("org.apache.http", "org.apache.httpcomponents:httpclient"),
        ("org.apache.kafka", "org.apache.kafka:kafka-clients"),
        ("org.apache.poi", "org.apache.poi:poi"),
        ("com.fasterxml.jackson.databind", "com.fasterxml.jackson.core:jackson-databind"),
        ("com.fasterxml.jackson.core", "com.fasterxml.jackson.core:jackson-core"),
        ("com.fasterxml.jackson.annotation", "com.fasterxml.jackson.core:jackson-annotations"),
        ("com.google.gson", "com.google.code.gson:gson"),
        ("com.google.common", "com.google.guava:guava"),
        ("com.squareup.okhttp3", "com.squareup.okhttp3:okhttp"),
        ("okhttp3", "com.squareup.okhttp3:okhttp"),
        ("retrofit2", "com.squareup.retrofit2:retrofit"),
        ("com.zaxxer.hikari", "com.zaxxer:HikariCP"),
        ("org.slf4j", "org.slf4j:slf4j-api"),
        ("ch.qos.logback", "ch.qos.logback:logback-classic"),
        ("org.junit.jupiter", "org.junit.jupiter:junit-jupiter"),
        ("org.testng", "org.testng:testng"),
        ("org.mockito", "org.mockito:mockito-core"),
        ("org.hamcrest", "org.hamcrest:hamcrest"),
        ("org.assertj", "org.assertj:assertj-core"),
        ("org.hibernate", "org.hibernate.orm:hibernate-core"),
        ("org.springframework.boot", "org.springframework.boot:spring-boot-starter"),
        ("org.springframework", "org.springframework:spring-context"),
        ("org.json", "org.json:json"),
        ("org.yaml.snakeyaml", "org.yaml:snakeyaml"),
        ("org.jsoup", "org.jsoup:jsoup"),
        ("org.bouncycastle", "org.bouncycastle:bcprov-jdk18on"),
        ("org.postgresql", "org.postgresql:postgresql"),
        ("com.mysql", "com.mysql:mysql-connector-j"),
        ("redis.clients.jedis", "redis.clients:jedis"),
        ("io.netty", "io.netty:netty-all"),
        ("com.opencsv", "com.opencsv:opencsv"),
        ("lombok", "org.projectlombok:lombok"),
        ("javax.servlet", "jakarta.servlet:jakarta.servlet-api"),
        ("jakarta.servlet", "jakarta.servlet:jakarta.servlet-api"),
    ];

    private static readonly (string Prefix, string Package)[] KnownComposerPackages =
    [
        ("GuzzleHttp", "guzzlehttp/guzzle"),
        ("Monolog", "monolog/monolog"),
        ("Carbon", "nesbot/carbon"),
        ("Ramsey\\Uuid", "ramsey/uuid"),
        ("Twig", "twig/twig"),
        ("Faker", "fakerphp/faker"),
        ("Doctrine\\ORM", "doctrine/orm"),
        ("Doctrine\\DBAL", "doctrine/dbal"),
        ("Doctrine\\Common", "doctrine/common"),
        ("Psr\\Log", "psr/log"),
        ("Psr\\Http", "psr/http-message"),
        ("Psr\\Container", "psr/container"),
        ("PhpOffice\\PhpSpreadsheet", "phpoffice/phpspreadsheet"),
        ("PhpOffice\\PhpWord", "phpoffice/phpword"),
        ("PHPUnit", "phpunit/phpunit"),
        ("PHPMailer", "phpmailer/phpmailer"),
        ("League\\Flysystem", "league/flysystem"),
        ("League\\Csv", "league/csv"),
        ("Intervention\\Image", "intervention/image"),
        ("Predis", "predis/predis"),
        ("Aws", "aws/aws-sdk-php"),
        ("Illuminate", "laravel/framework"),
        ("Symfony\\Component\\Console", "symfony/console"),
        ("Symfony\\Component\\HttpFoundation", "symfony/http-foundation"),
        ("Symfony\\Component\\HttpKernel", "symfony/http-kernel"),
        ("Symfony\\Component\\Routing", "symfony/routing"),
        ("Symfony\\Component\\Yaml", "symfony/yaml"),
        ("Symfony\\Component\\Finder", "symfony/finder"),
        ("Symfony\\Component\\Process", "symfony/process"),
        ("Symfony\\Component\\Mailer", "symfony/mailer"),
    ];

    /// <summary>What is missing, and the line that gets it.</summary>
    public sealed record Missing(
        string Requested,
        string Package,
        string Ecosystem,
        string Text,
        string TextDescription,
        string Url,
        string Note = "");

    public static Missing? Read(ParsedError error, string? workingDirectory = null)
    {
        foreach (var link in Chain(error))
        {
            var found = link.LanguageId switch
            {
                "node" => Node(link, workingDirectory),
                "ruby" => Ruby(link, workingDirectory),
                "go" => Go(link),
                "rust" => Rust(link),
                "csharp" => DotNet(link),
                "java" => Java(link, workingDirectory),
                "perl" => Perl(link),
                "php" => Php(link),
                "dart" => Dart(link),
                "lua" => Lua(link),
                _ => null,
            };

            if (found is not null) return found;
        }

        return null;
    }

    public static FixCandidate? For(ParsedError error, TargetSpec? spec)
    {
        if (Read(error, spec?.WorkingDirectory) is not { } missing) return null;

        var body =
            $"`{missing.Requested}` is not installed for this project.\n\n" +
            (missing.Note is { Length: > 0 } note ? note + "\n\n" : "") +
            $"    {missing.Text.Replace("\n", "\n    ")}\n";

        var candidate = new FixCandidate
        {
            SourceName = missing.Ecosystem,
            Id = $"dep:{missing.Package}",
            Title = string.Equals(missing.Requested, missing.Package, StringComparison.OrdinalIgnoreCase)
                ? $"Install {missing.Package}"
                : $"Install {missing.Package}, which provides {missing.Requested}",
            Url = missing.Url,
            BodyText = body,
            RawBody = body,
            RawBodyIsHtml = false,
            Tier = FixTier.Dependency,
            Command = missing.Text,
            CommandDescription = missing.TextDescription,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow,
            AnswerNoun = "suggestions",
        };

        candidate.Score = 100;
        candidate.ScoreComponents =
        [
            new ScoreComponent(
                "What the program actually needs", 1.0, 1.0,
                $"it reported {missing.Requested} missing, and {missing.Package} is what provides " +
                "it - this is not a search result"),
        ];

        return candidate;
    }

    private static Missing? Node(ParsedError error, string? workingDirectory)
    {
        if (Name(NodeMissing(), error) is not { Length: > 0 } requested) return null;

        if (requested.StartsWith('.') || requested.StartsWith('/') || requested.Contains('\\')) return null;

        if (requested.StartsWith("node:", StringComparison.Ordinal)) return null;

        var segments = requested.Split('/');
        var package = requested.StartsWith('@') && segments.Length >= 2
            ? $"{segments[0]}/{segments[1]}"
            : segments[0];

        if (NodeBuiltIns.Contains(package)) return null;
        if (!SafeNpm().IsMatch(package)) return null;

        var (tool, verb) = Manager(workingDirectory);

        return new Missing(
            requested, package, "Your Node project",
            $"{tool} {verb} {package}",
            $"the {tool} command that installs it",
            $"https://www.npmjs.com/package/{package}",
            requested == package
                ? ""
                : $"It is imported as `{requested}`, which is a path inside the `{package}` package.");
    }

    private static Missing? Ruby(ParsedError error, string? workingDirectory)
    {
        if (Name(RubyMissing(), error) is not { Length: > 0 } requested) return null;

        if (requested.StartsWith('.') || requested.StartsWith('/') || requested.Contains('\\')) return null;

        var package = KnownGems.TryGetValue(requested, out var exact)
            ? exact
            : KnownGems.TryGetValue(requested.Split('/')[0], out var byRoot)
                ? byRoot
                : requested.Split('/')[0];

        if (!SafeGem().IsMatch(package)) return null;

        var bundled = Has(workingDirectory, "Gemfile");

        return new Missing(
            requested, package, "Your Ruby project",
            bundled ? $"bundle add {package}" : $"gem install {package}",
            bundled ? "the bundler command that adds it" : "the command that installs it",
            $"https://rubygems.org/gems/{package}",
            string.Equals(requested, package, StringComparison.OrdinalIgnoreCase)
                ? ""
                : $"It is required as `{requested}` but published as `{package}`.");
    }

    private static Missing? Go(ParsedError error)
    {
        if (Name(GoMissing(), error) is not { Length: > 0 } requested) return null;

        var module = requested.Trim().Trim('"');

        if (!SafeGoModule().IsMatch(module)) return null;

        return new Missing(
            module, module, "Your Go module",
            $"go get {module}",
            "the command that adds it",
            $"https://pkg.go.dev/{module}");
    }

    private static Missing? Rust(ParsedError error)
    {
        if (Name(RustMissing(), error) is not { Length: > 0 } requested) return null;

        if (RustNonCrates.Contains(requested)) return null;
        if (!SafeCrate().IsMatch(requested)) return null;

        return new Missing(
            requested, requested, "Your Rust crate",
            $"cargo add {requested}",
            "the command that adds it",
            $"https://crates.io/crates/{requested}");
    }

    private static Missing? DotNet(ParsedError error)
    {
        if (Name(DotNetMissing(), error) is not { Length: > 0 } requested) return null;

        var package = requested.Trim();

        if (!SafeNuGet().IsMatch(package)) return null;

        return new Missing(
            package, package, "Your .NET project",
            $"dotnet add package {package}",
            "the command that adds it",
            $"https://www.nuget.org/packages/{package}");
    }

    private static Missing? Java(ParsedError error, string? workingDirectory)
    {
        if (Name(JavaMissing(), error) is not { Length: > 0 } requested) return null;

        var className = requested.Replace('/', '.').TrimEnd('.');

        if (!SafeJavaClass().IsMatch(className)) return null;

        var coordinate = KnownArtifacts
            .Where(known => className.StartsWith(known.Prefix + ".", StringComparison.Ordinal) ||
                            className.Equals(known.Prefix, StringComparison.Ordinal))
            .OrderByDescending(known => known.Prefix.Length)
            .Select(known => known.Coordinate)
            .FirstOrDefault();

        if (coordinate is null) return null;

        var group = coordinate.Split(':')[0];
        var artifact = coordinate.Split(':')[1];

        var gradle = Has(workingDirectory, "build.gradle") || Has(workingDirectory, "build.gradle.kts");

        var text = gradle
            ? $"implementation(\"{group}:{artifact}:VERSION\")"
            : $"<dependency>\n    <groupId>{group}</groupId>\n    <artifactId>{artifact}</artifactId>\n    <version>VERSION</version>\n</dependency>";

        return new Missing(
            className, coordinate, "Your Java project", text,
            gradle ? "the line to add to build.gradle" : "the block to add to pom.xml",
            $"https://central.sonatype.com/artifact/{group}/{artifact}",
            $"`{className}` ships in `{coordinate}`. Put the current version in place of VERSION - " +
            "FixFinder does not guess version numbers.");
    }

    private static Missing? Perl(ParsedError error)
    {
        if (Name(PerlMissing(), error) is not { Length: > 0 } requested) return null;

        var module = requested[..^3].Replace("/", "::", StringComparison.Ordinal);

        if (!SafePerlModule().IsMatch(module)) return null;

        return new Missing(
            requested, module, "Your Perl environment",
            $"cpanm {module}",
            "the command that installs it",
            $"https://metacpan.org/pod/{module}");
    }

    private static Missing? Php(ParsedError error)
    {
        if (Name(PhpMissing(), error) is not { Length: > 0 } requested) return null;

        var className = requested.TrimStart('\\');

        if (!SafePhpClass().IsMatch(className)) return null;

        var package = KnownComposerPackages
            .Where(known => className.StartsWith(known.Prefix + "\\", StringComparison.Ordinal) ||
                            className.Equals(known.Prefix, StringComparison.Ordinal))
            .OrderByDescending(known => known.Prefix.Length)
            .Select(known => known.Package)
            .FirstOrDefault();

        if (package is null) return null;

        return new Missing(
            className, package, "Your PHP project",
            $"composer require {package}",
            "the command that installs it",
            $"https://packagist.org/packages/{package}",
            $"`{className}` ships in `{package}`.");
    }

    private static Missing? Dart(ParsedError error)
    {
        if (Name(DartMissing(), error) is not { Length: > 0 } requested) return null;

        if (!SafePubPackage().IsMatch(requested)) return null;

        return new Missing(
            requested, requested, "Your Dart project",
            $"dart pub add {requested}",
            "the command that adds it",
            $"https://pub.dev/packages/{requested}");
    }

    private static Missing? Lua(ParsedError error)
    {
        if (Name(LuaMissing(), error) is not { Length: > 0 } requested) return null;

        var rock = requested.Split('.')[0];

        if (!SafeRock().IsMatch(rock)) return null;

        return new Missing(
            requested, rock, "Your Lua environment",
            $"luarocks install {rock}",
            "the command that installs it",
            $"https://luarocks.org/modules/{rock}",
            requested == rock ? "" : $"`{requested}` is part of the `{rock}` rock.");
    }

    private static string? Name(Regex pattern, ParsedError error)
    {
        foreach (var text in new[] { error.Message, error.RawText })
        {
            if (text is not { Length: > 0 }) continue;

            var match = pattern.Match(text);
            if (!match.Success) continue;

            var value = match.Groups["name"].Captures
                .Select(capture => capture.Value)
                .FirstOrDefault(candidate => candidate.Length > 0);

            if (value is { Length: > 0 }) return value;
        }

        return null;
    }

    private static (string Tool, string Verb) Manager(string? workingDirectory) =>
        Has(workingDirectory, "pnpm-lock.yaml") ? ("pnpm", "add")
        : Has(workingDirectory, "yarn.lock") ? ("yarn", "add")
        : Has(workingDirectory, "bun.lockb") ? ("bun", "add")
        : ("npm", "install");

    private static bool Has(string? workingDirectory, string fileName)
    {
        if (workingDirectory is not { Length: > 0 }) return false;

        try
        {
            var folder = new DirectoryInfo(workingDirectory);

            for (var depth = 0; folder is not null && depth < 6; depth++, folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, fileName)))
                    return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        return false;
    }

    private static IEnumerable<ParsedError> Chain(ParsedError error)
    {
        yield return error;

        foreach (var cause in error.Causes)
            foreach (var nested in Chain(cause))
                yield return nested;
    }
}
