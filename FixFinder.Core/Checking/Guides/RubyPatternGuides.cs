using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Ruby programs, each explained for someone new to programming. Every example is a whole
/// program, and RubyLiveTests checks each one with ruby -wc, and fails on any warning.
/// </summary>
internal static class RubyPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-ruby-integer-average"], "A decimal that lost its fraction",
            "Dividing one whole number by another in Ruby gives a whole number: the part after the point is dropped, so 3 / 2 is 1. " +
            ".to_f after the division comes too late - it turns the 1 into 1.0, as the fraction is already gone. Making the first " +
            "number a decimal first makes the division a decimal one.",
            "Averages and percentages come out rounded down to a whole number, with no error to say so.",
            "Make the first number a decimal before dividing: total.to_f / count.",
            """
            total = 3
            count = 2
            puts total.to_f / count
            """),

        GuideFor(["logic-ruby-float-equality"], "Decimal numbers compared exactly",
            "Computers store fractions like 0.1 in binary, where most of them can only be stored approximately - the way 1/3 can only be " +
            "written as 0.333... in decimal. Arithmetic on them leaves a tiny error, so 0.1 + 0.2 is 0.30000000000000004, and " +
            "0.1 + 0.2 == 0.3 is false.",
            "An exact comparison fails even when the numbers are equal for every practical purpose, so the code it guards never runs.",
            "Check that the difference is smaller than a tiny tolerance: (total - 0.3).abs < 1e-9.",
            """
            total = 0.1 + 0.2
            puts "equal" if (total - 0.3).abs < 1e-9
            """),

        GuideFor(["logic-ruby-result-discarded"], "A changed copy that is thrown away",
            "upcase, strip, sort, reverse and their like do not change the value they are called on - they give back a new, changed " +
            "copy. A line that calls one and keeps nothing changes nothing: the value is just as it was on the next line. Each has a " +
            "form ending in ! - upcase!, sort! - that changes the value itself.",
            "The change never happens, so the lines after it go on using the value as it was.",
            "Keep the copy - name = name.upcase - or use the ! form, which changes the value itself.",
            """
            name = "ada"
            name = name.upcase
            puts name
            """),
    ];
}
