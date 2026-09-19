# FixFinder

[![tests](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml/badge.svg)](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml)
[![test count](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fethanraybrown0707-ui%2FFixFinder%2Fmain%2F.github%2Fbadges%2Ftests.json)](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-blue.svg)](LICENSE)

FixFinder checks a program's **syntax and logic at the same time** and explains every mistake it finds: how serious it
is, how sure FixFinder is, the line it is on, what is wrong, why it matters, how to fix it, and the corrected code.

There is no AI in it. Every check is ordinary code you can read: compiler and interpreter output, rules that work a fix
out from an error message, patterns that recognise mistakes in the code itself, and a search that tries small changes
until the program prints what it should. FixFinder never changes your files.

## Using it

1. **Choose a program.** Drop the file on the window, or press **Choose a file…**.
2. **Press the language it is written in** - Python, Java, C#, C, C++, JavaScript or Go, or **Auto-detect**. That starts
   both checks at once. The program is compiled if it needs to be, and run.
3. **Optionally, say what it should print.** Arguments, the input to type and the expected output go in the boxes under
   the program, and **+ Add another run** adds more. With them, a program that runs but prints the wrong thing is caught
   too, and the change that makes it right is searched for.
4. **Read the report.** The filters show every finding, or only the errors, warnings or suggestions. Each finding has
   **Copy corrected code**, **Search online** for the error on GitHub and Stack Overflow, and **Show in folder**.
   **Copy report** copies every finding as plain text.

A program in more than one file is checked as the whole program: Python imports and JavaScript `require`s are followed,
Java is compiled from its source root, C# from its project, Go as its package, and C and C++ with the other files and
headers beside them.

## What each finding tells you

| | |
|---|---|
| **Severity** | **Error** - the program fails, or gives the wrong answer. **Warning** - it works, but not reliably, or not as intended. **Suggestion** - it works; this is a better way. |
| **Confidence** | **Certain** - the compiler or a run proved it, or the code cannot mean anything else. **Likely** - true for nearly every program written this way. **Possible** - worth a look; it depends on what the program is for. |
| **Line** | The file and line it is on. |
| **Explanation** | What is wrong, in the program's own names. |
| **Why it matters** | What goes wrong because of it. |
| **Suggested fix** | What to change. |
| **Example of corrected code** | Your own lines as they should be, when FixFinder worked the fix out and a compiler agreed with it - otherwise a general example. |

When two checks find the same mistake - a compiler warning and a logic pattern, or a crash and the pattern that explains
it - the report shows it once, at the surer of the two confidences.

## How the two checks work

The **syntax check** asks the language's own tools for every error and warning, then runs the program to catch the error
it stops with:

| Language | Checked by |
|---|---|
| Python | `compile()` on every file, with its warnings |
| Java | `javac -Xlint:cast,divzero,empty,fallthrough,finally,overrides,rawtypes,static,unchecked,deprecation` |
| C# | `dotnet build`, with the compiler's and analysers' warnings |
| C, C++ | gcc, clang or MSVC, with `-Wall -Wextra` or `/W3` |
| JavaScript | `node --check` on every file |
| Go | `go build`, then `go vet` |

For an error whose message pins the answer down - a missing import, a misspelt name, a semicolon, a loop one step too
long - a **fix rule** works out the change from the code. Every fix is made in a copy and checked by the compiler or
interpreter, and only offered if that passes.

The **logic check** reads the code for mistakes that compile and then give the wrong answer: `answer == "yes" or "y"`,
`total = 0` inside the loop that adds to it, `Console.Read()` used as a number, removing items while counting up through a
list. When an expected output was given, it also runs changed copies of the program to find the one that prints it:

1. Each run is repeated with coverage on, and every line is scored by how often the wrong runs reached it compared with
   the right ones (the Ochiai formula, with DStar breaking ties).
2. On the most suspicious lines, the small edits behind most logic bugs are generated: `<` for `<=`, a bound one out, the
   wrong operator, integer division, `min` for `max`, an `if` / `elif` chain in the wrong order.
3. Each edit is made in a private copy, built and run with every input. The first that prints exactly what was expected
   for every run is the answer. More runs, especially ones that go wrong in different ways, make the answer better.

For Python, Java and C#, the logic check also **follows every value through the code** (abstract interpretation). Each
language is read by its own parser - Python's `ast`, javac's tree API and Roslyn - into one shared form, and each
function into a graph of the ways through it. Every variable is tracked as the kinds of value it can hold, its range of
numbers, its range of lengths and whether it can be null, through every branch and loop until nothing changes:

