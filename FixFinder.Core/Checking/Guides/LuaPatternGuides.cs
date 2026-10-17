using static FixFinder.Core.Checking.Guides.LogicGuides;

namespace FixFinder.Core.Checking.Guides;

/// <summary>
/// Guides for the logic checks of Lua programs, each explained for someone new to programming. Every example is a whole
/// program, and LuaLiveTests checks each one with luac -p and runs it, and fails on anything Lua says.
/// </summary>
internal static class LuaPatternGuides
{
    public static IReadOnlyList<GuideEntry> All { get; } =
    [
        GuideFor(["logic-lua-loop-from-zero"], "A loop that counts a list from 0",
            "A Lua list's first item is at position 1, not 0, so a list of 3 items has positions 1, 2 and 3. A loop that starts at 0 " +
            "asks for marks[0] first - which is nil, as nothing is there.",
            "Lua gives no error for marks[0]: the loop's first pass works with nil, and a loop that stops at #marks - 1 misses the last item.",
            "Count from 1 to #marks, or go through the items themselves: for _, mark in ipairs(marks) do.",
            """
            local marks = {70, 80, 90}
            for i = 1, #marks do
              print(marks[i])
            end
            """),

        GuideFor(["logic-lua-text-compared-with-number"], "Text compared with a number",
            "== in Lua compares the kind of value as well as the value, and text is never equal to a number - so \"5\" == 5 is false. " +
            "What io.read() reads is text, even when what was typed is a number.",
            "The comparison gives the same answer every time, whatever the text holds, and Lua says nothing of it.",
            "Turn the text into a number first with tonumber, or read a number in the first place: io.read(\"n\").",
            """
            local typed = "5"
            if tonumber(typed) == 5 then
              print("five")
            end
            """),

        GuideFor(["logic-lua-float-equality"], "Decimal numbers compared exactly",
            "Decimals are stored in binary, which holds most of them only nearly: 0.1 + 0.2 is held as 0.30000000000000004, not 0.3 - " +
            "and print rounds it, showing 0.3, so the two look equal when they are not.",
            "== asks for an exact match, so a total worked out by arithmetic can fail to equal the decimal it should.",
            "Compare within a tiny tolerance: math.abs(total - 0.3) < 1e-9.",
            """
            local total = 0.1 + 0.2
            if math.abs(total - 0.3) < 1e-9 then
              print("equal")
            end
            """),

        GuideFor(["logic-lua-remove-while-looping-forward"], "Removing from a list while looping forward over it",
            "table.remove(marks, i) takes out the item at i and moves every item after it down a place to close the gap. A loop going " +
            "forward then moves on to i + 1 - so the item that moved into position i is never looked at. And #marks was worked out " +
            "once, as the loop began, so the loop now reaches past the end.",
            "Two items in a row that should both go leave one behind, and the last pass works with nil, with no error.",
            "Loop backwards - for i = #marks, 1, -1 do - so a removal only moves items the loop has already been past.",
            """
            local marks = {70, 40, 40, 90}
            for i = #marks, 1, -1 do
              if marks[i] == 40 then
                table.remove(marks, i)
              end
            end
            print(table.concat(marks, " "))
            """),

        GuideFor(["logic-lua-local-hides-outer"], "local inside a loop hides the outer variable",
            "local declares a new variable, which lasts until the end of the block it is in. Inside a loop, local total = total + mark " +
            "declares a new total each time round, worked out from the total outside the loop - which is never changed. Lua says " +
            "nothing, as declaring a variable with a name already in use is allowed.",
            "The total outside the loop keeps the value it started with: adding up 70, 80 and 90 prints Total: 0.",
            "Take local away inside the loop - total = total + mark - so the line changes the total declared before it.",
            """
            local marks = {70, 80, 90}
            local total = 0
            for _, mark in ipairs(marks) do
              total = total + mark
            end
            print("Total: " .. total)
            """),

        GuideFor(["logic-lua-table-shared"], "A second name for a table is not a copy",
            "local backup = marks does not copy the table: it gives the same table a second name. A change made " +
            "through either name - marks[1] = 100 - is a change to the one table, so backup shows it too. Lua says " +
            "nothing.",
            "The second name does not keep the values the table had: after marks[1] = 100, backup[1] prints 100.",
            "Make a copy of a list: local backup = {table.unpack(marks)} makes a new table with the same items, in " +
            "order, and a change to one no longer shows in the other.",
            """
            local marks = {70, 80, 90}
            local backup = {table.unpack(marks)}
            marks[1] = 100
            print(backup[1])
            """),
    ];
}
