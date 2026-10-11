using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Perl programs, each explained for someone new to programming. Every example is a whole
/// program, and PerlLiveTests checks each one with perl -wc and runs it, and fails on anything Perl says.
/// </summary>
internal static class PerlPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-perl-loop-to-count"], "A loop that runs one past the end",
            "A list's positions start at 0, so a list of 3 items has positions 0, 1 and 2 - its last position, $#marks, is one less than " +
            "its count. A loop that goes on while $i <= scalar(@marks) takes one step more than that.",
            "On the last pass $marks[$i] is past the end: undef, which Perl uses as nothing - warning of it only with use warnings.",
            "Loop while $i < scalar(@marks), or go through the items themselves: foreach my $mark (@marks).",
            """
            use strict;
            use warnings;

            my @marks = (70, 80, 90);
            for (my $i = 0; $i < scalar(@marks); $i++) {
                print $marks[$i], "\n";
            }
            """),

        GuideFor(["logic-perl-text-compared-as-numbers"], "Text compared as numbers",
            "Perl has two sets of comparisons: ==, !=, < and > for numbers, and eq, ne, lt and gt for text. == turns both sides into " +
            "numbers first, and text that is not a number becomes 0 - so \"yes\" == \"no\" is true.",
            "The comparison gives the same answer whatever the text holds.",
            "Compare text with eq and ne.",
            """
            use strict;
            use warnings;

            my $answer = "yes";
            if ($answer eq "no") {
                print "no\n";
            } else {
                print "yes\n";
            }
            """),

        GuideFor(["logic-perl-line-not-chomped"], "A line read with its newline still on",
            "<STDIN> gives the line that was typed together with the newline the Enter key made, so typing 5 gives \"5\\n\" - which is " +
            "not eq to \"5\".",
            "The comparison is false every time, whatever was typed, and Perl says nothing of it.",
            "Take the newline off with chomp as the line is read: chomp(my $typed = <STDIN>);.",
            """
            use strict;
            use warnings;

            my $typed = "5\n";
            chomp $typed;
            print $typed eq "5" ? "five\n" : "not five\n";
            """),

        GuideFor(["logic-perl-float-equality"], "Decimal numbers compared exactly",
            "Decimals are stored in binary, which holds most of them only nearly: 0.1 + 0.2 is held as 0.30000000000000004, not 0.3 - " +
            "and print rounds it, showing 0.3, so the two look equal when they are not.",
            "== asks for an exact match, so a total worked out by arithmetic can fail to equal the decimal it should.",
            "Compare within a tiny tolerance: abs($total - 0.3) < 1e-9.",
            """
            use strict;
            use warnings;

            my $total = 0.1 + 0.2;
            if (abs($total - 0.3) < 1e-9) {
                print "equal\n";
            }
            """),
    ];
}
