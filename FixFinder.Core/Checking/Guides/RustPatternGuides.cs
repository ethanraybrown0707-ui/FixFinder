using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Rust programs, each explained for someone new to programming. Every example is a whole
/// program, and RustLogicLiveTests compiles each one with rustc, and fails on any warning.
/// </summary>
internal static class RustPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-rust-range-to-length"], "A loop that runs one past the end",
            "0..=marks.len() counts from 0 up to and including marks.len(). But the positions in a Vec run from 0 to one less than its " +
            "length - a Vec of 3 items has positions 0, 1 and 2 - so the loop's last pass asks for a position that is not there. " +
            "0..marks.len() stops one before the length.",
            "The last pass stops the program with a panic, index out of bounds - after the earlier passes have already run.",
            "Count with .. instead of ..=, or loop over the items themselves: for mark in &marks.",
            """
            fn main() {
                let marks = vec![70, 80, 90];
                for i in 0..marks.len() {
                    println!("{}", marks[i]);
                }
            }
            """),

        GuideFor(["logic-rust-integer-average"], "A decimal that lost its fraction",
            "Dividing one whole number by another in Rust gives a whole number: the part after the point is dropped, so 3 / 2 is 1. " +
            "as f64 around the division comes too late - it turns the 1 into 1.0, as the fraction is already gone. Making each number " +
            "an f64 first makes the division a decimal one.",
            "Averages and percentages come out rounded down to a whole number, with no error to say so.",
            "Make each number an f64 before dividing: total as f64 / count as f64.",
            """
            fn main() {
                let total = 3;
                let count = 2;
                let average = total as f64 / count as f64;
                println!("{}", average);
            }
            """),

        GuideFor(["logic-rust-float-equality"], "Decimal numbers compared exactly",
            "Computers store fractions like 0.1 in binary, where most of them can only be stored approximately - the way 1/3 can only be " +
            "written as 0.333... in decimal. Arithmetic on them leaves a tiny error, so 0.1 + 0.2 is 0.30000000000000004, and " +
            "0.1 + 0.2 == 0.3 is false.",
            "An exact comparison fails even when the numbers are equal for every practical purpose, so the code it guards never runs.",
            "Check that the difference is smaller than a tiny tolerance: (total - 0.3).abs() < 1e-9.",
            """
            fn main() {
                let total: f64 = 0.1 + 0.2;
                if (total - 0.3).abs() < 1e-9 {
                    println!("equal");
                }
            }
            """),

        GuideFor(["logic-rust-caret-as-power"], "^ used as a power",
            "In maths side^2 means side squared, but in Rust ^ is exclusive or: it compares two whole numbers bit by bit " +
            "and keeps the bits that differ. 5 is 101 in binary and 2 is 010, so 5 ^ 2 is 111 - which is 7, not 25.",
            "The program runs and works out a number - just the wrong one - and rustc says nothing of it.",
            "Multiply the number by itself: side * side. A whole number whose type is given - let side: i32 = 5 - also has pow: " +
            "side.pow(2).",
            """
            fn main() {
                let side = 5;
                println!("{}", side * side);
            }
            """),
    ];
}
