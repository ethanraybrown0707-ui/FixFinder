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

        GuideFor(["logic-perl-length-of-array"], "length of an array",
            "length counts the characters of a piece of text. Given an array, it is given the array's count as text, and counts that " +
            "text's digits - so for an array of 12 items length(@marks) is 2, and for 3 items it is 1.",
            "The count is wrong for every array, with no error - only a warning, and only with use warnings.",
            "Count an array's items with scalar(@marks).",
            """
            use strict;
            use warnings;

            my @marks = (70, 80, 90);
            print scalar(@marks), "\n";
            """),

        GuideFor(["logic-perl-caret-as-power"], "^ used as a power",
            "In maths side^2 means side squared, but in Perl ^ is exclusive or: it compares two whole numbers bit by bit " +
            "and keeps the bits that differ. 5 is 101 in binary and 2 is 010, so 5 ^ 2 is 111 - which is 7, not 25.",
            "The program runs and works out a number - just the wrong one - and Perl says nothing of it, even with use warnings.",
            "Use Perl's power, **: $side ** 2.",
            """
            use strict;
            use warnings;

            my $side = 5;
            print $side ** 2, "\n";
            """),

        GuideFor(["logic-perl-power-of-one-over"], "A root written as a power of 1 over a number",
            "In maths the square root of side can be written as side to the power 1/2. In Perl ** is worked out before /, so " +
            "$side ** 1/2 is $side ** 1 - $side itself - divided by 2. With 16 it is 8, not 4.",
            "The program works out half of the number - or a third, for 1/3 - instead of its root, and Perl says nothing of it, even " +
            "with use warnings.",
            "Raise it to the fraction itself: $side ** 0.5, or $side ** (1 / 3) for a cube root, with the fraction in brackets - or " +
            "use sqrt($side).",
            """
            use strict;
            use warnings;

            my $side = 16;
            print $side ** 0.5, "\n";
            """),

        GuideFor(["logic-perl-numbers-sorted-as-text"], "Numbers sorted as text",
            "sort on its own puts things in order as text, comparing them character by character as a dictionary would - so 100 " +
            "comes before 9, as 1 comes before 9. To put numbers in order by size, sort needs telling how to compare two of them: " +
            "{ $a <=> $b }, where <=> compares two numbers.",
            "The numbers come out in the wrong order - 10, 9 and 100 come out as 10 100 9 - and Perl says nothing of it, even with " +
            "use warnings.",
            "Compare them as numbers: sort { $a <=> $b } @marks - or sort { $b <=> $a } @marks for the largest first.",
            """
            use strict;
            use warnings;

            my @marks = (10, 9, 100);
            my @sorted = sort { $a <=> $b } @marks;
            print "@sorted\n";
            """),

        GuideFor(["logic-perl-method-in-string"], "A method called inside text",
            "Perl puts a variable's value into text in double quotes, but it calls no method there. In \"Name: $student->name\" it puts " +
            "in $student - an object, which it writes as its class and where it is kept - and writes ->name after it as it is.",
            "The text holds something like Student=HASH(0x...)->name, with no error.",
            "Call the method outside the quotes, and join what it gives back to the text with . on each side.",
            """
            use strict;
            use warnings;

            package Student;
            sub new { my ($class, $name) = @_; return bless { name => $name }, $class; }
            sub name { return $_[0]{name}; }

            package main;
            my $student = Student->new("Ada");
            print "Name: " . $student->name . "\n";
            """),

        GuideFor(["logic-perl-variable-in-single-quotes"], "A variable inside single quotes",
            "Perl puts a variable's value into text only when the text is in double quotes: \"Hello, $name\" becomes Hello, Ada. In " +
            "single quotes, nothing in the text is worked out - $name is ordinary characters, and is printed as it is written. Perl " +
            "says nothing, even with use strict and use warnings, as the text is still text.",
            "The output shows $name instead of the name, which looks broken to anyone using the program.",
            "Put the text in double quotes. Text that has a \", a \\ or an @ of its own needs those written differently first: " +
            "double quotes read a \\ as the start of an escape, and an @ as an array to put into the text.",
            """
            use strict;
            use warnings;
            my $name = "Ada";
            print "Hello, $name\n";
            """),

        GuideFor(["logic-perl-my-hides-outer"], "my inside a loop hides the outer variable",
            "my declares a new variable, which lasts until the end of the block it is in. Inside a loop, my $total = $total + $mark " +
            "declares a new $total each time round, worked out from the $total outside the loop - which is never changed. Perl says " +
            "nothing, even with use strict and use warnings, as the new variable is in a block of its own.",
            "The $total outside the loop keeps the value it started with: adding up 70, 80 and 90 prints Total: 0.",
            "Take my away inside the loop - $total = $total + $mark - so the line changes the $total declared before it.",
            """
            use strict;
            use warnings;
            my @marks = (70, 80, 90);
            my $total = 0;
            foreach my $mark (@marks) {
                $total = $total + $mark;
            }
            print "Total: $total\n";
            """),

        GuideFor(["logic-perl-format-code-printed"], "A %d code that print does not fill in",
            "%d and %s are codes from C's printf: each marks where a value goes in the text. print writes each thing it is given " +
            "side by side and fills nothing in, so print \"Total: %d\", $total prints Total: %d150.",
            "The output shows the code as it is, with the value run on after it, and Perl says nothing of it, even with use warnings.",
            "Use printf, which fills the codes in and prints the text - printf(\"Total: %d\\n\", $total) - or give print the text " +
            "sprintf fills in: print sprintf(\"Total: %d\", $total).",
            """
            use strict;
            use warnings;

            my $total = 150;
            printf("Total: %d\n", $total);
            """),

        GuideFor(["logic-perl-split-character-as-text"], "split given . or | as text",
            "split reads its first argument as a regular expression, even when it is written in quotes, and there . means any " +
            "character, and | on its own matches the empty text between characters. So split(\".\", \"3.14\") splits at every character " +
            "and gives no parts at all, and split(\"|\", \"a|b\") gives a, | and b.",
            "The parts are wrong or missing, and Perl says nothing of it, even with use warnings.",
            "Write the pattern as one, with a \\ before the character: split(/\\./, $text).",
            """
            use strict;
            use warnings;

            my @parts = split(/\./, "3.14");
            print join(",", @parts), "\n";
            """),

        GuideFor(["logic-perl-loop-to-last-index"], "A loop that stops below $#array",
            "$#marks is the last position in @marks - one less than how many items it holds, as positions count from 0. A loop " +
            "that runs while $i < $#marks stops one position early, so the last item is never read.",
            "The last item is left out - (70, 80, 90) printed 70 and 80 - and Perl says nothing of it, even with use warnings.",
            "Go up to the last position itself: $i <= $#marks - or loop over the items with foreach my $mark (@marks).",
            """
            use strict;
            use warnings;

            my @marks = (70, 80, 90);
            for (my $i = 0; $i <= $#marks; $i++) {
                print $marks[$i], "\n";
            }
            """),

        GuideFor(["logic-perl-date-format-minutes"], "%M in a date, where the month was meant",
            "In strftime's format, %m is the month and %M is the minutes - the case matters. So \"%Y-%M-%d\" writes the minutes where " +
            "the month should be: 9:30 on 5 February 2024 gives 2024-30-05.",
            "The date is written wrongly, with no error, and perl -w says nothing.",
            "Use %m for the month: strftime(\"%Y-%m-%d\", ...). %M belongs beside an hour, as in %H:%M.",
            """
            use strict;
            use warnings;
            use POSIX qw(strftime);

            print strftime("%Y-%m-%d %H:%M", 0, 30, 9, 5, 1, 124), "\n";
            """),

        GuideFor(["logic-perl-arguments-counted"], "my $name = @_ - the count, not the argument",
            "An array given to a single scalar gives it the array's count. So my $name = @_; makes $name the number of arguments the sub " +
            "was given - 1 for greet(\"Ada\") - not the first of them.",
            "The sub works with a count where it meant a value: greet(\"Ada\") printed \"Hello, 1\", and use warnings says nothing.",
            "Put brackets round the name, so the arguments fill it as a list: my ($name) = @_; - or take the first with shift.",
            """
            use strict;
            use warnings;

            sub greet {
                my ($name) = @_;
                print "Hello, $name\n";
            }

            greet("Ada");
            """),

        GuideFor(["logic-perl-arrays-compared-by-count"], "Two arrays compared with ==",
            "== compares numbers, and an array used as a number is its count. So @first == @second asks whether the two arrays have as " +
            "many items - not whether they hold the same ones.",
            "Arrays of the same length always count as equal: (1, 2, 3) and (4, 5, 6) were \"same\".",
            "Compare what they hold - for simple values, as text: \"@first\" eq \"@second\", which joins each with spaces.",
            """
            use strict;
            use warnings;

            my @first = (1, 2, 3);
            my @second = (1, 2, 3);
            print "same\n" if "@first" eq "@second";
            """),

        GuideFor(["logic-perl-branch-never-reached"], "An elsif that can never run",
            "An if - elsif chain takes the first branch whose test passes. If an earlier test lets through every value a later one does - " +
            "$score >= 50 before $score >= 70 - the later branch never gets a value.",
            "That branch never runs, so no input can test it - a score of 75 got C, not B - and perl -w says nothing.",
            "Put the strictest test first: $score >= 70, then $score >= 50.",
            """
            use strict;
            use warnings;

            my $score = 75;
            if ($score >= 70) {
                print "B\n";
            } elsif ($score >= 50) {
                print "C\n";
            } else {
                print "F\n";
            }
            """),

        GuideFor(["logic-perl-loop-never-runs"], "A count down written as (10..1)",
            "A range counts up: (10..1) starts at 10, and 10 is already past 1 - so it holds nothing, and a loop over it runs no times at all.",
            "Nothing in the loop ever runs, so it cannot be tested, and perl -w says nothing.",
            "Reverse a range that counts up: reverse(1..10) gives 10, 9, ... 1.",
            """
            use strict;
            use warnings;

            for my $count (reverse(1..3)) {
                print "$count\n";
            }
            """),

        GuideFor(["logic-perl-code-after-return"], "Code after return",
            "return leaves the sub there and then - and die, exit, last and next leave the same way. A line straight after one of them, " +
            "in the same block, is never reached.",
            "That line never runs, so no run of the program can test it, and perl -w says nothing.",
            "Move the line before the return if it was meant to run, or take it out if it was not.",
            """
            use strict;
            use warnings;

            sub total {
                my @marks = @_;
                print "counted\n";
                return scalar(@marks);
            }

            print total(1, 2), "\n";
            """),
    ];
}
