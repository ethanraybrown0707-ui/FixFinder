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
    ];
}
