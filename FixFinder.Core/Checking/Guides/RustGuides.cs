using System.Text.RegularExpressions;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// rustc's errors and warnings, and the panics that stop Rust programs, each explained for someone new to programming.
/// </summary>
/// <remarks>
/// Every code and message matched here is one rustc 1.98 or a program it built was seen to give. Every example is a whole
/// program, and RustLiveTests compiles each one with rustc, as Rust 2024, and fails on any warning.
/// </remarks>
internal static class RustGuides
{
    private const string NothingRuns = "rustc refuses to build the program while this is wrong, so no part of it runs - not even the lines before this one.";

    private const string Panics = "The program stops - Rust calls it a panic - at this line, and nothing after it runs.";

    private static GuideEntry Compile(string[] codes, string explanation, string fix, string example, string? why = null) => new()
    {
        ExceptionTypes = ["compile error"],
        Codes = codes,
        Guide = new MistakeGuide(explanation, why ?? NothingRuns, fix, example),
    };

    private static GuideEntry CompileWords(string pattern, string explanation, string fix, string example) => new()
    {
        ExceptionTypes = ["compile error"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, NothingRuns, fix, example),
    };

    private static GuideEntry Warned(string[] lints, string explanation, string why, string fix, string example) => new()
    {
        ExceptionTypes = ["compile warning"],
        Codes = lints,
        Guide = new MistakeGuide(explanation, why, fix, example),
    };

    private static GuideEntry Panicked(string pattern, string explanation, string fix, string example, string? why = null) => new()
    {
        ExceptionTypes = ["panic"],
        Message = new Regex(pattern, RegexOptions.IgnoreCase),
        Guide = new MistakeGuide(explanation, why ?? Panics, fix, example),
    };

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        Compile(["E0425"],
            "Every name in Rust - a variable, a function - has to be made before it can be used: a variable with let, a function with fn. " +
            "rustc cannot find this name from here. It treats capital letters as different letters, and a variable made inside a block " +
            "{ } can only be used inside it. rustc often names the one it thinks was meant.",
            "Check the spelling against where the name is made, and make it before this line, where this line can see it.",
            """
            fn main() {
                let total = 10;
                println!("{}", total);
            }
            """),

        Compile(["E0433", "E0412", "E0432"],
            "A type from Rust's own library - HashMap, say - has to be brought in with a use line at the top of the file before its " +
            "short name can be used. This name has not been brought in, or is spelt differently from the type.",
            "Add the use line rustc names, such as use std::collections::HashMap;, at the top of the file - or check the name's spelling.",
            """
            use std::collections::HashMap;

            fn main() {
                let mut ages = HashMap::new();
                ages.insert("Ada", 36);
                println!("{}", ages["Ada"]);
            }
            """),

        Compile(["E0308"],
            "Rust checks that every value is of the type the place it goes needs, and this one is not: the message gives the type it " +
            "expected and the one it found. Text where a number is needed is one; a function that should give back a number but ends " +
            "with a semicolon is another - the semicolon makes its last line a statement that gives back nothing, which Rust writes ().",
            "Give the place a value of the type it needs - turn text into a number with .parse(), a number into text with .to_string() - " +
            "or take the semicolon off a function's last line, so its value is given back.",
            """
            fn add(a: i32, b: i32) -> i32 {
                a + b
            }

            fn main() {
                let total: i32 = add(2, 3);
                println!("{}", total);
            }
            """),

        Compile(["E0384"],
            "A variable made with let keeps the value it is first given: Rust's variables cannot change unless they are made with " +
            "let mut. This line gives one a new value.",
            "Make the variable with let mut where it is first made, as rustc suggests - or give the new value a new name.",
            """
            fn main() {
                let mut total = 10;
                total += 5;
                println!("{}", total);
            }
            """),

        Compile(["E0596"],
            "Changing what a variable holds - push onto a Vec, insert into a HashMap - needs the variable to be made with let mut. " +
            "This one was made with let alone, so Rust will not let it be changed.",
            "Make the variable with let mut where it is first made, as rustc suggests.",
            """
            fn main() {
                let mut marks = vec![1, 2];
                marks.push(3);
                println!("{:?}", marks);
            }
            """),

