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

1. **Choose a program.** Drop the file on the window, or press **File…** and pick it. When the file is hard to find, press
   **Paste code** and paste the program's code into the box; **Folder…** checks every program in a folder.
2. **Press the language it is written in** - Python, Java, C#, C, C++, JavaScript or Go, or **Auto-detect**. That starts
   both checks at once. The program is compiled if it needs to be, and run. For pasted code, **Auto-detect** works the
   language out from what only that language writes - `System.out.println`, `#include <iostream>`, a `def` line ending
   in `:` - and when the code shows no language more than another, asks for it rather than guessing.
3. **Optionally, say what it should print.** Arguments, the input to type and the expected output go in the boxes under
   the program, and **+ Add another run** adds more. With them, a program that runs but prints the wrong thing is caught
   too, and the change that makes it right is searched for.
4. **Read the report.** It has two tabs, laid out the same way. **Problems** lists what is wrong, and its filters show
   every problem, or only the errors, warnings or suggestions. **Efficiency** lists ways the program could do less work
   as its data grows - none of them is a mistake. The **Explanations** slider sets how in depth each finding is
   explained: **Beginner** in plain words, with the idea behind the mistake spelled out; **Student** as it is usually
   taught; **Technical** in the language's own terms. Only the wording changes - what was found, how sure FixFinder is
   and the fix stay the same. Each finding has **Copy corrected code**, **Search online** for the error on GitHub and
   Stack Overflow, and **Show in folder**. **Copy report** copies every finding as plain text.
5. **Optionally, tick Check on save.** FixFinder then reads the code again every time the program is saved - the logic
   patterns and every analysis - and updates the report. It does not compile or run the program, and the report says so;
   press the language to do that. Code that does not read as its language at all is noted rather than reported as having
   no mistakes.

Pasted code is saved exactly as pasted, as a file of its own in a folder of its own in the temp folder - a Java class as
the file of its name, under the folders of its `package` - and checked like any program, so the lines the report names are
the pasted lines. It is checked on its own: another file of its program that it imports, or a file it reads, is not there
with it, and a note says so. Check on save watches a file chosen from disk; for pasted code, press the language again to
check it after a change. The saved copy is removed when the window closes.

A program in more than one file is checked as the whole program: Python imports and JavaScript `require`s are followed,
Java is compiled from its source root, C# from its project, Go as its package, and C and C++ with the other files and
headers beside them. A Java folder of exercises, each with its own `main`, holds several programs: one the chosen file's
code does not reach is another program, and neither it nor what only it uses is read as part of the one checked.

A program with a window - one written with JavaFX or Swing, in the file chosen or one it names, or in Python with tkinter,
turtle, pygame, PyQt or PySide, wxPython, Kivy, or matplotlib's `show()` - runs until its window is closed, and a server -
on a `ServerSocket` or Java's `HttpServer`, a Spring Boot application with a web server such as Tomcat among its libraries,
or in Python with `http.server`, `socketserver`, a socket that listens, Flask's or uvicorn's `run`, aiohttp's `run_app` or
asyncio's `start_server` - runs until it is stopped, so either still running when its time runs out is said in a note, not
reported as a program that never finishes; what it does when someone uses the window, or something connects, is not
checked. For matplotlib, a socket and the servers after it, importing one is not enough - the program has to make the call
that waits - so a program that only saves a chart to a file, or talks to a server, is not taken for one.

A program runs from the folder its own files are looked for in, so one that opens `scores.txt` finds it. Java starts from
the project's folder - the one holding `src`, or `src/main/java` - as an IDE or a build tool starts it; C and C++ start
beside their source, as a program built at a command line does; the rest start where the program is. When a file the
program names in quotes is not there but is in another folder it could have been started from, it starts there instead.
The changes FixFinder tries are made and run in a copy of the program's folder, files and all, so
a copy reads what the original would; a folder holding more than a program's worth of files is not copied whole.

A Python program runs with **its project's own Python** when it has one, as its IDE runs it, so a package installed only
there is found. FixFinder looks in the program's folder and each one above it, up to the folder that marks the project - one
with a `.git`, `.idea`, `.vscode`, `pyproject.toml`, `setup.py`, `setup.cfg`, `requirements.txt`, `Pipfile` or
`environment.yml` - and takes the first of these it finds:

- the interpreter the project's VS Code settings name (`python.defaultInterpreterPath`, or the older `python.pythonPath`),
  or its PyCharm settings name: `.idea\misc.xml` gives the interpreter's name, and PyCharm's own list of interpreters,
  `jdk.table.xml`, says where it is;
- a virtual environment in the folder - `.venv`, `venv`, `env`, `.env` or `virtualenv`, holding its `pyvenv.cfg`;
- the **conda** environment the project's `environment.yml` names: by its `prefix:`, as `conda env export` writes it, or
  by its `name:` - looked for in conda's own list of the environments it made (`.conda\environments.txt`), in the folders
  `CONDA_ENVS_DIRS` (or the older `CONDA_ENVS_PATH`) names, in `.conda\envs`, and in the `envs` folder of an Anaconda,
  Miniconda, Miniforge or Mambaforge installation in the home folder, local application data or ProgramData. A folder is
  taken for an environment only if it holds conda's `conda-meta` record, so what a removal left behind is not;