| Check | For example |
|---|---|
| Dividing by something that can be zero | `total / count` when the loop that counts may never run |
| Using something that can be null | a variable set to null and only sometimes given a value; the result of `?.` used unchecked |
| A position that does not exist | `points[3]` on a list of three |
| Taking an item from something empty | `pop()` on an empty list, `Pop()` on an empty stack |
| Text that is not a number | `int("twelve")`, `Integer.parseInt("twelve")`, `int.Parse("twelve")` |
| A condition that can never be true, or is always true | `mark > 100 && mark < 0` |
| A loop that never runs, an assert that always fails | `while (n > 0)` with `n` still 0 |
| A loop that never ends | `while n > 0: print(n)` - nothing inside changes `n` |
| A call that breaks what a function checks for | `root(-4)`, when `root` starts with `if x < 0: raise ValueError` |
| A call with the wrong arguments (Python) | `area(3)`, when `area` takes a width and a height |
| A value that can never match its type hint (Python) | `def label(score: int) -> str` that returns `score` |
| Using a file after it is closed | `handle.readline()` after the `with` block that opened it |
| A lock that is not always released | `lock.lock()`, then a `return` before `unlock()` |

The checks look across functions. A call to one of the program's own functions is matched to it, so the call can be
checked against the arguments the function takes, the type hints it gives and the guards it starts with - its
**contract**. What a function returns is worked out once and used at every call, which is how a function that always
returns None is caught where its result is used. What a method returns is never assumed, because a subclass can
replace it. A variable's declared type also sets its range, so `b < 0` for a C# `byte` can never be true.

The order things happen in is checked too (**temporal properties**): once a file or stream is closed it must not be
used, and a lock that is taken must be released on every way out of the function.

These findings say **Found by abstract interpretation**. Anything the analysis cannot follow - a variable a lambda or
local function can change, a field another method can change, the result of an unknown call - is treated as unknown, so
the checks stay quiet rather than guess.

Then **symbolic execution** follows each function one path at a time, with a symbol for each value it does not know,
and asks a constraint solver which ways through are possible. The solver is FixFinder's own: an exact simplex over
fractions, with branching for whole numbers.

- A line that can fail gets the inputs that make it fail: *Fails when `values` is empty*, *Fails when `person` is null*.
- A possible mistake no path can actually reach is dropped as a false alarm, once every path has been followed - for
  example a `None` that only one branch gives, guarded later by the same test.
- A line that some path cannot help failing on is reported even where the values seen together could not show it:
  `if a == b:` and then `1 / (a - b)`.
- Dividing by a number someone types is reported with the number that breaks it.

A loop is followed as many times as its bound when the code shows one (`for i in range(10)`, `for (i = 0; i < n; i++)`
with `n` known), and a few times otherwise. A search cut short like that never drops a finding. Paths, steps and time
are all capped - a quarter of a second per function, five seconds per program - so a large program still checks quickly.

Each of these findings also shows **the lines that decide it**, found by **program slicing**: working backwards from the
value that goes wrong, through the assignments that can reach it and the conditions that decide whether they run. For
`return total / count` that is the `def` line, `count = 0`, the loop and `count += 1` - not the lines that only add up
`total`.

For Python, a finding with inputs that break it is then **tried for real**: the function is called with exactly those
inputs - or, for top-level code, the program is run with them typed in - under a line tracer. Only if it stops with the
predicted error on the predicted line does the finding become Certain, and it says what happened: *Running
`average(values=[])` stopped with ZeroDivisionError on line 8, where `count` was 0. Lines it ran: 1-5, 8.* The lines it
ran are compressed, so a loop that went round three times shows as `(5-7)×3`. A method, which needs its object, is left
as it was.

The two checks run side by side. The only wait is that the expected output can be checked once the program builds.

## How much is checked

Python, Java and C# are checked most thoroughly.

| Language | Fix rules | Logic checks | Guides |
|---|---:|---:|---:|
| Python | 88 | 33 | 41 |
| Java | 54 | 32 | 46 |
| C# | 47 | 30 | 49 |
| C | 46 | 18 | 39 |
| C++ | 45 | 23 | 39 |
| JavaScript | 41 | 25 | 15 |
| Go | 38 | - | 18 |

A guide is the explanation, the reason it matters and the example shown for one kind of mistake. Every logic check has
one. Every compiler warning is reported too, rated as an error, warning or suggestion.

A crash is read in fifteen languages, each with its own stack-trace parser: Python, C#, Java, JavaScript, Go, C, C++,
Rust, Ruby, PHP, PowerShell, Dart, Elixir, Perl and Lua. Anything else gets a generic reading of its file and line.

