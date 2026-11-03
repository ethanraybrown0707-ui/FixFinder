using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// The guides for the checks read in every language's own words - a largest started at 0, a total reset inside its loop, the
/// other end counted one too far, a changed copy thrown away - each with its example written in that language, as a reader of
/// that language would write it.
/// </summary>
internal static class EveryLanguagePatternGuides
{
    private static GuideEntry ResultDropped(string id, string example) =>
        GuideFor([id], "A changed copy thrown away",
            "Some functions do not change the value they are given: they work out a changed copy and give it back. Called on a line " +
            "of its own, the copy goes nowhere - the value is still what it was, and the lines after it read the old one.",
            "The program carries on with the value unchanged - \"ada\" where \"ADA\" was meant - and nothing says so.",
            "Keep what the function gives back, in the name it was made from.",
            example);

    private static GuideEntry BestStartsAtZero(string id, string list, string example) =>
        GuideFor([id], "A largest or smallest started at 0",
            $"A loop that keeps the largest item of {list} starts with some value to compare the first item against. Started at 0, it " +
            "only works when some item is above 0: with temperatures of -5, -2 and -8 no item is bigger than 0, so it stays 0 - a " +
            "temperature that was never there. A smallest started at 0 stays 0 the same way when every item is above 0.",
            "The program prints or uses a value the list does not have, with no error to say so.",
            "Start it at the first item, so it is one of the list's values from the start.",
            example);

    private static GuideEntry ResetInLoop(string id, string example) =>
        GuideFor([id], "A total reset inside its own loop",
            "To add things up, you set the total to 0 once and then add each item to it. Here the total is set back to its start " +
            "at the beginning of every pass, which throws away everything added so far - so at the end it holds only the last " +
            "item's part.",
            "After the loop it only holds the last item's contribution, not the sum of all of them.",
            "Set the starting value once, before the loop.",
            example);