- the environment **Poetry** made for a project it manages - one with `[tool.poetry]` in its `pyproject.toml`, or a
  `poetry.lock` beside it - under the name Poetry gives it, the project's name and a hash of its folder, worked out as
  Poetry works it out. It is looked for only where Poetry keeps environments: the folder `POETRY_VIRTUALENVS_PATH`, the
  project's `poetry.toml` or Poetry's `config.toml` names - read in that order, as Poetry reads a setting - and otherwise
  the `virtualenvs` folder of Poetry's cache. When Poetry made one for each of two Pythons, the one its `envs.toml` records
  as in use is taken, and neither when it records none;
- the environment **Pipenv** made for a project with a `Pipfile`: the one in `WORKON_HOME`, or in `.virtualenvs` in the
  home folder, whose `.project` file names the project's folder.

A notebook saved with a **Jupyter kernel** of its own - one made with `python -m ipykernel install --user --name ...` - runs
with that kernel's Python, found where Jupyter finds kernels: the folders `JUPYTER_PATH` names, then this user's Jupyter
folder (or `JUPYTER_DATA_DIR` in place of it), and ProgramData's only when `JUPYTER_USE_PROGRAMDATA` says to trust it, as
Jupyter does. The kernel every Python brings is called `python3` whichever Python it is, so a notebook saved with that one
has its project's own Python looked for as above. A tool run by the Microsoft Store's Python writes into that Python's own
copy of the application data folders, so Poetry's environments and Jupyter's kernels are looked for there too.

An environment whose Python has since been uninstalled is passed over, and one a project names that is not on this
computer is not guessed at: the Python on PATH is used instead. How the program is run says which Python it is, and the
copies changes are tried in run with the same one.

A Java program is built with **a JDK of the Java it is written for**, up to Java 27. FixFinder finds every JDK on the
computer - the one whose `javac` is on PATH, the one `JAVA_HOME` names, those Oracle's installer lists in the registry,
those Oracle's, Eclipse Temurin's, Microsoft's, Amazon Corretto's, Azul Zulu's, BellSoft's, IBM Semeru's, SAP's and Red
Hat's installers put in Program Files, those IntelliJ and Gradle download, Scoop's, and the Java an Eclipse brings with it
- and knows each by the version in its own `release` file; Settings lists them. Unless something asks for another, a
program is built with the JDK whose `javac` a terminal would run, and `javac`, `java` and `javap` always come from the one
JDK. The Java it is compiled for is the one chosen in Settings, else the one its project names: `pom.xml`'s
`maven.compiler.release`, or its source and target - as properties or as maven-compiler-plugin's own settings, from the
`pom.xml` or a parent - or Spring Boot's `java.version`; a Gradle build's toolchain, `options.release` or
`sourceCompatibility`, in the project's build file, the convention plugins it applies or the `subprojects { }` above it;
IntelliJ's language level; or Eclipse's compliance level. Preview features are on when the build turns them on. The JDK
is one of at least that Java: the one the project's IntelliJ or VS Code settings, or its Gradle toolchain, choose, when it
is here; else the usual one, when it is new enough; else the oldest that is. The code itself can need a later Java - a
compact source file, with no class around its methods, and `import module` need Java 25, an unnamed `_` Java 22 - or an
earlier one: an applet needs Java 25 or earlier, as Java 26 took the Applet API out, and a Lombok works only with the Java
releases it supports, as Lombok's changelog records them. A project that names a later Java than any JDK here has is not
built, and the report says which to install; code that needs a JDK that is not here is built with the nearest, and the run
explanation says what is missing. When javac says a feature is only a preview in the JDK that built it, or is newer than the
Java it is compiled for, or that a library was built for a later Java, a note says which Java it needs and whether a JDK of
it is on this computer.

A Java program is built and run with the **libraries** its project names, found as its own tools find them: a Maven
project's `pom.xml`, with its parents, its properties, the versions its dependencyManagement and imported BOMs set, and
each library's own dependencies, the nearest declaration winning as in Maven; a Gradle build file's `implementation` and
`testImplementation` lines, its `platform()` BOMs and version catalog, the highest version winning as in Gradle; the
libraries IntelliJ's `.idea` and `.iml` files, Eclipse's `.classpath` and VS Code's `java.project.referencedLibraries`
record; and jars kept in a `lib`, `libs` or `jars` folder. The JUnit that comes with Eclipse, which a `.classpath` takes
as its JUnit 4, 5 or 6 container, is taken from an Eclipse on this computer: the jars Eclipse makes that container of, at
the versions Eclipse allows, from among the bundles an Eclipse the installer put in the `eclipse` folder of the home folder
uses, or from the installer's shared pool of bundles in `.p2`. Each is looked for among what Maven and Gradle have already
downloaded to this computer - FixFinder never downloads anything, and does not run Maven or Gradle. A library that is
named but not here, or not named anywhere, is said once in a note, saying which it is and where FixFinder looked, and the
errors javac gives because of it are not reported as mistakes in the code - nor are uses of what such a library would
have written into the program's own classes, such as the getters Lombok adds to a class marked `@Data`, which cannot be
judged until the library is there. A package a letter or two from Java's own or the program's, such as `java.utils`, is
a typing mistake, and still is one; so is a one-word package that nothing imports, such as the `Sytem` javac reports for
`Sytem.out.println`.

