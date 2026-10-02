using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.Libraries;

/// <summary>
/// Running a Java class of tests - JUnit's @Test methods, and no main - with JUnit itself. A small launcher of FixFinder's,
/// compiled beside the tests, hands the class to JUnit 5's launcher or JUnit 4's JUnitCore, and prints what each test did
/// as one line, read back by <see cref="TestReport"/>; whether a test passed is JUnit's to say, not FixFinder's.
/// </summary>
public static partial class JavaTests
{
    public enum Framework { None, JUnit4, JUnit5 }

    /// <summary>What running a class of tests needs besides the project's libraries, or why it cannot be run.</summary>
    public sealed record Runner(Framework Framework, IReadOnlyList<string> ExtraJars, string? CannotRun);

    public const string LauncherClass = "FixFinderTestLauncher";

    [GeneratedRegex(@"(?m)^\s*import\s+(?:static\s+)?org\.junit\.(?<which>jupiter\.)?")]
    private static partial Regex JUnitImport();

    [GeneratedRegex(@"@(?:org\.junit\.(?:jupiter\.api\.)?)?(?:Test|ParameterizedTest|RepeatedTest|TestFactory)\b")]
    private static partial Regex TestAnnotation();

    [GeneratedRegex(@"junit-platform-engine-(?<version>[\w.-]+?)\.jar$", RegexOptions.IgnoreCase)]
    private static partial Regex PlatformEngineJar();

