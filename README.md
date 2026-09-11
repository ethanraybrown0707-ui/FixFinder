# FixFinder

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
   and whether to apply it.

Nothing else is asked for. Where the source lives, what to search for, which of forty results
to open, whether that result's patch fits your copy of the code - all of it is worked out, and
the parts that could not be are said out loud rather than turned into questions up front.

The prompt only appears when there is a real decision to make. A program that ran fine, or one
that crashed with nothing published about it, is reported in the window and never interrupts.

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

Four gates, outermost first:

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

**On this machine it launches and runs.** That is worth stating because it is not guaranteed:
the exe is signed with a local self-signed certificate, which satisfies the Application
Control policy, but Smart App Control judges an executable by *reputation* rather than by
signature and a self-signed build has none. If a future build is ever blocked,
`run-fixfinder.cmd` still works — it launches the DLL through `dotnet.exe`, which is already
trusted.

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

The published single-file exe is a different case and was measured rather than assumed: signed
with the same certificate, it launches and runs here. Smart App Control did not refuse it.

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

### Backups and verification

Every file is copied aside before it is touched, with a SHA-256 recorded per file and a
`manifest.json` naming the candidate responsible. Restoring re-checks every hash, so a backup
that was itself damaged is refused rather than written over working code. **Backups are never
deleted automatically.**

After applying, the build command runs and the target is re-run. The verdict compares error
*fingerprints*, not output text — a patch moves line numbers, so anything comparing raw text
would call every applied patch a different error:

| Verdict | What happens |
|---|---|
| `Fixed` | Kept |
| `SameErrorPersists` | **Rolled back automatically** |
| `BuildFailed` | **Rolled back automatically**, without even re-running |
| `DifferentError` | **Kept** — fixing the first of two bugs looks exactly like this |
| `Inconclusive` | Kept, and says why it proved nothing |

Re-running only proves anything when the crash reproduces from the same invocation with no
interaction. Input-, timing-, network- and click-dependent failures cannot be verified this way,
and neither can a server that was still running happily when the timeout stopped it. Those are
`Inconclusive` rather than a verdict that would read as a guarantee. For a compiled language
with no build command set, the re-run would run the binary from *before* the patch, so that is
`Inconclusive` too rather than a wrong answer.

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
