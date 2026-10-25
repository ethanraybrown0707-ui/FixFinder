using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Dart programs, each explained for someone new to programming. Every example is a whole
/// program, and DartLiveTests checks each one with dart analyze and runs it, and fails on anything Dart says.
/// </summary>
internal static class DartPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-dart-loop-to-length"], "A loop that runs one past the end",
            "A list's positions start at 0, so a list of 3 items has positions 0, 1 and 2. A loop that keeps going while " +
            "i <= marks.length takes one step more than that, and on its last step marks[i] asks for a position that is not there.",
            "The program stops on the loop's last pass with RangeError.",
            "Loop while i < marks.length, or go through the items themselves: for (var mark in marks).",
            """
            void main() {
              var marks = [70, 80, 90];
              for (var i = 0; i < marks.length; i++) {
                print(marks[i]);
              }
            }
            """),

        GuideFor(["logic-dart-lists-compared-with-equals"], "Two lists compared with ==",
            "== on two lists asks whether they are the very same list in memory, not whether they hold the same items - so two lists " +
            "made apart are never ==, even when every item matches.",
            "The comparison is false every time for two lists made separately, whatever they hold, and Dart says nothing of it.",
            "Compare the lengths, then each item at the same position.",
            """
            void main() {
              var first = [70, 80];
              var second = [70, 80];
              var same = first.length == second.length && Iterable.generate(first.length).every((i) => first[i] == second[i]);
              print(same);
            }
            """),

        GuideFor(["logic-dart-float-equality"], "Decimal numbers compared exactly",
            "Decimals are stored in binary, which holds most of them only nearly: 0.1 + 0.2 is 0.30000000000000004, not 0.3.",
            "== asks for an exact match, so a total worked out by arithmetic can fail to equal the decimal it should.",
            "Compare within a tiny tolerance: (total - 0.3).abs() < 1e-9.",
            """
            void main() {
              var total = 0.1 + 0.2;
              print((total - 0.3).abs() < 1e-9);
            }
            """),

        GuideFor(["logic-dart-caret-as-power"], "^ used as a power",
            "In maths side^2 means side squared, but in Dart ^ is exclusive or: it compares two whole numbers bit by bit " +
            "and keeps the bits that differ. 5 is 101 in binary and 2 is 010, so 5 ^ 2 is 111 - which is 7, not 25.",
            "The program runs and works out a number - just the wrong one - and dart analyze says nothing of it.",
            "Multiply the number by itself: side * side. pow, from dart:math, raises a number to any power: pow(side, 2).",
            """
            void main() {
              var side = 5;
              print(side * side);
            }
            """),

        GuideFor(["logic-dart-interpolated-property"], "A property after $ in text",
            "In text, $marks puts in the value of the name after the $ - and only the name. In '$marks.length' Dart puts in marks, " +
            "and writes .length after it as it is; the same goes for $marks[0].",
            "The text holds the whole list followed by the words, with no error - [70, 80, 90].length.",
            "Put the whole expression in ${ }: '${marks.length}' puts in the value of everything between the braces.",
            """
            void main() {
              var marks = [70, 80, 90];
              print('Marks counted: ${marks.length}');
            }
            """),

        GuideFor(["logic-dart-map-never-run"], "A map that never runs",
            "map does not run its function straight away: it gives back a list-like Iterable that works each item out only when " +
            "something reads it. On a line of its own - marks.map((mark) => print(mark)); - nothing reads it, so print is never " +
            "called. dart analyze says nothing.",
            "Nothing the function does happens: the marks are never printed, and a total added to inside it stays as it was.",
            "Use a for loop, which runs its body for each item straight away: for (var mark in marks) print(mark);",
            """
            void main() {
              var marks = [70, 80, 90];
              for (var mark in marks) print(mark);
            }
            """),

        GuideFor(["logic-dart-list-shared"], "A second name for a list is not a copy",
            "var backup = marks; does not copy the list: it gives the same list a second name. A change made through " +
            "either name - marks.add(100) - is a change to the one list, so backup shows it too. dart analyze says " +
            "nothing.",
            "The second name does not keep the values the list had: after marks.add(100), backup prints [70, 80, 90, " +
            "100].",
            "Make a copy: var backup = [...marks]; The copy is a new list with the same items, and a change to one no " +
            "longer shows in the other.",
            """
            void main() {
              var marks = [70, 80, 90];
              var backup = [...marks];
              marks.add(100);
              print(backup);
            }
            """),

        GuideFor(["logic-dart-rows-shared"], "A grid whose rows are one row",
            "List.filled(3, List.filled(3, 0)) makes one row and fills the grid with that one row three times. Every " +
            "row is the same list, so grid[0][0] = 1 changes the first item of every row. dart analyze says nothing.",
            "A change to one row shows in all of them: after grid[0][0] = 1, the grid prints [[1, 0, 0], [1, 0, 0], " +
            "[1, 0, 0]].",
            "Make each row its own list: List.generate(3, (_) => List.filled(3, 0)) makes a new row for each.",
            """
            void main() {
              var grid = List.generate(3, (_) => List.filled(3, 0));
              grid[0][0] = 1;
              print(grid);
            }
            """),

        GuideFor(["logic-dart-remove-while-counting-up"], "Removing from a list while counting up through it",
            "removeAt(i) takes out the item at i and moves every item after it down a place to close the gap. A loop counting up " +
            "then moves on to i + 1 - so the item that moved into position i is never looked at.",
            "Two items in a row that should both go leave one behind - [70, 40, 40, 90] keeps a 40 - and dart analyze says nothing.",
            "Count down instead - for (var i = marks.length - 1; i >= 0; i--) - so a removal only moves items the loop has already " +
            "been past. Or use marks.removeWhere((mark) => mark == 40).",
            """
            void main() {
              var marks = [70, 40, 40, 90];
              marks.removeWhere((mark) => mark == 40);
              print(marks);
            }
            """),

        GuideFor(["logic-dart-result-discarded"], "A String method's result thrown away",
            "Text in Dart never changes. trim, toUpperCase, replaceAll, substring and the rest give back a new, changed copy, and leave the " +
            "text they were called on as it was - so called on a line of their own, the copy is lost.",
            "The text stays as it was - the name printed unchanged - and dart analyze finds no issues.",
            "Keep the copy: name = name.trim(); - or use it straight away, as in print(name.trim()).",
            """
            void main() {
              var name = ' Ada ';
              name = name.trim();
              print('[$name]');
            }
            """),

        GuideFor(["logic-dart-future-used-as-value"], "An async function's result used without await",
            "An async function gives back a Future: a promise of its value, finished later. Called without await, what you get is the " +
            "Future, not the value - so printing it prints Instance of 'Future<int>'.",
            "The program uses the Future where it meant the value, and dart analyze finds no issues.",
            "Wait for the value with await, inside a function marked async: var result = await count(); in main() async.",
            """
            Future<int> count() async => 3;

            Future<void> main() async {
              var result = await count();
              print(result);
            }
            """),

        GuideFor(["logic-dart-handler-never-reached"], "An on clause that can never run",
            "A try's on clauses are tried in order, and the first that matches takes the exception. on Exception matches FormatException " +
            "too - FormatException is an Exception - so an on FormatException after it is never reached. dart analyze calls it dead code.",
            "The specific handler never runs, so it cannot be tested - int.parse(\"x\") printed the general message.",
            "Put the specific clause first: on FormatException, then on Exception.",
            """
            void main() {
              try {
                int.parse('x');
              } on FormatException {
                print('not a number');
              } on Exception {
                print('something went wrong');
              }
            }
            """),
    ];
}
