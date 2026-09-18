using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FixFinder.Core.Execution;

namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>What Java code can name without being told where it lives, and where everything else lives.</summary>
internal static partial class JavaTypes
{
    private static readonly (string Package, string Classes)[] Table =
    [
        ("java.util", "List ArrayList LinkedList Map HashMap TreeMap LinkedHashMap Set HashSet TreeSet LinkedHashSet Queue Deque ArrayDeque PriorityQueue Stack Vector Iterator ListIterator Collections Arrays Objects Optional OptionalInt OptionalDouble OptionalLong Random Scanner Date Calendar GregorianCalendar UUID StringJoiner Comparator Collection BitSet Locale Properties Timer TimerTask Hashtable EnumMap EnumSet NoSuchElementException ConcurrentModificationException InputMismatchException StringTokenizer Base64 Currency Formatter NavigableMap NavigableSet SortedMap SortedSet IdentityHashMap WeakHashMap"),
        ("java.util.function", "Function BiFunction Supplier Consumer BiConsumer Predicate BiPredicate UnaryOperator BinaryOperator IntFunction IntPredicate ToIntFunction IntUnaryOperator IntBinaryOperator BooleanSupplier"),
        ("java.util.stream", "Stream Collectors IntStream LongStream DoubleStream StreamSupport"),
        ("java.util.concurrent", "ExecutorService Executors Future CompletableFuture TimeUnit ConcurrentHashMap CountDownLatch Callable ThreadLocalRandom CopyOnWriteArrayList BlockingQueue LinkedBlockingQueue ArrayBlockingQueue ScheduledExecutorService ExecutionException TimeoutException Semaphore CyclicBarrier ConcurrentLinkedQueue ForkJoinPool"),
        ("java.util.concurrent.atomic", "AtomicInteger AtomicLong AtomicBoolean AtomicReference"),
        ("java.util.concurrent.locks", "ReentrantLock Lock ReadWriteLock ReentrantReadWriteLock Condition"),
        ("java.util.regex", "Pattern Matcher PatternSyntaxException"),
        ("java.io", "File FileReader FileWriter BufferedReader BufferedWriter InputStreamReader OutputStreamWriter PrintWriter PrintStream IOException FileNotFoundException InputStream OutputStream FileInputStream FileOutputStream Serializable UncheckedIOException ByteArrayOutputStream ByteArrayInputStream Reader Writer StringReader StringWriter Closeable EOFException ObjectInputStream ObjectOutputStream DataInputStream DataOutputStream BufferedInputStream BufferedOutputStream Console RandomAccessFile"),
        ("java.nio.file", "Files Path Paths StandardOpenOption NoSuchFileException DirectoryStream FileSystems StandardCopyOption"),
        ("java.nio.charset", "StandardCharsets Charset"),
        ("java.nio", "ByteBuffer CharBuffer ByteOrder"),
        ("java.math", "BigDecimal BigInteger RoundingMode MathContext"),
        ("java.time", "LocalDate LocalDateTime LocalTime Duration Instant ZonedDateTime ZoneId ZoneOffset Period DayOfWeek Month Year YearMonth OffsetDateTime Clock"),
        ("java.time.format", "DateTimeFormatter DateTimeParseException"),
        ("java.time.temporal", "ChronoUnit TemporalAdjusters"),
        ("java.text", "SimpleDateFormat DecimalFormat NumberFormat ParseException MessageFormat"),
        ("java.net", "URL URI HttpURLConnection Socket ServerSocket URLEncoder URLDecoder InetAddress URISyntaxException MalformedURLException InetSocketAddress"),
        ("java.net.http", "HttpClient HttpRequest HttpResponse"),
        ("java.sql", "Connection DriverManager PreparedStatement ResultSet SQLException Statement Timestamp"),
        ("javax.swing", "JFrame JPanel JButton JLabel JTextField JTextArea JOptionPane SwingUtilities JScrollPane JList JComboBox JCheckBox JMenu JMenuBar JMenuItem JTable"),
        ("java.awt", "Color Graphics Graphics2D Dimension BorderLayout FlowLayout GridLayout Font Point Rectangle Toolkit Image"),
        ("java.awt.event", "ActionEvent ActionListener KeyEvent KeyListener KeyAdapter MouseEvent MouseListener MouseAdapter WindowAdapter WindowEvent"),
        ("java.lang.reflect", "Method Field Constructor InvocationTargetException Modifier"),
    ];

    public static IReadOnlyDictionary<string, string> Packages { get; } = BuildPackages();