        Compile(["E0382"],
            "In Rust each value has one owner. Giving a Vec or a String to another variable, or to a function, moves it there, and the " +
            "first name can no longer be used - a number is copied instead, which is why this only happens with some types. This line " +
            "uses a name after its value was moved away.",
            "Use the value through the name it was moved to, lend it with & instead of moving it, or make a copy with .clone() where it is moved.",
            """
            fn main() {
                let marks = vec![1, 2];
                let copied = marks.clone();
                println!("{:?} {:?}", marks, copied);
            }
            """),

        Compile(["E0502", "E0499"],
            "Rust lets a value be read through several references at once, or changed through one - never both at the same time. " +
            "Here the value is changed while a reference to it is still going to be used, and that reference could end up pointing at " +
            "something that moved or is gone.",
            "Finish using the reference before changing the value - read what you need from it first, or copy the item out instead of borrowing it.",
            """
            fn main() {
                let mut marks = vec![1, 2];
                let first = marks[0];
                marks.push(3);
                println!("{} {:?}", first, marks);
            }
            """),

        Compile(["E0004"],
            "A match has to have an arm for every value that can arrive, and rustc checks it does before the program runs. The message " +
            "names the values no arm covers.",
            "Add arms for the values that are missing, or a last arm _ => ... for every value the others leave.",
            """
            fn describe(mark: u8) -> &'static str {
                match mark {
                    1 => "first",
                    2 => "second",
                    _ => "another",
                }
            }

            fn main() {
                println!("{}", describe(3));
            }
            """),

        Compile(["E0061"],
            "A function has to be given one value for each parameter it is written with, in order. This call gives it too few or too many.",
            "Look at the function's definition and give the call a value for each parameter it lists.",
            """
            fn area(width: i32, height: i32) -> i32 {
                width * height
            }

            fn main() {
                println!("{}", area(3, 4));
            }
            """),

        Compile(["E0599"],
            "A value can only use the methods its type has, and this type has no method by this name - often a spelling that is close " +
            "to the real one, which rustc names.",
            "Use the method's name as the type has it - rustc often names the one it thinks was meant.",
            """
            fn main() {
                let name = String::from("ada");
                println!("{}", name.to_uppercase());
            }
            """),

        Compile(["E0381"],
            "Rust will not let a variable be read before it has been given a value, on every way through the code to this line.",
            "Give the variable its value where it is made - let total = 0; - or on every way to this line.",
            """
            fn main() {
                let total: i32 = 0;
                println!("{}", total);
            }
            """),

        Compile(["E0369"],
            "+ can only join text when the left side is a String of its own, and the right side is borrowed text - &str. Two pieces of " +
            "&str cannot be added together, and other operators only work between values of types that have them.",
            "Make the left side an owned String - with .to_owned() or String::from - or build the text with format!.",
            """
            fn main() {
                let name = "Ada";
                let greeting = format!("Hello {}", name);
                println!("{}", greeting);
            }
            """),

        CompileWords(@"doesn't implement `std::fmt::Display`",
            "{} in println! shows a value the way it is meant to be read, and only types that say how - numbers, text - can be shown " +
            "that way. A Vec, or a struct of your own, does not say how. {:?} shows a value the way it is written for a programmer, and " +
            "most types can be shown that way.",
            "Use {:?} for this value, as rustc suggests - or show its parts one at a time.",
            """
            fn main() {
                let marks = vec![1, 2];
                println!("{:?}", marks);
            }
            """),

        CompileWords(@"^cannot (?:add|subtract|multiply|divide|calculate the remainder)",
            "Rust never mixes types in arithmetic by itself - not even a whole number and a decimal. Both sides of +, -, * and / have " +
            "to be the same type.",
            "Turn one side into the other's type yourself, with as - count as f64 - or write the number as a decimal: 3.0.",
            """
            fn main() {
                let count = 3;
                let average: f64 = 10.0 / count as f64;
                println!("{}", average);
            }
            """),

        Compile(["E0317"],
            "In Rust an if gives back a value, and one that is used as a value needs an else, so there is a value whichever way it goes.",
            "Add an else with the value for when the condition is false.",
            """
            fn main() {
                let mark = 70;
                let grade = if mark >= 40 { "pass" } else { "fail" };
                println!("{}", grade);
            }
            """),

