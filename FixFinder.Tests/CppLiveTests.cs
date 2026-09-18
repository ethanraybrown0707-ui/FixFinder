using FixFinder.Core.Engine;
using FixFinder.Core.Execution;
using FixFinder.Core.Http;
using FixFinder.Core.LocalFixes;
using FixFinder.Core.Sources;

namespace FixFinder.Tests;

/// <summary>
/// C++ programs broken the way beginners and later-year students break them, built and run for real with g++, with MSVC, and
/// rebuilt under AddressSanitizer - each must end with its rule's fix, checked and ready to copy.
/// </summary>
/// <remarks>
/// Live cases skip when their toolchain is missing, and a skip looks like a pass - an MSVC case that really ran takes seconds.
/// MSVC cases hide g++ for their own flow, since the build prefers g++.
/// </remarks>
public class CppLiveTests
{
    private const string Dog = "class Dog {\npublic:\n    int age = 3;\n};\n\n";

    private static string Main(string body, string includes = "#include <iostream>\n", string extra = "") =>
        includes + "\n" + extra + "int main() {\n" + body + "    return 0;\n}\n";

    /// <summary>The toolchain, the construct, the program, the rule, and text the copied fix must contain.</summary>
    public static TheoryData<string, string, string, string, string> Cases
    {
        get
        {
            var vector = "#include <iostream>\n#include <vector>\n";
            var text = "#include <iostream>\n#include <string>\n";

            var both = new (string Construct, string Source, string Rule, string Expected)[]
            {
                ("cout without std::", Main("    cout << \"hello\" << endl;\n"), "cpp-std-prefix", "std::cout << \"hello\" << std::endl;"),
                ("System.out.println", Main("    System.out.println(\"hello\");\n"), "cpp-foreign-print", "std::cout << \"hello\" << std::endl;"),
                ("cout with >>", Main("    int total = 5;\n    std::cout >> total;\n"), "cpp-stream-arrows", "std::cout << total;"),
                ("add on a vector", Main("    std::vector<int> values;\n    values.add(1);\n    std::cout << values.size() << std::endl;\n", vector), "cpp-container-member", "values.push_back(1);"),
                ("new without a pointer", Main("    Dog d = new Dog();\n    std::cout << d.age << std::endl;\n", extra: Dog), "cpp-new-without-pointer", "Dog d;"),
                (". on a pointer", Main("    Dog *d = new Dog();\n    std::cout << d.age << std::endl;\n    delete d;\n", extra: Dog), "cpp-member-operator", "d->age"),
                ("private member", Main("    Account a;\n    std::cout << a.balance << std::endl;\n", extra: "class Account {\n    int balance = 10;\n};\n\n"), "cpp-private-member", "public:\n    int balance = 10;"),
                ("most vexing parse", Main("    std::vector<int> values();\n    values.push_back(1);\n    std::cout << values.size() << std::endl;\n", vector), "cpp-vexing-parse", "std::vector<int> values;"),
                ("string plus an int", Main("    int age = 20;\n    std::string text = \"Age: \";\n    text = text + age;\n    std::cout << text << std::endl;\n", text), "cpp-string-plus-number", "text = text + std::to_string(age);"),
                ("a block closed too soon", "#include <iostream>\n\nint main() {\n    std::cout << \"hello\" << std::endl;\n    }\n    return 0;\n}\n", "c-extra-closing-brace", "std::cout << \"hello\" << std::endl;\n    return 0;"),
                ("override of a function not virtual",
                    Main("    Dog d;\n    d.speak();\n", extra: "class Animal {\npublic:\n    void speak() const { std::cout << \"...\" << std::endl; }\n};\n\nclass Dog : public Animal {\npublic:\n    void speak() const override { std::cout << \"Woof\" << std::endl; }\n};\n\n"),
                    "cpp-virtual-base", "virtual void speak() const"),
                ("misspelt override",
                    Main("    Dog d;\n    d.speak();\n", extra: "class Animal {\npublic:\n    virtual ~Animal() = default;\n    virtual void speak() const { std::cout << \"...\" << std::endl; }\n};\n\nclass Dog : public Animal {\npublic:\n    void speek() const override { std::cout << \"Woof\" << std::endl; }\n};\n\n"),
                    "cpp-override-typo", "void speak() const override"),
                ("private inheritance",
                    Main("    Dog d;\n    d.speak();\n", extra: "class Animal {\npublic:\n    void speak() const { std::cout << \"...\" << std::endl; }\n};\n\nclass Dog : Animal {\n};\n\n"),
                    "cpp-private-inheritance", "class Dog : public Animal {"),
                ("unique_ptr copied", Main("    auto first = std::make_unique<int>(5);\n    auto second = first;\n    std::cout << *second << std::endl;\n", "#include <iostream>\n#include <memory>\n"), "cpp-move-unique-ptr", "auto second = std::move(first);"),
                ("const object, method not const",
                    Main("    Counter c;\n    show(c);\n", extra: "class Counter {\npublic:\n    int count = 3;\n    int get() { return count; }\n};\n\nvoid show(const Counter& c) {\n    std::cout << c.get() << std::endl;\n}\n\n"),
                    "cpp-const-method", "int get() const { return count; }"),
                ("lambda without its capture", Main("    int total = 10;\n    auto addOne = []() { return total + 1; };\n    std::cout << addOne() << std::endl;\n"), "cpp-lambda-capture", "[&total]"),
                ("= into an explicit constructor",
                    Main("    Meters m = 5.0;\n    std::cout << m.value << std::endl;\n", extra: "class Meters {\npublic:\n    explicit Meters(double value) : value(value) {}\n    double value;\n};\n\n"),
                    "cpp-explicit-constructor", "Meters m(5.0);"),
                ("static member never defined",
                    Main("    Counter a;\n    Counter b;\n    std::cout << Counter::count << std::endl;\n", extra: "class Counter {\npublic:\n    static int count;\n    Counter() { count++; }\n};\n\n"),
                    "cpp-static-member-definition", "int Counter::count = 0;"),
                ("const member assigned in the constructor",
                    Main("    Circle c(2.0);\n    std::cout << c.radius << std::endl;\n", extra: "class Circle {\npublic:\n    const double radius;\n    Circle(double r) {\n        radius = r;\n    }\n};\n\n"),
                    "cpp-const-member-initialiser", "Circle(double r) : radius(r) {"),
                ("std::sort on a list", Main("    std::list<int> values = {3, 1, 2};\n    std::sort(values.begin(), values.end());\n    std::cout << values.front() << std::endl;\n", "#include <algorithm>\n#include <iostream>\n#include <list>\n"), "cpp-sort-list", "values.sort();"),
            };

            var data = new TheoryData<string, string, string, string, string>();

            foreach (var toolchain in new[] { "gcc", "msvc" })
            foreach (var (construct, source, rule, expected) in both)
                data.Add(toolchain, construct, source, rule, expected);

            // An exception nothing caught, and a thread never joined. g++'s runtime names both; an MSVC program ends
            // without a word, and is rebuilt with FixFinder's terminate handler to say the same thing.
            foreach (var toolchain in new[] { "gcc", "msvc" })
            {
                data.Add(toolchain, ".at() past the end", Main("    std::vector<int> values = {1, 2, 3};\n    for (size_t i = 0; i <= values.size(); i++) {\n        std::cout << values.at(i) << std::endl;\n    }\n", vector), "cpp-at-out-of-range", "i < values.size()");
                data.Add(toolchain, "thread never joined", Main("    std::thread worker(work);\n", "#include <iostream>\n#include <thread>\n", "void work() {\n    std::cout << \"working\" << std::endl;\n}\n\n"), "cpp-thread-not-joined", "worker.join();");
            }

            // Only g++ fails these; MSVC accepts them.
            data.Add("gcc", "typename missing", Main("    show(std::vector<int>{1, 2, 3});\n", vector, "template <typename T>\nvoid show(const std::vector<T>& values) {\n    for (std::vector<T>::const_iterator it = values.begin(); it != values.end(); ++it) {\n        std::cout << *it << std::endl;\n    }\n}\n\n"), "cpp-typename", "typename std::vector<T>::const_iterator");
            data.Add("gcc", "void main", "#include <iostream>\n\nvoid main() {\n    std::cout << \"hello\" << std::endl;\n}\n", "cpp-main-returns-int", "int main() {");

            // Only MSVC refuses these.
            data.Add("msvc", "auto parameter", Main("    show(5);\n", extra: "void show(auto value) {\n    std::cout << value << std::endl;\n}\n\n"), "cpp-auto-parameter", "template <typename T>\nvoid show(T value) {");
            data.Add("msvc", "no return type", Main("    std::cout << add(2, 3) << std::endl;\n", extra: "add(int a, int b) {\n    return a + b;\n}\n\n"), "cpp-missing-return-type", "int add(int a, int b) {");
            data.Add("msvc", "misspelt std::endl", Main("    std::cout << \"hello\" << std::endll;\n"), "cpp-std-name-typo", "std::endl;");

            // Found by AddressSanitizer, which is always MSVC's, whichever compiler built the program.
            data.Add("asan", "double delete", Main("    int *value = new int(5);\n    std::cout << *value << std::endl;\n    delete value;\n    delete value;\n"), "c-double-free", "delete value;\n    return 0;");
            data.Add("asan", "[0] on an empty vector", Main("    std::vector<int> values;\n    values[0] = 5;\n    std::cout << values[0] << std::endl;\n", vector), "cpp-index-empty-vector", "values.push_back(5);");

            // Only an MSVC build crashes erasing inside the loop; g++'s build happens to survive it.
            data.Add("msvc-asan", "erase inside a range-for",
                Main("    std::vector<int> values = {1, 2, 3, 4, 5, 6};\n    for (int v : values) {\n        if (v % 2 == 0) {\n            values.erase(std::find(values.begin(), values.end(), v));\n        }\n    }\n    std::cout << values.size() << std::endl;\n", "#include <algorithm>\n#include <iostream>\n#include <vector>\n"),
                "cpp-erase-in-loop", "values.erase(std::remove_if(values.begin(), values.end(), [&](int v) { return v % 2 == 0; }), values.end());");

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheConstructIsFixedByARealBuild(string toolchain, string construct, string source, string rule, string expected)
    {
        var ready = toolchain switch
        {
            "gcc" => Toolchains.FindGnu(cpp: true) is not null,
            "msvc" => Toolchains.FindMsvc() is not null,
            "asan" or "msvc-asan" => Toolchains.FindMsvc() is not null && LocalFixLiveTests.HasAddressSanitizer(),
            _ => false,
        };

        if (!ready) return;

        using var msvcOnly = toolchain is "msvc" or "msvc-asan" ? Toolchains.WithoutGnu() : null;
        using var temp = new TempFolder();

        var path = Path.Combine(temp.Path, "app.cpp");
        File.WriteAllText(path, source);

        var plan = TargetFactory.FromFile(path, TimeSpan.FromMinutes(5));
        Assert.True(plan.Ok, plan.Problem);

        using var http = new FixFinderHttpClient();
        var outcome = await new FixFinderSession(http, new FixSourceRegistry()).RunAsync(plan, new SearchBudget(Cache: CacheMode.CacheOnly));

        if (ApplicationControl.Refused(outcome)) return;
        Assert.True(outcome.Result == SessionResult.FoundFix, $"{construct} ({toolchain}): {outcome.Result} - {outcome.Headline}");
        Assert.Equal($"local:{rule}", outcome.Best!.Id);

        var copied = PasteableFix.For(new ExaminedCandidate(outcome.Best, 1, outcome.Candidates.Count, outcome.Harvest, outcome.Plan));
        Assert.NotNull(copied);
        Assert.Contains(expected, copied!.Text.ReplaceLineEndings("\n") + "\n", StringComparison.Ordinal);
    }
}
