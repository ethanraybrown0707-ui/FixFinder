# Sample programs

Twenty-one deliberately-broken programs across six languages, one per thing FixFinder has to get
right. They are chosen for
**what the tool does with them**, not for variety of exception names — two of them do not crash
at all, because "it found nothing" and "it did not break" are outcomes worth being able to see.

Drag any of them onto the FixFinder window, or onto the exe.

| # | Program | What happens | Why it is here |
|---|---|---|---|
| 1 | `1-missing-module.py` | `ModuleNotFoundError: No module named 'yaml'` | The case search is genuinely good at. The quoted `'yaml'` is kept in the precise query, because the name of the missing module *is* the error |
| 2 | `2-inside-a-library.py` | `requests.exceptions.InvalidSchema` | Crashes inside a library rather than your code. Names `requests` as the nearest library and confines the GitHub search to `psf/requests` |
| 3 | `3-none-attribute.py` | `AttributeError: 'NoneType' object has no attribute 'get'` | The most common real crash. Shows the honest warning that it is in **your own code**, where searching rarely helps |
| 4 | `4-chained-failure.py` | `RuntimeError` raised from a `KeyError` | Two exceptions, one printed inside the other. Searches for the **root cause**, not the wrapper whose message only your program has ever printed |
| 5 | `5-deep-recursion.py` | `RecursionError` | A volume test — thousands of frames |
| 6 | `6-noisy-then-crash.py` | `KeyError: 'bytes'` after 4,000 log lines | Real programs are not quiet. The traceback has to be found in output that looks nothing like one |
| 7 | `7-runs-fine.py` | nothing, exits 0 | **No prompt at all.** The tool must not invent a problem |
| 8 | `8-silent-failure.py` | exits 3, no traceback | Fails while saying nothing a machine can look up. Should say exactly that |
| 9 | `9-never-finishes.py` | runs forever | Stopped at the timeout and reported as *still running*, not as a crash |
| 10 | `10-dotnet-format/` | `FormatException` | A different language entirely, and the source found from a portable PDB |

## Compiled languages

These need a toolchain, and FixFinder finds it rather than asking you to. Pick the **source
file** — it is built first, and if the build fails, *that* is the error it looks up.

| # | Program | What happens | Why it is here |
|---|---|---|---|
| 11 | `11-c-wont-compile.c` | `error C2065: 'avarage': undeclared identifier` | **A compiler error is a searchable error.** The code is lifted out as its own high-weight term |
| 12 | `12-c-segfault.c` | builds, then dies with `0xC0000005` | A native crash on Windows prints *nothing*. The honest answer is "crashed, nothing to search with" |
| 13 | `13-cpp-wont-compile.cpp` | `error C2039: 'vector': is not a member of 'std'` | A missing `#include`, one of the most-asked C++ questions there is |
| 14 | `14-cpp-assert.cpp` | builds, then fails an assertion | The contrast with 12: an assertion names the condition, the file and the line, so there *is* something to look up |
| 15 | `15-csharp-wont-compile.cs` | `error CS0103: The name 'total' does not exist` | A loose `.cs` file, no project needed — the SDK builds and runs it in one command |
| 16 | `16-csharp-throws.cs` | `KeyNotFoundException` | Compiles, then throws from inside the framework, with real line numbers |
| 17 | `Main17.java` | `cannot find symbol` | The commonest javac error |
| 18 | `Main18.java` | `NullPointerException` | Since Java 14 the message names the expression that was null, which makes it far more searchable |

## The one search can never answer

| # | Program | What happens | Why it is here |
|---|---|---|---|
| 21 | `21-typo-only-you-have.py` | `AttributeError: ... has no attribute 'heavey'. Did you mean: 'heavy'?` | Nobody has ever written an answer about `heavey`, because it exists in one file. Python worked it out anyway, and FixFinder applies what Python said - **with the network off** |

This is the only sample where Apply lights up every time, and the only one that needs no network.

## More than one error

These two are the reason the loop exists. A program only ever reports one error per run - the
first one ends it - so everything behind that error is invisible until it is gone. Fixing it and
running again is the only way to find out what is next, and doing that by hand means pressing the
same button four times and losing track of which change was which.

| # | Program | What happens | Why it is here |
|---|---|---|---|
| 19 | `19-three-errors.py` | `ModuleNotFoundError: 'yaml'`, then `'requests'`, then a `TypeError` | Three errors deep, and all three are the kind search is good at. Only the first is visible on the first run |
| 20 | `20-two-compiler-errors.c` | `error C2065` and `error C2143` from one build | A compiler reports everything at once, so both are printed - and fixing one still leaves a build that fails, which is the case the verifier has to tell apart from a patch that did nothing |