**Annotation processors**, such as Lombok's and MapStruct's, are run as the build runs them: those a `pom.xml` gives
maven-compiler-plugin in `annotationProcessorPaths`, or a Gradle build gives `annotationProcessor`, and only those; and when
the build names none, any that the program's libraries hold, as javac did by default until JDK 23. As javac runs a
processor only on the files it is given by name, every file of the program is then named, as a build names them - its
tests' own only when the file checked is one of them. A **JavaFX** program is run with JavaFX's modules on the module
path, as JavaFX's documentation runs one, since java before Java 27 will not start a JavaFX application from the class
path - Java 27's starts one the JavaFX way only when JavaFX is among the modules it started with; with none to give it,
and a java that will not start it, a note says so rather than a finding. A `pom.xml`'s profiles are read as Maven would switch them on for this
computer, which is how JavaFX's own `pom.xml` picks the jars for Windows. A Gradle build that applies the JavaFX plugin,
`org.openjfx.javafxplugin`, has the modules its `javafx { }` block names, with those they need as the plugin's own list
has them, each as the jar of it for this computer that Gradle downloaded - or from the `lib` folder of the JavaFX SDK the
block names with `sdk`; a block whose version or modules FixFinder cannot read is named as not read. A program that stops
for want of a class, or of a database driver, while libraries its build names are not on this computer, is reported as
possibly that rather than as a mistake in the code. A class from a library the build does not name at all gets the block
to add to `pom.xml`, or the line to add to `build.gradle`.

A program written as a **module** - with a `module-info.java` at the top of its source - is built as Maven builds one: the
jars of the modules it `requires`, and of those they require in turn, go on the module path, and the rest on the class
path, so a module is neither told it cannot find what it requires nor let off using a library it does not require. Which
jar is which module is read as java reads it: from the jar's `module-info.class`, its manifest's `Automatic-Module-Name`,
or its file's name. A module's tests in `src/test/java`, and FixFinder's own JUnit launcher, are compiled patched into the
module, reading the class path, as a build compiles a module's tests; so is the copy a change is checked in. The program
is then run from the class path, as before.

A Gradle build of **several projects** is read as Gradle lays it out. Its `settings.gradle` - or `settings.gradle.kts` -
names the projects it includes, each in a folder of its own: `:libs:core` in `libs\core`, unless the settings move it with
`projectDir`. A project that uses another, with `implementation project(':core')`, is built with that project's source and
resources and the libraries it declares for its own code, and so on through the projects that one uses; what a project
uses only for its own tests stays with it. Each project is read with what the build gives it besides: the version catalog
and `gradle.properties` in the build's top folder, the values set there with `ext`, the dependencies the build file above it
puts in `allprojects { }`, `subprojects { }` or `project(':app') { }`, and the build's own convention plugins it applies,
written as `.gradle` files in `buildSrc` or in a build the settings include. A project named that the settings do not
include, or whose folder is not there, is said to be so, with why, rather than its classes taken for a missing library's;
the lines of a block that picks its projects as Gradle runs, such as `configure(subprojects.findAll { ... })`, are named as
not read. The projects' code is compiled together, from source, so a program that is itself a module and requires another
project's module cannot be built this way: a note says so, rather than javac's error being reported as a mistake.

A class of **JUnit** 4 or 5 tests is run with JUnit itself, through a small launcher of FixFinder's compiled beside it,
when JUnit is among the project's libraries; JUnit 5's launcher, which a Maven or Gradle project seldom names, is taken
from what Maven or Gradle has downloaded, when it is there. A test that fails is an error on the line of the test it
failed on, in JUnit's own words, and names the line of the program's code the failure was thrown from when it came from
there. When JUnit cannot run the tests, a note says what it is missing.

A Python file of **unittest** tests - one that imports unittest and has a class deriving `TestCase` - is run with unittest
itself, test by test, through a small launcher of FixFinder's, whether or not the file calls `unittest.main()`. A test that
fails is an error on the line of the test it failed on, in unittest's own words; a subtest that fails is named with what it
was run with, such as `test_shares (people=4)`, and an error raised in the program's own code names the function, line and
file it was raised in. When setting up for a class's or a module's tests fails - in `setUpClass`, say - that is reported as
what it is, and the summary says those tests did not run; when cleaning up after them fails, in `tearDownClass`, the tests
ran, and that is said apart from them. The summary says how many of the tests failed.

