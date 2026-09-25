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

In every language it can read, the logic check also **follows every value through the code** (abstract interpretation).
Each language is read by a parser that agrees with its own compiler where there is one to ask - Python's `ast`, javac's
tree API, Roslyn and `go/parser` - and by FixFinder's own reader for C, C++ and JavaScript, where this machine has no
parser to ask. Everything lands in one shared form, and each
function becomes a graph of the ways through it. Every variable is tracked as the kinds of value it can hold, its range of
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

**Threads** are followed from where the program starts them - `new Thread(...)`, `Task.Run`, `Parallel.For`,
`threading.Thread(target=...)`, an executor - to the code they run, and whether more than one copy of it runs at once:

| Check | For example |
|---|---|
| An update two threads can lose | `count++` in a `Runnable` given to two threads, `total += x` in a `Parallel.For` body |
| A flag a thread may never see change (the memory model) | `while (running)` on a field that is not `volatile` |
| Locks taken in opposite orders | `synchronized (a) { synchronized (b) ... }` in one place, `b` then `a` in another |
| Locks taken round a circle, of any length | `first` then `second`, `second` then `third`, `third` then `first` - three threads, one at each |
| A Python `Lock` taken again by the thread holding it | `with self.lock:` around a call to a method that takes `self.lock` too |
| A result read before the threads changing it have finished | `print(total)` between `worker.start()` and `worker.join()` |
| Shared data used without the lock that guards it elsewhere | a `synchronized` `deposit()` and a `getBalance()` that is not, called on another thread |
| `wait` or `notify` without its lock, or `wait` outside a loop | `wait()` in a method that is not `synchronized` |
| `run()` called instead of `start()` | `worker.run()`, which runs the work on the calling thread |

Deadlocks are found in a **lock-order graph**: an edge from one lock to another wherever the second is taken while the
first is held, including inside a function called while it is held - with that function's parameters replaced by what
the call passes, so `transfer(a, b)` on one thread and `transfer(b, a)` on another are seen to take the same two locks
in opposite orders. Any cycle in the graph is a possible deadlock, however many locks it goes through, but only when
threads could be at all its places at once: a lock held around all of them lets one thread in at a time, and the main
thread cannot be at two places together, so neither is reported. Java's and C#'s locks can be taken again by the thread
holding them; a Python `threading.Lock` cannot, which is why taking one twice is a deadlock with no second thread.

Races come from two relations. **Happens-before** orders the code that starts threads against the threads: what comes
before `start()` happens before everything the thread does, and everything the thread does happens before the `join()`
that waits for it - so code between the two runs at the same time as the thread. A thread handed to other code, which
could join it anywhere, is never claimed to be unfinished. The **lockset** of each access is every lock held at it -
by `synchronized`, `lock` or `with`, by `lock()` and an `unlock()` in a `finally`, or by the code that called the
function it is in. Two accesses to the same field or variable race when nothing orders them, one changes it, and no lock
is held at both. A field of an object is only shared by threads using that same object.

Each language keeps its own rules, and a finding says what that language actually does:

| | |
|---|---|
| **Go** | A nil slice has no items and a nil map reads as missing, so `len`, indexing and `range` on them are all fine, while reading a field through a nil pointer is a panic - and a method with a nil receiver is ordinary Go, so `if c == nil` at the top of one is not a test that can never be true. Both sides of a division have the same type, so a whole-number divisor means whole-number division. `panic` is what a guard raises; a deferred call runs on every way out, so a lock released by `defer` is never reported as left locked, and one taken by `defer` is taken for the caller. A slice is a value, so handing it to other code cannot change how long it is. Goroutines started with `go` are followed like any other thread. |
| **JavaScript** | Dividing by zero gives Infinity rather than failing, and a position past the end gives `undefined`, so neither is reported. Every object and array is true however empty, only `0`, `""`, `null` and `undefined` are false, and `a?.b.c` gives nothing when `a` is nothing - the whole chain is skipped, not just the next step. `typeof x === "number"` says x is something. A variable declared with no value is `undefined`, so reading a field of it is a TypeError. |
| **C and C++** | Dividing by zero, going through a null pointer and reading past the end of an array are undefined behaviour, which usually stops the program. `malloc` and its like can come back with nothing, so what they return is checked before it is used. |
| **Python** | Dividing by zero is ZeroDivisionError whatever the numbers are; an empty list is false; text and numbers cannot be added. |
| **Java and C#** | Whole-number division by zero fails while real division gives infinity; a declared type sets what a variable can hold and how large it can be. |

For **C and C++** the same walk through the graph also checks what happens to memory:

