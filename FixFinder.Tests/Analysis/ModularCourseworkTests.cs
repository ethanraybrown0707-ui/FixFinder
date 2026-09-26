using FixFinder.Core.Analysis.Checks;
using FixFinder.Core.Analysis.Frontends;
using FixFinder.Core.Analysis.Ir;
using FixFinder.Core.Checking;
using Xunit.Abstractions;

namespace FixFinder.Tests;

/// <summary>
/// Programs split into modules the way a software engineering course splits them - a model, a store, the rules, and a
/// main that ties them together - written correctly, and with the mistakes that only show across the files: a helper
/// that prints its answer instead of returning it, a call that breaks what another module checks for, text from the
/// user handed to a module that runs it as SQL, a list changed by a helper while the caller walks it.
/// </summary>
/// <remarks>
/// Following calls from file to file gives an analysis more to say, and more room to be wrong. Every correct program
/// here must draw no error or warning, and every planted mistake must be found in the file that makes the call - with
/// the other file named where the finding points into it.
/// </remarks>
public class ModularCourseworkTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    // ---- The programs, each file starting with a line ==> name <== ----

    private const string Library = """
        ==> main.py <==
        import catalogue
        from loans import lend, overdue_fee

        catalogue.add("Dune", "Herbert", copies=2)
        catalogue.add("Emma", "Austen")

        print(lend("Dune", days=14))
        print(lend("Ulysses", days=7))
        print(catalogue.describe("Dune"))
        print(overdue_fee(days_late=3))
        print(catalogue.average_copies())
        ==> books.py <==
        class Book:
            def __init__(self, title, author, copies):
                self.title = title
                self.author = author
                self.copies = copies

            def available(self):
                return self.copies > 0
        ==> catalogue.py <==
        from books import Book

        shelf = []


        def add(title, author, copies=1):
            shelf.append(Book(title, author, copies))


        def find(title):
            for book in shelf:
                if book.title == title:
                    return book
            return None


        def describe(title):
            book = find(title)
            if book is None:
                return "not in the catalogue"
            return f"{book.title} by {book.author}, {book.copies} left"


        def average_copies():
            if not shelf:
                return 0
            return sum(book.copies for book in shelf) / len(shelf)
        ==> loans.py <==
        import catalogue

        DAILY_FEE = 0.25


        def lend(title, days):
            if days <= 0:
                raise ValueError("a loan lasts at least a day")
            book = catalogue.find(title)
            if book is None or not book.available():
                return False
            book.copies -= 1
            return True


        def overdue_fee(days_late):
            return max(days_late, 0) * DAILY_FEE
        """;

    private const string Shop = """
        ==> main.py <==
        from shop.basket import Basket

        basket = Basket()
        basket.add("tea", 2)
        basket.add("milk", 1)
        print(basket.total())
        print(basket.average_price())
        ==> shop/__init__.py <==
        ==> shop/prices.py <==
        PRICES = {"tea": 3.5, "milk": 1.2, "bread": 2.0}


        def price_of(item):
            if item not in PRICES:
                raise KeyError(f"no price for {item}")
            return PRICES[item]
        ==> shop/basket.py <==
        from .prices import price_of


        class Basket:
            def __init__(self):
                self.lines = []

            def add(self, item, quantity):
                self.lines.append((item, quantity))

            def total(self):
                return sum(price_of(item) * quantity for item, quantity in self.lines)

            def average_price(self):
                if not self.lines:
                    return 0
                return self.total() / len(self.lines)
        """;

    private const string Checkout = """
        ==> main.js <==
        const Cart = require('./cart');
        const { applyDiscount } = require('./pricing');

        const cart = new Cart();
        cart.add('pen', 1.5, 4);
        cart.add('book', 12);
        console.log(cart.checkout('SAVE10').toFixed(2));
        console.log(cart.checkout('NOPE').toFixed(2));
        console.log(applyDiscount(20, 0.25));
        ==> pricing.js <==
        const discounts = { SAVE10: 0.1, HALF: 0.5 };

        function discountFor(code) {
          if (!(code in discounts)) {
            return null;
          }
          return discounts[code];
        }

        function totalOf(items) {
          return items.reduce((sum, item) => sum + item.price * item.quantity, 0);
        }

        function applyDiscount(total, rate) {
          if (rate < 0 || rate > 1) {
            throw new RangeError('a discount is between 0 and 1');
          }
          return total * (1 - rate);
        }

        module.exports = { discountFor, totalOf, applyDiscount };
        ==> cart.js <==
        const { discountFor, totalOf } = require('./pricing');

        class Cart {
          constructor() {
            this.items = [];
          }

          add(name, price, quantity = 1) {
            if (quantity < 1) {
              throw new RangeError('a cart holds at least one of each item');
            }
            this.items.push({ name, price, quantity });
          }

          checkout(code) {
            const total = totalOf(this.items);
            const discount = discountFor(code);
            if (discount === null) {
              return total;
            }
            return total * (1 - discount);
          }
        }

        module.exports = Cart;
        """;

    private const string Scores = """
        ==> main.mjs <==
        import { parseScores, mean } from './scores.mjs';
        import report from './report.mjs';

        const scores = parseScores('72, 64, 91');
        console.log(report(mean(scores)));
        ==> scores.mjs <==
        export function parseScores(text) {
          return text.split(',').map((part) => Number(part.trim()));
        }

        export function mean(values) {
          if (values.length === 0) {
            return 0;
          }
          return values.reduce((sum, value) => sum + value, 0) / values.length;
        }

        export function previousScores() {
          return [];
        }
        ==> report.mjs <==
        export default function report(average) {
          return `Mean score: ${average.toFixed(1)}`;
        }
        """;

    private const string Stack = """
        ==> main.c <==
        #include <stdio.h>
        #include "stack.h"

        int main(void)
        {
            Stack stack;
            int value;

            stack_init(&stack);
            stack_push(&stack, 3);
            stack_push(&stack, 4);

            int count = stack_size(&stack);
            if (count > 0) {
                printf("%d\n", 100 / count);
            }

            while (stack_pop(&stack, &value)) {
                printf("%d\n", value);
            }
            return 0;
        }
        ==> stack.h <==
        #ifndef STACK_H
        #define STACK_H

        #define CAPACITY 16

        typedef struct {
            int items[CAPACITY];
            int size;
        } Stack;

        void stack_init(Stack *stack);
        int stack_push(Stack *stack, int value);
        int stack_pop(Stack *stack, int *value);
        int stack_size(const Stack *stack);

        #endif
        ==> stack.c <==
        #include "stack.h"

        void stack_init(Stack *stack)
        {
            stack->size = 0;
        }

        int stack_push(Stack *stack, int value)
        {
            if (stack->size == CAPACITY) {
                return 0;
            }
            stack->items[stack->size++] = value;
            return 1;
        }

        int stack_pop(Stack *stack, int *value)
        {
            if (stack->size == 0) {
                return 0;
            }
            *value = stack->items[--stack->size];
            return 1;
        }

        int stack_size(const Stack *stack)
        {
            return stack->size;
        }
        """;

    private const string Geometry = """
        ==> main.cpp <==
        #include <iostream>

        double side_from_area(double area);

        int main()
        {
            std::cout << side_from_area(16.0) << std::endl;
            return 0;
        }
        ==> geometry.cpp <==
        #include <cmath>
        #include <stdexcept>

        double side_from_area(double area)
        {
            if (area < 0) {
                throw std::invalid_argument("an area cannot be negative");
            }
            return std::sqrt(area);
        }
        """;

    private const string Bank = """
        ==> Main.java <==
        public class Main {
            public static void main(String[] args) {
                Account account = new Account("Ada", 100.0);
                account.deposit(50.0);
                System.out.println(Money.format(account.balance()));
                System.out.println(Money.split(90.0, 3));
            }
        }
        ==> Account.java <==
        public class Account {
            private final String owner;
            private double balance;

            public Account(String owner, double opening) {
                this.owner = owner;
                this.balance = Money.requireNotNegative(opening);
            }

            public void deposit(double amount) {
                balance += Money.requireNotNegative(amount);
            }

            public double balance() {
                return balance;
            }

            public String owner() {
                return owner;
            }
        }
        ==> Money.java <==
        public final class Money {
            private Money() {
            }

            public static double requireNotNegative(double amount) {
                if (amount < 0) {
                    throw new IllegalArgumentException("an amount cannot be negative");
                }
                return amount;
            }

            public static String format(double amount) {
                return String.format("%.2f", amount);
            }

            public static double split(double total, int people) {
                if (people <= 0) {
                    throw new IllegalArgumentException("split between at least one person");
                }
                return total / people;
            }
        }
        """;

    public static TheoryData<string, string> Correct => new()
    {
        { "a library split into a model, a catalogue, loans and a main", Library },
        { "a shop package whose modules import each other relatively", Shop },
        { "a checkout in CommonJS modules", Checkout },
        { "scores in ES modules, with a default export", Scores },
        { "a C stack with a header, its implementation and a main", Stack },
        { "C++ split into a declaration and a definition", Geometry },
        { "a bank account and its money rules in Java classes of their own", Bank },
    };

    /// <summary>A mistake planted in one of the programs: what is changed, the check that finds it, where, and what the finding must say.</summary>
    public static TheoryData<string, string, string, string, string> Mistakes => new()
    {
        {
            "a helper that prints its answer instead of returning it", "analysis-null-used", "main.py", "",
            Changed(Library,
                ("""
                        if book is None:
                            return "not in the catalogue"
                        return f"{book.title} by {book.author}, {book.copies} left"
                    """, """
                        if book is None:
                            print("not in the catalogue")
                        else:
                            print(f"{book.title} by {book.author}, {book.copies} left")
                    """),
                ("print(catalogue.describe(\"Dune\"))", "print(catalogue.describe(\"Dune\").upper())"))
        },
        {
            "a loan for no days at all", "analysis-contract-broken", "main.py", "of loans.py",
            Changed(Library, ("lend(\"Dune\", days=14)", "lend(\"Dune\", days=0)"))
        },
        {
            "a member search built from what was typed", "analysis-sql-injection", "main.py", "of members.py", """
            ==> main.py <==
            from members import open_register, find_member

            register = open_register("library.db")
            print(find_member(register, input("Name: ")))
            ==> members.py <==
            import sqlite3


            def open_register(path):
                return sqlite3.connect(path)


            def find_member(connection, name):
                cursor = connection.cursor()
                cursor.execute("SELECT id, name FROM members WHERE name = '" + name + "'")
                return cursor.fetchone()
            """
        },
        {
            "members removed by a helper while the caller walks the list", ChangedWhileLooping.Rule, "main.py", "", """
            ==> main.py <==
            from registry import expire

            members = [{"name": "Ada", "fines": 12}, {"name": "Alan", "fines": 0}]
            for member in members:
                expire(members, member)
            print(len(members))
            ==> registry.py <==
            def expire(members, member):
                if member["fines"] > 10:
                    members.remove(member)
            """
        },
        {
            "a discount of more than the whole price", "analysis-contract-broken", "main.js", "of pricing.js",
            Changed(Checkout, ("applyDiscount(20, 0.25)", "applyDiscount(20, 1.5)"))
        },
        {
            "scores that are not there yet", "analysis-null-used", "main.mjs", "",
            Changed(Scores,
                ("return [];", "return null;"),
                ("import { parseScores, mean } from './scores.mjs';", "import { parseScores, mean, previousScores } from './scores.mjs';"),
                ("console.log(report(mean(scores)));", "console.log(report(mean(scores)));\nconsole.log(previousScores().length);"))
        },
        {
            "dividing by a count that is not written yet", "analysis-division-by-zero", "main.c", "",
            Changed(Stack,
                ("int stack_size(const Stack *stack);", "int stack_size(const Stack *stack);\nint stack_peak(const Stack *stack);"),
                ("""
                    int stack_size(const Stack *stack)
                    {
                        return stack->size;
                    }
                    """, """
                    int stack_size(const Stack *stack)
                    {
                        return stack->size;
                    }

                    int stack_peak(const Stack *stack)
                    {
                        return 0;
                    }
                    """),
                ("    return 0;\n}\n==> stack.h", "    printf(\"%d\\n\", 100 / stack_peak(&stack));\n    return 0;\n}\n==> stack.h"))
        },
        {
            "a negative area", "analysis-contract-broken", "main.cpp", "of geometry.cpp",
            Changed(Geometry, ("side_from_area(16.0)", "side_from_area(-4.0)"))
        },
        {
            "a bill split between nobody", "analysis-contract-broken", "Main.java", "of Money.java",
            Changed(Bank, ("Money.split(90.0, 3)", "Money.split(90.0, 0)"))
        },
    };

    /// <summary>
    /// A program with edits made to it, whatever line endings the source was checked out with. An edit that finds nothing
    /// to replace stops the test, since the program would then be the correct one and prove nothing.
    /// </summary>
    private static string Changed(string program, params (string Before, string After)[] edits)
    {
        var changed = program.ReplaceLineEndings("\n");
        foreach (var (before, after) in edits)
        {
            var replaced = before.ReplaceLineEndings("\n");
            if (!changed.Contains(replaced, StringComparison.Ordinal)) throw new InvalidOperationException($"The program has no \"{replaced}\" to change.");
            changed = changed.Replace(replaced, after.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }

        return changed;
    }

    [Theory]
    [MemberData(nameof(Correct))]
    public async Task CorrectModularProgramsAreLeftAlone(string name, string program)
    {
        if (await CheckAsync(program) is not { } found) return;

        var mistakes = found.Where(f => f.Severity != Severity.Suggestion).ToList();
        Show(name, mistakes);

        Assert.Empty(mistakes);
    }

    [Theory]
    [MemberData(nameof(Mistakes))]
    public async Task TheMistakeIsFoundWhereTheCallIs(string name, string check, string file, string mentions, string program)
    {
        if (await CheckAsync(program) is not { } found) return;

        Show(name, found);
        var finding = Assert.Single(found, f => f.CheckId == check && Path.GetFileName(f.Span.File) == file);
        Assert.Contains(mentions, finding.Message);
    }

    /// <summary>A program written as one text: each file starts with a line ==> name <==, the way head prints several files.</summary>
    private List<string> Write(string program)
    {
        var files = new List<string>();
        string? path = null;
        var lines = new List<string>();

        void Finish()
        {
            if (path is null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, string.Join('\n', lines) + "\n");
            files.Add(path);
        }

        foreach (var line in program.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("==> ", StringComparison.Ordinal) && line.EndsWith(" <==", StringComparison.Ordinal))
            {
                Finish();
                path = Path.Combine(_temp.Path, line["==> ".Length..^" <==".Length]);
                lines.Clear();
                continue;
            }

            lines.Add(line);
        }

        Finish();
        return files;
    }

    /// <summary>What the analyses find in the program, read by the reader for its language - or null when that language's tools are missing.</summary>
    private async Task<IReadOnlyList<AnalysisFinding>?> CheckAsync(string program)
    {
        var files = Write(program);
        IrProgram? read = Path.GetExtension(files[0]) switch
        {
            ".py" => PythonFrontend.FindInterpreter() is { } python ? await PythonFrontend.ReadAsync(files, python) : null,
            ".js" or ".mjs" => await JavaScriptFrontend.ReadAsync(files),
            ".c" or ".cpp" => await CFrontend.ReadAsync(files),
            ".java" => JavaFrontend.FindTools() is { } tools ? await JavaFrontend.ReadAsync(files, tools.Javac, tools.Java) : null,
            var other => throw new ArgumentException($"no reader for {other} files"),
        };

        if (read is null) return null;

        foreach (var problem in read.Problems) output.WriteLine($"problem: {problem}");
        Assert.Empty(read.Problems);
        return AbstractChecks.Run(read, new SourceText());
    }

    private void Show(string name, IEnumerable<AnalysisFinding> findings)
    {
        foreach (var finding in findings)
            output.WriteLine($"{name}: {Path.GetFileName(finding.Span.File)}:{finding.Span.Line} [{finding.Severity}] {finding.CheckId}: {finding.Message}");
    }
}