    private static GuideEntry MirrorIndex(string id, string example) =>
        GuideFor([id], "The other end counted one too far",
            "Positions in a list or text run from 0 to one less than its length - \"level\" has positions 0 to 4. The position " +
            "matching i from the other end is length - 1 - i, the last one when i is 0. length - i is one further: on the first pass " +
            "it asks for position 5 of \"level\", which is not there, and every pass after it compares the wrong two letters.",
            "A palindrome check says \"level\" is not one, or the program stops with an index error, depending on the language.",
            "Count from the other end with length - 1 - i.",
            example);

    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        ResultDropped("logic-c-result-discarded",
            """
            char letter = 'a';
            letter = toupper(letter);
            printf("%c\n", letter);
            """),
        ResultDropped("logic-php-result-discarded",
            """
            $name = "ada";
            $name = strtoupper($name);
            echo $name, PHP_EOL;
            """),
        ResultDropped("logic-lua-result-discarded",
            """
            local name = "ada"
            name = string.upper(name)
            print(name)
            """),
        ResultDropped("logic-perl-result-discarded",
            """
            my $name = "ada";
            $name = uc($name);
            print "$name\n";
            """),
        ResultDropped("logic-rust-result-discarded",
            """
            let name = String::from("ada");
            let name = name.to_uppercase();
            println!("{}", name);
            """),
        MirrorIndex("logic-mirror-index",
            """
            for (int i = 0; i < length; i++) {
                if (word[i] != word[length - 1 - i]) {
                    same = false;
                }
            }
            """),
        MirrorIndex("logic-python-mirror-index",
            """
            for i in range(len(word)):
                if word[i] != word[len(word) - 1 - i]:
                    same = False
            """),
        MirrorIndex("logic-go-mirror-index",
            """
            for i := 0; i < len(word); i++ {
                if word[i] != word[len(word)-1-i] {
                    same = false
                }
            }
            """),
        MirrorIndex("logic-rust-mirror-index",
            """
            for i in 0..word.len() {
                if word[i] != word[word.len() - 1 - i] {
                    same = false;
                }
            }
            """),
        MirrorIndex("logic-ruby-mirror-index",
            """
            (0...word.length).each do |i|
              same = false if word[i] != word[word.length - 1 - i]
            end
            """),
        MirrorIndex("logic-php-mirror-index",
            """
            for ($i = 0; $i < strlen($word); $i++) {
                if ($word[$i] != $word[strlen($word) - 1 - $i]) {
                    $same = false;
                }
            }
            """),
        MirrorIndex("logic-perl-mirror-index",
            """
            for my $i (0 .. length($word) - 1) {
                if (substr($word, $i, 1) ne substr($word, length($word) - 1 - $i, 1)) {
                    $same = 0;
                }
            }
            """),
        MirrorIndex("logic-dart-mirror-index",
            """
            for (var i = 0; i < word.length; i++) {
              if (word[i] != word[word.length - 1 - i]) {
                same = false;
              }
            }
            """),
        MirrorIndex("logic-powershell-mirror-index",
            """
            for ($i = 0; $i -lt $word.Length; $i++) {
                if ($word[$i] -ne $word[$word.Length - 1 - $i]) {
                    $same = $false
                }
            }
            """),
        MirrorIndex("logic-fsharp-mirror-index",
            """
            for i in 0 .. word.Length - 1 do
                if word.[i] <> word.[word.Length - 1 - i] then
                    same <- false
            """),
        MirrorIndex("logic-scala-mirror-index",
            """
            for i <- 0 until word.length do
              if word(i) != word(word.length - 1 - i) then same = false
            """),
        MirrorIndex("logic-elixir-mirror-index",
            """
            same = Enum.all?(0..(String.length(word) - 1), fn i ->
              String.at(word, i) == String.at(word, String.length(word) - 1 - i)
            end)
            """),
        ResetInLoop("logic-go-reset-in-loop",
            """
            total := 0
            for _, mark := range marks {
                total += mark
            }
            """),
        ResetInLoop("logic-rust-reset-in-loop",
            """
            let mut total = 0;
            for mark in &marks {
                total += mark;
            }
            """),
        ResetInLoop("logic-php-reset-in-loop",
            """
            $total = 0;
            foreach ($marks as $mark) {
                $total += $mark;
            }
            """),
        ResetInLoop("logic-perl-reset-in-loop",
            """
            my $total = 0;
            foreach my $mark (@marks) {
                $total += $mark;
            }
            """),
        ResetInLoop("logic-dart-reset-in-loop",
            """
            var total = 0;
            for (var mark in marks) {
              total += mark;
            }
            """),
        ResetInLoop("logic-powershell-reset-in-loop",
            """
            $total = 0
            foreach ($mark in $marks) {
                $total += $mark
            }
            """),
        ResetInLoop("logic-scala-reset-in-loop",
            """
            var total = 0
            for mark <- marks do
              total += mark
            """),
        ResetInLoop("logic-ruby-reset-in-loop",
            """
            total = 0
            marks.each do |mark|
              total += mark
            end
            """),
        ResetInLoop("logic-lua-reset-in-loop",
            """
            local total = 0
            for _, mark in ipairs(marks) do
              total = total + mark
            end
            """),
        ResetInLoop("logic-fortran-reset-in-loop",
            """
            total = 0
            do i = 1, size(scores)
              total = total + scores(i)
            end do
            """),
        ResetInLoop("logic-fsharp-reset-in-loop",
            """
            let mutable total = 0
            for mark in marks do
                total <- total + mark
            """),
        BestStartsAtZero("logic-best-starts-at-zero", "an array",
            """
            int highest = temperatures[0];
            for (int i = 0; i < count; i++) {
                if (temperatures[i] > highest) {
                    highest = temperatures[i];
                }
            }
            """),
        BestStartsAtZero("logic-go-best-starts-at-zero", "a slice",
            """
            highest := temperatures[0]
            for _, t := range temperatures {
                if t > highest {
                    highest = t
                }
            }
            """),
        BestStartsAtZero("logic-rust-best-starts-at-zero", "a vector",
            """
            let mut highest = temperatures[0];
            for t in &temperatures {
                if *t > highest {
                    highest = *t;
                }
            }
            """),
        BestStartsAtZero("logic-ruby-best-starts-at-zero", "an array",
            """
            highest = temperatures[0]
            temperatures.each do |t|
              highest = t if t > highest
            end
            """),
        BestStartsAtZero("logic-php-best-starts-at-zero", "an array",
            """
            $highest = $temperatures[0];
            foreach ($temperatures as $t) {
                if ($t > $highest) {
                    $highest = $t;
                }
            }
            """),
        BestStartsAtZero("logic-lua-best-starts-at-zero", "a table",
            """
            local highest = temperatures[1]
            for _, t in ipairs(temperatures) do
              if t > highest then
                highest = t
              end
            end
            """),
        BestStartsAtZero("logic-perl-best-starts-at-zero", "an array",
            """
            my $highest = $temperatures[0];
            foreach my $t (@temperatures) {
                if ($t > $highest) {
                    $highest = $t;
                }
            }
            """),
        BestStartsAtZero("logic-dart-best-starts-at-zero", "a list",
            """
            var highest = temperatures[0];
            for (var t in temperatures) {
              if (t > highest) {
                highest = t;
              }
            }
            """),
        BestStartsAtZero("logic-fortran-best-starts-at-zero", "an array",
            """
            highest = temperatures(1)
            do i = 1, size(temperatures)
              if (temperatures(i) > highest) then
                highest = temperatures(i)
              end if
            end do
            """),
        BestStartsAtZero("logic-powershell-best-starts-at-zero", "an array",
            """
            $highest = $temperatures[0]
            foreach ($t in $temperatures) {
                if ($t -gt $highest) {
                    $highest = $t
                }
            }
            """),
        BestStartsAtZero("logic-fsharp-best-starts-at-zero", "a list",
            """
            let mutable highest = temperatures.[0]
            for t in temperatures do
                if t > highest then
                    highest <- t
            """),
        BestStartsAtZero("logic-scala-best-starts-at-zero", "a list",
            """
            var highest = temperatures(0)
            for t <- temperatures do
              if t > highest then highest = t
            """),
        BestStartsAtZero("logic-elixir-best-starts-at-zero", "a list",
            """
            highest = Enum.reduce(temperatures, hd(temperatures), fn t, best -> if t > best, do: t, else: best end)
            """),
        BestStartsAtZero("logic-erlang-best-starts-at-zero", "a list",
            """
            Highest = lists:foldl(fun(T, Best) when T > Best -> T; (_, Best) -> Best end, hd(Temperatures), Temperatures).
            """),
    ];
}
