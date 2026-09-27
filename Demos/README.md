# Demo programs

Thirteen broken programs across seven languages, and one that is not broken at all. Every one was
run through FixFinder before this file was written, and what it said is recorded below — if a demo
does not say this on the day, something has changed and it is worth knowing before you are standing
in front of people.

Each demo is a folder with two **separate** copies:

```
07-c-freed-then-used/
  broken/readings.c     <- show this one
  fixed/readings.c      <- show this one afterwards
```

They are separate folders on purpose. FixFinder checks a program together with the other files
beside it, so a fixed copy sitting next to the broken one would be pulled into the same check.

## Running order

The first three make the point on their own. If time is short, show **1, 6 and 14**.

| # | Language | What FixFinder says | Why it is worth showing |
|---|---|---|---|
| 1 | Python | `SyntaxError: expected ':'` — Error/Certain, line 9 | It rewrites **your own line** with the colon in place, not a general example |
| 2 | Python | The program printed the wrong output — Error/Possible, line 5 | Nothing is wrong with this code. It runs, it prints an answer, and the answer is wrong. **Type the expected output in first** (see below) |
| 3 | Python | `TypeError: 'NoneType' object is not subscriptable` — Error/Certain, line 17 | A search that finds nothing returns `None`, and the caller uses it straight away. The commonest real bug there is |
| 4 | Java | `NullPointerException`, naming `Roster.nameAt` — Error/Certain, line 14 | The null comes back from a **different method** than the one that crashes |
| 5 | Java | An update two threads can lose — Warning/Likely, line 7 | **Found without running it.** The program prints a plausible number every time; the bug is that the number is wrong and differently wrong each run |
| 6 | C# | Two findings on line 7: *asking for a position that does not exist* (Likely) and the crash itself (Certain) | The static check and the real run agree, from opposite directions — and the fix rewrites the loop with `<` |
| 7 | C | Use after free — Certain — plus the null check it never did, plus gcc's own warning | Four findings on one small program, from four different places |
| 8 | C | A total that starts from garbage — Warning/Likely, line 7 — and `lines` used on line 20 without checking what `malloc` gave back — Error/Possible | `int total;` in C holds whatever was in that memory. **This program prints the right answer on this machine** and is still wrong |
| 9 | C++ | Memory freed twice — Error/Certain, line 16 | The second `delete[]` is inside an `if`, so it does not happen every run |
| 10 | JavaScript | `Cannot read properties of undefined` — Error/Certain, line 6 | `basket[basket.length]` is off the end, and JavaScript hands back `undefined` rather than complaining |
| 11 | JavaScript | A condition that can never be true — Warning/Likely, line 9 | The `if` branch is dead code: the list is built and never filled |
| 12 | Go | `panic: assignment to entry in nil map` — Error/Certain, line 11 | The fix names `make(map[string]int)` and rewrites the line |
| 13 | Go | Dividing by something that can be zero — Error/Possible, line 12 | **The program runs perfectly.** `len(marks)` is 3 today; the finding is about the day it is 0 |
| 14 | Python | Nothing found | The one that proves the rest. A correct program, and FixFinder says so rather than inventing something |

## Demo 2 needs one extra step

It is the only one that needs anything typed. Before pressing the language button, put this in
**Expected output**:

```
Each winner gets 30
```

FixFinder then runs the program, sees `Each winner gets 40`, and searches for the change that
makes it print the right thing. It finds it: `winners - 1` becomes `winners`.

## What to say when it finds four things at once

Demo 7 reports the same line from four directions — FixFinder's own memory automaton, the real
run under the sanitiser, gcc's warning, and a separate null check. That is the point rather than
noise: none of the four is guessing, and each would have caught it alone.

## Two that are worth mentioning out loud

**Demo 5 and demo 13 never fail on stage.** Both programs run and print a sensible answer every
time. Testing them would find nothing. That is the difference between running a program and
reading it, and it is the strongest thing in the set.

## If Windows will not start a demo

On a PC with Smart App Control on, Windows can refuse to start a program FixFinder has just
built. On 27 September it refused demo 13's Go program, and FixFinder then also said the
program *finished unhappily, but printed no error* - Windows stopped it, not the code. When the
same happens to the AddressSanitizer build of a C or C++ demo, FixFinder says so in a note.
The findings that come from reading the code are the same either way.
