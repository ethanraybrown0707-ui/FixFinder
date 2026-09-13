namespace FixFinder.Core.LocalFixes.Rules;

/// <summary>Which standard header declares what, for C and for C++.</summary>
/// <remarks>
/// Hand-written, and deliberately limited to the standard libraries. A name from a third-party
/// library is not answered here: the header is only half of that fix, and the other half - getting
/// the library installed where this compiler looks - is not something a table can know.
/// </remarks>
internal static class CStandardLibrary
{
    private static readonly (string Header, string Names)[] C =
    [
        ("stdio.h", "printf scanf fprintf sprintf snprintf puts gets fgets fopen fclose fread fwrite fseek ftell rewind getchar putchar perror fflush sscanf fscanf fputs fgetc fputc getc putc remove rename tmpfile EOF FILE stdin stdout stderr setvbuf vprintf vfprintf vsnprintf vsprintf fopen_s printf_s scanf_s"),
        ("stdlib.h", "malloc calloc realloc free exit atoi atol atoll atof strtol strtoul strtoll strtoull strtod strtof rand srand abs labs llabs qsort bsearch getenv system EXIT_SUCCESS EXIT_FAILURE RAND_MAX abort atexit div ldiv"),
        ("string.h", "strlen strcpy strncpy strcat strncat strcmp strncmp strchr strrchr strstr strtok memcpy memmove memset memcmp memchr strdup strerror strspn strcspn strpbrk strcpy_s strcat_s"),
        ("math.h", "sqrt pow sin cos tan asin acos atan atan2 sinh cosh tanh exp log log10 log2 floor ceil fabs round fmod hypot trunc cbrt lround"),
        ("ctype.h", "isalpha isdigit isalnum isspace isupper islower toupper tolower ispunct isxdigit iscntrl isprint isgraph"),
        ("time.h", "time clock difftime localtime gmtime strftime mktime asctime ctime CLOCKS_PER_SEC time_t clock_t"),
        ("stdbool.h", "bool true false"),
        ("stddef.h", "size_t NULL ptrdiff_t offsetof"),
        ("stdint.h", "int8_t int16_t int32_t int64_t uint8_t uint16_t uint32_t uint64_t intptr_t uintptr_t intmax_t uintmax_t INT8_MAX INT16_MAX INT32_MAX INT64_MAX UINT8_MAX UINT16_MAX UINT32_MAX UINT64_MAX INT32_MIN INT64_MIN SIZE_MAX"),
        ("limits.h", "INT_MAX INT_MIN UINT_MAX LONG_MAX LONG_MIN ULONG_MAX CHAR_BIT CHAR_MAX CHAR_MIN SHRT_MAX SHRT_MIN LLONG_MAX LLONG_MIN"),
        ("assert.h", "assert"),
        ("errno.h", "errno ERANGE EINVAL ENOENT EDOM"),
        ("stdarg.h", "va_list va_start va_end va_arg va_copy"),
        ("float.h", "FLT_MAX DBL_MAX FLT_MIN DBL_MIN DBL_EPSILON FLT_EPSILON"),
        ("signal.h", "signal raise SIGINT SIGSEGV SIGTERM SIGABRT"),
        ("setjmp.h", "setjmp longjmp jmp_buf"),
        ("locale.h", "setlocale LC_ALL"),
        ("inttypes.h", "PRId64 PRIu64 PRIx64 PRId32 PRIu32"),
        ("wchar.h", "wchar_t wprintf wcslen wcscpy"),
    ];