A file of **pytest** tests - one that imports pytest, or a `test_*.py` or `*_test.py` file of top-level `test_` functions -
is run with pytest itself, through a launcher of FixFinder's, from the Python the project runs with, so the project's own
`pytest.ini`, `pyproject.toml`, `tox.ini` or `setup.cfg` settings and its `conftest.py` fixtures are pytest's to read as usual. Each
test that fails is an error on its own line, in pytest's own words - `assert 25.0 == 20`, with pytest's
`where 25.0 = share_of(4)` - and each set of parameters is a test of its own, as pytest counts them: `test_shares[4-20]`. A
fixture that fails is reported as setting up that test, on the fixture's line, and the test as not run; the rest of a
fixture after its `yield` failing is reported as cleaning up after a test that ran. A test marked `xfail` that fails as
expected is not a failure, as pytest does not count it one. A file pytest cannot import is reported as the error that stops
it, as any program is; a file it skips whole, as `pytest.importorskip` does, is said to have had none of its tests run;
when a test stops pytest partway, with `pytest.exit()`, the summary says how many tests ran before it; and when pytest will
not start - an option in the project's settings from a plugin that is not installed, say - a note quotes what pytest said. pytest's cache goes in a folder of its own, removed afterwards, rather than into the project, and no
compiled files are written beside the code. pytest is not part of Python, so when it is not installed for the Python the
project runs with, a note says so, and the file is run as a program instead - FixFinder never installs it.

A **Jupyter notebook** (`.ipynb`) is checked as Jupyter's Run All runs it: its code cells in order, as one program, from the
notebook's folder - so its own modules and data files are found - and with its kernel's Python, or its project's own, when
it has one. IPython's commands are made plain Python where that can be done without touching anything outside the run:
`%cd` changes the folder, `%env` sets a variable, `%run helpers.py` runs the script and keeps what it defines, and `%time`,
`%timeit`, `%%time` and `%%timeit` keep the code they time, running it once. Those that only show something or install a
package do nothing: `%matplotlib inline`, and `!pip install`, which install nothing. The rest - a shell command such as
`!wget`, a cell in another language such as `%%bash`, the file `%%writefile` would write - are not carried out, and a note
names each one and where it is, since the cells after it run without what it would have done. A command written without
its `%` - `pip install pandas`, `ls`, `cd data`, `time total = sum(marks)` - is read as IPython reads it, as the command,
unless the notebook gives a name of that name a value itself, as `run = 2` does.

Code that uses `await` - or `async for` or `async with` - outside a function, as a cell may, runs as Jupyter runs it:
compiled with Python's own flag for that, and run on an event loop through a small runner of FixFinder's, where plain
Python would refuse the whole file. matplotlib's plots are made without opening a window, as Jupyter makes them, so the
cells after a `show()` run; plotly's `show()` shows nothing, rather than opening a browser and waiting for it; `display()`
prints what it is given when IPython is not installed. Code written for Google Colab - a notebook or a program that
imports `google.colab`, which is only on Colab's own machines - is reported as that, with what to change to run it
elsewhere, rather than as a package to install.

Everything found is said as the notebook is read: the cell, counted from the notebook's top with Markdown cells included,
and the line within it. That is not the number Jupyter shows beside a cell that has run - the order the cells were run
in - and a note says so. What the program printed is shown as it was printed, except that a place in the one script the
cells were run as - in a traceback, or a warning - is given as the cell, counted the same way, and the line in it. A line
an explanation names in another cell is named with its cell, and an expression that ends a cell is not reported as a
value thrown away, since Jupyter shows it under the cell. A notebook of another language, such as R, is said to be one
and is not run. The script and the copies changes are tried in are made in the temp folder; the notebook itself is never
changed.

## What each finding tells you

| | |
|---|---|
| **Severity** | **Error** - the program fails, or gives the wrong answer. **Warning** - it works, but not reliably, or not as intended. **Suggestion** - it works; this is a better way. |
| **Confidence** | **Certain** - the compiler or a run proved it, or the code cannot mean anything else. **Likely** - true for nearly every program written this way. **Possible** - worth a look; it depends on what the program is for. |
| **Line** | The file and line it is on - for a notebook, the cell and the line in it. |
| **Explanation** | What is wrong, in the program's own names - explained at the depth the **Explanations** slider is set to. Every kind of mistake FixFinder knows is written three ways: for a beginner, with the idea behind it spelled out in plain words; as it is usually taught; and in the language's own terms, saying which version of the language changed the rule where one did. Whatever the depth, the finding starts with what was found in this program. |
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

What a program prints is its own output, not a crash. A run is reported as failing only when the language's runtime says
it failed - a traceback, an uncaught exception, a panic, the java launcher unable to start it - or when it ends with a code
only a crash gives. A program that ends itself with a failing exit code - `System.exit(1)` after printing how to run
it, say - is reported as possibly wrong, quoting the last thing it printed, since that may be just what it should do
without an argument or a file. An exception printed on the way - `printStackTrace` in a `catch` - is a warning when the
program still finishes. A Java class with no main method has nothing to run, and a note says so. When Windows refuses to
start a program that has just been built, and says so, a note says that too rather than blaming the code. A program that
stops because a connection it made was refused - its database or server not running - is reported as that, in any
language, from what its runtime said: the address, when the error names one, and what usually listens on that port, such
as PostgreSQL on 5432 - not as a mistake in the code. Java's HTTP client gives no reason with its ConnectException, so for
it that is said to be likely rather than certain.

