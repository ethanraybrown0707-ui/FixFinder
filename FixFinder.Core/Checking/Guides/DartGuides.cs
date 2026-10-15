using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// What dart analyze says of a Dart program before it runs, and the exceptions that stop one - each explained for someone
/// new to programming.
/// </summary>
/// <remarks>
/// Every diagnostic and message matched here is one Dart 3.13 was seen to give; the analyzer's are found by their names,
/// such as undefined_identifier. Every example is a whole program, and DartLiveTests checks each one with dart analyze and
/// runs it, and fails on anything Dart says.
/// </remarks>
internal static class DartGuides
{
    private const string NothingRuns = "Dart will not compile a program with this error, so while it is there no part of the program runs.";

    private const string Stops = "The program stops at this line, and nothing after it runs.";

    private const string Runs = "The program still runs, but this is very likely a mistake - or code that does nothing.";

    private static GuideEntry Analyzed(string code, string explanation, string fix, string example) => new()
    {
        Codes = [code],
        Guide = new MistakeGuide(explanation, NothingRuns, fix, example),
    };

    private static GuideEntry Warned(string code, string explanation, string fix, string example) => new()
    {
        Codes = [code],
        Guide = new MistakeGuide(explanation, Runs, fix, example),
    };

    private static GuideEntry Thrown(string? type, string? pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = type is null ? [] : [type],
        Message = pattern is null ? null : new Regex(pattern),
        Guide = new MistakeGuide(explanation, Stops, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Analyzed("expected_token",
            "Dart expected a particular symbol here and did not find it - the message says which. Most often it is the ; that ends " +
            "every statement, or a bracket that was opened and not closed.",
            "Put the symbol the message names where it points.",
            """
            void main() {
              var total = 10;
              print(total);
            }
            """),

        Analyzed("undefined_identifier",
            "Dart cannot find anything by this name - it was never declared, is spelt differently, or was declared inside a block this " +
            "line is outside of.",
            "Check the spelling against where the name is declared, and declare it before this line if it is new.",
            """
            void main() {
              var total = 10;
              print(total + 1);
            }
            """),

        Analyzed("undefined_function",
            "Dart cannot find a function by this name - it is spelt differently from where it is written, or it is in a file this one " +
            "does not import.",
            "Check the name against the function's own, or import the file it is in.",
            """
            int average(List<int> marks) => marks.isEmpty ? 0 : marks.reduce((a, b) => a + b) ~/ marks.length;

            void main() {
              print(average([70, 80]));
            }
            """),

        Analyzed("undefined_method",
            "The value's type has no method by this name - often a slip in its spelling, as Dart's names are case-sensitive: " +
            "toUpperCase, not toUppercase.",
            "Check the method's name against the type's own - the message says which type it looked in.",
            """
            void main() {
              var name = "Ada";
              print(name.toUpperCase());
            }
            """),

        Analyzed("undefined_getter",
            "The value's type has no property by this name - often a slip in its spelling: length, not lenght.",
            "Check the property's name against the type's own - the message says which type it looked in.",
            """
            void main() {
              var name = "Ada";
              print(name.length);
            }
            """),

        Analyzed("invalid_assignment",
            "Every Dart variable has a type, and the value given to it is of another one. The message names both: a decimal where a " +
            "whole number was declared - / always gives a decimal - text where a number was declared, or a value that may be null, " +
            "int?, where one that cannot be null was declared.",
            "Give the variable a value of its type: ~/ divides as whole numbers, int.parse turns text into a number, and ?? gives a " +
            "value to use when the other is null.",
            """
            void main() {
              int total = 7;
              int count = 2;
              int average = total ~/ count;
              Map<String, int> ages = {"Ada": 36};
              int age = ages["Bob"] ?? 0;
              print(average + age);
            }
            """),

        Analyzed("argument_type_not_assignable",
            "A function, or an operator such as +, was given a value of a type it does not take - a number where text was wanted, " +
            "say. Dart does not change one into the other by itself.",
            "Turn the value into the type wanted: toString() makes a number into text, or write it inside the text: \"Total: $total\".",
            """
            void main() {
              var total = 12;
              print("Total: " + total.toString());
              print("Total: $total");
            }
            """),

        Analyzed("non_bool_condition",
            "The condition of an if or a while has to be true or false, and this one is not - often because it has = where == was " +
            "meant, which stores a value instead of comparing.",
            "Compare with ==.",
            """
            void main() {
              var mark = 70;
              if (mark == 40) {
                print("forty");
              }
            }
            """),

        Analyzed("body_might_complete_normally",
            "The function says it gives back a value, but it can reach its end without a return - and then it would give back null, " +
            "which its type does not allow.",
            "Return the value on every way through the function: return n * 2;.",
            """
            int doubled(int n) {
              return n * 2;
            }

            void main() {
              print(doubled(4));
            }
            """),

        Analyzed("assignment_to_final_local",
            "A final variable can be set only once, and this line sets it again.",
            "Declare it with var, or its type, instead of final if its value has to change.",
            """
            void main() {
              var passMark = 40;
              passMark = 50;
              print(passMark);
            }
            """),

        Analyzed("not_enough_positional_arguments",
            "The call gives the function fewer values than its parameters - the message says how many it expects.",
            "Give the call a value for each parameter the function lists.",
            """
            int area(int width, int height) => width * height;

            void main() {
              print(area(3, 4));
            }
            """),

        Analyzed("extra_positional_arguments",
            "The call gives the function more values than it has parameters for.",
            "Give the call only as many values as the function lists.",
            """
            int area(int width, int height) => width * height;

            void main() {
              print(area(3, 4));
            }
            """),

        Analyzed("unchecked_use_of_nullable_value",
            "This value's type ends in ?, so it may be null - and Dart will not use something that may be null as if it were there.",
            "Check it is not null first, or give a value to use instead: (name ?? '').length.",
            """
            void main() {
              String? name;
              print((name ?? '').length);
            }
            """),

        Warned("unused_local_variable",
            "This variable is given a value that is never used - often because a later line uses a name spelt slightly differently.",
            "Use the variable, or remove it if it is not needed.",
            """
            void main() {
              var total = 10;
              print(total);
            }
            """),

        Warned("dead_code",
            "This code can never run - something above it, a return or a throw, always ends the function first.",
            "Move the code above whatever ends the function, or remove it.",
            """
            int sign(int n) {
              print("checking");
              return 1;
            }

            void main() {
              print(sign(2));
            }
            """),

        Warned("unused_import",
            "This file imports a library and uses nothing from it.",
            "Remove the import, or use what it was imported for.",
            """
            import 'dart:math';

            void main() {
              print(max(3, 4));
            }
            """),

        Thrown(null, @"^Null check operator used on a null value",
            "! after a value says \"this is not null\" - and here it was. A map gives null for a key it does not have, and a variable " +
            "of a ? type can hold null.",
            "Give a value to use in its place with ?? rather than !, or check for null first.",
            """
            void main() {
              Map<String, int> ages = {"Ada": 36};
              int age = ages["Bob"] ?? 0;
              print(age + 1);
            }
            """),

        Thrown("IntegerDivisionByZeroException", null,
            "A whole number was divided by zero with ~/ or %, which has no answer, so Dart stopped. Dividing with / does not stop: it " +
            "gives Infinity, or NaN for zero by zero.",
            "Check the divisor is not zero first, and decide what the answer should be when it is.",
            """
            void main() {
              var marks = <int>[];
              print(marks.isEmpty ? 0 : 70 ~/ marks.length);
            }
            """),

        Thrown(null, @"^Invalid value: Not in inclusive range|^Index out of range",
            "This asks a list for a position it does not have. Positions start at 0, so the last is length - 1, and a loop that runs " +
            "while i <= length takes one step too many.",
            "Loop while i < list.length, or go through the items themselves: for (var mark in marks).",
            """
            void main() {
              var marks = [70, 80, 90];
              for (var i = 0; i < marks.length; i++) {
                print(marks[i]);
              }
            }
            """),

        Thrown("FormatException", null,
            "int.parse or double.parse was given text that is not a number - the message shows the text, with a ^ under where it went wrong.",
            "Use int.tryParse, which gives null for text that is not a number, and decide what to do then.",
            """
            void main() {
              var typed = "twenty";
              var age = int.tryParse(typed);
              print(age == null ? "Please type a number" : age + 1);
            }
            """),

        Thrown("LateInitializationError", null,
            "A variable marked late was used before anything gave it a value - late promises Dart it will have one by then.",
            "Give it its value before the first line that uses it, or give it one where it is declared.",
            """
            late String name;

            void main() {
              name = "Ada";
              print(name);
            }
            """),

        Thrown(null, @"^Stack Overflow",
            "A function called itself, and that call called itself, with nothing to stop it, until Dart ran out of room to keep track.",
            "Give the function a case that returns without calling itself, and make each call come nearer to it.",
            """
            int countDown(int n) => n == 0 ? 0 : 1 + countDown(n - 1);

            void main() {
              print(countDown(3));
            }
            """),

        Thrown(null, @"^Bad state: No element",
            "first, last or single was asked of a list with nothing in it.",
            "Check the list is not empty first: marks.isEmpty.",
            """
            void main() {
              var marks = <int>[];
              print(marks.isEmpty ? "No marks" : marks.first);
            }
            """),
    ];
}
