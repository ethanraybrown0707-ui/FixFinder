using FixFinder.Core.Analysis.Dynamic;

namespace FixFinder.Tests;

/// <summary>
/// Which ifs, else ifs, elses and whiles around a line decided that it ran, in the languages a copy of the program follows -
/// braces on the header's line or on their own, and Ruby's and Lua's blocks that end with end - and which are left out.
/// </summary>
public class BraceBranchesTests
{
    private static string[] Around(BraceDialect dialect, string code, int line)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var read = new BraceCode(dialect, lines, dialect.Blanked(lines));
        return BraceBranches.Of(dialect, read, lines, line, read.FunctionAround(line))
            .Select(decision => $"{decision.Line} {decision.Kind} {decision.WasTrue} {decision.Code}")
            .ToArray();
    }

    [Fact]
    public void BracesOnTheirOwnLinesAreReadAsTheirHeadersBlocks()
    {
        const string code = """
            public static class Program
            {
                static int Describe(int temperature, int[] readings)
                {
                    if (temperature < 0)
                    {
                        return readings[0];
                    }
                    else if (temperature < 15)
                    {
                        return readings[1];
                    }
                    else
                    {
                        var index = 0;
                        while (index <= readings.Length)
                        {
                            index++;
                        }
                        return readings[index];
                    }
                }
            }
            """;

        Assert.Equal(["5 If False if (temperature < 0)", "9 ElseIf False else if (temperature < 15)", "13 Else True else", "16 While True while (index <= readings.Length)"],
            Around(CSharpNullOrigin.Dialect, code, 18));

        // After the loop, the while no longer holds the line.
        Assert.Equal(["5 If False if (temperature < 0)", "9 ElseIf False else if (temperature < 15)", "13 Else True else"], Around(CSharpNullOrigin.Dialect, code, 20));
        Assert.Equal(["5 If False if (temperature < 0)", "9 ElseIf True else if (temperature < 15)"], Around(CSharpNullOrigin.Dialect, code, 11));
    }

    [Fact]
    public void AnElseOfABlockInsideABranchIsThatBlocksNotTheChains()
    {
        const string ruby = """
            def label(count, names)
              if count > 1
                if names.empty?
                  "none"
                else
                  names.join(", ")
                end
              elsif count == 1
                names.first.upcase
              end
            end
            """;

        Assert.Equal(["2 If False if count > 1", "8 ElseIf True elsif count == 1"], Around(RubyNilOrigin.Dialect, ruby, 9));
        Assert.Equal(["2 If True if count > 1", "3 If False if names.empty?", "5 Else True else"], Around(RubyNilOrigin.Dialect, ruby, 6));

        const string lua = """
            local function label(count, names)
              if count > 1 then
                for _, name in ipairs(names) do
                  print(name)
                end
                return table.concat(names, ", ")
              elseif count == 1 then
                return names[1]:upper()
              end
            end
            """;

        Assert.Equal(["2 If False if count > 1 then", "7 ElseIf True elseif count == 1 then"], Around(LuaNilOrigin.Dialect, lua, 8));
    }

    [Fact]
    public void OnlyAForWithAConditionAloneIsGosWhile()
    {
        const string code = """
            package main

            func total(prices []int) int {
            	sum := 0
            	for index := 0; index <= len(prices); index++ {
            		sum += prices[index]
            	}
            	for sum < 100 {
            		sum += prices[len(prices)]
            	}
            	return sum
            }
            """;

        Assert.Empty(Around(GoNilOrigin.Dialect, code, 6));
        Assert.Equal(["8 While True for sum < 100"], Around(GoNilOrigin.Dialect, code, 9));
    }

    [Fact]
    public void WhatIsNotReadHereIsLeftOut()
    {
        // A while whose condition changes what it reads - its body sees index after the ++ - and an if without a block.
        const string code = """
            function total(prices) {
              let index = 0;
              let sum = 0;
              while (index++ < prices.length) {
                sum += prices[index].value;
              }
              if (sum > 0) sum += prices[index].value;
              return sum;
            }
            """;

        Assert.Empty(Around(JavaScriptMissingOrigin.Dialect, code, 5));
        Assert.Empty(Around(JavaScriptMissingOrigin.Dialect, code, 7));
    }
}