For an error whose message pins the answer down - a missing import, a misspelt name, a semicolon, a loop one step too
long - a **fix rule** works out the change from the code. When the language names the answer itself - Python's `Did you
mean: 'print'?`, gcc's and clang's `did you mean` - that is the change, made on the line it names and credited to Python,
or to the compiler, rather than to a rule of FixFinder's. Every fix is made in a copy and checked by the compiler or interpreter, and
only offered if that passes. The copy is then run - built first, for C, C++ and Java - and the fix is said to be verified
only when the copy ran without the failure; a copy that could not be built or started is said to be that, never taken as
a pass.

The **logic check** reads the code for mistakes that compile and then give the wrong answer: `answer == "yes" or "y"`,
`total = 0` inside the loop that adds to it, `Console.Read()` used as a number, removing items while counting up through a
list. When an expected output was given, it also runs changed copies of the program to find the one that prints it:

1. Each run is repeated with coverage on, and every line is scored by how often the wrong runs reached it compared with
   the right ones (the Ochiai formula, with DStar breaking ties).
2. On the most suspicious lines, the small edits behind most logic bugs are generated: `<` for `<=`, a bound one out, the
   wrong operator, integer division, `min` for `max`, an `if` / `elif` chain in the wrong order.
3. Each edit is made in a private copy, built and run with every input. The first that prints exactly what was expected
   for every run is the answer. More runs, especially ones that go wrong in different ways, make the answer better.

A run that stops with an error, or is still going when the time runs out, is compared as far as it got and said to have
stopped - never to have run to the end - and an edit only counts if the program then finishes.

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

A loop whose test holds the first time it is made - `i = 0` against `i < 4`, or `range(4)` - goes round at least once, so
what its body certainly does is certain after it: a list it adds to is not empty, and dividing by its length is safe.

The checks look across functions. A call to one of the program's own functions is matched to it, so the call can be
checked against the arguments the function takes, the type hints it gives and the guards it starts with - its
**contract**. A guard that ends the whole program instead of raising - `sys.exit`, `System.exit`, C's `exit` - is where the
program stops rather than a contract, so a call that reaches it is not reported as breaking one. What a function returns
is worked out once and used at every call, which is how a function that always returns None is caught where its result
is used. A function that divides by what it is given - `sum(values) / len(values)` - is followed from every call the program
makes to it, with that call's own arguments: when none of them can make the divisor zero, the finding stays, since the
function still does not check what it is given, but as a warning that says every call gives it what keeps this from
happening. When one call can, nothing calls the function, the function is handed around as a value, or it is called from
more than twenty places, the finding is reported as it was found. What a method returns is never assumed, because a subclass can
replace it. A variable's declared type also sets its range, so `b < 0` for a C# `byte` can never be true, and a number
kept in a `double` divides into infinity rather than failing, since dividing a double by zero is no error.

**Across files**, a program is checked as one. A call is matched to the function it runs wherever that is written:

| Language | Followed through |
|---|---|
| Python | `import helpers` then `helpers.total()`; `from helpers import total as sum`; relative imports inside a package; star imports |
| JavaScript | `require` - kept whole, destructured, or `require('./helpers').total` - and `import` of a named, default or `* as` export, found in the other file's `module.exports`, `exports.total` or `export` |
| C, C++ | the function of that name defined in another `.c` file - one in the caller's own file first, as a `static` function there hides the rest |
| Java, C#, Go | the classes, and the package, the files share |

A call is followed only when one function is certainly meant. A name bound twice, set again after it is declared, or
exported inside a branch or a function; a module that is not one of the program's own files; two functions that could
both be meant - each leaves the call alone rather than guess. Top-level code runs in order, so a call written above the
`def` or `const` it uses fails there with a NameError or ReferenceError, and is not followed either; a JavaScript
`function` declaration is ready from the first line. What the function in the other file returns, the guards it
starts with and the text it runs are all checked at the call, and a finding that points at a line in another file names
the file: `when x < 0 it raises ValueError (line 2 of maths.py)`. Every Python module has variables of its own, so a
function in another file that changes its `items` is not taken to change the caller's.

The order things happen in is checked too (**temporal properties**): once a file or stream is closed it must not be
used, and a lock that is taken must be released on every way out of the function.

Protocols are followed as **state machines** along every way through a function: a file is opened, used, then closed;
a lock is taken, then released; a thread is made, then started - once - and only then waited for. Starting a thread a
second time, or joining one never started, is found - including a thread made before a loop and started inside it.

**Exception flow**: a `return`, `break` or `continue` in a `finally` block replaces what the `try` block returned and
throws its error away, so it is reported in Java and Python (C# refuses to compile one). In Python, a variable given its
value only inside a `try` has none if the `try` failed before that line, so reading it in the `finally` block, or after an
`except` block that carries on without giving it one, raises `UnboundLocalError` - and a read behind a condition, which a
flag set by the `try` may guard, is not claimed.

A failure the code **catches on purpose** is not reported: trying first and handling what goes wrong - `int(text)` inside
a `try` with `except ValueError` - or a test checking that bad input is refused, in `with pytest.raises(ValueError):` or
`with self.assertRaises(ValueError):`, in a JUnit 4 test marked `@Test(expected = X.class)`, or in a lambda handed to
JUnit's `assertThrows(X.class, ...)`, to `Assert.Throws<X>(...)`, or to Jest's `expect(...).toThrow()`. An assertion
that wants exactly one type - `assertThrowsExactly`, MSTest's `ThrowsException` - is held to it, and a test that expects
another exception, or none with `.not.toThrow()`, is still reported. Only a handler in the same function counts, and
only one that certainly catches:
it names the exception the failure raises, or a type the language's own documentation puts above it - `except
ArithmeticError` catches a `ZeroDivisionError`, `catch (IllegalArgumentException e)` a `NumberFormatException` - or one
of the program's own classes the exception extends; it has no `when` test that could let the exception past; and it does
not raise it again. Where the code does not show which exception a failure raises, every one it could be has to be
caught: an index outside a C# array raises an `IndexOutOfRangeException`, and outside a `List` an
`ArgumentOutOfRangeException`. A caller's `try` around a call is not enough, since another caller need not have one.

**Loops** are reasoned about with **relations between variables**, settled by the constraint solver. A counted loop's
counter is tied to its limit - inside `for i in range(len(a))`, `i ≤ len(a) - 1` - so `a[i + 1]` is found past the end
on the last time round whatever the list's length, as is `a[i]` under `i <= a.length`, a loop counting down from
`a.length`, or `a[i - 1]` on a Java loop's first time round. A while loop's **candidate invariants** - its condition's
relation, such as `lo <= hi`, and the bounds its starting values give - are kept only when the solver shows them
**inductive**: true on entry, and kept by every way through the body. Within them, a state that one time round leaves
unchanged is a loop that never ends - the binary search that sets `lo = mid` gets stuck when `hi` is `lo + 1`, and the
finding shows that state and the invariants that make it reachable.

**Taint**: text the person running the program controls - what they type, the program's arguments, its environment -
is followed through assignments, joining and formatting text, and the program's own functions in both directions, to
where it becomes something that runs: `eval` and `exec`, a shell command, SQL. Turning it into a number ends it, and a
query given its values separately (`execute("... WHERE name = ?", (name,))`) is safe, since only a query's own text
is checked. What code FixFinder cannot see returns is never assumed to carry taint.

**Resource ownership**: a file or stream a function opens is its own until it is closed or handed on - returned, stored,
passed to a call, or wrapped in another stream, which owns it from then on (**escape analysis**). One still its own and
still open on a way out of the function is never closed, and for a writer that can mean the file is left empty. Python's
top-level code is left out: the interpreter closes its files when the program ends.

**Aliasing** is followed: after `b = a`, both names hold the one list until either is given another value, so `b.clear()`
empties `a` too - and a finding about `a` says that `b` is the same list. Where two ways through the code meet, two names
share a list only if they do on both.

Each function has an **effect summary**: which of its parameters' lists, dictionaries and sets it adds to or removes
from, which fields of its object, which module variables - with the line that does it, followed through the functions it
calls. With aliasing, that makes changing a collection while a loop walks over it a matter of meaning, not spelling:
`passed.remove(mark)` inside `for mark in marks:` when `passed = marks`, or `drop(marks, mark)` when `drop` removes from
the list it is given. A list then skips items, a Python dictionary or set raises `RuntimeError`, Java can throw
`ConcurrentModificationException` and C# throws `InvalidOperationException`.

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

Each finding that is an instance of a weakness in MITRE's **Common Weakness Enumeration** says which - CWE-89 for SQL
injection, CWE-833 for a deadlock, CWE-835 for a loop that never ends, and so on - with a link to the entry, an
authoritative description independent of FixFinder's own. Every entry was fetched and its title copied from it; CWE
numbers are permanent, so the links do not move. A rule is classified only where the entry's own description fits
everything the rule reports, in that language: CWE-584 is a `return` in a `finally` block, and FixFinder's rule also
reports `break` and `continue`; CWE-129 is an index that comes in from outside unchecked, and FixFinder's index rule
also reports one worked out a step too far inside the function; CWE-476 is a NULL *pointer*, which Java's `null` and
Go's `nil` are and Python's `None` is not. The command line's JSON carries the CWE too.

Each language keeps its own rules, and a finding says what that language actually does:

| | |
|---|---|
| **Go** | A nil slice has no items and a nil map reads as missing, so `len`, indexing and `range` on them are all fine, while reading a field through a nil pointer is a panic - and a method with a nil receiver is ordinary Go, so `if c == nil` at the top of one is not a test that can never be true. Both sides of a division have the same type, so a whole-number divisor means whole-number division. `panic` is what a guard raises, while `os.Exit` and `log.Fatal` end the program, so nothing after one runs; a deferred call runs on every way out, so a lock released by `defer` is never reported as left locked, and one taken by `defer` is taken for the caller. A slice is a value, so handing it to other code cannot change how long it is. Goroutines started with `go` are followed like any other thread. |
| **JavaScript** | Dividing by zero gives Infinity rather than failing, and a position past the end gives `undefined`, so neither is reported. Every object and array is true however empty, only `0`, `""`, `null` and `undefined` are false, and `a?.b.c` gives nothing when `a` is nothing - the whole chain is skipped, not just the next step. `typeof x === "number"` says x is something. A variable declared with no value is `undefined`, so reading a field of it is a TypeError. `process.exit` ends the program, so nothing after it runs. |
| **C and C++** | Dividing by zero, going through a null pointer and reading past the end of an array are undefined behaviour, which usually stops the program. `malloc` and its like can come back with nothing, so what they return is checked before it is used. `exit` and `abort` never return, so nothing after one runs. In C++ an overloaded operator is read as a function of its own - `operator*`, `operator<<` - and `<<` and `>>` on a stream are the stream's writes and reads, not shifts: `std::cin >> count` gives `count` the number typed. A variable whose address is handed out - `&end` to `strtoll`, `&count` to a function of the program's own - is changed by the calls given the address and by writes through a pointer that holds it, when the address only goes to C's own functions that keep none of it (`scanf`, `strtol`, `printf` and the like) or to the program's functions that only read and write through it; once it may be kept anywhere, any call at all can change it. A tie two variables keep through a call is not followed - that a function adds to `count` only when it gives `items` memory - so `if (count > 0)` after the loop that fills them does not show `items` has memory, and a read of it there can be reported as possibly NULL. |
| **Python** | Dividing by zero is ZeroDivisionError whatever the numbers are; an empty list is false; text and numbers cannot be added. `sys.exit`, `exit` and `quit` end the program, so nothing after one runs - unless the program defines an `exit` of its own. |
| **Java and C#** | Whole-number division by zero fails while real division gives infinity; a declared type sets what a variable can hold and how large it can be. `System.exit` and `Environment.Exit` end the program, so nothing after one runs. |

For **C and C++** the same walk through the graph also checks what happens to memory:

| Check | For example |
|---|---|
| Memory used after it is freed | `free(node); printf("%d", node->value);` - including `n = n->next` after `free(n)` in a loop |
| Memory freed twice | `free(buffer);` on a way through the function that already freed it |
| Memory nobody frees | `malloc` into a local that is never freed, never returned and never handed on |
| The address of something that is about to go | `return &count;` or `return &scores[0];`, where `count` or the array `scores` belongs to the function that is returning - not a global, a `static` or a C++ reference, whose memory outlasts the call |
| A value read before it is given one | `int total; printf("%d", total);` - unless its address was taken first, as `scanf("%d", &total)` does. A `static` starts at zero, and on later calls holds what the last one left |

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
ran are compressed, so a loop that went round three times shows as `(5-7)×3`; in a notebook they are given cell by cell,
`cell 2: 1-4; cell 3: 2`. A method, which needs its object, is left as it was.

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

The **Efficiency** tab lists work a program repeats as its data grows. FixFinder reports a list, an array or a string
searched from the start on every pass of a loop that does not change it - `if word in stop_words:` inside
`for word in text.split():` - and only where the code shows what is searched: a set, a dictionary or a range goes
straight to the item, so searching one is never reported, and a parameter with no type is reported as *possible*, with
the finding saying that FixFinder cannot see which kind of collection it is.

Where FixFinder can show that a change gives exactly the same answers, the finding shows it on your own lines - a set
made once before the loop and searched instead - and says why it is quicker. That takes all of these: the list is the
function's own, made from a literal or a copy, and is never handed to other code or seen by a nested function; the loop
only reads it; it has its value on every way to the loop; its items compare the same way in a set (plain values in
Python, `String`, `Integer` and the like in Java, `string`, `int` and the like in C#, anything in JavaScript, whose
`Set.has` compares exactly as `includes` does); and the new line cannot land inside anything else, such as an `if`
written without braces. A Java or C# change also has to compile before it is shown. Searching text for a piece of text
is never changed this way. C and C++ are not checked for this: FixFinder's reader does not follow C++'s template types,
and a member `count` or `find` there belongs to a set or a map as often as to a string.

Text built a piece at a time in a Java or C# loop - `text += name` - is reported too (CWE-1046): a string there never
changes once made, so each `+=` makes a new one and copies all the text built so far into it, and the work grows with
the square of the number of pieces. Python and JavaScript are left out, since CPython usually grows the string in place
and JavaScript engines join strings lazily. The change offered builds the text in a `StringBuilder` - made from it before
the loop, added to inside, and turned back into the text after - and only where it gives the same text: the text is the
function's own and starts as written text, so it is never null; each piece is text, a character, a number or a truth
value, which a `StringBuilder` writes exactly as `+` does, and `text = text + a + b` adds `a` and then `b` rather than
their sum; nothing reads the text inside the loop or from a lambda written in the function; and the loop is not inside a
`try`, where a `catch` could read the text half built. A `var` counts as a string when it is given written text, and a
loop's `var` takes the type of the items its collection is declared to hold - `List<string>`, `String[]`. The logic
lane's own pattern for the same `+=` is folded into this finding, so the line is reported once.

Taking the first item out of a list, or putting one in front of it, inside a loop is reported as well - `queue.pop(0)`
or `history.insert(0, line)` in Python, `remove(0)` or `add(0, x)` on a Java `ArrayList`, `RemoveAt(0)` or
`Insert(0, x)` on a C# `List`. Each language's documentation says these move every other item one place, so doing one on
every pass makes the work grow with the passes times the length of the list, and a queue emptied from the front costs the
square of its length. Only a list the code shows moves its items is reported: a Java list only when every value it can
hold is made as an `ArrayList`, since a `LinkedList` takes from its front without moving anything, and never a deque, a
dictionary or a parameter nothing describes. A loop with a number of passes written into the code - `range(3)`, a list
written out in full, `i < 3` - does the moving a fixed number of times and is left alone, as is a loop over the same list,
which the check for a collection changed while looping covers. No change is offered: a `deque`, an `ArrayDeque` or a
`Queue` does not do everything a list does - a deque has no slices, an `ArrayDeque` holds no nulls, and neither an
`ArrayDeque` nor a `Queue` can be read by position - so the guide shows what to use, and whether it fits is left to
whoever knows the rest of the program. JavaScript's `shift()` is left out, since how long it takes is up to the engine rather than the language.

Within a session, **only what an edit could change is analysed again**. FixFinder keeps what each function's analysis
found, filed under everything that analysis depends on: the function's own lines and where they are; every function it
can call - found by name, so a call through any object still counts, and by following its calls through what its module
imports, so a function imported under a name of its own counts too - and every function those can call; every line
outside a function in every file; and the list of every function, with what each declares global. After an edit, a
function is analysed again only if one of those changed, and the logic lane says how many were unchanged. The summaries
of what each function returns, and the checks across the whole program - threads, locks, text from outside, repeated
work - are always worked out afresh. The tests compare every result taken from the cache with a fresh analysis of the
same code.

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

Some limits are part of how FixFinder works:

- **Time.** Each run of a program is given 60 seconds - six minutes for Go, whose first build compiles its standard
  library - and is stopped when its time runs out. A program with a window, or a server, runs until it is closed or
  stopped, so it is always stopped this way: what it did until then is checked, and what it would do when someone uses
  the window, or something connects, is not.
- **Libraries.** FixFinder never downloads anything. A library a project names that is not on this computer is said once,
  in a note, with where FixFinder looked; opening the project in its IDE, or building it once with its build tool,
  downloads it.
- **Files.** The code is read for logic mistakes from at most 200 of a program's files: the file chosen and the files its
  code uses come first, and a note says how many were left out. Python's and JavaScript's own syntax checks look at the
  same files; the program still runs whole, and Java, C# and Go are built as their own tools build them. A C or C++ file
  is built with the other source files beside it only when its folder holds no more than 200 files and exactly one of
  them has a `main`; otherwise it is built on its own.
- **Reading the code.** The values are followed through the code by its own language's parser - Python's, javac's, Go's -
  run by the program's own tools; when that cannot run, a note says the code was checked against the logic patterns
  alone.

Each check is written to stay quiet when it is not sure, because a check that fires on correct code teaches people to
ignore it. The newest checks were run over large bodies of working code - Python's standard library, part of the JDK's
own library, npm, and FixFinder itself - and each false alarm found there was fixed and kept as a test. So are
fifty-two correct programs written the way each language is really written - Python dataclasses, match statements
and threads; Java records, streams and executors; C# LINQ and pattern matching; JavaScript classes, prototypes and
async functions; C that manages its own memory; C++ templates, lambdas, RAII and overloaded operators; Go generics and
goroutines - and correct code written to look like the mistakes the patterns look for. Each must be read whole and draw
no error or warning, beside programs split into modules whose mistakes cross from one file to another and must be found
where the call is made.

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
| `Execution` | Finding toolchains, building and running programs - and, in `Execution/Libraries`, the libraries a Java program is built with and the running of its JUnit tests. |
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
dependable way in. Each project signs its Debug build - its DLL, and the launcher exe beside it - with `sign-for-wdac.ps1`
when a code-signing certificate is present, and does nothing when there is not one, as on CI.

The logo is drawn by `FixFinder.Gui\Assets\make-icon.py`, which draws every icon size at its own scale so the small ones
stay sharp.

## Licence

MIT. See [LICENSE](LICENSE).