    public static IReadOnlySet<string> Lang { get; } = new HashSet<string>(
        ("String Object Integer Long Double Float Short Byte Boolean Character Math StrictMath System StringBuilder " +
         "StringBuffer Thread Runnable Exception RuntimeException Error Throwable IllegalArgumentException " +
         "IllegalStateException NullPointerException ArithmeticException ArrayIndexOutOfBoundsException " +
         "IndexOutOfBoundsException StringIndexOutOfBoundsException NumberFormatException ClassCastException " +
         "UnsupportedOperationException InterruptedException CloneNotSupportedException ClassNotFoundException " +
         "ReflectiveOperationException Iterable Comparable CharSequence Enum Record Override Deprecated " +
         "SuppressWarnings FunctionalInterface Class Void Number Process ProcessBuilder Runtime AutoCloseable")
        .Split(' '), StringComparer.Ordinal);

    public static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(
        ("abstract assert boolean break byte case catch char class const continue default do double else enum " +
         "extends final finally float for goto if implements import instanceof int interface long native new " +
         "package private protected public return short static strictfp super switch synchronized this throw " +
         "throws transient try void volatile while var record yield sealed permits true false null")
        .Split(' '), StringComparer.Ordinal);

    [GeneratedRegex(@"^\s*import\s+(?<target>[\w.$]+(?:\.\*)?)\s*;")]
    private static partial Regex ImportPattern();

    [GeneratedRegex(@"^[A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)+$")]
    private static partial Regex QualifiedName();

    [GeneratedRegex(@"^\s+public\b[^(=]*?\s(?<name>[A-Za-z_$][\w$]*)\(")]
    private static partial Regex MethodDeclaration();

    [GeneratedRegex(@"^\s+public\b[^(=]*\s(?<name>[A-Za-z_$][\w$]*);\s*$")]
    private static partial Regex FieldDeclaration();

    private static readonly Dictionary<string, IReadOnlyList<string>> MemberCache = new(StringComparer.Ordinal);

    private static Dictionary<string, string> BuildPackages()
    {
        var packages = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (package, classes) in Table)
            foreach (var name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                packages.TryAdd(name, package);

        return packages;
    }

    public static bool IsImported(string simpleName, IReadOnlyList<string> lines)
    {
        Packages.TryGetValue(simpleName, out var package);

        foreach (var line in lines)
        {
            if (ImportPattern().Match(line) is not { Success: true } import) continue;

            var target = import.Groups["target"].Value;

            if (target.EndsWith("." + simpleName, StringComparison.Ordinal)) return true;
            if (package is not null && target == package + ".*") return true;
        }

        return false;
    }

    public static string? Fqn(string type, IReadOnlyList<string> lines)
    {
        if (type.Contains('.')) return type;

        foreach (var line in lines)
        {
            if (ImportPattern().Match(line) is { Success: true } import &&
                import.Groups["target"].Value.EndsWith("." + type, StringComparison.Ordinal))
                return import.Groups["target"].Value;
        }

        if (Packages.TryGetValue(type, out var package)) return $"{package}.{type}";

        return Lang.Contains(type) ? $"java.lang.{type}" : null;
    }

    public static IReadOnlyList<string> Members(string fqn, bool methods)
    {
        var key = (methods ? "m:" : "f:") + fqn;

        lock (MemberCache)
        {
            if (MemberCache.TryGetValue(key, out var cached)) return cached;
        }

        var names = new List<string>();

        if (QualifiedName().IsMatch(fqn) &&
            Toolchains.FindJavac() is { } javac &&
            Path.GetDirectoryName(javac.Program) is { } bin &&
            Path.Combine(bin, OperatingSystem.IsWindows() ? "javap.exe" : "javap") is var javap &&
            File.Exists(javap))
        {
            var simple = fqn[(fqn.LastIndexOf('.') + 1)..];

            foreach (var line in RunJavap(javap, fqn).Split('\n'))
            {
                var match = methods ? MethodDeclaration().Match(line) : FieldDeclaration().Match(line);

                if (match.Success && match.Groups["name"].Value != simple) names.Add(match.Groups["name"].Value);
            }
        }

        lock (MemberCache)
        {
            MemberCache[key] = names;
        }

        return names;
    }

    private static string RunJavap(string javap, string fqn)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(javap)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.StartInfo.ArgumentList.Add("-public");
            process.StartInfo.ArgumentList.Add(fqn);
            process.ErrorDataReceived += (_, _) => { };

            process.Start();
            process.BeginErrorReadLine();

            var output = process.StandardOutput.ReadToEndAsync();

            if (!process.WaitForExit(20_000))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }

                return "";
            }

            return output.Wait(5_000) ? output.Result : "";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return "";
        }
    }
}
