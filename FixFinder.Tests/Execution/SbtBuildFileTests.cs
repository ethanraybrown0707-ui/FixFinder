using FixFinder.Core.Execution;

namespace FixFinder.Tests;

/// <summary>The libraries a build.sbt names, read as Scala CLI's --dep takes them - and what is left out, and what could not be read.</summary>
public class SbtBuildFileTests
{
    [Fact]
    public void ALibraryForEachScalaAndAJavaLibraryAreReadAsScalaCliNamesThem()
    {
        var build = SbtBuildFile.Of("""
            val gsonVersion = "2.10.1"

            libraryDependencies += "org.typelevel" %% "cats-core" % "2.10.0"
            libraryDependencies += "com.google.code.gson" % "gson" % gsonVersion
            """);

        Assert.Equal(new[] { "org.typelevel::cats-core:2.10.0", "com.google.code.gson:gson:2.10.1" }, build.Libraries);
        Assert.Empty(build.NotRead);
    }

    [Fact]
    public void LibrariesOnlyTheTestsUseAndOnesCommentedOutAreLeftOut()
    {
        var build = SbtBuildFile.Of("""
            libraryDependencies ++= Seq(
              "com.lihaoyi" %% "os-lib" % "0.9.1",
              "org.scalatest" %% "scalatest" % "3.2.17" % Test,
              "org.scalameta" %% "munit" % "0.7.29" % "test"
            )
            // libraryDependencies += "org.typelevel" %% "cats-core" % "2.10.0"
            /* libraryDependencies += "org.typelevel" %% "cats-effect" % "3.5.2" */
            """);

        Assert.Equal(new[] { "com.lihaoyi::os-lib:0.9.1" }, build.Libraries);
    }

    [Fact]
    public void APluginIsNotALibraryOfTheProgramsAndWhatCannotBeReadIsSaidToBeSo()
    {
        var build = SbtBuildFile.Of("""
            addSbtPlugin("org.scalameta" % "sbt-scalafmt" % "2.5.2")
            libraryDependencies += "org.scala-js" %%% "scalajs-dom" % "2.8.0"
            libraryDependencies += "com.example" %% "marks" % versionFromGit()
            scalaVersion := "3.3.1"
            """);

        Assert.Empty(build.Libraries);
        Assert.Equal(2, build.NotRead.Count);
        Assert.Equal("3.3.1", build.ScalaVersion);
    }
}
