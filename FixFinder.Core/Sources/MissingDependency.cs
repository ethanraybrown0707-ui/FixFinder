using System.Text.RegularExpressions;
using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Ranking;

namespace FixFinder.Core.Sources;

/// <summary>
/// Turns "the thing you imported is not installed" into the line that installs it, in whichever
/// ecosystem the program was written in.
/// </summary>
/// <remarks>
/// <see cref="MissingModule"/> did this for Python and proved the shape: a missing package is the
/// commonest fixable crash there is, the code is not wrong, and the answer is one line rather than
/// a patch. Every other language FixFinder can read has the same error and had no answer for it,
/// which meant nine languages could be understood and one could be helped.
/// <para>
/// <b>The name always arrives from untrusted output.</b> A program can print anything it likes,
/// including <c>Cannot find module '--registry=http://evil.invalid'</c>. Every ecosystem below
/// therefore validates what it extracted against its own anchored allow-list before the name goes
/// anywhere near a command line, and none of them accept a leading dash. A name that does not
/// match is refused outright rather than cleaned up: the interesting question about
/// <c>'; rm -rf /'</c> is not how to escape it.
/// </para>
/// <para>
/// <b>Nothing here runs anything.</b> The command is text to be read, copied and run by hand, so
/// the worst case for a mis-parse is a line that does not work - not a line that does something.
/// </para>
/// </remarks>
public static partial class MissingDependency
{
    // ------------------------------------------------------------------ what was asked for

    /// <summary>Node, both the CommonJS and the ES-module wording.</summary>
    [GeneratedRegex(@"Cannot find (?:module|package)\s+'(?<name>[^']*)'")]
    private static partial Regex NodeMissing();

