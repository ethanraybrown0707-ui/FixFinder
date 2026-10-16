namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>A loop that never ends, in each language: what it does, shown by running a program that does it.</summary>
internal static class EndlessLoopLessons
{
    private static Concept Concept => Concepts.EndlessLoop;

    public static IReadOnlyList<Lesson> All { get; } = [Python, Java, Php, Lua, Perl, Dart, Fortran, PowerShell, Elixir];

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "A while loop runs until its condition is false, and nothing makes the condition change but the loop's own lines. " +
        "A loop that never moves on to the next position, or that steps past the value it is waiting for, runs until it is " +
        "stopped.",
        [
            new WorkedExample("Adding up marks",
                Broken: """
                    marks = [70, 80, 90]
                    total = 0
                    i = 0
                    while i < len(marks):
                        total = total + marks[i]
                    print(total)
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    marks = [70, 80, 90]
                    total = 0
                    i = 0
                    while i < len(marks):
                        total = total + marks[i]
                        i = i + 1
                    print(total)
                    """,
                FixedDoes: Behaviour.Printing("240"),
                WhatChanged: "i = i + 1 moves the loop on to the next mark, so i reaches len(marks) and the loop ends."),
            new WorkedExample("Counting down in threes",
                Broken: """
                    number = 10
                    steps = 0
                    while number != 0:
                        number = number - 3
                        steps = steps + 1
                    print(steps)
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    number = 10
                    steps = 0
                    while number > 0:
                        number = number - 3
                        steps = steps + 1
                    print(steps)
                    """,
                FixedDoes: Behaviour.Printing("4"),
                WhatChanged: "number goes 10, 7, 4, 1, -2 and is never exactly 0, so the loop now stops once it is 0 or below."),
        ]);

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A while loop runs until its condition is false, and nothing makes the condition change but the loop's own lines. " +
        "A loop whose position is never moved on runs until it is stopped.",
        [
            new WorkedExample("Adding up marks",
                Broken: """
                    public class Total {
                        public static void main(String[] args) {
                            int[] marks = {70, 80, 90};
                            int total = 0;
                            int i = 0;
                            while (i < marks.length) {
                                total = total + marks[i];
                            }
                            System.out.println(total);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    public class Total {
                        public static void main(String[] args) {
                            int[] marks = {70, 80, 90};
                            int total = 0;
                            int i = 0;
                            while (i < marks.length) {
                                total = total + marks[i];
                                i++;
                            }
                            System.out.println(total);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("240"),
                WhatChanged: "i++ moves the loop on to the next mark, so i reaches marks.length and the loop ends."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "A while loop runs for as long as its condition is true, so something inside it has to change what the condition checks. " +
        "When nothing does, the loop never ends: PHP gives no error, and the program has to be stopped.",
        [
            new WorkedExample("Adding up to three",
                Broken: """
                    <?php
                    $count = 1;
                    $total = 0;
                    while ($count <= 3) {
                        $total += $count;
                    }
                    echo $total;
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    <?php
                    $count = 1;
                    $total = 0;
                    while ($count <= 3) {
                        $total += $count;
                        $count++;
                    }
                    echo $total;
                    """,
                FixedDoes: Behaviour.Printing("6"),
                WhatChanged: "$count++ moves the count on each time, so the condition is false once it passes 3."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "A while loop runs for as long as its condition is true, so something inside it has to change what the condition checks. " +
        "When nothing does, the loop never ends: Lua gives no error, and the program has to be stopped.",
        [
            new WorkedExample("Adding up to three",
                Broken: """
                    local count = 1
                    local total = 0
                    while count <= 3 do
                      total = total + count
                    end
                    print(total)
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    local count = 1
                    local total = 0
                    while count <= 3 do
                      total = total + count
                      count = count + 1
                    end
                    print(total)
                    """,
                FixedDoes: Behaviour.Printing("6"),
                WhatChanged: "count = count + 1 moves the count on each time, so the condition is false once it passes 3."),
        ]);

    private static Lesson Perl => new(Concept, CodeLanguage.Perl,
        "A while loop runs for as long as its condition is true, so something inside it has to change what the condition checks. " +
        "When nothing does, the loop never ends: Perl gives no error, and the program has to be stopped.",
        [
            new WorkedExample("Adding up to three",
                Broken: """
                    use strict;
                    use warnings;

                    my $count = 1;
                    my $total = 0;
                    while ($count <= 3) {
                        $total += $count;
                    }
                    print "$total\n";
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    use strict;
                    use warnings;

                    my $count = 1;
                    my $total = 0;
                    while ($count <= 3) {
                        $total += $count;
                        $count++;
                    }
                    print "$total\n";
                    """,
                FixedDoes: Behaviour.Printing("6"),
                WhatChanged: "$count++ moves the count on each time, so the condition is false once it passes 3."),
        ]);

    private static Lesson Dart => new(Concept, CodeLanguage.Dart,
        "A while loop runs for as long as its condition is true, so something inside it has to change what the condition checks. " +
        "When nothing does, the loop never ends: Dart gives no error, and the program has to be stopped.",
        [
            new WorkedExample("Adding up to three",
                Broken: """
                    void main() {
                      var count = 1;
                      var total = 0;
                      while (count <= 3) {
                        total += count;
                      }
                      print(total);
                    }
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    void main() {
                      var count = 1;
                      var total = 0;
                      while (count <= 3) {
                        total += count;
                        count++;
                      }
                      print(total);
                    }
                    """,
                FixedDoes: Behaviour.Printing("6"),
                WhatChanged: "count++ moves the count on each time, so the condition is false once it passes 3."),
        ]);

    private static Lesson Fortran => new(Concept, CodeLanguage.Fortran,
        "A do while loop runs for as long as its condition is true, so something inside it has to change what the condition checks. " +
        "When nothing does, the loop never ends: Fortran gives no error, and the program has to be stopped.",
        [
            new WorkedExample("Adding up to three",
                Broken: """
                    program marks
                      implicit none
                      integer :: count, total
                      count = 1
                      total = 0
                      do while (count <= 3)
                        total = total + count
                      end do
                      print '(I0)', total
                    end program marks
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    program marks
                      implicit none
                      integer :: count, total
                      count = 1
                      total = 0
                      do while (count <= 3)
                        total = total + count
                        count = count + 1
                      end do
                      print '(I0)', total
                    end program marks
                    """,
                FixedDoes: Behaviour.Printing("6"),
                WhatChanged: "count = count + 1 moves the count on each time, so the condition is false once it passes 3."),
        ]);

    private static Lesson PowerShell => new(Concept, CodeLanguage.PowerShell,
        "A while loop runs for as long as its condition is true, so something inside it has to change what the condition checks. " +
        "When nothing does, the loop never ends: PowerShell gives no error, and the script has to be stopped.",
        [
            new WorkedExample("Adding up to three",
                Broken: """
                    $count = 1
                    $total = 0
                    while ($count -le 3) {
                        $total += $count
                    }
                    Write-Output $total
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    $count = 1
                    $total = 0
                    while ($count -le 3) {
                        $total += $count
                        $count++
                    }
                    Write-Output $total
                    """,
                FixedDoes: Behaviour.Printing("6"),
                WhatChanged: "$count++ moves the count on each time, so the condition is false once it passes 3."),
        ]);

    private static Lesson Elixir => new(Concept, CodeLanguage.Elixir,
        "Elixir has no while loop: a function that calls itself is its loop. When the call that should stop it never comes - from(0), " +
        "when n goes 5, 3, 1, -1 - it calls itself for ever, and as calling itself is the last thing each call does, it never runs out of room.",
        [
            new WorkedExample("A countdown in twos",
                Broken: """
                    defmodule Countdown do
                      def from(0), do: IO.puts("lift off")
                      def from(n), do: from(n - 2)
                    end

                    Countdown.from(5)
                    """,
                BrokenDoes: Behaviour.Running(),
                Fixed: """
                    defmodule Countdown do
                      def from(n) when n <= 0, do: IO.puts("lift off")
                      def from(n), do: from(n - 2)
                    end

                    Countdown.from(5)
                    """,
                FixedDoes: Behaviour.Printing("lift off"),
                WhatChanged: "The first clause now stops for any n of 0 or less, so the countdown stops at -1."),
        ]);
}
