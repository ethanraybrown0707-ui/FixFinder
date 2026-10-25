using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Fortran programs, each explained for someone new to programming. Every example is a whole
/// program, and FortranLiveTests builds each one with -Wall and runs it, and fails on anything gfortran or the program says.
/// </summary>
internal static class FortranPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-fortran-whole-number-division"], "Whole numbers divided, kept as a real",
            "When both sides of / are whole numbers, Fortran works the answer out as a whole number, dropping the fraction - and only " +
            "then keeps it in the real on the left. 7 / 2 gives 3, kept as 3.0.",
            "The average, or whatever the division works out, is wrong without any error.",
            "Turn one side into a real before dividing: real(total) / count.",
            """
            program marks
              implicit none
              integer :: total, count
              real :: average
              total = 7
              count = 2
              average = real(total) / count
              print *, average
            end program marks
            """),

        GuideFor(["logic-fortran-no-implicit-none"], "A program without implicit none",
            "Without implicit none, Fortran makes any name it has not seen a new variable, without being asked - a name starting with " +
            "i to n is a whole number, any other a real. So a misspelt name is not an error: it is a new variable with no value.",
            "A misspelling builds and runs, and the program works with the wrong variable.",
            "Put implicit none on the line after program, and declare every variable.",
            """
            program marks
              implicit none
              integer :: total
              total = 10
              print *, total + 1
            end program marks
            """),

        GuideFor(["logic-fortran-total-not-started"], "A total added to before it is given a value",
            "Fortran gives a variable no starting value of its own: until it is given one, it holds whatever was in that memory. A " +
            "total that is only ever added to starts from that.",
            "The total can be right by chance, when the memory happened to hold 0, and wrong on another run or another computer.",
            "Give the total its starting value before the loop: total = 0.",
            """
            program marks
              implicit none
              integer :: total, i
              total = 0
              do i = 1, 3
                total = total + i
              end do
              print *, total
            end program marks
            """),

        GuideFor(["logic-fortran-float-equality"], "Decimal numbers compared exactly",
            "Decimals are stored in binary, which holds most of them only nearly: in double precision 0.1d0 + 0.2d0 is " +
            "0.30000000000000004, not 0.3.",
            "== asks for an exact match, so a total worked out by arithmetic can fail to equal the decimal it should.",
            "Compare within a tiny tolerance: abs(total - 0.3d0) < 1d-9.",
            """
            program marks
              implicit none
              double precision :: total
              total = 0.1d0 + 0.2d0
              if (abs(total - 0.3d0) < 1d-9) then
                print *, 'equal'
              end if
            end program marks
            """),

        GuideFor(["logic-fortran-power-of-one-over"], "A root written as a power of 1 over a number",
            "In maths the square root of side can be written as side to the power 1/2. In Fortran ** is worked out before /, so " +
            "side ** 1/2 is side ** 1 - side itself - divided by 2. With 16.0 it is 8.0, not 4.0. Putting 1 / 3 in brackets is not " +
            "enough either: two whole numbers divide to a whole number in Fortran, so 1 / 3 is 0, and 27.0 ** (1 / 3) is 1.0.",
            "The program works out half of the number - or a third, for 1/3 - instead of its root, and gfortran -Wall says nothing " +
            "of it.",
            "Raise it to a decimal fraction: side ** 0.5, or side ** (1.0 / 3) for a cube root - or use sqrt(side).",
            """
            program roots
              implicit none
              real :: side
              side = 16.0
              print *, side ** 0.5
            end program roots
            """),

        GuideFor(["logic-fortran-percent-divided-first"], "A percentage that divides first",
            "In Fortran, / of two integers gives an integer - the part below 1 is thrown away. In score / total * 100 the division " +
            "comes first, so for 45 out of 60 it is 0, and 0 * 100 is 0.",
            "Every score below the total comes out as 0%, and gfortran -Wall says nothing of it.",
            "Multiply first - score * 100 / total - so only the last division drops a fraction: 45 * 100 / 60 is 75.",
            """
            program marks
              implicit none
              integer :: score, total
              score = 45
              total = 60
              print *, score * 100 / total
            end program marks
            """),

        GuideFor(["logic-fortran-single-precision-constant"], "A double given a single-precision decimal",
            "In Fortran a decimal written plainly - 0.1 - is a default real, single precision, which keeps about seven digits. " +
            "Given to a double precision variable it is rounded to those seven digits first, so the variable holds " +
            "0.10000000149011612, not 0.1.",
            "The double holds a less exact value than written, which shows in the later digits of every sum, and gfortran -Wall " +
            "says nothing of it.",
            "Write the decimal as a double, with d0 after it: rate = 0.1d0.",
            """
            program marks
              implicit none
              double precision :: rate
              rate = 0.1d0
              print *, rate
            end program marks
            """),

        GuideFor(["logic-fortran-odd-test-negative"], "An odd number looked for with mod(n, 2) == 1",
            "mod gives what is left over after dividing, and it keeps the sign of the number divided: mod(7, 2) is 1, but mod(-7, 2) is " +
            "-1. So mod(n, 2) == 1 is true only for odd numbers above 0 - a negative odd number leaves -1, and is taken for even.",
            "The program is right until it meets a negative odd number, which it treats as even, and gfortran -Wall says nothing.",
            "Ask whether anything is left over: mod(n, 2) /= 0 is true for every odd number, negative or not. modulo, which takes the " +
            "sign of the number divided by, gives 1 for both, so modulo(n, 2) == 1 is right too.",
            """
            program parity
              implicit none
              integer :: n
              n = -3
              if (mod(n, 2) /= 0) then
                print *, 'odd'
              end if
            end program parity
            """),

        GuideFor(["logic-fortran-implicit-save"], "A value given where a name is declared, in a subroutine",
            "In a subroutine or function, integer :: count = 0 does not set count to 0 each call. A value given in the declaration gives " +
            "the name the save attribute: it is set once, when the program starts, and keeps whatever it held when the subroutine last " +
            "ended.",
            "The second call starts from where the first left off - a counter set to 0 this way printed 1 and then 2 - and gfortran -Wall " +
            "says nothing.",
            "Declare it without a value, and give it its value as the first thing the subroutine does: integer :: count, then count = 0.",
            """
            program calls
              implicit none
              call count_up()
              call count_up()
            contains
              subroutine count_up()
                integer :: count
                count = 0
                count = count + 1
                print *, count
              end subroutine count_up
            end program calls
            """),

        GuideFor(["logic-fortran-padded-text-printed"], "Text declared with a length, printed before something else",
            "A character(len=10) name always holds ten characters: the text given to it, and spaces after it up to ten. Printed with " +
            "more after it, those spaces are printed too.",
            "print *, 'Hello ', name, '!' printed \"Hello Ada       !\", with no error.",
            "Print it without its spaces: trim(name).",
            """
            program greeting
              implicit none
              character(len=10) :: name
              name = 'Ada'
              print *, 'Hello ', trim(name), '!'
            end program greeting
            """),

        GuideFor(["logic-fortran-len-of-padded-text"], "len of text declared with a length",
            "len(name) is the length name was declared with - always 10 for a character(len=10) - not the length of the text in it, as " +
            "name holds spaces after its text up to that length.",
            "A count of the text's characters comes out as the declared length: 10 for Ada.",
            "Count the text with len_trim(name), which stops at the last character that is not a space.",
            """
            program lengths
              implicit none
              character(len=10) :: name
              name = 'Ada'
              print *, len_trim(name)
            end program lengths
            """),

        GuideFor(["logic-fortran-branch-never-reached"], "An else if that can never run",
            "An if - else if chain takes the first branch whose test passes. If an earlier test lets through every value a later one " +
            "does - score >= 50 before score >= 70 - the later branch never gets a value.",
            "That branch never runs, so no input can test it - a score of 75 got C, not B - and gfortran -Wall says nothing.",
            "Put the strictest test first: score >= 70, then score >= 50.",
            """
            program grades
              implicit none
              integer :: score
              score = 75
              if (score >= 70) then
                print *, 'B'
              else if (score >= 50) then
                print *, 'C'
              else
                print *, 'F'
              end if
            end program grades
            """),

        GuideFor(["logic-fortran-code-after-return"], "Code after return",
            "return leaves the function or subroutine there and then - stop ends the program, and exit and cycle leave the loop or its " +
            "pass. A statement straight after one of them, in the same block, is never reached.",
            "That statement never runs, so no run of the program can test it, and gfortran -Wall says nothing.",
            "Move the statement before the return if it was meant to run, or take it out if it was not.",
            """
            program counting
              implicit none
              print *, total(2)
            contains
              integer function total(count)
                integer, intent(in) :: count
                total = count
              end function total
            end program counting
            """),

        GuideFor(["logic-fortran-loop-never-runs"], "A count down written as do i = 10, 1",
            "A do loop counts up by 1 unless it is given a step: do i = 10, 1 starts at 10, which is already past 1, so it runs no times at " +
            "all. gfortran -Wall says it \"will be executed zero times\".",
            "Nothing in the loop ever runs, so it cannot be tested.",
            "Give it a step of -1: do i = 10, 1, -1.",
            """
            program countdown
              implicit none
              integer :: count
              do count = 3, 1, -1
                print *, count
              end do
            end program countdown
            """),
    ];
}
