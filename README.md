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

Three gates, outermost first:

1. A blocking prompt on startup naming all three capabilities (runs a program you pick · sends
   your error text to github.com and api.stackexchange.com · can modify source files under a
   folder it works out).
2. A confirmation naming the exact command line and working directory, before anything launches.
3. A typed `APPLY` before any file is written — with **dry-run on by default**, so the first
   attempt is a preview even if you click through everything else.

(`run-fixfinder.cmd`, which runs the Debug build, adds a typed `YES` in front of all three.)

Simplifying the window did not touch any of these. The one that was tempting to collapse is the
third: the prompt says what was *found*, and the preview that opens from it is where you see
what would actually be *done* - which files, resolved to which paths on your disk, and where the
backup goes. Those are different questions and they get different answers.

Backups are written before every patch and are never deleted automatically.

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

## Applying a patch

The only part of FixFinder that writes to disk, and the rules are deliberately unforgiving.

**Where it may write.** Every path in a patch is resolved with `Path.GetFullPath` and must then
sit beneath the resolved source root. That is a structural check on a real path, not a scan of
the patch text for `..` — the text scan is the version that misses absolute paths, alternate
separators and encoding tricks. A diff naming
`a/../../../../Windows/System32/drivers/etc/hosts` is refused and writes nothing.

**Which file it means.** `src/cart/basket.py` may live at `app/cart/basket.py` here, so
progressively shorter path suffixes are matched against an index of the source root. Two files
that match equally well are reported as ambiguous and **never guessed between** — only a file
named in the stack trace breaks a tie.

**Exact context, zero fuzz.** Each hunk's context and removed lines must be found byte for byte
within 200 lines of where the patch says. No whitespace-insensitive mode, no partial-context
fallback, no `patch --fuzz` equivalent. Fuzz is how a patch tool silently corrupts a file, and
there is nothing downstream here that would notice. A hunk that fits in two places is refused
for the same reason: two equally good answers means the right one is not knowable.

**Relevance, not just fit.** A patch whose files never appear in the crash is refused even when
it applies perfectly. Context matching cannot tell a relevant patch from an irrelevant one that
happens to fit.

**All or nothing.** Every hunk in every file is located before a single byte is written. A
half-applied cross-file patch compiles about as often as an unpatched tree and matches neither
the original nor the fix.

**Preserved as found.** Line endings and the byte-order mark come from the file, never from the
patch — otherwise a one-line fix to a CRLF file arrives as a whole-file diff for the next person
who reads it.

Refused as unsafe: read-only files, anything over 2 MB, files with NUL bytes, and any patch that
deletes a file.

### Patching a library, when the fix belongs to one

The commonest published fix in existence is a fix to a library, and it changes that library's own
files. Those files are on your disk - in `site-packages`, `node_modules` - and they are not in
your project, so for a long time every one of them resolved to *"no file called adapters.py
exists anywhere under the source root"*. True, and useless: the patch was real, relevant, and
landed nowhere FixFinder was allowed to write.

It can now write there, and only there:

- **The project is always tried first.** A patch that fits your own code is never diverted into a
  dependency.
- **The root is the package, not the packages folder.** `site-packages/requests`, never
  `site-packages` - so refusing to write outside the root is refusing to touch anything but the
  library named in the crash. A scoped npm package roots at `node_modules/@scope/thing` for the
  same reason.
- **Each ecosystem gets its own rule, because depth is not one thing.** `site-packages/requests`
  and `node_modules/express` are one level down; `node_modules/@scope/thing` is two; a Cargo crate
  is always `registry/src/<index>/<crate>-<version>`; and a Go module is identified by the
  `@version` on its directory, since a module path is three segments for `github.com/pkg/errors`
  and two for `gopkg.in/yaml.v2`. A shared guess at depth is wrong for at least one of them.
- **Vendored Go reads `vendor/modules.txt`** rather than guessing, which is what makes that folder
  safe to root in at all. Composer's `vendor/` has no equivalent here, so it is still left alone.
- **Go's module cache is read-only on purpose**, so a patch there is refused - and says so, naming
  `go mod vendor`, instead of stopping at "the file is read-only". Cargo re-extracts a crate whose
  checksum stops matching, so that warns about `cargo vendor`. Those are the ecosystems telling you
  their cache is not the place to edit, and the warning passes the message on rather than fighting
  it.
