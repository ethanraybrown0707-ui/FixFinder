using System.Text.RegularExpressions;
using FixFinder.Core.Parsing.Parsers;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What gfortran says of a Fortran program before it runs, and what stops a program it built - each explained for someone
/// new to programming.
/// </summary>
/// <remarks>
/// Every message matched here is one gfortran 13.2 was seen to give. Every example is a whole program, and FortranLiveTests
/// builds each one with -Wall and runs it, and fails on anything gfortran or the program says.
/// </remarks>
internal static class FortranGuides
{
    private const string NothingRuns = "gfortran will not build a program with this error, so while it is there none of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private const string Runs = "The program still builds and runs, but this is very likely a mistake.";

    private static GuideEntry Compiled(string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = ["compile error"],
        Message = new Regex(pattern),
        Guide = new MistakeGuide(explanation, NothingRuns, fix, example),
    };

    private static GuideEntry Warned(string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = ["compile warning"],
        Message = new Regex(pattern),
        Guide = new MistakeGuide(explanation, Runs, fix, example),
    };

    private static GuideEntry Stopped(string type, string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = [type],
        Message = new Regex(pattern),
        Guide = new MistakeGuide(explanation, Stops, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Compiled(@"^Unexpected end of file",
            "The file ended while something in it was still open. A program runs from its program line to its own end program line, " +
            "and a subroutine or function inside it to its own end line - and the file finished before that line. gfortran names no " +
            "line, as the line it wanted is the one that is not there.",
            "Add the missing end line where what it closes ends: end program, end subroutine or end function, with its name.",
            """
            program marks
              implicit none
              call show(70)
            contains
              subroutine show(mark)
                integer, intent(in) :: mark
                print *, mark
              end subroutine show
            end program marks
            """),

        new GuideEntry
        {
            ExceptionTypes = ["link error"],
            Message = new Regex(@"^undefined reference to"),
            Guide = new MistakeGuide(
                "Building a program takes two steps: gfortran compiles the code, then links it into a program. The call compiled, as Fortran " +
                "lets a program call a subroutine another file holds; the linker then found no subroutine by that name in any file of the " +
                "build - it is spelt differently where it is written, never written, or in a file that is not part of the build. gfortran " +
                "adds _ to the end of the name, so show is named show_.",
                NothingRuns,
                "Write the subroutine - after contains, inside the program - or spell the call as it is written.",
                """
                program marks
                  implicit none
                  call show(70)
                contains
                  subroutine show(mark)
                    integer, intent(in) :: mark
                    print *, mark
                  end subroutine show
                end program marks
                """),
        },

        Compiled(@"has no IMPLICIT type",
            "With implicit none, every name has to be declared before it is used, and this one never was - often because it is spelt " +
            "differently from where it was declared. gfortran often says which declared name it thinks was meant.",
            "Spell it as it is declared, or declare it with its type at the top of the program.",
            """
            program marks
              implicit none
              integer :: total
              total = 10
              print *, total + 1
            end program marks
            """),

        Compiled(@"^Syntax error in IF-expression",
            "An if's condition has to compare, and this one has = - which gives a variable a value - where == was meant.",
            "Compare with ==.",
            """
            program marks
              implicit none
              integer :: mark
              mark = 70
              if (mark == 40) then
                print *, 'forty'
              end if
            end program marks
            """),

        Compiled(@"^Syntax error in IF-clause",
            "An if that has lines of its own, closed by end if, ends its condition with then - and this one does not.",
            "Put then at the end of the if line.",
            """
            program marks
              implicit none
              integer :: mark
              mark = 50
              if (mark > 40) then
                print *, 'pass'
              end if
            end program marks
            """),

        Compiled(@"^Missing '\)' in statement",
            "gfortran reached the end of what it read without the bracket that closes this line's open one. Often the line has != - " +
            "and ! starts a comment in Fortran, so the rest of the line was never read. Fortran writes \"is not equal to\" as /=.",
            "Write /= for \"is not equal to\", and close each bracket the line opens.",
            """
            program marks
              implicit none
              integer :: mark
              mark = 50
              if (mark /= 40) then
                print *, 'not forty'
              end if
            end program marks
            """),

        Compiled(@"^Expecting END (?:DO|IF) statement",
            "A do loop or a block if was opened and not closed, so gfortran reached a line that ends something else - often the end of " +
            "the program - still inside it.",
            "Close the loop with end do, or the if with end if, where its lines finish.",
            """
            program marks
              implicit none
              integer :: i
              do i = 1, 3
                print *, i
              end do
            end program marks
            """),

        Compiled(@"^Unterminated character constant",
            "A piece of text was started with a quote that is never closed on its line, so gfortran took the rest of the line as text.",
            "Close the text with the same kind of quote it opened with.",
            """
            program marks
              implicit none
              print *, 'hello'
            end program marks
            """),

        Compiled(@"^Cannot convert \S+ to \S+",
            "A value of one type is given to a variable of another that it cannot become - text to a whole number, say. gfortran says " +
            "both types.",
            "Give the variable a value of its own type; text is turned into a number by reading it: read(text, *) number.",
            """
            program marks
              implicit none
              integer :: total
              total = 10
              print *, total
            end program marks
            """),

        Compiled(@"^Missing actual argument for argument",
            "The call gives a function or subroutine fewer values than it has arguments - the message names the one with no value.",
            "Give the call a value for each argument the function lists.",
            """
            program marks
              implicit none
              print *, area(3, 4)
            contains
              integer function area(width, height)
                integer, intent(in) :: width, height
                area = width * height
              end function area
            end program marks
            """),

        Compiled(@"^Named constant '\w+' in variable definition context",
            "A parameter is a constant: its value is set once, where it is declared, and this line tries to give it another.",
            "Leave the constant as it is, or declare it without parameter if its value has to change.",
            """
            program marks
              implicit none
              integer :: pass_mark
              pass_mark = 40
              pass_mark = 50
              print *, pass_mark
            end program marks
            """),

        Compiled(@"^Dummy argument '\w+' with INTENT\(IN\) in variable definition context",
            "intent(in) promises the subroutine only reads this argument, and this line changes it.",
            "Declare it intent(inout) if changing it is the subroutine's job, or work on a copy of your own.",
            """
            program marks
              implicit none
              integer :: total
              total = 5
              call add_one(total)
              print *, total
            contains
              subroutine add_one(n)
                integer, intent(inout) :: n
                n = n + 1
              end subroutine add_one
            end program marks
            """),

        Compiled(@"^Syntax error in expression",
            "gfortran could not make sense of the expression it points at - often one left unfinished, such as 10 + with nothing after it.",
            "Finish the expression, or join it to the next line with & at the end of this one.",
            """
            program marks
              implicit none
              integer :: total
              total = 10 + 5
              print *, total
            end program marks
            """),

        Warned(@"^Array reference at \(1\) out of bounds",
            "gfortran can see, before the program runs, that the loop reads a position the array does not have: Fortran counts from " +
            "1, so an array of 3 has positions 1 to 3.",
            "Count the loop from 1 to size(scores).",
            """
            program marks
              implicit none
              integer :: scores(3) = [70, 80, 90]
              integer :: i
              do i = 1, size(scores)
                print *, scores(i)
              end do
            end program marks
            """),

        Warned(@"may be used uninitialized",
            "This variable is used before anything gave it a value - often because it is spelt differently from the one that was given " +
            "one, in a program without implicit none, where a misspelt name is a new variable.",
            "Give the variable its value first - and put implicit none at the top, so a misspelt name is refused.",
            """
            program marks
              implicit none
              real :: total
              total = 10.0
              print *, total + 1.0
            end program marks
            """),

        Warned(@"^Return value of function '\w+' at \(1\) not set",
            "A function gives back what is put in its result - the variable named after it - and this one never puts anything there.",
            "Give the result its value: doubled = n * 2.",
            """
            program marks
              implicit none
              print *, doubled(4)
            contains
              integer function doubled(n)
                integer, intent(in) :: n
                doubled = n * 2
              end function doubled
            end program marks
            """),

        Stopped(FortranParser.RuntimeError, @"^Index '-?\d+' of dimension \d+ of array '\w+' (?:below lower|above upper) bound",
            "The program asked an array for a position it does not have - Fortran counts from 1, so an array of 3 has positions 1 to 3, " +
            "and a loop from 0, or to size + 1, reads outside it.",
            "Count the loop from 1 to size(scores).",
            """
            program marks
              implicit none
              integer :: scores(3) = [70, 80, 90]
              integer :: i
              do i = 1, size(scores)
                print *, scores(i)
              end do
            end program marks
            """),

        Stopped(FortranParser.RuntimeError, @"^Bad \w+ for item \d+ in list input",
            "The program read a number, and what it was given is not one - a word where a whole number was expected, say.",
            "Read with iostat=, which gives a number other than 0 when the reading failed, and say so.",
            """
            program marks
              implicit none
              character(len=10) :: typed = 'twenty'
              integer :: age, status
              read(typed, *, iostat=status) age
              if (status /= 0) then
                print *, 'Please type a number'
              else
                print *, age + 1
              end if
            end program marks
            """),

        Stopped("SIGFPE", @"^Floating-point exception",
            "A whole number was divided by zero, which has no answer, so the program stopped with SIGFPE. On Windows it names no line " +
            "for it. Dividing reals by zero does not stop it: it gives Infinity.",
            "Check the divisor is not zero first, and decide what the answer should be when it is.",
            """
            program marks
              implicit none
              integer :: total, count
              total = 70
              count = 0
              if (count == 0) then
                print *, 0
              else
                print *, total / count
              end if
            end program marks
            """),
    ];
}