    /// <summary>Ruby's LoadError, which names the path it tried rather than the gem.</summary>
    /// <remarks>
    /// Taken to the end of the line rather than to the first space, for the reason set out on
    /// <see cref="GoMissing"/>: stopping at whitespace reads <c>nokogiri &amp;&amp; curl
    /// evil.invalid</c> as <c>nokogiri</c> and offers an ordinary-looking install for a line that
    /// said something else.
    /// <para>
    /// The one thing allowed to follow the name is Ruby's own <c>(LoadError)</c> suffix, because
    /// that genuinely is part of the format - <c>app.rb:1:in 'require': cannot load such file --
    /// nokogiri (LoadError)</c>. It is a narrow, anchored exception rather than general
    /// trailing-junk tolerance.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"cannot load such file\s+--\s+(?<name>[^\r\n]+?)(?:\s+\([A-Za-z.:]*Error\))?[ \t]*$",
        RegexOptions.Multiline)]
    private static partial Regex RubyMissing();

    /// <summary>
    /// Go, which is unusually helpful - it prints the exact command as part of the error.
    /// </summary>
    /// <remarks>
    /// The command it prints is still not used verbatim. It is rebuilt from the module path after
    /// that path has been validated, because "the compiler told me to" is not a reason to run a
    /// string a program emitted.
    /// <para>
    /// <b>Terminated by the semicolon or the end of the line, not by whitespace.</b> Stopping at
    /// the first space looked equivalent and was not, in exactly the way
    /// <see cref="MissingModule"/> was already caught by: <c>provides package github.com/x/y
    /// &amp;&amp; echo hi</c> read as <c>github.com/x/y</c> and was offered as an ordinary install,
    /// quietly turning a line a program made up into a different, plausible one. Taking the whole
    /// token means the validation below sees what was printed and refuses it. Go's real wording
    /// ends the path with <c>;</c> - "to add it:" follows - so nothing legitimate is lost.
    /// </para>
    /// </remarks>
    [GeneratedRegex(
        @"(?:no required module provides package|cannot find module providing package)\s+(?<name>[^;\r\n]+)" +
        @"|package\s+(?<name>[^;\r\n]+?)\s+is not in (?:GOROOT|std)")]
    private static partial Regex GoMissing();

    /// <summary>Rust, across the three ways rustc says it.</summary>
    [GeneratedRegex(
        @"can't find crate for\s+[`'](?<name>[A-Za-z0-9_]+)[`']" +
        @"|use of undeclared crate or module\s+[`'](?<name>[A-Za-z0-9_]+)[`']" +
        @"|unresolved import\s+[`'](?<name>[A-Za-z0-9_]+)")]
    private static partial Regex RustMissing();

    /// <summary>
    /// .NET, taking the assembly's simple name and leaving the version and key behind.
    /// </summary>
    /// <remarks>
    /// The printed name is a full display name - <c>'Newtonsoft.Json, Version=13.0.0.0,
    /// Culture=neutral, PublicKeyToken=30ad4fe6b2a6aeed'</c>. Only the part before the first comma
    /// is the package.
    /// </remarks>
    [GeneratedRegex(@"Could not load file or assembly\s+'(?<name>[^',]*)")]
    private static partial Regex DotNetMissing();

    /// <summary>
    /// Perl, which names the module it could not find and often the fix alongside it.
    /// </summary>
    /// <remarks>
    /// The most helpful error in this file: <c>Can't locate Foo/Bar.pm in @INC (you may need to
    /// install the Foo::Bar module)</c>. The path is the module with <c>::</c> written as <c>/</c>,
    /// so the translation is exact rather than a lookup.
    /// </remarks>
    [GeneratedRegex(@"Can't locate\s+(?<name>[A-Za-z0-9_/]+\.pm)\s+in\s+@INC")]
    private static partial Regex PerlMissing();

    /// <summary>PHP, which names the class rather than the package that ships it.</summary>
    [GeneratedRegex(@"Class\s+[""'](?<name>[^""']*)[""']\s+not found")]
    private static partial Regex PhpMissing();

    /// <summary>Dart, resolving a package that is not in pubspec.yaml.</summary>
    [GeneratedRegex(@"Couldn't resolve the package\s+'(?<name>[^']*)'")]
    private static partial Regex DartMissing();

    /// <summary>Lua, where a failed require names the rock.</summary>
    [GeneratedRegex(@"module\s+'(?<name>[^']*)'\s+not found")]
    private static partial Regex LuaMissing();

    /// <summary>Java, where the class is named but the artifact that ships it is not.</summary>
    /// <remarks>
    /// To the end of the line, again for the reason on <see cref="GoMissing"/>. The class name is
    /// the last thing on its line in every real trace, wrapped or not, so nothing legitimate ends
    /// up outside the capture - while <c>NoClassDefFoundError: org/x/Y (wrong name: org/x/Z)</c>,
    /// which is a misplaced class file rather than a missing dependency, now fails validation
    /// instead of being answered with the wrong artifact.
    /// </remarks>
    [GeneratedRegex(
        @"(?:ClassNotFoundException|NoClassDefFoundError)[:\s]+(?<name>[^\r\n]+?)[ \t]*$",
        RegexOptions.Multiline)]
    private static partial Regex JavaMissing();

    // ------------------------------------------------------------------ what is allowed through

    /// <summary>An npm name, optionally scoped. Lower-case, as the registry requires.</summary>
    [GeneratedRegex(@"^(?:@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex SafeNpm();

    /// <summary>A gem name.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeGem();

    /// <summary>
    /// A Go module path: a dotted host, then at least one path segment.
    /// </summary>
    /// <remarks>
    /// The host must contain a dot and there must be a slash after it, which is what separates a
    /// real module path from a bare word. <c>..</c> cannot appear as a segment, so a path cannot
    /// climb anywhere even though this one legitimately contains slashes.
    /// </remarks>
    [GeneratedRegex(@"^[a-zA-Z0-9][a-zA-Z0-9_~-]*(\.[a-zA-Z0-9_~-]+)+(/[a-zA-Z0-9][a-zA-Z0-9._~-]*)+$")]
    private static partial Regex SafeGoModule();

    /// <summary>A crate name.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*$")]
    private static partial Regex SafeCrate();

    /// <summary>A NuGet package id.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeNuGet();

    /// <summary>A fully-qualified Java class name.</summary>
    [GeneratedRegex(@"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)*$")]
    private static partial Regex SafeJavaClass();

    /// <summary>A Perl module name, in its <c>::</c> form.</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(::[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex SafePerlModule();

    /// <summary>A PHP class name, namespace separators included.</summary>
    [GeneratedRegex(@"^\\?[A-Za-z_][A-Za-z0-9_]*(\\[A-Za-z_][A-Za-z0-9_]*)*$")]
    private static partial Regex SafePhpClass();

    /// <summary>A pub package name, which is always lower snake case.</summary>
    [GeneratedRegex(@"^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafePubPackage();

    /// <summary>A LuaRocks rock name.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeRock();

    // ------------------------------------------------------------------ names that are not packages

    /// <summary>
    /// Node's built-in modules, which are never missing and never installable.
    /// </summary>
    /// <remarks>
    /// A failed <c>require('fs')</c> means something else is wrong - a bundler config, or a
    /// browser target. Offering <c>npm install fs</c> would send someone to a squatted placeholder
    /// package, which is worse than saying nothing.
    /// </remarks>
    private static readonly HashSet<string> NodeBuiltIns = new(StringComparer.OrdinalIgnoreCase)
    {
        "assert", "async_hooks", "buffer", "child_process", "cluster", "console", "constants",
        "crypto", "dgram", "diagnostics_channel", "dns", "domain", "events", "fs", "http", "http2",
        "https", "inspector", "module", "net", "os", "path", "perf_hooks", "process", "punycode",
        "querystring", "readline", "repl", "stream", "string_decoder", "sys", "timers", "tls",
        "trace_events", "tty", "url", "util", "v8", "vm", "wasi", "worker_threads", "zlib",
    };

    /// <summary>Rust path roots that are keywords or the standard library, not crates.</summary>
    private static readonly HashSet<string> RustNonCrates = new(StringComparer.Ordinal)
    {
        "crate", "self", "super", "std", "core", "alloc",
    };

    // ------------------------------------------------------------------ name is not the package

    /// <summary>
    /// Ruby's require-path-to-gem mismatches.
    /// </summary>
    /// <remarks>
    /// Rails is the reason this is not empty: every one of its components is required with
    /// underscores and published without them.
    /// </remarks>
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

    /// <summary>
    /// Java package prefixes and the Maven coordinate that ships them, longest prefix wins.
    /// </summary>
    /// <remarks>
    /// A class name does not contain its artifact, and there is no offline way to derive one - so
    /// this is the one ecosystem where an unknown name produces no answer at all rather than a
    /// guess. Guessing here would be uniquely bad: <c>groupId</c> and <c>artifactId</c> are rarely
    /// the package name, so a made-up coordinate would not merely be wrong, it would not resolve.
    /// </remarks>
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

    /// <summary>
    /// PHP namespace roots and the Composer package that ships them, longest prefix wins.
    /// </summary>
    /// <remarks>
    /// The same problem Java has, and answered the same way: a namespace is not a package name, so
    /// an unknown one produces nothing rather than a <c>composer require</c> that resolves to
    /// nothing. Symfony is the tempting exception - <c>Symfony\Component\Console</c> really is
    /// <c>symfony/console</c> every time - but deriving it would be a rule with one example, so
    /// the components that come up are listed instead.
    /// </remarks>
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

    // ------------------------------------------------------------------ the answer

    /// <summary>What is missing, and the line that gets it.</summary>
    /// <param name="Requested">The name as the program asked for it.</param>
    /// <param name="Package">What the registry calls it, which is usually the same.</param>
    /// <param name="Ecosystem">Where it comes from, for the label on the result.</param>
    /// <param name="Text">The command to run, or the snippet to paste, already validated.</param>
    /// <param name="TextDescription">What <paramref name="Text"/> is, for the copy button.</param>
    /// <param name="Url">The registry page, so the claim can be checked.</param>
    /// <param name="Note">Why the package name differs, when it does. Empty otherwise.</param>
    public sealed record Missing(
        string Requested,
        string Package,
        string Ecosystem,
        string Text,
        string TextDescription,
        string Url,
        string Note = "");

    /// <summary>
    /// Reads a missing dependency out of a crash in any supported ecosystem, or returns null.
    /// </summary>
    /// <remarks>
    /// The whole cause chain is searched, not just the outermost error. Java in particular almost
    /// always reports this wrapped - an <c>ExceptionInInitializerError</c> whose third
    /// <c>Caused by</c> is the <c>ClassNotFoundException</c> that actually explains it.
    /// </remarks>
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

    /// <summary>Builds the result offering the install, or null when there is nothing to offer.</summary>
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

        // Not ranked among the search results, for the same reason the runtime's own correction is
        // not: this did not come from anybody's opinion about what the error resembles. The program
        // named what it could not load.
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

    // ------------------------------------------------------------------ per ecosystem

    private static Missing? Node(ParsedError error, string? workingDirectory)
    {
        if (Name(NodeMissing(), error) is not { Length: > 0 } requested) return null;

        // A relative specifier is a file in the project that is missing or misspelled, not a
        // package. Installing a name off the registry that happens to match would be actively
        // harmful, so these are refused rather than resolved.
        if (requested.StartsWith('.') || requested.StartsWith('/') || requested.Contains('\\')) return null;

        if (requested.StartsWith("node:", StringComparison.Ordinal)) return null;

        // A subpath import names the package before the first slash - except a scoped package,
        // where the name is the first two segments.
        var segments = requested.Split('/');
        var package = requested.StartsWith('@') && segments.Length >= 2
            ? $"{segments[0]}/{segments[1]}"
            : segments[0];

        if (NodeBuiltIns.Contains(package)) return null;
        if (!SafeNpm().IsMatch(package)) return null;

        // Whichever manager this project is actually using. Running npm in a pnpm workspace
        // produces a second, conflicting lockfile, which is a worse day than the missing package.
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

        // A require path can reach inside a gem - require 'active_support/all'. The gem is the
        // first segment, unless the whole path is one of the known mismatches.
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

        // NoClassDefFoundError prints the internal form, with slashes for dots.
        var className = requested.Replace('/', '.').TrimEnd('.');

        if (!SafeJavaClass().IsMatch(className)) return null;

        var coordinate = KnownArtifacts
            .Where(known => className.StartsWith(known.Prefix + ".", StringComparison.Ordinal) ||
                            className.Equals(known.Prefix, StringComparison.Ordinal))
            .OrderByDescending(known => known.Prefix.Length)
            .Select(known => known.Coordinate)
            .FirstOrDefault();

        // No coordinate, no answer. See KnownArtifacts: a guessed groupId does not resolve, so
        // this is one case where the honest result is to leave it to the search.
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

        // Foo/Bar.pm is Foo::Bar written the way the filesystem stores it.
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

        // As with Java: no mapping, no answer. A namespace is not a Composer package.
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

        // A submodule require names the rock before the first dot - require 'socket.http'.
        var rock = requested.Split('.')[0];

        if (!SafeRock().IsMatch(rock)) return null;

        return new Missing(
            requested, rock, "Your Lua environment",
            $"luarocks install {rock}",
            "the command that installs it",
            $"https://luarocks.org/modules/{rock}",
            requested == rock ? "" : $"`{requested}` is part of the `{rock}` rock.");
    }

    // ------------------------------------------------------------------ shared

    /// <summary>
    /// The first non-empty capture, searched in the message and then the raw text.
    /// </summary>
    /// <remarks>
    /// Both are tried because where the detail sits differs by language. Rust puts it on the
    /// <c>error[E0432]</c> line that the parser keeps as the message; Java's
    /// <c>NoClassDefFoundError</c> often carries the name only in the wrapped block.
    /// </remarks>
    private static string? Name(Regex pattern, ParsedError error)
    {
        foreach (var text in new[] { error.Message, error.RawText })
        {
            if (text is not { Length: > 0 }) continue;

            var match = pattern.Match(text);
            if (!match.Success) continue;

            // Alternation means only one of the name groups fills in; the rest are empty.
            var value = match.Groups["name"].Captures
                .Select(capture => capture.Value)
                .FirstOrDefault(candidate => candidate.Length > 0);

            if (value is { Length: > 0 }) return value;
        }

        return null;
    }

    /// <summary>Which Node package manager this project uses, read off its lockfile.</summary>
    private static (string Tool, string Verb) Manager(string? workingDirectory) =>
        Has(workingDirectory, "pnpm-lock.yaml") ? ("pnpm", "add")
        : Has(workingDirectory, "yarn.lock") ? ("yarn", "add")
        : Has(workingDirectory, "bun.lockb") ? ("bun", "add")
        : ("npm", "install");

    /// <summary>
    /// Whether a marker file sits in the working directory or a folder above it.
    /// </summary>
    /// <remarks>
    /// Walks up because the program is often run from a subdirectory of the project it belongs to.
    /// Bounded, and never throws - an unreadable path just means the default is used, which is
    /// right often enough that a permissions error should not stop a suggestion being made.
    /// </remarks>
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

    /// <summary>The error and everything it was caused by, outermost first.</summary>
    private static IEnumerable<ParsedError> Chain(ParsedError error)
    {
        yield return error;

        foreach (var cause in error.Causes)
            foreach (var nested in Chain(cause))
                yield return nested;
    }
}
