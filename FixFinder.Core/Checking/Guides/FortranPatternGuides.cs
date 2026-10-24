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
    ];
}