        CompileWords(@"^expected `;`",
            "Each statement in Rust ends with a semicolon. Without one, rustc reads on into the next line as if it were part of the " +
            "same statement, and cannot make sense of the two together.",
            "Put a ; at the end of the statement rustc points at.",
            """
            fn main() {
                let x = 5;
                println!("{}", x);
            }
            """),

        CompileWords(@"closing delimiter|unclosed delimiter",
            "Brackets come in pairs: every ( needs a ), every [ a ], every { a }. One is missing, or closed by the wrong kind, so rustc " +
            "cannot tell where the code inside it ends - and it can only notice at the place it finds the wrong one.",
            "Find the bracket that was never closed - rustc points at it as an unclosed delimiter - and close it in the right place.",
            """
            fn main() {
                println!("{}", (1 + 2));
            }
            """),

        CompileWords(@"^format argument must be a string literal",
            "println! and format! take text in quotes first, saying how to show what follows - \"{}\" shows one value - and then the " +
            "values. A value on its own in that first place is not that text.",
            "Put \"{}\", before the value, as rustc suggests: println!(\"{}\", age);",
            """
            fn main() {
                let age = 5;
                println!("{}", age);
            }
            """),

        Warned(["unused_variables"],
            "This variable is made and given a value that nothing reads. That does no harm on its own, but it is often a sign of a " +
            "slip nearby: a result stored under one name and then read from another.",
            "The program runs, but the value it worked out here is never used.",
            "Use the variable, or remove it. If it is there on purpose, starting its name with _ tells rustc so.",
            """
            fn main() {
                let total = 2 + 3;
                println!("{}", total);
            }
            """),

        Warned(["unused_mut"],
            "This variable is made with let mut, but nothing ever changes it, so it does not need to be changeable.",
            "The program works; leaving out mut says plainly that the value never changes.",
            "Take mut off where the variable is made, as rustc suggests.",
            """
            fn main() {
                let count = 1;
                println!("{}", count);
            }
            """),

        Warned(["dead_code"],
            "Nothing in the program calls this function, or uses this item, so it never runs.",
            "The program works without it - but if it was meant to run, nothing is calling it.",
            "Call it where it was meant to run, or remove it.",
            """
            fn helper() -> i32 {
                42
            }

            fn main() {
                println!("{}", helper());
            }
            """),

        Warned(["unused_must_use"],
            "This call gives back a value that must be used - a new, changed copy, or a Result that says whether it worked - and the " +
            "line throws it away. String's to_uppercase, trim and replace never change the text they are called on; they give back a " +
            "new String.",
            "Whatever the call was for never happens: the line changes nothing, or an error it reports goes unseen.",
            "Keep what the call gives back - let upper = name.to_uppercase(); - or, for a Result, deal with the error it may hold.",
            """
            fn main() {
                let name = String::from("ada");
                let upper = name.to_uppercase();
                println!("{}", upper);
            }
            """),

        Warned(["unused_assignments"],
            "A value is stored in this variable and never read: before anything reads it, the variable is given another value, or " +
            "the program ends.",
            "The value worked out here is never used - often a sign that the wrong variable is read later.",
            "Read the value where it was meant to be used, or remove the line that stores it.",
            """
            fn main() {
                let mut total = 0;
                total += 5;
                println!("{}", total);
            }
            """),

        Warned(["unconditional_recursion"],
            "This function calls itself on every way through it, so it can never stop calling itself: there is no case where it " +
            "gives back an answer without another call.",
            "When it runs, the calls pile up until the program runs out of room for them and stops with a stack overflow.",
            "Add a case where it gives back an answer without calling itself - if n == 0 { return 0; } - and make every call move towards it.",
            """
            fn count_down(n: u64) -> u64 {
                if n == 0 {
                    return 0;
                }
                count_down(n - 1)
            }

            fn main() {
                println!("{}", count_down(3));
            }
            """),

        Warned(["unreachable_code", "unreachable_patterns"],
            "This code can never run: something before it always leaves first - a return, a panic!, a loop that never ends - or, in a " +
            "match, an arm above it already takes every value this one would.",
            "Code that never runs does nothing; if it was meant to, the code before it is in the wrong order.",
            "Move the code before whatever leaves first, or remove it - and in a match, put the general arm last.",
            """
            fn main() {
                println!("first");
                println!("second");
            }
            """),

