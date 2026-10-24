using FixFinder.Core.LocalFixes;
using FixFinder.Core.Logic;

namespace FixFinder.Tests;

/// <summary>
/// # of a Lua table given only keyed entries: found where nothing gives it items at positions, with its change where lines
/// can go before the line, and left alone where the table is a list, is given one, or is named again.
/// </summary>
public class LuaLogicPatternTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose()
    {
        _temp.Dispose();
        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<LogicFinding> Scan(string code)
    {
        var path = Path.Combine(_temp.Path, "main.lua");
        File.WriteAllText(path, code);
        return LogicPatterns.Scan(SourceFile.Read(path)!);
    }

    private const string Check = "logic-lua-length-of-keyed-table";

    [Theory]
    [InlineData("local ages = {Ada = 36, Bob = 20}\nprint(\"People: \" .. #ages)\n", 2,
        "local agesCount = 0\nfor _ in pairs(ages) do agesCount = agesCount + 1 end\nprint(\"People: \" .. agesCount)")]
    // Made over several lines, with keys in brackets and a table inside it.
    [InlineData("local marks = {\n  [\"maths\"] = 70,\n  art = 80,\n  science = {90, 95},\n}\nif #marks == 0 then\n  print(\"none\")\nend\n", 6,
        "local marksCount = 0\nfor _ in pairs(marks) do marksCount = marksCount + 1 end\nif marksCount == 0 then")]
    // Inside a function, at its indent.
    [InlineData("local ages = {Ada = 36}\nlocal function show()\n  print(#ages)\nend\nshow()\n", 3,
        "  local agesCount = 0\n  for _ in pairs(ages) do agesCount = agesCount + 1 end\n  print(agesCount)")]
    // Given another key later: still nothing at position 1.
    [InlineData("local ages = {Ada = 36}\nages[\"Bob\"] = 20\nages.Cy = 41\nprint(#ages)\n", 4,
        "local agesCount = 0\nfor _ in pairs(ages) do agesCount = agesCount + 1 end\nprint(agesCount)")]
    public void LengthOfATableGivenOnlyKeysIsFoundAndCountedWithPairs(string code, int line, string changedTo)
    {
        var found = Assert.Single(Scan(code), finding => finding.PatternId == Check);

        Assert.Equal(line, found.Line);
        Assert.Equal(changedTo, string.Join("\n", found.Fix!.NewLines));
    }

    [Fact]
    public void TheMessageNamesTheTablesKeysAsWritten()
    {
        var found = Assert.Single(Scan("local marks = {[\"maths\"] = 70, art = 80, music = 60}\nprint(#marks)\n"), finding => finding.PatternId == Check);

        Assert.Contains("([\"maths\"], art, ...)", found.Message, StringComparison.Ordinal);
        Assert.EndsWith("so #marks is 0, with no error", found.Message, StringComparison.Ordinal);
    }

    [Theory]
    // elseif closes a block, so no line can go before it.
    [InlineData("local ages = {Ada = 36}\nif false then\n  print(1)\nelseif #ages == 0 then\n  print(\"none\")\nend\n", 4)]
    // A line carried on from the one before.
    [InlineData("local ages = {Ada = 36}\nprint(\"People:\",\n  #ages)\n", 3)]
    // The name the count would take is in use.
    [InlineData("local ages = {Ada = 36}\nlocal agesCount = 5\nprint(#ages, agesCount)\n", 3)]
    public void WhereNoLinesCanGoBeforeItIsFoundWithNoChange(string code, int line)
    {
        var found = Assert.Single(Scan(code), finding => finding.PatternId == Check);

        Assert.Equal(line, found.Line);
        Assert.Null(found.Fix);
    }

    [Theory]
    // A list, or a table with a list's items among its keys.
    [InlineData("local marks = {70, 80}\nprint(#marks)\n")]
    [InlineData("local marks = {70, total = 2}\nprint(#marks)\n")]
    // Given items at positions: by table.insert, by a number, or by a name that may hold one.
    [InlineData("local ages = {Ada = 36}\ntable.insert(ages, 40)\nprint(#ages)\n")]
    [InlineData("local ages = {Ada = 36}\nages[1] = 40\nprint(#ages)\n")]
    [InlineData("local ages = {Ada = 36}\nlocal i = 1\nages[i] = 40\nprint(#ages)\n")]
    // A metatable, whose __len can say what # gives.
    [InlineData("local ages = {Ada = 36}\nsetmetatable(ages, {__len = function() return 1 end})\nprint(#ages)\n")]
    // Named again: given a new table, or as a function's parameter.
    [InlineData("local ages = {Ada = 36}\nages = {70, 80}\nprint(#ages)\n")]
    [InlineData("local ages = {Ada = 36}\nlocal function count(ages)\n  return #ages\nend\nprint(count({1, 2}))\n")]
    // # of a list inside the table, and # inside text and a comment.
    [InlineData("local data = {marks = {70, 80}}\nprint(#data.marks)\n")]
    [InlineData("local ages = {Ada = 36}\nprint(\"#ages\")\n-- #ages is 0\n")]
    public void AListOrATableGivenItemsElsewhereIsLeftAlone(string code) =>
        Assert.DoesNotContain(Scan(code), finding => finding.PatternId == Check);
}