- **The runtime is never a package.** No issue asks you to hand-edit the standard library.
- **The confirmation names the library.** You type `APPLY TO REQUESTS`, not `APPLY`, because
  "APPLY" typed for the hundredth time is a reflex and this one is not the usual thing.
- **Apply all is withheld.** Working unattended through a machine's installed packages is a
  different proposition from working through one project.

Worth being plain about the limits. The patch is written against the library's latest code and you
have a released version, so exact-context matching will often still refuse - this turns *never*
into *sometimes*, not into *usually*. Every program on the machine that imports the library gets
the change, and the next install of that package overwrites it. Upgrading to a release that
already contains the fix is the durable version of the same thing.

### Backups and verification

Every file is copied aside before it is touched, with a SHA-256 recorded per file and a
`manifest.json` naming the candidate responsible. Restoring re-checks every hash, so a backup
that was itself damaged is refused rather than written over working code. **Backups are never
deleted automatically.**

After applying, the build command runs and the target is re-run. The verdict compares error
*fingerprints*, not output text — a patch moves line numbers, so anything comparing raw text
would call every applied patch a different error:

| Verdict | What happens | And the loop |
|---|---|---|
| `Fixed` | Kept | Finished |
| `SameErrorPersists` | **Rolled back automatically** | Stops |
| `BuildFailed` | **Rolled back automatically**, without even re-running | Stops |
| `DifferentError` | **Kept** - fixing the first of two bugs looks exactly like this | **Goes round again** |
| `Inconclusive` | Kept, and says why it proved nothing | Stops rather than stack a change on one it cannot vouch for |

Re-running only proves anything when the crash reproduces from the same invocation with no
interaction. Input-, timing-, network- and click-dependent failures cannot be verified this way,
and neither can a server that was still running happily when the timeout stopped it. Those are
`Inconclusive` rather than a verdict that would read as a guarantee. For a compiled language
with no build command set, the re-run would run the binary from *before* the patch, so that is
`Inconclusive` too rather than a wrong answer.

### More than one error

A program reports one error per run, because the first one ends it. Everything behind it is
invisible until it is gone, so a tool that runs a program once is telling you about a fraction of
the problem and has no way to know that.

`DifferentError` is what makes the rest reachable. It already meant "the original error is gone
and another one has appeared", and it was already never rolled back, because that is what fixing
the first of two bugs looks like. So it is also the signal to go round again: search the new
error, offer its fix, apply, rebuild, re-run. Every other verdict ends the run.

The re-run the verifier has already done *is* the next round's run. Launching a third time would
be slower and less honest - a fresh run can fail differently, and the search would then be about
an error nobody was shown.

**Skip** exists for the problems nothing can fix. Two buttons, because they answer different
questions: **Next result** walks the thirty-odd other results found for the same error, and
**Skip problem** leaves the error entirely for the next one the run reported. One button doing
both would put "move past this" thirty-seven clicks away.

Skipping the problem is live only for compiler output, and the reason is not a limitation to work
around. A compiler reports everything it found and exits, so the second diagnostic is really there
to be looked up; a program that crashed has exactly one error, because the first one ended it -
whatever would have failed next has not happened yet and no parser could find it. Where there is
nowhere to go the button is disabled carrying that sentence, rather than sitting dim.

**Apply all** asks once and then stops asking. It is the same code path as pressing Apply each
time, with the same score floor, the same containment, the same exact-context matching, the same
backup per round and the same automatic rollback; what it drops is the typed confirmation per
round, which is why the one it does ask for is worded as covering the whole sequence.

Two things stop it running away:

- **Five rounds**, then it reports where it got to and leaves the rest to another run. A tool
  that edits source should not keep doing so indefinitely while nobody is watching.
- **An error it has already seen this run** ends it immediately, as does a candidate it has
  already applied. Two patches that undo each other produce a different error every round and
  would otherwise look like progress all the way to the limit.

A round that turns up only advice brings the prompt back rather than closing on a result nobody
saw - "all" cannot apply prose, so there is nothing for it to do quietly.

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
