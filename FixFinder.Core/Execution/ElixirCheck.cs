using System.Security.Cryptography;
using System.Text;

namespace FixFinder.Core.Execution;

/// <summary>
/// How an Elixir file is checked before it runs: compiled by Elixir itself, with none of it run. A small script of
/// FixFinder's reads the file with Elixir's own parser, compiles each module the file defines, and compiles the rest of the
/// file into a function that is never called - so Elixir writes every error and warning it would give when the program
/// runs, in its own words and at the same places, and runs nothing but what sits directly inside a defmodule, as running
/// the program would. Code.require_file, IO.gets and Mix.install are compiled and not run. A file of ExUnit tests calls
/// ExUnit.start() before its test module, and use ExUnit.Case will not compile without ExUnit's application running - so,
/// when the file's own top level calls ExUnit.start, the check starts that application first, as the file would, but does
/// not ask ExUnit to run the tests at exit, as ExUnit.start does: the check of a test file that ran to "Result: 1 passed" had
/// stopped with "cannot use ExUnit.Case without starting the ExUnit application". Code Elixir cannot read at all
/// ends the check with the error's own header - <c>** (TokenMissingError) token missing on marks.exs:6:4:</c> - and one
/// it will not compile with <c>** (CompileError) marks.exs: cannot compile file (errors have been logged)</c>, after the
/// errors themselves; both with a failing code.
/// </summary>
public static class ElixirCheck
{
    private const string CheckSource = """
        [path] = System.argv()

        try do
          quoted = Code.string_to_quoted!(File.read!(path), file: path, columns: true)

          statements =
            case quoted do
              {:__block__, _, statements} -> statements
              statement -> [statement]
            end

          {modules, rest} = Enum.split_with(statements, &match?({:defmodule, _, _}, &1))

          if Enum.any?(rest, &match?({{:., _, [{:__aliases__, _, [:ExUnit]}, :start]}, _, _}, &1)),
            do: Application.ensure_all_started(:ex_unit)

          for module <- modules, do: Code.eval_quoted(module, [], file: path)
          Code.eval_quoted({:fn, [], [{:->, [], [[], {:__block__, [], rest}]}]}, [], file: path)
        rescue
          error ->
            IO.puts(:stderr, Exception.format_banner(:error, error))
            System.halt(1)
        end
        """;

    /// <summary>The arguments that run the check, before the file's name: the check's own file, written once where it is read from.</summary>
    public static string Arguments => $"\"{Written()}\"";

    /// <summary>Writes the check where Elixir can read it - the temp folder - named for its own text, and says where.</summary>
    private static string Written()
    {
        var name = $"fixfinder_compile_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CheckSource)))[..12].ToLowerInvariant()}.exs";
        var check = Path.Combine(Path.GetTempPath(), "FixFinder-tests", name);

        if (!File.Exists(check))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(check)!);
            File.WriteAllText(check, CheckSource, new UTF8Encoding(false));
        }

        return check;
    }
}