    private static readonly (string Header, string Names)[] Cpp =
    [
        ("iostream", "cout cin cerr clog endl"),
        ("string", "string to_string stoi stol stoll stoul stof stod getline wstring"),
        ("vector", "vector"),
        ("map", "map multimap"),
        ("unordered_map", "unordered_map"),
        ("set", "set multiset"),
        ("unordered_set", "unordered_set"),
        ("algorithm", "sort stable_sort find find_if count count_if max min max_element min_element reverse remove_if unique binary_search lower_bound upper_bound fill copy transform all_of any_of none_of for_each clamp"),
        ("iomanip", "setw setprecision setfill"),
        ("utility", "pair make_pair move swap forward"),
        ("memory", "unique_ptr shared_ptr weak_ptr make_unique make_shared"),
        ("functional", "function bind hash"),
        ("thread", "thread"),
        ("mutex", "mutex lock_guard unique_lock"),
        ("optional", "optional nullopt"),
        ("variant", "variant visit"),
        ("array", "array"),
        ("deque", "deque"),
        ("queue", "queue priority_queue"),
        ("stack", "stack"),
        ("list", "list"),
        ("sstream", "stringstream istringstream ostringstream"),
        ("fstream", "ifstream ofstream fstream"),
        ("numeric", "accumulate iota"),
        ("stdexcept", "runtime_error invalid_argument out_of_range logic_error"),
        ("chrono", "chrono"),
        ("limits", "numeric_limits"),
        ("tuple", "tuple tie make_tuple get"),
        ("cmath", "sqrt pow abs fabs floor ceil round"),
        ("cstdlib", "exit rand srand"),
        ("cstdio", "printf scanf"),
        ("cstring", "strlen memcpy strcpy strcmp"),
        ("cstddef", "size_t"),
        ("cstdint", "int32_t int64_t uint8_t uint32_t uint64_t"),
        ("cctype", "isdigit isalpha toupper tolower"),
    ];

    private static readonly Dictionary<string, string> CHeaders = Build(C);
    private static readonly Dictionary<string, string> CppHeaders = Build(Cpp);

    /// <summary>C functions that return a pointer, which C cuts to 32 bits when they are undeclared.</summary>
    public static IReadOnlySet<string> ReturnsPointer { get; } = new HashSet<string>(
        "malloc calloc realloc strdup fopen getenv strchr strrchr strstr strtok memcpy memmove memset memchr strcpy strncpy strcat strncat localtime gmtime ctime asctime strerror tmpfile".Split(' '),
        StringComparer.Ordinal);

    public static IReadOnlySet<string> Headers { get; } = new HashSet<string>(
        C.Select(e => e.Header).Concat(Cpp.Select(e => e.Header)).Concat(
            ("iso646.h complex.h fenv.h tgmath.h uchar.h stdalign.h stdnoreturn.h threads.h stdatomic.h wctype.h " +
             "windows.h conio.h io.h direct.h process.h malloc.h exception typeinfo regex random bitset atomic " +
             "condition_variable future filesystem any string_view iterator initializer_list type_traits ios " +
             "ostream istream streambuf climits cassert cerrno cfloat ctime cwchar valarray ratio span format " +
             "ranges numbers").Split(' ')),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>C standard functions and macros, as candidates for a misspelt call.</summary>
    public static IEnumerable<string> CNames => CHeaders.Keys;

    public static IReadOnlySet<string> Keywords { get; } = new HashSet<string>(
        ("auto break case char const continue default do double else enum extern float for goto if inline int " +
         "long register restrict return short signed sizeof static struct switch typedef union unsigned void " +
         "volatile while _Bool class namespace template typename public private protected virtual override new " +
         "delete this operator friend using try catch throw nullptr bool const_cast static_cast dynamic_cast " +
         "reinterpret_cast explicit mutable constexpr noexcept decltype include define ifdef ifndef endif pragma " +
         "undef elif").Split(' '),
        StringComparer.Ordinal);

    public static string? CHeaderOf(string name) => CHeaders.GetValueOrDefault(name);

    public static string? CppHeaderOf(string name) => CppHeaders.GetValueOrDefault(name);

    /// <summary>The C++ table as written, for the test that compiles every entry against its header.</summary>
    internal static IReadOnlyDictionary<string, string> CppTable => CppHeaders;

    private static Dictionary<string, string> Build((string Header, string Names)[] table)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (header, names) in table)
            foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                map.TryAdd(name, header);

        return map;
    }
}
