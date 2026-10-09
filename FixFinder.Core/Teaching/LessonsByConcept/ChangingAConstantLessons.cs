namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Changing something that cannot change, in each language: what it does, shown by running a program that does it.</summary>
internal static class ChangingAConstantLessons
{
    private static Concept Concept => Concepts.ChangingAConstant;

    public static IReadOnlyList<Lesson> All { get; } = [Java, JavaScript, Scala];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "A variable declared final can be given a value once. javac refuses a line that gives it another, before any of the " +
        "program runs.",
        [
            new WorkedExample("Adding a bonus to a score",
                Broken: """
                    public class Score {
                        public static void main(String[] args) {
                            final int score = 10;
                            score = score + 5;
                            System.out.println(score);
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("cannot assign a value to final variable", 4),
                Fixed: """
                    public class Score {
                        public static void main(String[] args) {
                            int score = 10;
                            score = score + 5;
                            System.out.println(score);
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("15"),
                WhatChanged: "Without final, score is a variable that may be given new values."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "A name made with const keeps the value it is first given. JavaScript only finds out when the line that changes it " +
        "runs, and stops the program there with TypeError; let makes a name whose value can change.",
        [
            new WorkedExample("Adding a bonus to a score",
                Broken: """
                    const score = 10;
                    score = score + 5;
                    console.log(score);
                    """,
                BrokenDoes: Behaviour.Stopping("TypeError", 2),
                Fixed: """
                    let score = 10;
                    score = score + 5;
                    console.log(score);
                    """,
                FixedDoes: Behaviour.Printing("15"),
                WhatChanged: "let makes score a name whose value can be changed."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "A name made with val keeps the value it is first given, and the compiler refuses a line that gives it another, " +
        "before any of the program runs. var makes a name whose value can change.",
        [
            new WorkedExample("Adding a bonus to a score",
                Broken: """
                    object Score {
                      def main(args: Array[String]): Unit = {
                        val score = 10
                        score = score + 5
                        println(score)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Refused("Reassignment to val", 4),
                Fixed: """
                    object Score {
                      def main(args: Array[String]): Unit = {
                        var score = 10
                        score = score + 5
                        println(score)
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("15"),
                WhatChanged: "var makes score a name whose value can be changed."),
        ]);
}
