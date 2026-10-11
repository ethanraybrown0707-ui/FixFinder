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
    ];
}
