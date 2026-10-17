namespace FixFinder.Core.Teaching.LessonsByConcept;

/// <summary>Equal values, or the same object, in each language: what it does, shown by running a program that does it.</summary>
internal static class SameValueOrSameObjectLessons
{
    private static Concept Concept => Concepts.SameValueOrSameObject;

    public static IReadOnlyList<Lesson> All { get; } = [Java, Python, JavaScript, Scala, Php, Lua, Perl, Dart, PowerShell, Elixir, Erlang];

    private static Lesson Java => new(Concept, CodeLanguage.Java,
        "For objects - Strings among them - == asks whether two names are the very same object, and equals asks whether they hold " +
        "the same value. Text read in or built while the program runs is a new String, so == can say false for two equal texts.",
        [
            new WorkedExample("A password typed in",
                Broken: """
                    public class Password {
                        public static void main(String[] args) {
                            String typed = new StringBuilder("secret").toString();
                            if (typed == "secret") {
                                System.out.println("welcome");
                            } else {
                                System.out.println("wrong password");
                            }
                        }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("wrong password"),
                Fixed: """
                    public class Password {
                        public static void main(String[] args) {
                            String typed = new StringBuilder("secret").toString();
                            if (typed.equals("secret")) {
                                System.out.println("welcome");
                            } else {
                                System.out.println("wrong password");
                            }
                        }
                    }
                    """,
                FixedDoes: Behaviour.Printing("welcome"),
                WhatChanged: "equals compares the letters; == compared whether typed is the very same String as \"secret\", which a String built while the program runs is not."),
        ]);

    private static Lesson Python => new(Concept, CodeLanguage.Python,
        "== asks whether two values are equal, and is asks whether they are the very same object. Two lists made apart are equal " +
        "when they hold the same items, but they are never the same list.",
        [
            new WorkedExample("Two lists of the same marks",
                Broken: """
                    first = [70, 80]
                    second = [70, 80]
                    print(first is second)
                    """,
                BrokenDoes: Behaviour.Printing("False"),
                Fixed: """
                    first = [70, 80]
                    second = [70, 80]
                    print(first == second)
                    """,
                FixedDoes: Behaviour.Printing("True"),
                WhatChanged: "== compares the items the lists hold; is asked whether they are one and the same list, which two lists made apart never are."),
        ]);

    private static Lesson JavaScript => new(Concept, CodeLanguage.JavaScript,
        "== turns its two sides into the same type before comparing - so the text \"0\" is == to the number 0 - while === compares " +
        "them as they are. For arrays and objects, both ask whether they are the very same one, never whether they hold the same items.",
        [
            new WorkedExample("A number typed in",
                Broken: """
                    const typed = "0";
                    console.log(typed == 0);
                    """,
                BrokenDoes: Behaviour.Printing("true"),
                Fixed: """
                    const typed = "0";
                    console.log(typed === 0);
                    """,
                FixedDoes: Behaviour.Printing("false"),
                WhatChanged: "=== does not change either side's type, so text is never equal to a number."),
            new WorkedExample("Two arrays of the same marks",
                Broken: """
                    const first = [70, 80];
                    const second = [70, 80];
                    console.log(first === second);
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    const first = [70, 80];
                    const second = [70, 80];
                    console.log(first.length === second.length && first.every((mark, i) => mark === second[i]));
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "=== asked whether they are the same array; the fixed line compares the items one by one."),
        ]);

    private static Lesson Scala => new(Concept, CodeLanguage.Scala,
        "== on two Lists compares the items they hold. == on two Arrays does not: it asks whether they are the very same array, so " +
        "two arrays made apart are never ==, whatever they hold. sameElements compares an array's items.",
        [
            new WorkedExample("Two arrays of the same marks",
                Broken: """
                    object Marks {
                      def main(args: Array[String]): Unit = {
                        val first = Array(70, 80)
                        val second = Array(70, 80)
                        println(first == second)
                      }
                    }
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    object Marks {
                      def main(args: Array[String]): Unit = {
                        val first = Array(70, 80)
                        val second = Array(70, 80)
                        println(first.sameElements(second))
                      }
                    }
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "sameElements compares the items; == asked whether they are one and the same array."),
        ]);

    private static Lesson Php => new(Concept, CodeLanguage.Php,
        "== compares two texts as numbers when both look like numbers - so \"1e3\" == \"1000\" is true, 1e3 being 1000 written " +
        "another way - while === compares them exactly as they are. For objects, == asks whether they hold the same values, and " +
        "=== whether they are the very same object.",
        [
            new WorkedExample("A code typed in",
                Broken: """
                    <?php
                    $typed = "1e3";
                    if ($typed == "1000") {
                        echo "match";
                    } else {
                        echo "no match";
                    }
                    """,
                BrokenDoes: Behaviour.Printing("match"),
                Fixed: """
                    <?php
                    $typed = "1e3";
                    if ($typed === "1000") {
                        echo "match";
                    } else {
                        echo "no match";
                    }
                    """,
                FixedDoes: Behaviour.Printing("no match"),
                WhatChanged: "=== compares the two texts as they are, without reading them as numbers."),
            new WorkedExample("Two marks of the same value",
                Broken: """
                    <?php
                    class Mark {
                        public function __construct(public int $value) {}
                    }
                    $first = new Mark(70);
                    $second = new Mark(70);
                    echo $first === $second ? "same" : "different";
                    """,
                BrokenDoes: Behaviour.Printing("different"),
                Fixed: """
                    <?php
                    class Mark {
                        public function __construct(public int $value) {}
                    }
                    $first = new Mark(70);
                    $second = new Mark(70);
                    echo $first == $second ? "same" : "different";
                    """,
                FixedDoes: Behaviour.Printing("same"),
                WhatChanged: "== compares what the two objects hold; === asked whether they are one and the same object, which two made with new never are."),
        ]);

    private static Lesson Lua => new(Concept, CodeLanguage.Lua,
        "== compares numbers and text by their value: \"ada\" == \"ada\" is true. On two tables it asks whether they are the very same " +
        "table, never whether they hold the same items, so two tables made apart are never ==.",
        [
            new WorkedExample("Two lists of the same marks",
                Broken: """
                    local first = {70, 80}
                    local second = {70, 80}
                    print(first == second)
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    local first = {70, 80}
                    local second = {70, 80}
                    local same = #first == #second
                    for i = 1, #first do
                      same = same and first[i] == second[i]
                    end
                    print(same)
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "== asked whether they are the same table; the fixed code compares the items one by one."),
        ]);