        Panicked(@"^attempt to divide by zero|^attempt to calculate the remainder with a divisor of zero",
            "Dividing a whole number by zero has no answer, so Rust stops the program. The number divided by is zero at this point - " +
            "often because it counts items, and there were none.",
            "Check the number is not zero before dividing by it, and decide what the answer should be when it is.",
            """
            fn main() {
                let total = 10;
                let count = 0;
                let average = if count == 0 { 0 } else { total / count };
                println!("{}", average);
            }
            """),

        Panicked(@"^index out of bounds",
            "This asks for a position the Vec or array does not have. Positions start at 0, so a Vec of 3 items has positions 0, 1 and " +
            "2 - the message gives its length and the position asked for.",
            "Ask only for positions below the length: loop with for item in &marks, or 0..marks.len() - or use .get(i), which gives None instead of stopping.",
            """
            fn main() {
                let marks = vec![70, 80, 90];
                for i in 0..marks.len() {
                    println!("{}", marks[i]);
                }
            }
            """),

        Panicked(@"Option::unwrap\(\)` on a `None` value",
            "An Option holds a value - Some(value) - or nothing - None. unwrap takes the value out, and stops the program when there is " +
            "none. A search that finds nothing, first() of an empty Vec, and get with a key that is not there all give None.",
            "Say what to do when there is none: if let Some(x) = ... { }, a match on Some and None, or .unwrap_or(a value to use instead).",
            """
            fn main() {
                let marks: Vec<i32> = Vec::new();
                match marks.first() {
                    Some(first) => println!("{}", first),
                    None => println!("no marks"),
                }
            }
            """),

        Panicked(@"Result::unwrap\(\)` on an `Err` value|: Parse(?:Int|Float)Error",
            "A Result holds a value - Ok(value) - or an error - Err(error). unwrap, or expect, takes the value out, and stops the program " +
            "when it holds an error. parse gives an error for text that is not a number, which is what ParseIntError says.",
            "Handle the error: match on Ok and Err, or if let Ok(x) = ... - and for typed-in numbers, say what was wrong instead of stopping.",
            """
            fn main() {
                let typed = "twelve";
                match typed.parse::<i32>() {
                    Ok(age) => println!("{}", age + 1),
                    Err(_) => println!("not a whole number"),
                }
            }
            """),

        Panicked(@"^attempt to (?:add|subtract|multiply|negate|shift left|shift right) with overflow",
            "Each whole-number type holds numbers up to a limit - a u8 up to 255, an i32 up to 2147483647 - and an unsigned one none below " +
            "0. This arithmetic went past it, and a program built for testing, as FixFinder builds it, stops rather than wrap around.",
            "Use a type big enough for the numbers - i64, u64 - or check before the arithmetic, or use checked_add, which gives None when it would go past.",
            """
            fn main() {
                let mut total: u32 = 250;
                for _ in 0..10 {
                    total += 1;
                }
                println!("{}", total);
            }
            """),

        Panicked(@"^no entry found for key",
            "map[&key] looks a key up and stops the program when the key is not in the map.",
            "Use .get(&key), which gives None for a key that is not there, and say what to do then - or check with contains_key first.",
            """
            use std::collections::HashMap;

            fn main() {
                let mut ages = HashMap::new();
                ages.insert("Ada", 36);
                match ages.get("Bob") {
                    Some(age) => println!("{}", age),
                    None => println!("no age for Bob"),
                }
            }
            """),

        Panicked(@"^removal index \(is \d+\) should be < len|^insertion index \(is \d+\) should be <= len|^swap_remove index",
            "remove takes out the item at a position, and stops the program when the Vec has no item there - the message gives the " +
            "position asked for and the length.",
            "Only remove at a position below the length - check i < marks.len() first.",
            """
            fn main() {
                let mut marks = vec![1, 2, 3];
                let position = 5;
                if position < marks.len() {
                    marks.remove(position);
                }
                println!("{:?}", marks);
            }
            """),

        Panicked(@".",
            "The program stopped itself here - a panic. The line after where it happened is the message: one the program wrote with " +
            "panic! or expect, or one from Rust's own library saying what could not be done.",
            "Read the message and the line it names: decide what the program should do in that case instead of stopping.",
            """
            fn main() {
                let mark = 120;
                if mark > 100 {
                    println!("a mark cannot be over 100");
                    return;
                }
                println!("{}", mark);
            }
            """),
    ];
}