| Check | For example |
|---|---|
| Memory used after it is freed | `free(node); printf("%d", node->value);` - including `n = n->next` after `free(n)` in a loop |
| Memory freed twice | `free(buffer);` on a way through the function that already freed it |
| Memory nobody frees | `malloc` into a local that is never freed, never returned and never handed on |
| The address of something that is about to go | `return &count;`, where `count` belongs to the function that is returning |
| A value read before it is given one | `int total; printf("%d", total);` - unless its address was taken first, as `scanf("%d", &total)` does |

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

Every fix for Python, Java or C# code is also checked for **what it changes** (semantic diffing). The fix is made in a
copy, both versions are read, and each function it touches is followed path by path in both - the same parameter, list
length or typed number is the same symbol in each - so the solver can find the inputs for which the two versions end
differently. The finding then says, under **What the fix changes**:

- *When `values` is empty: before, `average` stopped with ZeroDivisionError on line 5; now it returns 0.*
- *When `score` is 50: before, `grade` returned "fail"; now it returns "pass". For every other input it behaves exactly
  as before.*
- *`double` behaves exactly as before for every input - only the code changes*, for a rewrite that changes nothing.

"Exactly as before" is only said when every pair of paths was compared. A path that depends on something the analysis
cannot follow - an unknown call, a value it had to approximate - means the finding says it could not compare every input
instead. The same operation on the same inputs counts as the same value in both versions, so `total / people` in both
agrees without being worked out.

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

## From your editor

`fixfinder` runs the same check from a terminal or from any editor's task, and prints each finding as one line in the
format compilers use - so the findings land in the editor's own list of problems, and a click goes to the line. Nothing
has to be installed into the editor.

```
dotnet FixFinder.Cli/bin/Debug/net8.0/fixfinder.dll marks.py
dotnet FixFinder.Cli/bin/Debug/net8.0/fixfinder.dll Grades.java --expect "Average: 68" --level beginner
```

| | |
|---|---|
| `--format msbuild` | The default. The format Visual Studio and Rider use, and the one VS Code's built-in `$msCompile` reads - checked against that matcher's pattern, copied from VS Code's source, in `CommandLineTests`. |
| `--format gcc` | `file:line:column: error: message`, for tools that expect gcc's form, Eclipse among them. A suggestion is a `note`, since gcc's form has no word for it. |
| `--format json` | Everything each finding says, for another program to use. |
| `--level` | `beginner`, `student` or `technical` - how much each finding explains. Your saved setting otherwise. |
| `--expect` | What the program should print, so a program that runs but gives the wrong answer is caught too. |

It compiles and runs the program, exactly as the window does, and only ever the one named on its command line. Only
findings go to standard output; what it says about the run goes to standard error, so an editor never mistakes it for a
problem. It exits with 0 when nothing is wrong, 1 when there is at least one error - so a build step can stop on it - and
2 when the program could not be checked at all. The language versions chosen in Settings apply here too.

**VS Code:** copy `Editors/vscode-tasks.json` into your project's `.vscode/tasks.json` and change the path to
`fixfinder.dll`. *Terminal → Run Task → FixFinder: check this file* checks whatever file is open.

**Eclipse, Visual Studio, and anything else with external tools:** add `dotnet` as an external tool with the path to
`fixfinder.dll` and the current file as its arguments, choosing `--format gcc` where the tool reads gcc's form.

## Layout

| Project | |
|---|---|
| `FixFinder.Core` | Everything but the window. `net8.0`, so the tests run without a desktop. |
| `FixFinder.Gui` | The WPF window. |
| `FixFinder.Cli` | `fixfinder`, the same check from a terminal or an editor. All of it is `Core/Engine/CommandLine.cs`; this only hands it the console. |
| `FixFinder.Tests` | xUnit tests, in folders that mirror `FixFinder.Core`. |
| `TestTargets` | Small programs that crash, hang or print the wrong thing, to point FixFinder at. |

Inside `FixFinder.Core`:

| Folder | |
|---|---|
| `Checking` | The two checks, the finding model, compiler diagnostics, and the guides in `Checking/Guides`. |
| `Logic` | The logic checks, and the search for the change that fixes the output. |
| `Analysis` | Following the values: the shared form (`Ir`), each language's reader (`Frontends`), the graph of the ways through a function (`Flow`), the values tracked (`Abstract`), the constraint solver (`Solver`), path-by-path execution and loop bounds (`Symbolic`), backward slices (`Slicing`), running a prediction for real with a line tracer (`Dynamic`), comparing a fix with the original (`Diffing`) and the checks (`Checks`), including what each language does when a program goes wrong (`Checks/Failures.cs`). |
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