Each check is written to stay quiet when it is not sure, because a check that fires on correct code teaches people to
ignore it. The newest checks were run over large bodies of working code - Python's standard library, part of the JDK's
own library, npm, and FixFinder itself - and each false alarm found there was fixed and kept as a test.

## Searching online

**Search online** on a finding looks the error up on GitHub Issues and Stack Overflow. Only the error's text is sent -
never your code - and nothing is sent until you press it.

Results are ranked by a fixed formula, written out in `CandidateRanker`: whether it names the same error type (0.30),
how much of the message it shares (0.22), whether the query is in the title (0.10), whether it was resolved (0.10),
votes (0.08, capped so one famous answer cannot drown a precise one), age (0.06), language (0.06) and whether a patch is
attached (0.08). A patch from GitHub is shown as the code it should end up as, never as a diff to paste.

Without a key, Stack Overflow allows 300 requests a day and GitHub 10 searches a minute. A GitHub token with **no
permissions selected** raises those limits; FixFinder only reads public data, so do not give it more. Tokens are stored
encrypted with Windows DPAPI under your account, and responses are cached as plain JSON under
`%LOCALAPPDATA%\FixFinder\cache`. Settings shows what is stored where, and which languages this computer can run.

## Layout

| Project | |
|---|---|
| `FixFinder.Core` | Everything but the window. `net8.0`, so the tests run without a desktop. |
| `FixFinder.Gui` | The WPF window. |
| `FixFinder.Tests` | xUnit tests, in folders that mirror `FixFinder.Core`. |
| `TestTargets` | Small programs that crash, hang or print the wrong thing, to point FixFinder at. |

Inside `FixFinder.Core`:

| Folder | |
|---|---|
| `Checking` | The two checks, the finding model, compiler diagnostics, and the guides in `Checking/Guides`. |
| `Logic` | The logic checks, and the search for the change that fixes the output. |
| `Analysis` | Following the values: the shared form (`Ir`), each language's reader (`Frontends`), the graph of the ways through a function (`Flow`), the values tracked (`Abstract`), the constraint solver (`Solver`), path-by-path execution and loop bounds (`Symbolic`), backward slices (`Slicing`), running a prediction for real with a line tracer (`Dynamic`) and the checks (`Checks`). |
| `LocalFixes/Rules` | The fix rules: one folder per language, one file per kind of mistake (`SyntaxRules`, `NameRules`, `TypeRules`, `ClassRules`, `CrashRules`, ...), and one helper class per language (`PythonCode`, `JavaCode`, `CSharpCode`, ...). |
| `Execution` | Finding toolchains, building and running programs. |
| `Parsing` | The stack-trace parsers. |
| `Fingerprinting`, `Sources`, `Ranking`, `Http`, `Security` | Online search: the query, GitHub and Stack Overflow, ranking, caching and token storage. |
| `Patching` | Reading diffs from search results and working out where they would land in your code. |

To add a check: a logic check goes in `Logic`, with a guide in `Checking/Guides` and cases in
`CodeReviewPatternTests`, including correct code it must leave alone. A fix rule goes in its language's folder under
`LocalFixes/Rules`, is listed in `LocalFixEngine.Rules`, and gets a guide and a test.

The NuGet dependencies are `System.Security.Cryptography.ProtectedData`, for the token, and `Microsoft.CodeAnalysis.CSharp`
(Roslyn), to read C#.

## Build and run

```
dotnet build FixFinder.Gui\FixFinder.Gui.csproj
dotnet test  FixFinder.Tests\FixFinder.Tests.csproj
```

Run the build with `run-fixfinder.cmd`, or `dotnet FixFinder.Gui\bin\Debug\net8.0-windows\FixFinder.Gui.dll`. Tests that
need a compiler or runtime that is not installed skip themselves.

To make a single executable with .NET included:

```
powershell -ExecutionPolicy Bypass -File publish-exe.ps1
```

That writes `publish\FixFinder.exe`. Add `-FrameworkDependent` for a much smaller exe that needs the .NET 8 desktop
runtime installed. Keep it somewhere writable, since it writes its `Logs` folder beside itself.

On a machine with Windows Smart App Control, a newly built exe or DLL can be refused until Windows has seen it before,
even when it is signed. `run-fixfinder.cmd` starts the DLL through `dotnet.exe`, which Windows already trusts, and is the
dependable way in. Each project signs its Debug build with `sign-for-wdac.ps1` when a code-signing certificate is present,
and does nothing when there is not one, as on CI.

The logo is drawn by `FixFinder.Gui\Assets\make-icon.py`, which draws every icon size at its own scale so the small ones
stay sharp.

## Licence

MIT. See [LICENSE](LICENSE).
