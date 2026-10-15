using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Perl's compilation errors, what stops its programs, and the warnings it prints while it carries on - each explained for
/// someone new to programming.
/// </summary>
/// <remarks>
/// Every message matched here is one Strawberry Perl 5.42 was seen to give. Perl gives what stops a program no type, so each
/// guide is found by the message's words. Every example is a whole program, and PerlLiveTests checks each one with perl -wc
/// and runs it, and fails on anything Perl says.
/// </remarks>
internal static class PerlGuides
{
    private const string NothingRuns = "Perl reads the whole file before it runs any of it, so while this is wrong no part of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private const string CarriesOn =
        "Perl only warns and carries on - so the program goes on with a wrong value, and what it prints or works out from here can be " +
        "wrong without any error. Without use warnings, Perl does not even warn.";

    private static GuideEntry Refused(string pattern, string explanation, string fix, string example) => Entry(pattern, explanation, NothingRuns, fix, example);

    private static GuideEntry Stopped(string pattern, string explanation, string fix, string example) => Entry(pattern, explanation, Stops, fix, example);

    private static GuideEntry Warned(string pattern, string explanation, string fix, string example) => Entry(pattern, explanation, CarriesOn, fix, example);

    private static GuideEntry Entry(string pattern, string explanation, string consequence, string fix, string example) => new()
    {
        Message = new Regex(pattern),
        Guide = new MistakeGuide(explanation, consequence, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Refused(@"^Global symbol ""[$@%]\w+"" requires explicit package name",
            "use strict asks Perl to refuse any variable that was not declared with my first - which catches a name spelt differently " +
            "from where it was declared, as well as one never declared at all.",
            "Declare the variable with my where it is first given a value, or spell it as it was declared.",
            """
            use strict;
            use warnings;

            my $total = 10;
            print $total + 1, "\n";
            """),

        Refused(@"^Missing right curly or square bracket",
            "A { or a [ was opened and never closed, so Perl reached the end of the file still waiting for it - the line it names is the " +
            "end of the file, not where the bracket is missing.",
            "Close each block where it ends. Indenting each block's lines shows which one is missing its }.",
            """
            use strict;
            use warnings;

            my @marks = (70, 80);
            foreach my $mark (@marks) {
                if ($mark > 75) {
                    print "$mark\n";
                }
            }
            """),

        Refused(@"^elseif should be elsif",
            "Perl spells \"else if\" as elsif, with no e in the middle, and its message says so.",
            "Write elsif.",
            """
            use strict;
            use warnings;

            my $mark = 50;
            if ($mark > 70) {
                print "high\n";
            } elsif ($mark > 40) {
                print "pass\n";
            }
            """),

        Refused(@"^(?:Scalar|String|Bareword|Number) found where operator expected",
            "Perl found a value where it expected something to join it to what came before - most often because text was opened with " +
            "a quote on an earlier line and never closed, so Perl took the lines after it as part of the text.",
            "Look at the line Perl names and the lines just above it for a quote that is not closed, or a missing comma or semicolon.",
            """
            use strict;
            use warnings;

            my $name = "Ada";
            print "$name\n";
            """),

        Refused(@"^Can't modify constant item",
            "A constant made with use constant can never be given another value, and Perl will not even read a line that tries.",
            "Leave the constant as it is, or use a variable, declared with my, if the value has to change.",
            """
            use strict;
            use warnings;

            my $pass_mark = 40;
            $pass_mark = 50;
            print "$pass_mark\n";
            """),

        Refused(@"^Can't locate \S+\.pm in @INC",
            "Perl could not find the module this line uses - it is not installed on this computer, or its name is spelt differently. " +
            "FixFinder never installs anything itself.",
            "Check the module's name, and install it if it is needed - Perl's message names the module to install.",
            """
            use strict;
            use warnings;
            use List::Util qw(sum);

            print sum(70, 80), "\n";
            """),

        Refused(@"^syntax error",
            "Perl could not make sense of the code where it names. Most often the line before it is missing its semicolon - each statement " +
            "in Perl ends with ; - or an if, elsif or while has no round brackets around its condition, which Perl needs.",
            "Look at the end of the line before the one Perl names for a missing ;, and check each if and while has its condition in brackets.",
            """
            use strict;
            use warnings;

            my $mark = 50;
            if ($mark > 40) {
                print "pass\n";
            }
            """),

        Stopped(@"^Illegal (?:division by zero|modulus zero)",
            "Dividing by zero has no answer, so Perl stopped the program. The number divided by is zero at this point - often because it " +
            "counts the items of a list that turned out to be empty.",
            "Check the number is not zero before dividing by it, and decide what the answer should be when it is.",
            """
            use strict;
            use warnings;

            my @marks = ();
            my $count = scalar(@marks);
            my $average = $count == 0 ? 0 : 100 / $count;
            print "$average\n";
            """),

        Stopped(@"^Undefined subroutine &",
            "Perl was asked to call a sub by a name it has no sub for - it is spelt differently from where it is written, or it is in a " +
            "file this one does not use.",
            "Check the name against the sub's own, on its sub line.",
            """
            use strict;
            use warnings;

            sub average {
                my @marks = @_;
                return 0 unless @marks;
                my $total = 0;
                $total += $_ for @marks;
                return $total / scalar(@marks);
            }
            print average(70, 80), "\n";
            """),

        Stopped(@"^Can't call method ""\w+"" on an undefined value",
            "A method was called on a variable that holds undef - nothing - because it was never given an object, or was given what a " +
            "search returned when it found nothing.",
            "Check the variable holds an object before calling a method on it: if (defined $student) { ... }.",
            """
            use strict;
            use warnings;

            my $student;
            if (defined $student) {
                print $student->name, "\n";
            } else {
                print "No student\n";
            }
            """),

        Stopped(@"^Can't call method ""\w+"" on unblessed reference",
            "A method was called on a plain hash or array reference, not an object made from a class - so it has no methods. Its values " +
            "are reached with -> and brackets instead.",
            "Read the value with $student->{name}, or make the object from its class with new.",
            """
            use strict;
            use warnings;

            my $student = {name => "Ada"};
            print $student->{name}, "\n";
            """),

        Stopped(@"^Can't use an undefined value as an? (?:ARRAY|HASH|SCALAR|CODE) reference",
            "A variable that holds undef - nothing - was used as a reference to a list or a hash, so there was nothing to look in.",
            "Give the variable a reference first - my $marks = []; for a list, or {} for a hash.",
            """
            use strict;
            use warnings;

            my $marks = [];
            print scalar(@$marks), "\n";
            """),

        Stopped(@"^Not an? (?:ARRAY|HASH|CODE|SCALAR) reference",
            "A reference to one kind of thing was used as another - a hash reference as a list, say - so Perl could not follow it.",
            "Use the reference as what it is: @$list for a list reference, %$hash or $hash->{key} for a hash reference.",
            """
            use strict;
            use warnings;

            my $marks = {ada => 70};
            print $marks->{ada}, "\n";
            """),

        Warned(@"^Use of uninitialized value",
            "A variable holding undef - nothing - was used as a value: one never given one, a key a hash does not have, or a position past " +
            "the end of a list. Perl uses it as 0, or as empty text.",
            "Give the variable a value before this line, or check it is defined first: defined $age.",
            """
            use strict;
            use warnings;

            my %ages = (Ada => 36);
            my $age = $ages{Bob} // 0;
            print $age + 1, "\n";
            """),

        Warned(@"^Argument "".*"" isn't numeric in numeric (?:eq|ne|lt|gt|le|ge|comparison)",
            "==, !=, < and > compare numbers, and one side is text - so Perl turned it into a number, and text that is not a number becomes " +
            "0. Any two words are then equal.",
            "Compare text with eq, ne, lt and gt.",
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

        Warned(@"^Argument "".*"" isn't numeric",
            "Arithmetic needs numbers, and this text is not one - so Perl used the number at its start, or 0 when there is none.",
            "Join text with ., not +; and check text is a number before using it as one.",
            """
            use strict;
            use warnings;

            my $total = 12;
            print "Total: " . $total, "\n";
            """),

        Warned(@"^Found = in conditional",
            "= stores a value and == compares; in a condition, = stores the value and the condition is then that value - the same every " +
            "time, and the variable has lost its own.",
            "Compare numbers with ==, and text with eq.",
            """
            use strict;
            use warnings;

            my $mark = 70;
            if ($mark == 40) {
                print "forty\n";
            }
            print "$mark\n";
            """),

        Warned(@"^Name "".*"" used only once: possible typo",
            "This variable's name appears only once in the whole file - so it is almost certainly a different spelling of one used elsewhere. " +
            "use strict would refuse it outright.",
            "Spell it as the variable is spelt where it is given its value - and put use strict; at the top to catch these.",
            """
            use strict;
            use warnings;

            my $total = 10;
            print $total + 1, "\n";
            """),

        Warned(@"^Useless use of .* in void context",
            "This line works something out and does nothing with it - the answer is thrown away. Often = was left out: $total + 1 where " +
            "$total = $total + 1 was meant.",
            "Keep the answer: give it to a variable, print it, or return it.",
            """
            use strict;
            use warnings;

            my $total = 10;
            $total = $total + 1;
            print "$total\n";
            """),

        Warned(@"^Odd number of elements in hash assignment",
            "A hash is filled from pairs - a key, then its value - and this list has one too few or one too many, so a key has no value.",
            "Give every key a value, and write the pairs with => to see them: (Ada => 36, Bob => 41).",
            """
            use strict;
            use warnings;

            my %ages = (Ada => 36, Bob => 41);
            print scalar(keys %ages), "\n";
            """),

        Warned(@"^Deep recursion on subroutine",
            "A sub is 100 calls deep in itself, with none of the calls finished - often because it has no case that stops it. Perl sets " +
            "no limit of its own, so such a sub goes on calling itself for as long as there is memory to keep track of the calls.",
            "Give the sub a case that returns without calling itself, and make each call come nearer to it.",
            """
            use strict;
            use warnings;

            sub count_down {
                my ($number) = @_;
                return 0 if $number == 0;
                return 1 + count_down($number - 1);
            }
            print count_down(3), "\n";
            """),

        Warned(@"^""my"" variable .* masks earlier declaration",
            "The same variable is declared with my twice in the same block, so the second hides the first - and anything that changed the " +
            "first is lost.",
            "Declare it once, with my, and give it new values without my after that.",
            """
            use strict;
            use warnings;

            my $total = 1;
            $total = 2;
            print "$total\n";
            """),

        Warned(@"^Scalar value @\w+\[.*\] better written as",
            "One item of a list is written with $, not @: $marks[0]. @marks[0] asks for a slice - a list of items - that has one item in it.",
            "Write one item as $marks[0].",
            """
            use strict;
            use warnings;

            my @marks = (70, 80);
            print $marks[0], "\n";
            """),

        Warned(@"^readline\(\) on (?:closed|unopened) filehandle",
            "A line was read from a file that is not open - it was closed, or opening it failed and nothing checked.",
            "Check open worked - open(...) or die \"...: $!\" - and read the file before closing it.",
            """
            use strict;
            use warnings;

            open(my $file, "<", $0) or die "Cannot open $0: $!";
            my $first = <$file>;
            close($file);
            print length($first) > 0 ? "read a line\n" : "empty\n";
            """),
    ];
}
