using FixFinder.Core.Execution;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Parsing;

namespace FixFinder.Tests;

/// <summary>
/// The C++ mistakes of later years - inheritance and virtual functions, templates, smart pointers, lambdas, const, threads - each
/// rule's fix and refusals from g++'s and MSVC's words.
/// </summary>
public class CppUniversityTests : IDisposable
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
        FirstLineSequence = 1,
        ExceptionType = type,
        ErrorCode = code,
        Message = message,
        Frames = file is null ? [] : [new ErrorFrame { Order = 0, File = file, Line = line, RawLine = "" }],
    };

    private LocalFix? Fix(string rule, ParsedError error, IReadOnlyList<ParsedError>? others = null, params string[] output) =>
        LocalFixEngine.Rules.Single(r => r.Id == rule).Propose(new LocalFixContext
        {
            Error = error,
            Others = others ?? [],
            Output = output.Select((text, i) => new CapturedLine(i, StreamKind.StdErr, text, TimeSpan.Zero)).ToList(),
            SourceRoot = _temp.Path,
        });

    private const string VirtualBase = "class Animal {\npublic:\n    void speak() const {}\n};\nclass Dog : public Animal {\npublic:\n    void speak() const override {}\n};\n";
    private const string OverrideTypo = "class Animal {\npublic:\n    virtual void speak() const {}\n};\nclass Dog : public Animal {\npublic:\n    void speek() const override {}\n};\n";
    private const string PrivateBase = "class Animal {\npublic:\n    void speak() const {}\n};\nclass Dog : Animal {\n};\nint main() {\n    Dog d;\n    d.speak();\n}\n";
    private const string OutsideMember = "class Dog {\npublic:\n    int age = 3;\n    void speak() const;\n};\nvoid speak() const {\n}\n";
    private const string CopiedOwner = "#include <memory>\nint main() {\n    auto first = std::make_unique<int>(5);\n    auto second = first;\n    return *second;\n}\n";
    private const string PassedOwner = "#include <memory>\nvoid show(std::unique_ptr<int> value) {}\nint main() {\n    auto number = std::make_unique<int>(5);\n    show(number);\n}\n";
    private const string UsedAfterCopy = "#include <memory>\nint main() {\n    auto first = std::make_unique<int>(5);\n    auto second = first;\n    return *first;\n}\n";
    private const string ConstCall = "class Counter {\npublic:\n    int count = 3;\n    int get() { return count; }\n};\nint show(const Counter& c) {\n    return c.get();\n}\n";
    private const string Explicit = "class Meters {\npublic:\n    explicit Meters(double value) : value(value) {}\n    double value;\n};\nint main() {\n    Meters m = 5.0;\n    return 0;\n}\n";
    private const string NotExplicit = "class Meters {\npublic:\n    Meters(double value) : value(value) {}\n    double value;\n};\nint main() {\n    Meters m = 5.0;\n    return 0;\n}\n";
    private const string Defaults = "#include <string>\nvoid greet(std::string name = \"you\");\nvoid greet(std::string name = \"you\") {\n}\n";
    private const string ConstMember = "class Circle {\npublic:\n    const double radius;\n    Circle(double r) {\n        radius = r;\n    }\n};\n";

    [Theory]
    [InlineData("cpp-virtual-base", "gcc", null, "'void Dog::speak() const' marked 'override', but does not override", VirtualBase, 7, "    virtual void speak() const {}")]
    [InlineData("cpp-virtual-base", "msvc", "C3668", "'Dog::speak': method with override specifier 'override' did not override any base class methods", VirtualBase, 7, "    virtual void speak() const {}")]
    [InlineData("cpp-virtual-base", "gcc", null, "'void Dog::speek() const' marked 'override', but does not override", OverrideTypo, 7, null)]
    [InlineData("cpp-override-typo", "gcc", null, "'void Dog::speek() const' marked 'override', but does not override", OverrideTypo, 7, "    void speak() const override {}")]
    [InlineData("cpp-override-typo", "msvc", "C3668", "'Dog::speek': method with override specifier 'override' did not override any base class methods", OverrideTypo, 7, "    void speak() const override {}")]
    [InlineData("cpp-override-typo", "gcc", null, "'void Dog::speak() const' marked 'override', but does not override", VirtualBase, 7, null)]
    [InlineData("cpp-private-inheritance", "gcc", null, "'Animal' is not an accessible base of 'Dog'", PrivateBase, 9, "class Dog : public Animal {")]
    [InlineData("cpp-private-inheritance", "gcc", null, "'void Animal::speak() const' is inaccessible within this context", PrivateBase, 9, "class Dog : public Animal {")]
    [InlineData("cpp-private-inheritance", "msvc", "C2247", "'Animal::speak' not accessible because 'Dog' uses 'private' to inherit from 'Animal'", PrivateBase, 9, "class Dog : public Animal {")]
    [InlineData("cpp-member-without-class-name", "gcc", null, "non-member function 'void speak()' cannot have cv-qualifier", OutsideMember, 6, "void Dog::speak() const {")]
    [InlineData("cpp-member-without-class-name", "msvc", "C2270", "'speak': modifiers not allowed on nonmember functions", OutsideMember, 6, "void Dog::speak() const {")]
    [InlineData("cpp-move-unique-ptr", "gcc", null, "use of deleted function 'std::unique_ptr<_Tp, _Dp>::unique_ptr(const std::unique_ptr<_Tp, _Dp>&) [with _Tp = int; _Dp = std::default_delete<int>]'", CopiedOwner, 4, "    auto second = std::move(first);")]
    [InlineData("cpp-move-unique-ptr", "msvc", "C2280", "'std::unique_ptr<int,std::default_delete<int>>::unique_ptr(const std::unique_ptr<int,std::default_delete<int>> &)': attempting to reference a deleted function", PassedOwner, 5, "    show(std::move(number));")]
    [InlineData("cpp-move-unique-ptr", "gcc", null, "use of deleted function 'std::unique_ptr<_Tp, _Dp>::unique_ptr(const std::unique_ptr<_Tp, _Dp>&) [with _Tp = int; _Dp = std::default_delete<int>]'", UsedAfterCopy, 4, null)]
    [InlineData("cpp-const-method", "msvc", "C2662", "'int Counter::get(void)': cannot convert 'this' pointer from 'const Counter' to 'Counter &'", ConstCall, 7, "    int get() const { return count; }")]
    [InlineData("cpp-typename", "gcc", null, "need 'typename' before 'std::vector<T>::const_iterator' because 'std::vector<T>' is a dependent scope", "    for (std::vector<T>::const_iterator it = values.begin(); it != values.end(); ++it) {\n", 1, "    for (typename std::vector<T>::const_iterator it = values.begin(); it != values.end(); ++it) {")]
    [InlineData("cpp-typename", "gcc", null, "need 'typename' before 'std::vector<T>::const_iterator' because 'std::vector<T>' is a dependent scope [-Wtemplate-body]", "    for (std::vector<T>::const_iterator it = values.begin(); it != values.end(); ++it) {\n", 1, "    for (typename std::vector<T>::const_iterator it = values.begin(); it != values.end(); ++it) {")]
    [InlineData("cpp-lambda-capture", "gcc", null, "'total' is not captured","    auto addOne = []() { return total + 1; };\n", 1, "    auto addOne = [&total]() { return total + 1; };")]
    [InlineData("cpp-lambda-capture", "msvc", "C3493", "'total' cannot be implicitly captured because no default capture mode has been specified", "    auto addOne = [scale]() { return total * scale; };\n", 1, "    auto addOne = [scale, &total]() { return total * scale; };")]
    [InlineData("cpp-lambda-capture", "msvc", "C3491", "'count': a by copy capture cannot be modified in a non-mutable lambda", "    auto bump = [count]() { count++; };\n", 1, "    auto bump = [&count]() { count++; };")]
    [InlineData("cpp-lambda-capture", "gcc", null, "increment of read-only variable 'count'", "    auto bump = [count]() { count++; };\n", 1, "    auto bump = [&count]() { count++; };")]
    [InlineData("cpp-lambda-capture", "gcc", null, "increment of read-only variable 'count'", "    count++;\n", 1, null)]
    [InlineData("cpp-explicit-constructor", "gcc", null, "conversion from 'double' to non-scalar type 'Meters' requested", Explicit, 7, "    Meters m(5.0);")]
    [InlineData("cpp-explicit-constructor", "msvc", "C2440", "'initializing': cannot convert from 'double' to 'Meters'", Explicit, 7, "    Meters m(5.0);")]
    [InlineData("cpp-explicit-constructor", "gcc", null, "conversion from 'double' to non-scalar type 'Meters' requested", NotExplicit, 7, null)]
    [InlineData("cpp-char-compared-with-string", "gcc", null, "ISO C++ forbids comparison between pointer and integer [-fpermissive]", "    if (word[0] == \"a\") {\n", 1, "    if (word[0] == 'a') {")]
    [InlineData("cpp-char-compared-with-string", "msvc", "C2446", "'==': no conversion from 'const char [2]' to 'int'", "    if (word.at(0) != \"a\") {\n", 1, "    if (word.at(0) != 'a') {")]
    [InlineData("cpp-char-compared-with-string", "gcc", null, "ISO C++ forbids comparison between pointer and integer [-fpermissive]", "int main() {\n    int count = 1;\n    if (count == \"a\") {\n", 3, null)]
    [InlineData("cpp-auto-parameter", "msvc", "C3533", "a parameter cannot have a type that contains 'auto'", "#include <iostream>\nvoid show(auto value) {\n    std::cout << value;\n}\n", 2, "template <typename T>|void show(T value) {")]
    [InlineData("cpp-auto-parameter", "msvc", "C3533", "a parameter cannot have a type that contains 'auto'", "struct T {};\nvoid show(auto value) {\n}\n", 2, null)]
    [InlineData("cpp-default-argument-repeated", "gcc", null, "default argument given for parameter 1 of 'void greet(std::string)' [-fpermissive]", Defaults, 3, "void greet(std::string name) {")]
    [InlineData("cpp-default-argument-repeated", "msvc", "C2572", "'greet': redefinition of default argument: parameter 1", Defaults, 3, "void greet(std::string name) {")]
    [InlineData("cpp-const-member-initialiser", "msvc", "C2789", "'Circle::radius': an object of const-qualified type must be initialized", ConstMember, 4, "    Circle(double r) : radius(r) {")]
    [InlineData("cpp-index-empty-vector", "gcc", null, "access-violation on unknown address 0x000000000000 (a null pointer)", "#include <vector>\nint main() {\n    std::vector<int> values;\n    values[0] = 5;\n    return values[0];\n}\n", 4, "    values.push_back(5);")]
    [InlineData("cpp-index-empty-vector", "gcc", null, "access-violation on unknown address 0x000000000000 (a null pointer)", "#include <vector>\nint main() {\n    std::vector<int> values;\n    values[0] = 5;\n    values[1] = 6;\n    return values[0];\n}\n", 4, null)]
    public void EachConstructGetsItsFixOrARefusal(string rule, string language, string? code, string message, string source, int line, string? expected)
    {
        var file = Write("app.cpp", source);
        var type = rule == "cpp-index-empty-vector" ? "access-violation" : "compile error";
        var fix = Fix(rule, Diagnostic(language, code, message, file, line, type));

        Assert.Equal(expected, fix is null ? null : string.Join("|", fix.NewLines));
    }

    /// <summary>g++ names the member only in the note that follows its error.</summary>
    [Fact]
    public void GccsNoteNamesTheMemberThatNeedsConst()
    {
        var file = Write("app.cpp", ConstCall);
        var error = Diagnostic("gcc", null, "passing 'const Counter' as 'this' argument discards qualifiers [-fpermissive]", file, 7, "compile error");

        var fix = Fix("cpp-const-method", error, null,
            "app.cpp: In function 'int show(const Counter&)':",
            "app.cpp:7:16: error: passing 'const Counter' as 'this' argument discards qualifiers [-fpermissive]",
            "app.cpp:4:9: note:   in call to 'int Counter::get()'");

        Assert.Equal("    int get() const { return count; }", Assert.Single(fix!.NewLines));
    }

    /// <summary>g++'s first error names only the type; the member is in the error after it.</summary>
    [Fact]
    public void AConstMemberIsInitialisedBeforeTheConstructorsBody()
    {
        var file = Write("app.cpp", ConstMember);
        var error = Diagnostic("gcc", null, "uninitialized const member in 'const double' [-fpermissive]", file, 4, "compile error");
        var assignment = Diagnostic("gcc", null, "assignment of read-only member 'Circle::radius'", file, 5, "compile error");

        var fix = Fix("cpp-const-member-initialiser", error, [assignment]);

        Assert.NotNull(fix);
        Assert.Equal(4, fix!.StartLine);
        Assert.Equal(2, fix.RemoveCount);
        Assert.Equal("    Circle(double r) : radius(r) {", Assert.Single(fix.NewLines));
    }

    /// <summary>The linker names no line; the class is found in the program, and the definition goes after it.</summary>
    [Theory]
    [InlineData("gcc", null, "undefined reference to 'Counter::count'")]
    [InlineData("msvc", "LNK2001", "unresolved external symbol \"public: static int Counter::count\" (?count@Counter@@2HA)")]
    public void AStaticMemberIsDefinedAfterItsClass(string language, string? code, string message)
    {
        Write("app.cpp", "class Counter {\npublic:\n    static int count;\n    Counter() { count++; }\n};\nint main() {\n    Counter a;\n    return Counter::count;\n}\n");

        var fix = Fix("cpp-static-member-definition", Diagnostic(language, code, message, null, 0, "link error"));

        Assert.NotNull(fix);
        Assert.Equal(6, fix!.StartLine);
        Assert.Equal(0, fix.RemoveCount);
        Assert.Equal("int Counter::count = 0;", Assert.Single(fix.NewLines));
    }

    [Fact]
    public void AStaticMemberAlreadyDefinedIsLeftAlone()
    {
        Write("app.cpp", "class Counter {\npublic:\n    static int count;\n};\nint Counter::count = 0;\nint main() {\n    return Counter::count;\n}\n");

        Assert.Null(Fix("cpp-static-member-definition", Diagnostic("gcc", null, "undefined reference to 'Counter::count'", null, 0, "link error")));
    }

    /// <summary>The error is reported from inside the standard library's own header; the call is found in the program.</summary>
    [Theory]
    [InlineData("gcc", null, "no match for 'operator-' (operand types are 'std::_List_iterator<int>' and 'std::_List_iterator<int>')")]
    [InlineData("msvc", "C2676", "binary '-': 'const std::_List_unchecked_iterator<std::_List_val<std::_List_simple_types<_Ty>>>' does not define this operator or a conversion to a type acceptable to the predefined operator")]
    public void AListIsSortedWithItsOwnSort(string language, string? code, string message)
    {
        Write("app.cpp", "#include <algorithm>\n#include <list>\nint main() {\n    std::list<int> values = {3, 1, 2};\n    std::sort(values.begin(), values.end());\n    return values.front();\n}\n");

        var fix = Fix("cpp-sort-list", Diagnostic(language, code, message, @"C:\Strawberry\c\include\c++\13.2.0\bits\stl_algo.h", 1948, "compile error"));

        Assert.Equal("    values.sort();", Assert.Single(fix!.NewLines));
    }

    [Theory]
    [InlineData("#include <thread>\nvoid work() {}\nint main() {\n    std::thread worker(work);\n    return 0;\n}\n", 5)]
    [InlineData("#include <thread>\nvoid work() {}\nint main() {\n    std::thread worker(work);\n}\n", 5)]
    [InlineData("#include <thread>\nvoid work() {}\nint main() {\n    std::thread worker(work);\n    worker.join();\n    return 0;\n}\n", null)]
    public void AThreadIsJoinedBeforeItsVariableGoes(string source, int? before)
    {
        Write("app.cpp", source);

        var fix = Fix("cpp-thread-not-joined", Diagnostic("gcc", null, "terminate called without an active exception", null, 0, "std::terminate"));

        if (before is null)
        {
            Assert.Null(fix);
            return;
        }

        Assert.NotNull(fix);
        Assert.Equal(before, fix!.StartLine);
        Assert.Equal("    worker.join();", Assert.Single(fix.NewLines));
    }

    [Theory]
    [InlineData("msvc", "C4172", "returning address of local variable or temporary : text")]
    [InlineData("gcc", null, "reference to local variable 'text' returned [-Wreturn-local-addr]")]
    public void AReferenceToALocalBecomesAValue(string language, string? code, string message)
    {
        var file = Write("app.cpp", "#include <string>\nconst std::string& greeting() {\n    std::string text = \"hello\";\n    return text;\n}\n");

        var fix = Fix("cpp-return-local-reference", Diagnostic(language, code, message, file, 4, "compile warning"));

        Assert.NotNull(fix);
        Assert.Equal(2, fix!.StartLine);
        Assert.Equal("std::string greeting() {", Assert.Single(fix.NewLines));
        Assert.Equal(message, fix.ResolvesWarning);
    }

    /// <summary>The whole loop becomes one erase of what std::remove_if left - the same shape removeIf is in Java.</summary>
    [Theory]
    [InlineData("            values.erase(std::find(values.begin(), values.end(), v));\n", "    values.erase(std::remove_if(values.begin(), values.end(), [&](int v) { return v % 2 == 0; }), values.end());")]
    [InlineData("            std::cout << v << std::endl;\n", null)]
    public void AnEraseInsideARangeForBecomesRemoveIf(string body, string? expected)
    {
        var file = Write("app.cpp",
            "#include <algorithm>\n#include <iostream>\n#include <vector>\n\nint main() {\n    std::vector<int> values = {1, 2, 3, 4, 5, 6};\n" +
            "    for (int v : values) {\n        if (v % 2 == 0) {\n" + body + "        }\n    }\n    std::cout << values.size() << std::endl;\n    return 0;\n}\n");

        var fix = Fix("cpp-erase-in-loop", Diagnostic("gcc", null, "container-overflow on address 0x11b80a2a0740", file, 7, "container-overflow"));

        if (expected is null)
        {
            Assert.Null(fix);
            return;
        }

        Assert.NotNull(fix);
        Assert.Equal(7, fix!.StartLine);
        Assert.Equal(5, fix.RemoveCount);
        Assert.Equal(expected, Assert.Single(fix.NewLines));
    }
}