    private static Lesson Perl => new(Concept, CodeLanguage.Perl,
        "Perl has two kinds of comparison: == and != for numbers, eq and ne for text. == turns both sides into numbers first, and a " +
        "word becomes 0 - so \"yes\" == \"no\" is true. With use warnings Perl warns, Argument isn't numeric, but carries on.",
        [
            new WorkedExample("Checking an answer",
                Broken: """
                    use strict;
                    use warnings;

                    my $answer = "yes";
                    if ($answer == "no") {
                        print "no\n";
                    } else {
                        print "yes\n";
                    }
                    """,
                BrokenDoes: Behaviour.Printing("no"),
                Fixed: """
                    use strict;
                    use warnings;

                    my $answer = "yes";
                    if ($answer eq "no") {
                        print "no\n";
                    } else {
                        print "yes\n";
                    }
                    """,
                FixedDoes: Behaviour.Printing("yes"),
                WhatChanged: "eq compares the two pieces of text as text, so \"yes\" and \"no\" are different."),
        ]);

    private static Lesson Dart => new(Concept, CodeLanguage.Dart,
        "== compares numbers and text by their value. On two lists it asks whether they are the very same list, never whether they " +
        "hold the same items, so two lists made apart are never ==.",
        [
            new WorkedExample("Two lists of the same marks",
                Broken: """
                    void main() {
                      var first = [70, 80];
                      var second = [70, 80];
                      print(first == second);
                    }
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    void main() {
                      var first = [70, 80];
                      var second = [70, 80];
                      print(first.length == second.length && Iterable.generate(first.length).every((i) => first[i] == second[i]));
                    }
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "== asked whether they are the same list; the fixed line compares the items one by one."),
        ]);

    private static Lesson PowerShell => new(Concept, CodeLanguage.PowerShell,
        "-eq compares text without minding capital letters, so \"Ada\" -eq \"ada\" is true. -ceq compares it letter for letter, capitals " +
        "and all - and so on for each comparison: -cne, -clt, -cgt.",
        [
            new WorkedExample("Two spellings of a name",
                Broken: """
                    if ("Ada" -eq "ada") {
                        Write-Output "same"
                    } else {
                        Write-Output "different"
                    }
                    """,
                BrokenDoes: Behaviour.Printing("same"),
                Fixed: """
                    if ("Ada" -ceq "ada") {
                        Write-Output "same"
                    } else {
                        Write-Output "different"
                    }
                    """,
                FixedDoes: Behaviour.Printing("different"),
                WhatChanged: "-ceq counts a capital A as different from a small a."),
        ]);

    private static Lesson Elixir => new(Concept, CodeLanguage.Elixir,
        "Text in single quotes is not text in Elixir but a charlist - a list of character codes - so 'Ada' == \"Ada\" is false: the " +
        "two hold the same letters as different kinds of value. Elixir warns that single quotes for text are deprecated.",
        [
            new WorkedExample("Is the name Ada?",
                Broken: """
                    name = 'Ada'
                    IO.puts(name == "Ada")
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    name = "Ada"
                    IO.puts(name == "Ada")
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "In double quotes the name is text, the same kind of value as \"Ada\", so the two are equal."),
        ]);

    private static Lesson Erlang => new(Concept, CodeLanguage.Erlang,
        "yes without quotes is an atom - a fixed value of its own - and \"yes\" in double quotes is text. They look alike, but they are " +
        "different kinds of value, so they are never equal.",
        [
            new WorkedExample("Did they say yes?",
                Broken: """
                    -module(answers).
                    -export([main/0]).

                    agreed(Answer) -> Answer == yes.

                    main() ->
                        io:format("~p~n", [agreed("yes")]).
                    """,
                BrokenDoes: Behaviour.Printing("false"),
                Fixed: """
                    -module(answers).
                    -export([main/0]).

                    agreed(Answer) -> Answer == "yes".

                    main() ->
                        io:format("~p~n", [agreed("yes")]).
                    """,
                FixedDoes: Behaviour.Printing("true"),
                WhatChanged: "The answer is compared with the text \"yes\", the kind of value it is."),
        ]);
}
