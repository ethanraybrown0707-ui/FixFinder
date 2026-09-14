using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Tests;

/// <summary>
/// The C++ mistakes of someone learning it, and of someone arriving from Java, C# or Python: each rule's fix and refusals
/// from g++'s and MSVC's own words, and the shared C rules reading C++.
/// </summary>
public class CppConstructTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string name, string body)
    {
        var path = Path.Combine(_temp.Path, name);
        File.WriteAllText(path, body);
        return path;
    }

    private static ParsedError Diagnostic(string language, string? code, string message, string? file, int line, string type) => new()
    {
        LanguageId = language,
        Confidence = 90,
        RawText = message,
        FirstLineSequence = 0,
        ExceptionType = type,
        ErrorCode = code,
        Message = message,
        Frames = file is null ? [] : [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private LocalFix? Fix(string rule, ParsedError error, IReadOnlyList<ParsedError>? others = null) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext { Error = error, Others = others ?? [], SourceRoot = _temp.Path });

    private static IReadOnlyList<CapturedLine> Lines(params string[] text) =>
        text.Select((line, i) => new CapturedLine(i, StreamKind.StdErr, line, TimeSpan.Zero)).ToList();

    // ------------------------------------------------------------------ every rule, and what it refuses

    [Theory]
    [InlineData("cpp-std-prefix", "gcc", null, "'string' was not declared in this scope", "#include <string>\nint main() {\n    string name = \"Ada\";\n}\n", 3, "    std::string name = \"Ada\";")]
    [InlineData("cpp-std-prefix", "msvc", "C2065", "'cout': undeclared identifier", "#include <iostream>\nusing namespace std;\nint main() {\n    cout << 1;\n}\n", 4, null)]
    [InlineData("cpp-std-prefix", "gcc", null, "'vector' was not declared in this scope", "#include <iostream>\nint main() {\n    vector<int> v;\n}\n", 3, null)]
    [InlineData("cpp-foreign-print", "gcc", null, "'System' was not declared in this scope; did you mean 'system'?", "#include <iostream>\nint main() {\n    int total = 3;\n    System.out.println(\"Total: \" + total);\n}\n", 4, "    std::cout << \"Total: \" << total << std::endl;")]
    [InlineData("cpp-foreign-print", "msvc", "C3861", "'print': identifier not found", "#include <iostream>\nint main() {\n    int b = 2;\n    print(\"b is\", b);\n}\n", 4, "    std::cout << \"b is\" << \" \" << b << std::endl;")]
    [InlineData("cpp-foreign-print", "msvc", "C2065", "'Console': undeclared identifier", "#include <iostream>\nusing namespace std;\nint main() {\n    Console.Write(\"x\");\n}\n", 4, "    cout << \"x\";")]
    [InlineData("cpp-foreign-print", "msvc", "C2065", "'Console': undeclared identifier", "#include <iostream>\nint main() {\n    Console.WriteLine(\"{0}\", 5);\n}\n", 3, null)]
    [InlineData("cpp-foreign-print", "gcc", null, "'System' was not declared in this scope", "int main() {\n    System.out.println(\"hi\");\n}\n", 2, null)]
    [InlineData("cpp-foreign-word", "gcc", null, "'null' was not declared in this scope", "int main() {\n    int *p = null;\n}\n", 2, "    int *p = nullptr;")]
    [InlineData("cpp-foreign-word", "msvc", "C2065", "'True': undeclared identifier", "int main() {\n    bool ready = True;\n}\n", 2, "    bool ready = true;")]
    [InlineData("cpp-foreign-word", "gcc", null, "'String' was not declared in this scope", "#include <string>\nint main() {\n    String name = \"Ada\";\n}\n", 3, "    std::string name = \"Ada\";")]
    [InlineData("cpp-foreign-word", "gcc", null, "'String' was not declared in this scope", "int main() {\n    String name = \"Ada\";\n}\n", 2, null)]
    [InlineData("cpp-stream-arrows", "gcc", null, "no match for 'operator>>' (operand types are 'std::ostream' {aka 'std::basic_ostream<char>'} and 'int')", "    std::cout >> total >> std::endl;\n", 1, "    std::cout << total << std::endl;")]
    [InlineData("cpp-stream-arrows", "msvc", "C2678", "binary '<<': no operator found which takes a left-hand operand of type 'std::istream' (or there is no acceptable conversion)", "    std::cin << age;\n", 1, "    std::cin >> age;")]
    [InlineData("cpp-stream-arrows", "gcc", null, "no match for 'operator<<' (operand types are 'std::ostream' {aka 'std::basic_ostream<char>'} and 'Point')", "    std::cout << p;\n", 1, null)]
    [InlineData("cpp-container-member", "gcc", null, "'class std::vector<int>' has no member named 'add'", "    values.add(1);\n", 1, "    values.push_back(1);")]
    [InlineData("cpp-container-member", "msvc", "C2039", "'length': is not a member of 'std::vector<int,std::allocator<int>>'", "    std::cout << values.length();\n", 1, "    std::cout << values.size();")]
    [InlineData("cpp-container-member", "msvc", "C2039", "'push_bak': is not a member of 'std::vector<int,std::allocator<int>>'", "    values.push_bak(1);\n", 1, "    values.push_back(1);")]
    [InlineData("cpp-container-member", "gcc", null, "'class std::map<std::__cxx11::basic_string<char>, int>' has no member named 'containsKey'", "    if (ages.containsKey(\"Ada\")) {\n", 1, "    if (ages.count(\"Ada\")) {")]
    [InlineData("cpp-container-member", "gcc", null, "'class std::vector<int>' has no member named 'Count'", "    int n = values.Count;\n", 1, "    int n = values.size();")]
    [InlineData("cpp-container-member", "gcc", null, "'class std::map<std::__cxx11::basic_string<char>, int>' has no member named 'add'", "    ages.add(\"Ada\", 36);\n", 1, null)]
    [InlineData("cpp-method-without-call", "msvc", "C3867", "'std::basic_string<char,std::char_traits<char>,std::allocator<char>>::size': non-standard syntax; use '&' to create a pointer to member", "    std::cout << name.size << std::endl;\n", 1, "    std::cout << name.size() << std::endl;")]
    [InlineData("cpp-method-without-call", "gcc", null, "no match for 'operator<<' (operand types are 'std::ostream' {aka 'std::basic_ostream<char>'} and '<unresolved overloaded function type>')", "    std::cout << name.size << std::endl;\n", 1, "    std::cout << name.size() << std::endl;")]
    [InlineData("cpp-new-without-pointer", "gcc", null, "conversion from 'Dog*' to non-scalar type 'Dog' requested", "    Dog d = new Dog();\n", 1, "    Dog d;")]
    [InlineData("cpp-new-without-pointer", "msvc", "C2440", "'initializing': cannot convert from 'Dog *' to 'Dog'", "    Dog d = new Dog(\"Rex\", 3);\n", 1, "    Dog d(\"Rex\", 3);")]
    [InlineData("cpp-member-operator", "gcc", null, "base operand of '->' has non-pointer type 'Dog'", "struct Dog { int age; };\nint main() {\n    Dog d;\n    return d->age;\n}\n", 4, "    return d.age;")]
    [InlineData("cpp-member-operator", "gcc", null, "request for member 'age' in 'd', which is of pointer type 'Dog*' (maybe you meant to use '->' ?)", "struct Dog { int age; };\nint main() {\n    Dog *d = new Dog();\n    return d.age;\n}\n", 4, "    return d->age;")]
    [InlineData("cpp-member-operator", "msvc", "C2228", "left of '.name' must have class/struct/union", "class Dog {\npublic:\n    int name;\n    Dog(int name) { this.name = name; }\n};\n", 4, "    Dog(int name) { this->name = name; }")]
    [InlineData("cpp-member-operator", "msvc", "C2228", "left of '.push_back' must have class/struct/union", "#include <vector>\nint main() {\n    std::vector<int> values();\n    values.push_back(1);\n}\n", 4, null)]
    [InlineData("cpp-private-member", "gcc", null, "'int Account::balance' is private within this context", "class Account {\n    int balance = 10;\n};\nint main() {\n    Account a;\n    return a.balance;\n}\n", 6, "public:")]
    [InlineData("cpp-private-member", "msvc", "C2248", "'Account::balance': cannot access private member declared in class 'Account'", "class Account {\nprivate:\n    int balance = 10;\n};\nint main() {\n    Account a;\n    return a.balance;\n}\n", 7, null)]
    [InlineData("cpp-main-returns-int", "gcc", null, "'::main' must return 'int'", "void main() {\n}\n", 1, "int main() {")]
    [InlineData("cpp-char-for-string", "gcc", null, "invalid conversion from 'const char*' to 'char' [-fpermissive]", "#include <iostream>\nint main() {\n    char name = \"Ada\";\n}\n", 3, "    std::string name = \"Ada\";")]
    [InlineData("cpp-char-for-string", "msvc", "C2440", "'initializing': cannot convert from 'const char [4]' to 'char'", "int main() {\n    char name = \"Ada\";\n}\n", 2, null)]
    [InlineData("cpp-multichar-string", "gcc", null, "conversion from 'int' to non-scalar type 'std::string' {aka 'std::__cxx11::basic_string<char>'} requested", "    std::string name = 'Ada';\n", 1, "    std::string name = \"Ada\";")]
    [InlineData("cpp-multichar-string", "msvc", "C2440", "'initializing': cannot convert from 'int' to 'std::basic_string<char,std::char_traits<char>,std::allocator<char>>'", "    std::string name = 'A';\n", 1, null)]
    [InlineData("cpp-vexing-parse", "gcc", null, "request for member 'push_back' in 'values', which is of non-class type 'std::vector<int>()'", "#include <vector>\nint main() {\n    std::vector<int> values();\n    values.push_back(1);\n}\n", 4, "    std::vector<int> values;")]
    [InlineData("cpp-vexing-parse", "msvc", "C2228", "left of '.push_back' must have class/struct/union", "#include <vector>\nint main() {\n    std::vector<int> values();\n    values.push_back(1);\n}\n", 4, "    std::vector<int> values;")]
    [InlineData("cpp-literal-concatenation", "gcc", null, "invalid operands of types 'const char [8]' and 'const char [6]' to binary 'operator+'", "#include <string>\nint main() {\n    std::string greeting = \"Hello, \" + \"world\";\n}\n", 3, "    std::string greeting = std::string(\"Hello, \") + \"world\";")]
    [InlineData("cpp-literal-concatenation", "msvc", "C2110", "'+': cannot add two pointers", "#include <string>\nusing namespace std;\nint main() {\n    string s = \"a\" + \"b\" + \"c\";\n}\n", 4, "    string s = string(\"a\") + \"b\" + \"c\";")]
    [InlineData("cpp-string-plus-number", "gcc", null, "no match for 'operator+' (operand types are 'std::string' {aka 'std::__cxx11::basic_string<char>'} and 'int')", "#include <string>\nint main() {\n    int age = 20;\n    std::string text = \"Age: \";\n    text = text + age;\n}\n", 5, "    text = text + std::to_string(age);")]
    [InlineData("cpp-string-plus-number", "msvc", "C2676", "binary '+': 'std::string' does not define this operator or a conversion to a type acceptable to the predefined operator", "#include <string>\nint main() {\n    int age = 20;\n    std::string text = \"Age: \";\n    text = text + age;\n}\n", 5, "    text = text + std::to_string(age);")]
    [InlineData("cpp-missing-return-type", "msvc", "C4430", "missing type specifier - int assumed. Note: C++ does not support default-int", "add(int a, int b) {\n    return a + b;\n}\n", 1, "int add(int a, int b) {")]
    [InlineData("cpp-missing-return-type", "msvc", "C4430", "missing type specifier - int assumed. Note: C++ does not support default-int", "greet() {\n}\n", 1, "void greet() {")]
    [InlineData("cpp-missing-return-type", "msvc", "C4430", "missing type specifier - int assumed. Note: C++ does not support default-int", "pick(int a, double b) {\n    return a + b;\n}\n", 1, null)]
    // The shared C rules, reading C++.
    [InlineData("c-struct-semicolon", "msvc", "C2628", "'Dog' followed by 'int' is illegal (did you forget a ';'?)", "class Dog {\npublic:\n    int age = 3;\n}\n\nint main() {\n", 6, "};")]
    [InlineData("c-function-prototype", "gcc", null, "'square' was not declared in this scope", "#include <iostream>\nint main() {\n    std::cout << square(4);\n    return 0;\n}\nint square(int x) {\n    return x * x;\n}\n", 3, "int square(int x);")]
    [InlineData("c-function-prototype", "msvc", "C3861", "'square': identifier not found", "#include <iostream>\nint main() {\n    std::cout << square(4);\n    return 0;\n}\nint square(int x) {\n    return x * x;\n}\n", 3, "int square(int x);")]
    [InlineData("c-redefinition", "gcc", null, "redeclaration of 'int x'", "int main() {\n    int x = 1;\n    int x = 2;\n    return x;\n}\n", 3, "    x = 2;")]
    [InlineData("c-missing-semicolon", "msvc", "C2760", "syntax error: 'int' was unexpected here; expected ';'", "#include <iostream>\nusing namespace std\n\nint main() {\n", 4, "using namespace std;")]
    public void EachConstructGetsItsFixOrARefusal(string rule, string language, string? code, string message, string source, int line, string? expected)
    {
        var file = Write("app.cpp", source);
        var fix = Fix(rule, Diagnostic(language, code, message, file, line, "compile error"));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    /// <summary>MSVC never corrects a misspelt standard name; the nearest one in the standard table is the answer.</summary>
    [Theory]
    [InlineData("#include <iostream>\nint main() {\n    std::cout << \"hello\" << std::endll;\n}\n", "'endll': is not a member of 'std'", "    std::cout << \"hello\" << std::endl;")]
    [InlineData("#include <iostream>\nint main() {\n    std::vector<int> v;\n}\n", "'vector': is not a member of 'std'", null)]
    public void AMisspeltStandardNameIsCorrected(string source, string message, string? expected)
    {
        var file = Write("app.cpp", source);
        var fix = Fix("cpp-std-name-typo", Diagnostic("msvc", "C2039", message, file, 3, "compile error"));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    /// <summary>
    /// An error reported from inside the standard library's own headers is not the project's code - or the folder its source is
    /// looked for in. Before this, g++'s headers under Strawberry became the source root.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Strawberry\c\include\c++\13.2.0\bits\stl_algo.h")]
    [InlineData(@"C:\Strawberry\c\lib\gcc\x86_64-w64-mingw32\13.2.0\include\stddef.h")]
    [InlineData("/usr/include/c++/13/bits/stl_algo.h")]
    public void TheCompilersOwnCppHeadersAreNotTheUsersCode(string header) =>
        Assert.True(FrameClassifier.IsVendored(header));

    /// <summary>gcc suggests std::cout for the first name alone; the line only compiles with std::endl as well.</summary>
    [Fact]
    public void EveryStandardNameOnTheLineIsQualifiedAtOnce()
    {
        var file = Write("app.cpp", "#include <iostream>\nint main() {\n    cout << \"hello\" << endl;\n}\n");
        var cout = Diagnostic("gcc", null, "'cout' was not declared in this scope; did you mean 'std::cout'?", file, 3, "compile error");
        var endl = Diagnostic("gcc", null, "'endl' was not declared in this scope; did you mean 'std::endl'?", file, 3, "compile error");

        var fix = Fix("cpp-std-prefix", cout, [endl]);

        Assert.Equal("    std::cout << \"hello\" << std::endl;", Assert.Single(fix!.NewLines));
    }

    /// <summary>A brace that closed main too soon is the one removed - not the last one, which only looks spare.</summary>
    [Theory]
    [InlineData("gcc", null, "expected unqualified-id before 'return'")]
    [InlineData("msvc", "C2059", "syntax error: 'return'")]
    public void ABlockClosedTooSoonLosesItsEarlyBrace(string language, string? code, string message)
    {
        var file = Write("app.cpp", "#include <iostream>\n\nint main() {\n    std::cout << \"hello\" << std::endl;\n    }\n    return 0;\n}\n");

        var fix = Fix("c-extra-closing-brace", Diagnostic(language, code, message, file, 6, "compile error"));

        Assert.NotNull(fix);
        Assert.Equal(5, fix!.StartLine);
        Assert.Equal(1, fix.RemoveCount);
        Assert.Empty(fix.NewLines);
    }

    /// <summary>A double delete is caught inside AddressSanitizer's own operator delete; the line is the first one in the program.</summary>
    [Fact]
    public void ASecondDeleteIsFoundPastTheSanitizersOwnFrame()
    {
        var file = Write("app.cpp", "int main() {\n    int *value = new int(5);\n    delete value;\n    delete value;\n    return 0;\n}\n");

        var error = new ParsedError
        {
            LanguageId = "gcc",
            Confidence = 92,
            RawText = "attempting double-free on 0x11ccc31a0250 in thread T0:",
            FirstLineSequence = 0,
            ExceptionType = "attempting double-free",
            Message = "attempting double-free on 0x11ccc31a0250 in thread T0:",
            Frames =
            [
                new ErrorFrame { Order = 0, File = @"D:\a\_work\1\s\src\vctools\asan\llvm\compiler-rt\lib\asan\asan_win_delete_scalar_size_thunk.cpp", Line = 41, RawLine = "" },
                new ErrorFrame { Order = 1, File = file, Line = 4, RawLine = "" },
            ],
        };

        var fix = Fix("c-double-free", error);

        Assert.NotNull(fix);
        Assert.Equal(4, fix!.StartLine);
        Assert.Empty(fix.NewLines);
        Assert.Equal("Remove the second delete value", fix.Title);
    }

    // ------------------------------------------------------------------ an exception nobody caught

    [Fact]
    public void AnUncaughtExceptionIsReadFromLibstdcxxsLastWords()
    {
        var lines = Lines("1", "terminate called after throwing an instance of 'std::out_of_range'", "  what():  vector::_M_range_check: __n (which is 3) >= this->size() (which is 3)");
        var parser = new GccClangParser();

        Assert.True(parser.Detect(lines.Select(l => l.Text).ToList()) > 30);

        var error = parser.Parse(lines);

        Assert.NotNull(error);
        Assert.Equal("std::out_of_range", error!.ExceptionType);
        Assert.Equal("vector::_M_range_check: __n (which is 3) >= this->size() (which is 3)", error.Message);
        Assert.Empty(error.Frames);
    }

    [Fact]
    public void AThreadNeverJoinedIsReadAsTerminate()
    {
        var error = new GccClangParser().Parse(Lines("terminate called without an active exception"));

        Assert.NotNull(error);
        Assert.Equal("std::terminate", error!.ExceptionType);
        Assert.Equal("terminate called without an active exception", error.Message);
    }

    /// <summary>No line comes with the exception, so the loop is the one in the program reading .at(i) up to .size().</summary>
    [Theory]
    [InlineData("vector::_M_range_check: __n (which is 3) >= this->size() (which is 3)", "    for (size_t i = 0; i < values.size(); i++) {")]
    [InlineData("vector::_M_range_check: __n (which is 7) >= this->size() (which is 3)", null)]
    public void AnAtPastTheEndIsTracedToItsLoop(string message, string? expected)
    {
        Write("app.cpp", "#include <iostream>\n#include <vector>\nint main() {\n    std::vector<int> values = {1, 2, 3};\n    for (size_t i = 0; i <= values.size(); i++) {\n        std::cout << values.at(i) << std::endl;\n    }\n}\n");

        var fix = Fix("cpp-at-out-of-range", Diagnostic("gcc", null, message, null, 0, "std::out_of_range"));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }
}
