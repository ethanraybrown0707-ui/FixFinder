# FixFinder

[![tests](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml/badge.svg)](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml)
[![test count](https://img.shields.io/endpoint?url=https%3A%2F%2Fraw.githubusercontent.com%2Fethanraybrown0707-ui%2FFixFinder%2Fmain%2F.github%2Fbadges%2Ftests.json)](https://github.com/ethanraybrown0707-ui/FixFinder/actions/workflows/tests.yml)
[![Licence: MIT](https://img.shields.io/badge/licence-MIT-blue.svg)](LICENSE)
[![Stars](https://img.shields.io/github/stars/ethanraybrown0707-ui/FixFinder?style=flat&label=stars)](https://github.com/ethanraybrown0707-ui/FixFinder/stargazers)
[![languages](https://img.shields.io/static/v1?label=languages&message=C,%20C%2B%2B,%20C%23,%20Go,%20Java,%20JavaScript,%20Python,%20Ruby,%20Rust&color=blue)](#languages)

Run a program in any language, catch its crash, look for a published fix, and — with your
explicit approval — apply it and check whether it worked.

No local AI and no model calls. Everything here is deterministic: regex stack-trace parsers,
rule-based normalisation, API search, a scored ranking formula you can read, and a unified-diff
engine that applies patches with exact context or refuses.

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
2. **Press "Run it and find a fix"**, and confirm the command line it shows you.
   C, C++ and Java are compiled first — and if the build fails, the **compiler error** is what
   gets looked up, which is often a better search term than a runtime message: `C2065` and
   `CS0103` are globally unique and everybody who hits one pastes it verbatim into a search box.
3. **If it crashed and something was found, you get asked** - what went wrong, what was found,
   and whether to apply it. **Apply all** is the same thing without the asking: it applies, runs
   the program again, and looks up whatever error comes next, until it runs cleanly or a change
   fails to help.

Nothing else is asked for. Where the source lives, what to search for, which of forty results
to open, whether that result's patch fits your copy of the code - all of it is worked out, and
the parts that could not be are said out loud rather than turned into questions up front.

The prompt only appears when there is a real decision to make. A program that ran fine, or one
that crashed with nothing published about it, is reported in the window and never interrupts.

## Languages

Nine languages have a dedicated stack-trace parser; anything else falls back to a generic one
that harvests a file and line number, capped at low confidence so it can never outrank a real
parse.

| Language | Runtime errors | Compiler errors | Exercised end to end |
|---|---|---|---|
| Python | traceback, chained causes, 3.11+ carets | — | yes |
| C# / .NET | inner exceptions, async resume frames | `CS####` | yes |
| C | — *(a native crash on Windows prints nothing)* | `C####` via MSVC, or gcc/clang | yes |
| C++ | assertions, aborts | `C####` via MSVC, or gcc/clang | yes |
| Java | `Caused by` chains, `... N more` | javac `cannot find symbol` and friends | yes |
| JavaScript / Node | stack frames, `node:` internals | — | parser only |
| Go | two-line panic frames, goroutine blocks | — | parser only |
| Rust | modern and legacy panic formats | — | parser only |
| Ruby | Ruby 3.4 quoting and the older form | — | parser only |

**"Parser only"** means the parser is tested against captured output from that runtime, but the
runtime is not installed on the machine this was built on, so the full launch-and-catch loop has
not been run against it here. The parsing is the part that is hard; running a program that
already exists on your machine is not.

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
context, previewed, backed up, applied, and verified by re-running. A locally produced fix that
skipped any of those would be the one patch in the tool nobody had checked.

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
| Python 3.12+ | yes - `Did you mean: 'average'?` | **proven end to end**, applied and verified |
| gcc | yes - `'avarage' undeclared ... did you mean 'average'?` | pattern tested against captured output |
| clang | yes - `use of undeclared identifier 'avarage'; did you mean ...` | pattern tested against captured output |
| Ruby | yes - `Did you mean?  average`, on its own line | pattern tested against captured output |
| javac | **no** - `cannot find symbol`, nothing more | measured on this machine |
| C# / Roslyn | **no** - `'totl' does not exist in the current context` | measured on this machine |
| MSVC | **no** - `error C2065: 'avarage': undeclared identifier` | measured on this machine |
| Node, Go, Rust | not read yet | rustc carries machine-applicable suggestions in a different form |

The three that say no are not a gap in FixFinder: those compilers genuinely do not compute a
suggestion, so there is nothing to read. Checking was worth it - gcc and clang put the name on
*opposite sides* of the word `undeclared`, and Ruby writes a question mark where Python writes a
colon and puts the answer on the next line, so a pattern that looked like it covered all of them
covered one.

## When the fix is not a patch

A missing package is one of the commonest Python errors there is, and nothing in your code is
wrong when it happens - the environment is short of something. There is no file to edit, so
`FixTier.Dependency`, reserved from the start for exactly this shape of answer, carries a command
instead of a diff and the prompt offers **Install it** rather than Apply.

```
ModuleNotFoundError: No module named 'yaml'
  -> "C:\...\python.EXE" -m pip install pyyaml
```

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
- **The package name arrives from untrusted output.** A program can print anything on stderr,
  including a line shaped exactly like a `ModuleNotFoundError`, so the name is validated as a
  plain Python identifier before it goes anywhere near a command line, passed as its own argument
  rather than through a shell, and shown in full for confirmation. Anything carrying a switch, a
  path, a URL or a shell separator is refused outright rather than cleaned up.

Running it is never part of **Apply all**: fetching and executing code from the network is a
bigger step than editing a file, and it is not one a single button should start. The program is
re-run afterwards either way, so an install that did not help is reported like any other change
that did not help.

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
| **A — auto-appliable** | Unified diffs found in GitHub issue/PR bodies, and linked commits fetched as `.patch` | Previewed, then applied on your confirmation, then verified by re-running |
| **B — advisory** | Everything else, including every Stack Overflow answer | Shown with the relevant code block. Never written to disk |

Three things are worth knowing before you use it:

- **Tier A is usually empty, and that is the tool working correctly.** A diff from a stranger's
  repo, at a different version, mapped onto your source tree and applying with exact context is
  a rare alignment. `0 auto-appliable, 6 advisory` is the normal result.
- **Web search only helps for third-party errors.** The most common real crash is a
  `NullReferenceException` in your own method, and no issue or answer exists for that. FixFinder
  detects when the culprit frame is your own code and says so instead of listing 30 irrelevant
  links.
- **A patch harvested from an issue comment is untrusted input.** Every file a patch touches
  must resolve inside the source root you picked, and must appear in the parsed stack trace,
  or it is refused.

## Consent

Two gates, and both are about running a program rather than about writing to one:

1. **`run-fixfinder.cmd`** asks you to type YES before the window opens, and the window itself
   opens with a blocking notice naming what the tool does: it runs a program you choose, and it
   sends your error text to github.com and api.stackexchange.com. Cancel shuts it down.
2. **Before anything is launched**, a confirmation names the exact command line and working
   directory. A compiled language runs two commands and the confirmation names both, because
   showing only the second would be describing something other than what is about to happen.

There used to be a third - a preview, a dry-run tick and the word APPLY typed in full - guarding
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

`run-fixfinder.cmd` always works, because it launches the DLL through `dotnet.exe`, which is
already trusted. Use it when the exe is refused.

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

- [x] **M0** — projects, WDAC signing, consent chain, steps 1–3 laid out
- [x] **M1** — launch a target and watch its stdout/stderr live
- [x] **M2** — stack-trace parsers, fingerprinting, source-root detection
- [x] **M3** — GitHub and Stack Overflow search, caching, quota, token storage
- [x] **M4** — ranking and the candidate list *(the point at which it is genuinely useful)*
- [x] **M5** — diff parsing and read-only patch preview
- [x] **M6** — patch application, backups, typed-`APPLY` gate
- [x] **M7** — build, re-run, verify, auto-rollback

## Licence

MIT. See [LICENSE](LICENSE).