    /// <summary>Which JUnit a file's tests are written for, or None when it is not a class of tests.</summary>
    public static Framework FrameworkOf(string javaFile)
    {
        string text;
        try
        {
            text = File.ReadAllText(javaFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Framework.None;
        }

        if (!TestAnnotation().IsMatch(text) || JUnitImport().Match(text) is not { Success: true } import) return Framework.None;
        return import.Groups["which"].Success ? Framework.JUnit5 : Framework.JUnit4;
    }

    /// <summary>
    /// What running the tests needs: for JUnit 5, its engine and its launcher - the launcher is often not one of a
    /// project's libraries, since Maven and Gradle bring their own, so it is looked for among what is downloaded, at the
    /// version of the platform the project uses; for JUnit 4, its jar. The reason it cannot be run, when something is missing.
    /// </summary>
    public static Runner RunnerFor(Framework framework, IReadOnlyList<string> classPath, LibraryStore store)
    {
        bool Has(string name) => classPath.Any(jar => Path.GetFileName(jar).Contains(name, StringComparison.OrdinalIgnoreCase));

        if (framework == Framework.JUnit4)
        {
            // Maven's junit-4.13.2.jar, a junit.jar kept by hand, or the org.junit_4.13.2 bundle Eclipse's own JUnit is.
            return Has("junit-4") || Has("junit.jar") || classPath.Any(jar => Regex.IsMatch(Path.GetFileName(jar), @"^(?:junit[-_]?4|org\.junit_4\.)", RegexOptions.IgnoreCase))
                ? new Runner(framework, [], null)
                : new Runner(framework, [], "JUnit 4's jar is not among the project's libraries");
        }

        if (framework != Framework.JUnit5) return new Runner(framework, [], "it is not a class of JUnit tests");
        if (Has("junit-platform-console-standalone")) return new Runner(framework, [], null);

        if (!Has("junit-jupiter-engine"))
            return new Runner(framework, [], "JUnit 5's engine, junit-jupiter-engine, is not among the project's libraries - junit-jupiter-api alone lets tests compile but not run");

        if (Has("junit-platform-launcher")) return new Runner(framework, [], null);

        var version = classPath.Select(jar => PlatformEngineJar().Match(Path.GetFileName(jar))).FirstOrDefault(match => match.Success)?.Groups["version"].Value;
        if (version is not null && store.FileOf(new LibraryName("org.junit.platform", "junit-platform-launcher", version), "jar") is { } launcher)
            return new Runner(framework, [launcher], null);

        return new Runner(framework, [],
            $"JUnit 5's launcher, junit-platform-launcher{(version is null ? "" : " " + version)}, is not among the project's libraries or in {store.Name} - " +
            "Maven and Gradle download it when they run the tests, and an IDE brings its own");
    }

    /// <summary>
    /// The package the launcher is in: a package of its own, so it can be compiled into a program written as a module, as a
    /// module's tests are compiled - a module cannot hold a class in no package.
    /// </summary>
    public const string LauncherPackage = "fixfinder";

    /// <summary>The launcher's class by its full name, as java is told to run it.</summary>
    public const string LauncherFullName = LauncherPackage + "." + LauncherClass;

    /// <summary>The launcher's Java source for this JUnit: plain Java 8, so it compiles under any release a course uses.</summary>
    public static string LauncherSource(Framework framework) =>
        $"package {LauncherPackage};\n\n" + (framework == Framework.JUnit4 ? JUnit4Launcher : JUnit5Launcher) + Reporting + "}\n";

    private const string JUnit5Launcher = """
        import org.junit.platform.engine.TestExecutionResult;
        import org.junit.platform.engine.discovery.DiscoverySelectors;
        import org.junit.platform.engine.support.descriptor.MethodSource;
        import org.junit.platform.launcher.Launcher;
        import org.junit.platform.launcher.LauncherDiscoveryRequest;
        import org.junit.platform.launcher.TestExecutionListener;
        import org.junit.platform.launcher.TestIdentifier;
        import org.junit.platform.launcher.core.LauncherDiscoveryRequestBuilder;
        import org.junit.platform.launcher.core.LauncherFactory;

        /** Written by FixFinder: runs one class of tests with JUnit 5 and prints what each test did, one line each. */
        public final class FixFinderTestLauncher {
            public static void main(String[] args) {
                LauncherDiscoveryRequest request = LauncherDiscoveryRequestBuilder.request()
                    .selectors(DiscoverySelectors.selectClass(args[0])).build();
                Launcher launcher = LauncherFactory.create();
                final int[] found = {0};
                launcher.execute(request, new TestExecutionListener() {
                    @Override
                    public void executionSkipped(TestIdentifier test, String reason) {
                        if (test.isTest()) {
                            found[0]++;
                            report("SKIPPED", test.getDisplayName(), sourceClass(test), sourceMethod(test), null, reason);
                        }
                    }

                    @Override
                    public void executionFinished(TestIdentifier test, TestExecutionResult result) {
                        Throwable thrown = result.getThrowable().orElse(null);
                        if (test.isTest()) {
                            found[0]++;
                            report(result.getStatus().name(), test.getDisplayName(), sourceClass(test), sourceMethod(test), thrown, null);
                        } else if (result.getStatus() == TestExecutionResult.Status.FAILED && thrown != null) {
                            report("FAILED", test.getDisplayName(), "", "", thrown, null);
                        }
                    }
                });
                System.out.println("FIXFINDER-TESTS-FOUND\t" + found[0]);
            }

            private static String sourceClass(TestIdentifier test) {
                return test.getSource().isPresent() && test.getSource().get() instanceof MethodSource
                    ? ((MethodSource) test.getSource().get()).getClassName() : "";
            }

            private static String sourceMethod(TestIdentifier test) {
                return test.getSource().isPresent() && test.getSource().get() instanceof MethodSource
                    ? ((MethodSource) test.getSource().get()).getMethodName() : "";
            }

        """;

    private const string JUnit4Launcher = """
        import java.util.HashSet;
        import java.util.Set;
        import org.junit.runner.Description;
        import org.junit.runner.JUnitCore;
        import org.junit.runner.Request;
        import org.junit.runner.notification.Failure;
        import org.junit.runner.notification.RunListener;

        /** Written by FixFinder: runs one class of tests with JUnit 4 and prints what each test did, one line each. */
        public final class FixFinderTestLauncher {
            public static void main(String[] args) throws Exception {
                JUnitCore core = new JUnitCore();
                final Set<Description> finishedBadly = new HashSet<Description>();
                final int[] found = {0};
                core.addListener(new RunListener() {
                    @Override
                    public void testFailure(Failure failure) {
                        finishedBadly.add(failure.getDescription());
                        Description test = failure.getDescription();
                        report("FAILED", test.getDisplayName(), nonNull(test.getClassName()), nonNull(test.getMethodName()), failure.getException(), null);
                    }

                    @Override
                    public void testAssumptionFailure(Failure failure) {
                        finishedBadly.add(failure.getDescription());
                        Description test = failure.getDescription();
                        report("ABORTED", test.getDisplayName(), nonNull(test.getClassName()), nonNull(test.getMethodName()), failure.getException(), null);
                    }

                    @Override
                    public void testIgnored(Description test) {
                        found[0]++;
                        report("SKIPPED", test.getDisplayName(), nonNull(test.getClassName()), nonNull(test.getMethodName()), null, "@Ignore");
                    }

                    @Override
                    public void testFinished(Description test) {
                        found[0]++;
                        if (!finishedBadly.contains(test)) {
                            report("SUCCESSFUL", test.getDisplayName(), nonNull(test.getClassName()), nonNull(test.getMethodName()), null, null);
                        }
                    }
                });
                core.run(Request.aClass(Class.forName(args[0])));
                System.out.println("FIXFINDER-TESTS-FOUND\t" + found[0]);
            }

            private static String nonNull(String text) {
                return text == null ? "" : text;
            }

        """;

    private const string Reporting = """
            private static void report(String status, String name, String className, String method, Throwable thrown, String reason) {
                StringBuilder frames = new StringBuilder();
                if (thrown != null) {
                    for (StackTraceElement frame : thrown.getStackTrace()) {
                        frames.append(frame.getClassName()).append('#').append(frame.getMethodName()).append('#')
                            .append(frame.getFileName()).append('#').append(frame.getLineNumber()).append(';');
                    }
                }
                String message = thrown != null ? String.valueOf(thrown.getMessage()) : reason == null ? "" : reason;
                System.out.println("FIXFINDER-TEST\t" + clean(status) + "\t" + clean(name) + "\t" + clean(className) + "\t" + clean(method)
                    + "\t" + (thrown == null ? "" : clean(thrown.getClass().getName())) + "\t" + clean(message) + "\t" + clean(frames.toString()));
            }

            private static String clean(String text) {
                return text.replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n");
            }
        """;
}