**Skip problem** steps past a flagged error to the next one the same run reported. It is live only
for a compiler, and sample 20 is why: a build reports everything it found and exits, so the second
diagnostic is genuinely there to look up while the first is still unfixed. A crashed program has
one error - the first one ended it - so the button is greyed out carrying that reason rather than
pretending. **Next result** is the separate one: it walks the other thirty-odd results found for
the *same* error, which is what to press when the top one is no use.

**Apply all** answers the prompts for you. It asks once, then works through as many errors as it
can: apply, rebuild, re-run, look up whatever comes next. It stops when the program runs cleanly,
when a change fails to help, after five rounds, or the moment an error it has already seen comes
back - because two patches undoing each other look like progress every single round.

The Java files are named `Main17`/`Main18` because **javac requires the file name to match the
public class** — rename them and they stop compiling for a reason that has nothing to do with
the bug being demonstrated.

### What you need installed

FixFinder looks on PATH *and* in the usual install locations, so none of these has to be on PATH:

| Language | Needs | Found automatically |
|---|---|---|
| C, C++ | Visual Studio / Build Tools, or gcc / clang | Visual Studio, via `vcvarsall.bat` |
| C# | the .NET SDK | yes |
| Java | any JDK | `Program Files\Java`, Adoptium, Microsoft, Corretto, Zulu |

All four are present on this machine: MSVC 18 Community, the .NET SDK, and Microsoft OpenJDK 21
at `C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot`, installed with `winget install Microsoft.OpenJDK.21`.

**Settings → "Languages it can run here"** lists what is present on this machine. If something is
missing, the tool names it and gives the command to install it — for Java,
`winget install Microsoft.OpenJDK.21`.

Number 10 needs building once:

```
dotnet build TestTargets\Samples\10-dotnet-format\DotNetFormat.csproj
```

then point FixFinder at `10-dotnet-format\bin\Debug\net8.0\DotNetFormat.dll`.

## What detection makes of each

Run without any network, so this is what FixFinder knows *before* it searches:

```
1-missing-module   Python   ModuleNotFoundError: No module named 'yaml'      "ModuleNotFoundError" "yaml" module named
2-inside-a-library Python   InvalidSchema: No connection adapters...         "requests.exceptions.InvalidSchema" ... (nearest library: requests)
3-none-attribute   Python   AttributeError: 'NoneType' has no 'get'          your own code
4-chained-failure  Python   RuntimeError -> KeyError                          "KeyError" "timeout"
5-deep-recursion   Python   RecursionError: maximum recursion depth          "RecursionError" maximum recursion exceeded depth
6-noisy-then-crash Python   KeyError: 'bytes'  (found among 4,007 lines)     "KeyError" "bytes"
7-runs-fine        —        nothing parses as an error                        no prompt
8-silent-failure   Generic  exit 3, confidence 20                             nothing reliable to search with
9-never-finishes   —        timed out, still running                          no prompt
10-dotnet-format   .NET     FormatException: '30s' was not in a correct...   "System.FormatException" "30s" correct input string format
```

## Five bugs these found

Worth recording, because they are the reason the samples exist.

1. **The Python parser only accepted exception types ending in `Error` or `Exception`.**
   `requests.exceptions.InvalidSchema` does not, so the type and message were dropped and the
   search query came out **empty** — failing hardest on exactly the third-party errors the tool
   is best at. Sample 2 found it.

2. **The source root resolved to `site-packages\requests`.** The innermost frame of a crash
   inside a library lives in the installed package, and rooting there would bound every file
   FixFinder may write to the inside of a dependency. It now refuses to root in vendored code.

3. **"The crash is in your own code" was shown for `ModuleNotFoundError`.** Technically true —
   the `import` line is in your file — and badly wrong as advice, since a missing module is one
   of the most answered questions there is. Dependency errors are now exempt from that warning.

4. **Every C and C++ build failed with `Command line error D8003: missing source filename`.**
   A quoted Windows path ending in a separator — `"C:\out\obj\"` — has a backslash immediately
   before the closing quote, which the command-line parser reads as an *escaped quote*. The
   argument swallowed the next token and `cl` never saw the source file, while the generated
   command looked perfectly correct. The build now runs from inside the output folder so the
   path is not needed at all.

5. **`.cs` files were handed to `dotnet` as though they were assemblies.** The shortcut that
   routes a `.dll` through the dotnet host fired for anything launched by `dotnet`, silently
   dropping the `run` verb. Caught by a unit test before it ever reached the window.
