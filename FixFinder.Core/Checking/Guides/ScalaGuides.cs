using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Scala's compile errors and warnings - in Scala 3's words and Scala 2's - and the JVM's exceptions as a Scala program
/// meets them, each explained for someone new to programming.
/// </summary>
/// <remarks>
/// Every message matched here is one a Scala compiler or a Scala program was seen to give: Scala 3.8.4 and Scala 2.13.18,
/// built and run with Scala CLI. Every example compiles and runs with both.
/// </remarks>
internal static class ScalaGuides
{
    private const string NothingRuns = "Scala refuses to build the program while this is wrong, so no part of it runs - not even the lines before this one.";

    private const string Crashes = "The program stops at this line, and nothing after it runs.";

    private static GuideEntry Compile(string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        ExceptionTypes = ["compile error"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry Warned(string pattern, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["compile warning"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private static GuideEntry Thrown(string type, string explanation, string fix, string example, string? pattern = null, string? why = null) => new()
    {
        ExceptionTypes = [type],
        Message = pattern is null ? null : new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? Crashes, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Compile(@"^Not found:|^not found: (?:value|type|object|method)",
            "Every name in Scala - a value, a method, a class, a type - has to be defined before it can be used: with val, var, def, " +
            "class or object, or brought in with an import. Scala cannot find this name from here. It treats capital letters as " +
            "different letters, so String and string are two different names - and Scala 3 often says which name it thinks was meant.",
            "Check the spelling and the capitals against where the name is defined, define it where this line can see it, or add the import it needs.",
            """
            val name = "Ada"
            println("Hello, " + name)
            """),

        Compile(@"^Found:|^type mismatch",
            "Scala checks that every value is of the type the place it goes needs, and this one is not: the message gives the type it " +
            "found and the type that was required. A piece of text where a number is needed is one; a Double where an Int is needed is " +
            "another, as Scala never turns a Double into an Int by itself - that would throw away everything after the point.",
            "Give the place a value of the type it needs - convert it yourself with .toInt, .toDouble or .toString - or change the type the place is declared with.",
            """
            val price: Double = 2.5
            val total: Double = 3 * price
            """),

        Compile(@"is not a member of",
            "A value can only be asked for what its type has: a List has a length, but nothing called lenght. This line asks a value for " +
            "something its type does not have - usually a misspelt name, or a method that belongs to another type. Scala often says which " +
            "name it thinks was meant.",
            "Check the spelling against what the value's type really has, or turn the value into the type that has it first.",
            """
            val words = List("a", "b")
            println(words.length)
            """),

        Compile(@"^Reassignment to val|^reassignment to val",
            "A val is given its value once and keeps it - that is what makes it a val rather than a var. This line gives a val a new " +
            "value. A value that really has to change is made with var instead.",
            "Make it a var where it is defined - or keep it a val and give the new value a val of its own.",
            """
            var count = 0
            count = count + 1
            """),

        Compile(@"too many arguments|missing argument|not enough arguments",
            "A method is defined with a list of parameters, and every call has to give it one value for each, in the same order. This " +
            "call gives it more, or fewer, than it was defined with - the message shows the parameters the method has.",
            "Give one value for each parameter, in the order the method lists them.",
            """
            def add(a: Int, b: Int): Int = a + b
            println(add(1, 2))
            """),

        Compile(@"^'=' expected, but '\{' found",
            "Scala expected an = sign here and found a {. A method's body comes after =, as in def main(args: Array[String]): Unit = { ... }. " +
            "Scala 2 let the = be left out before a body in braces - a method written that way gave nothing back - but Scala 3 no longer " +
            "allows it.",
            "Put : Unit = between the method's parameters and its { - or, for a method that gives a value back, a colon, its type and =.",
            """
            def main(args: Array[String]): Unit = {
              println("hello")
            }
            """),

        Compile(@"^unclosed string literal",
            "A piece of text in Scala starts and ends with a double quote \". This one starts, but never ends on its line, so Scala " +
            "cannot tell where the text stops and the code starts again.",
            "Add the closing \" where the text should end.",
            """
            println("hi")
            """),

        Compile(@"^'[)\]}]' expected|^Missing closing brace",
            "Brackets and braces come in pairs: every ( needs a ), every { a } and every [ a ]. Scala reached a point where one was " +
            "still open - and as it only notices when it meets something that cannot be inside the pair, the one left open can be a " +
            "line or more above where it says.",
            "Find the bracket or brace that is opened on or above this line, and close it where what is inside it ends.",
            """
            val xs = List(1, 2)
            println(xs)
            """),

        Compile(@"cannot be compared with == or !=",
            "== asks whether two values are equal, and Scala 3 refuses to compare values of two types that can never be equal - a number " +
            "and a piece of text, say - because the answer could only ever be false. Usually one of them was meant to be the other type: " +
            "\"3\" in quotes is text, 3 without is a number.",
            "Compare values of the same type: turn one of them into the other's type first, with .toString or .toInt, or compare with the value you meant.",
            """
            val count = 3
            if (count == 3) println("three")
            """),

        Compile(@"' expected|^expected (?:start of definition|class or object definition)|^illegal start of",
            "Scala could not read the code here: something it needs is missing - often an =, a bracket, a comma or the end of the line " +
            "before - or something is where it cannot be. Scala names what it expected and what it found instead.",
            "Look at what Scala says it expected, on this line and at the end of the line before it, and add what is missing or move what is out of place.",
            """
            val total = add(1, 2)
            """),

        Compile(@"^Found several main classes",
            "A Scala program starts at a main, and the files built together hold more than one - Scala CLI names them - so it cannot tell " +
            "which to start.",
            "Choose the file whose main you want to run: when a file has one main of its own, FixFinder runs that one.",
            """
            object Grades {
              def main(args: Array[String]): Unit = println("grades")
            }
            """),

        Warned(@"^match may not be exhaustive",
            "A match has a case for each value it is ready for, and this one leaves some out - the compiler names one it would fail on. " +
            "When a value comes along that no case fits, the program stops with a scala.MatchError.",
            "The program crashes with a MatchError the first time a value no case fits comes along.",
            "Add a case for each value the warning names - or a final case _ => for every value the others leave.",
            """
            mark match {
              case Pass => "passed"
              case Fail => "failed"
              case Merit => "merit"
            }
            """),

        Warned(@"^Unreachable case|^unreachable code|^patterns after a variable pattern cannot match",
            "A match tries its cases from the top and stops at the first that fits. A case above this one already fits every value this " +
            "one could - case _, or a case that is just a name, fits anything - so this case can never be reached.",
            "What this case does never happens, whatever the value is.",
            "Put the case that fits anything last, after the cases for particular values.",
            """
            n match {
              case 1 => "one"
              case _ => "any"
            }
            """),

        Warned(@"will always yield (?:false|true)",
            "This compares values of two types that can never be equal - a number and a piece of text, say - so the answer is the same " +
            "every time, and is decided before the program runs. Usually one of them was meant to be the other type: \"3\" in quotes is " +
            "text, 3 without is a number.",
            "The comparison always comes out the same, so what it guards always runs, or never does.",
            "Compare values of the same type: turn one of them into the other's type first, with .toString or .toInt.",
            """
            val count = 3
            if (count == 3) println("three")
            """),

        Warned(@"^unused (?:import|local definition|private member)|is never used",
            "This is made - imported, or given a name - and then never used anywhere. It does the program no harm, but it is often a " +
            "sign that something else was used where this was meant, or that it was left behind after a change.",
            "Nothing goes wrong, but it is clutter - and sometimes a sign the wrong name was used somewhere else.",
            "Use it where it was meant to be used, or remove it.",
            """
            val spare = 3
            println(spare)
            """),

        Warned(@"is deprecated|^Procedure syntax",
            "What this line uses still works in this version of Scala, but the people who make Scala have marked it as on its way out, " +
            "and a later version may not accept it. The warning says what to use instead.",
            "It works now, but may stop building with a later version of Scala.",
            "Change it to what the warning suggests.",
            """
            def main(args: Array[String]): Unit = {
              println("hello")
            }
            """),

        Thrown("ArithmeticException",
            "Dividing a whole number - an Int or a Long - by zero has no answer, so the program stops with an ArithmeticException. The " +
            "number divided by is zero at this point - often because it counts items, and there were none. A Double divided by zero does " +
            "not stop the program: it gives Infinity - or NaN, for 0.0 / 0.",
            "Check the number is not zero before dividing by it, and decide what the answer should be when it is.",
            """
            val average = if (scores.isEmpty) 0 else scores.sum / scores.length
            """,
            why: "The program crashes whenever the number divided by is zero - often when a list is empty."),

        Thrown("NoSuchElementException",
            "An Option holds a value - Some(value) - or nothing at all - None. .get takes the value out, and a None has none to take, so " +
            "the program stops. A Map's get gives back None for a key that is not there.",
            "Use getOrElse with the value to have when there is none, or match on Some and None.",
            """
            val age = ages.get("Bob").getOrElse(0)
            """,
            pattern: @"^None\.get"),

        Thrown("NoSuchElementException",
            "Looking a key up in a Map with map(key) needs the key to be there, and this one is not - the message names it - so the program stops.",
            "Use getOrElse(key, value) with the value to have when the key is not there, or check map.contains(key) first.",
            """
            val age = ages.getOrElse("Bob", 0)
            """,
            pattern: @"^key not found"),

        Thrown("NoSuchElementException",
            "head takes the first item of a list, and last the last one - and an empty list has neither, so the program stops.",
            "Check the list is not empty first, or use headOption or lastOption, which give back None for an empty list.",
            """
            val first = marks.headOption.getOrElse(0)
            """,
            pattern: @"^(?:head|last) of empty list"),

        Thrown("UnsupportedOperationException",
            "The largest, the smallest or the total of no values at all has no answer, and neither has the rest of an empty list after " +
            "its first item - so asking an empty list for one stops the program. The list is empty at this point.",
            "Check the list is not empty first - or use maxOption or minOption, which give back None for an empty list.",
            """
            val best = if (marks.isEmpty) 0 else marks.max
            """,
            pattern: @"^empty\.(?:max|min|reduceLeft|reduceRight)|^tail of empty list"),

        Thrown("NumberFormatException",
            "toInt turns text such as \"42\" into a number. It can only do that when the text is exactly a whole number - no spaces, no " +
            "letters, no decimal point, and not empty - and this text is not: the message shows it.",
            "Trim the text, or use toIntOption, which gives back None rather than stopping when the text is not a number.",
            """
            val age = typed.trim.toIntOption.getOrElse(0)
            """,
            why: "The program crashes as soon as it reads text that is not a number."),

        Thrown("ArrayIndexOutOfBoundsException",
            "The items of an Array are numbered from 0, so an Array of 3 items has positions 0, 1 and 2. This line asks for a position " +
            "outside that - often because a loop runs 0 to xs.length, which goes one past the end, where 0 until xs.length stops in time.",
            "Loop over xs.indices, or with until, or over the items themselves.",
            """
            for (i <- grades.indices) println(grades(i))
            """),

        Thrown("IndexOutOfBoundsException",
            "The items of a List are numbered from 0, so a List of 3 items has positions 0, 1 and 2. This line asks for a position that " +
            "is not in the list - the message gives the position asked for.",
            "Check the position is less than the list's length first, or loop over the items themselves.",
            """
            for (word <- words) println(word)
            """),

        Thrown("MatchError",
            "A match tried every one of its cases and none fitted this value, so the program stopped with a MatchError - the message " +
            "names the value no case fitted.",
            "Add a case for the value it names, or a final case _ => for every value the others leave.",
            """
            mark match {
              case Pass => "passed"
              case _ => "not passed"
            }
            """),

        Thrown("NullPointerException",
            "null means there is no object at all. Scala code seldom uses it - an Option stands for a value that may be missing - but " +
            "values from Java's libraries, and fields not set yet, can be null. This line asks a null for something only a real object has.",
            "Give it a real value before this line, or wrap a value that can be null in Option(...) and handle the None.",
            """
            val length = Option(name).map(_.length).getOrElse(0)
            """),

        Thrown("StackOverflowError",
            "A method that calls itself has to reach a point where it stops. This one kept calling itself until the memory for calls ran " +
            "out. (In an object, a method whose very last step is calling itself is turned into a loop by Scala - so the same mistake " +
            "there runs for ever instead of stopping with this error.)",
            "Give the method a case that returns without calling itself, and make sure each call comes closer to it.",
            """
            def countDown(n: Int): Int =
              if (n <= 0) 0 else countDown(n - 1)
            """),

        Thrown("ClassCastException",
            "asInstanceOf tells Scala to treat a value as another type without checking that it is one. When the program runs, the " +
            "value is not that type - the message says what it really is - so the program stops.",
            "Use a match with a typed case - case n: Int => - which checks the type before using the value, instead of asInstanceOf.",
            """
            value match {
              case n: Int => println(n + 1)
              case other => println("not a number: " + other)
            }
            """),
    ];
}
