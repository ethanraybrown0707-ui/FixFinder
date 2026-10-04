using System.Globalization;
using System.Text.RegularExpressions;

namespace FixFinder.Core.Execution.BuildFiles;

/// <summary>
/// The conditions of if(), elseif() and while(), worked out as CMake works them out: parentheses, then the tests of one
/// argument (DEFINED, EXISTS, TARGET), then those of two (STREQUAL, VERSION_LESS, MATCHES), then NOT, AND and OR.
/// </summary>
/// <remarks>
/// A condition that depends on something not known - what execute_process would have printed, say - is taken as not
/// holding, and named where it matters, rather than guessed either way.
/// </remarks>
internal sealed partial class CMakeProject
{
    private static readonly HashSet<string> TrueConstants = new(StringComparer.OrdinalIgnoreCase) { "1", "ON", "YES", "TRUE", "Y" };

    private static readonly HashSet<string> FalseConstants = new(StringComparer.OrdinalIgnoreCase) { "0", "OFF", "NO", "FALSE", "N", "IGNORE", "NOTFOUND", "" };

    private static readonly HashSet<string> UnaryTests = new(StringComparer.Ordinal)
    {
        "EXISTS", "COMMAND", "POLICY", "TARGET", "TEST", "DEFINED", "IS_DIRECTORY", "IS_SYMLINK", "IS_ABSOLUTE", "IS_READABLE",
        "IS_WRITABLE", "IS_EXECUTABLE",
    };

    private static readonly HashSet<string> BinaryTests = new(StringComparer.Ordinal)
    {
        "EQUAL", "LESS", "GREATER", "LESS_EQUAL", "GREATER_EQUAL", "STREQUAL", "STRLESS", "STRGREATER", "STRLESS_EQUAL",
        "STRGREATER_EQUAL", "VERSION_EQUAL", "VERSION_LESS", "VERSION_GREATER", "VERSION_LESS_EQUAL", "VERSION_GREATER_EQUAL",
        "MATCHES", "IN_LIST", "PATH_EQUAL", "IS_NEWER_THAN",
    };

    /// <summary>CMake's own commands, for if(COMMAND ...): the ones a project asks about before using them.</summary>
    private static readonly HashSet<string> BuiltInCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "add_compile_definitions", "add_compile_options", "add_custom_command", "add_custom_target", "add_definitions", "add_dependencies",
        "add_executable", "add_library", "add_link_options", "add_subdirectory", "add_test", "aux_source_directory", "block", "break",
        "cmake_minimum_required", "cmake_parse_arguments", "cmake_path", "cmake_policy", "configure_file", "continue", "enable_language",
        "enable_testing", "execute_process", "file", "find_file", "find_library", "find_package", "find_path", "find_program", "foreach",
        "function", "get_filename_component", "get_property", "get_target_property", "if", "include", "include_directories", "install",
        "link_directories", "link_libraries", "list", "macro", "math", "message", "option", "project", "return", "set", "set_property",
        "set_target_properties", "string", "target_compile_definitions", "target_compile_features", "target_compile_options",
        "target_include_directories", "target_link_directories", "target_link_libraries", "target_link_options", "target_sources",
        "unset", "while",
    };

    public static bool IsTrueConstant(string text) =>
        TrueConstants.Contains(text) || (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number != 0);

    private static bool IsFalseConstant(string text) =>
        FalseConstants.Contains(text) || text.EndsWith("-NOTFOUND", StringComparison.OrdinalIgnoreCase) ||
        (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 0);

    private bool Condition(List<(string Text, bool Quoted)> arguments, Frame frame, CMakeCommand command)
    {
        var reading = new ConditionReading(this, arguments, frame, command);
        return reading.Or();
    }

    /// <summary>One condition being worked out, left to right, with where it has got to.</summary>
    private sealed class ConditionReading(CMakeProject project, List<(string Text, bool Quoted)> tokens, Frame frame, CMakeCommand command)
    {
        private int _at;

        private bool AtWord(string word) => _at < tokens.Count && !tokens[_at].Quoted && tokens[_at].Text == word;

        public bool Or()
        {
            var holds = And();

            while (AtWord("OR"))
            {
                _at++;
                holds = And() | holds;
            }

            return holds;
        }

        private bool And()
        {
            var holds = Not();

            while (AtWord("AND"))
            {
                _at++;
                holds = Not() & holds;
            }

            return holds;
        }

        private bool Not()
        {
            if (!AtWord("NOT")) return Comparison();

            _at++;
            return !Not();
        }

        private bool Comparison()
        {
            if (AtWord("("))
            {
                _at++;
                var inner = Or();
                if (AtWord(")")) _at++;
                return inner;
            }

            if (_at < tokens.Count && !tokens[_at].Quoted && UnaryTests.Contains(tokens[_at].Text) && _at + 1 < tokens.Count)
            {
                var test = tokens[_at].Text;
                var operand = tokens[_at + 1];
                _at += 2;
                return Unary(test, operand.Text);
            }

            if (_at >= tokens.Count) return false;

            var left = tokens[_at++];

            if (_at + 1 <= tokens.Count - 1 && !tokens[_at].Quoted && BinaryTests.Contains(tokens[_at].Text))
            {
                var test = tokens[_at].Text;
                var right = tokens[_at + 1];
                _at += 2;
                return Binary(test, left, right);
            }

            return Truthy(left);
        }

        private bool Unary(string test, string operand)
        {
            if (Unknown(operand)) return false;

            var path = Full(operand, frame.SourceFolder);

            return test switch
            {
                "DEFINED" when operand.StartsWith("ENV{", StringComparison.Ordinal) && operand.EndsWith('}') =>
                    Environment.GetEnvironmentVariable(operand[4..^1]) is not null,
                "DEFINED" when operand.StartsWith("CACHE{", StringComparison.Ordinal) && operand.EndsWith('}') =>
                    project._cache.ContainsKey(operand[6..^1]),
                "DEFINED" => project.IsDefined(operand, frame),
                "TARGET" => project._targets.ContainsKey(operand) || project._aliases.ContainsKey(operand),
                "COMMAND" => BuiltInCommands.Contains(operand) || project._definedCommands.ContainsKey(operand),
                "POLICY" => operand.StartsWith("CMP", StringComparison.Ordinal),
                "EXISTS" or "IS_READABLE" or "IS_WRITABLE" or "IS_EXECUTABLE" => File.Exists(path) || Directory.Exists(path),
                "IS_DIRECTORY" => Directory.Exists(path),
                "IS_ABSOLUTE" => Path.IsPathRooted(operand),
                _ => false,
            };
        }

        private bool Binary(string test, (string Text, bool Quoted) left, (string Text, bool Quoted) right)
        {
            var leftValue = Value(left);
            var rightValue = test == "IN_LIST" ? project.Variable(right.Text, frame) : Value(right);

            if (Unknown(leftValue) || Unknown(rightValue)) return false;

            return test switch
            {
                "STREQUAL" => leftValue == rightValue,
                "STRLESS" => string.CompareOrdinal(leftValue, rightValue) < 0,
                "STRGREATER" => string.CompareOrdinal(leftValue, rightValue) > 0,
                "STRLESS_EQUAL" => string.CompareOrdinal(leftValue, rightValue) <= 0,
                "STRGREATER_EQUAL" => string.CompareOrdinal(leftValue, rightValue) >= 0,
                "EQUAL" or "LESS" or "GREATER" or "LESS_EQUAL" or "GREATER_EQUAL" => Numbers(test, leftValue, rightValue),
                "MATCHES" => CMakeRegex(rightValue)?.IsMatch(leftValue) == true,
                "IN_LIST" => SplitList(rightValue).Contains(leftValue),
                "PATH_EQUAL" => string.Equals(Slashed(leftValue).TrimEnd('/'), Slashed(rightValue).TrimEnd('/'), StringComparison.Ordinal),
                "IS_NEWER_THAN" => !File.Exists(Full(leftValue, frame.SourceFolder)) || !File.Exists(Full(rightValue, frame.SourceFolder)) ||
                                   File.GetLastWriteTimeUtc(Full(leftValue, frame.SourceFolder)) >= File.GetLastWriteTimeUtc(Full(rightValue, frame.SourceFolder)),
                _ => Versions(test, leftValue, rightValue),
            };
        }

        /// <summary>An operand of a comparison: a quoted argument is the text; an unquoted one names a variable if there is one by that name.</summary>
        private string Value((string Text, bool Quoted) operand) =>
            !operand.Quoted && project.IsDefined(operand.Text, frame) ? project.Variable(operand.Text, frame) : operand.Text;

        /// <summary>
        /// if(argument) on its own: a constant is true or false; an unquoted name that is not a constant is a variable,
        /// true when it has a value that is not a false constant; a quoted argument is true only when it is a true constant.
        /// </summary>
        private bool Truthy((string Text, bool Quoted) operand)
        {
            if (Unknown(operand.Text)) return false;
            if (IsTrueConstant(operand.Text)) return true;
            if (operand.Quoted || IsFalseConstant(operand.Text)) return false;
            if (!project.IsDefined(operand.Text, frame)) return false;

            var value = project.Variable(operand.Text, frame);
            return !Unknown(value) && !IsFalseConstant(value);
        }

        private bool Unknown(string text)
        {
            if (!Unknowable.IsIn(text)) return false;

            foreach (var unknown in Unknowable.SourcesIn(text))
                project.NotFollowed($"line {command.Line} of {Path.GetFileName(command.File)} asks about {unknown}, which FixFinder does not know; it went on as if the condition did not hold");

            return true;
        }

        private static bool Numbers(string test, string left, string right)
        {
            if (!double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber) ||
                !double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber)) return false;

            return test switch
            {
                "EQUAL" => leftNumber == rightNumber,
                "LESS" => leftNumber < rightNumber,
                "GREATER" => leftNumber > rightNumber,
                "LESS_EQUAL" => leftNumber <= rightNumber,
                _ => leftNumber >= rightNumber,
            };
        }

        /// <summary>VERSION_LESS and the rest: dotted numbers compared part by part, a missing part counting as 0.</summary>
        private static bool Versions(string test, string left, string right)
        {
            var leftParts = VersionParts(left);
            var rightParts = VersionParts(right);
            var order = 0;

            for (var part = 0; part < Math.Max(leftParts.Count, rightParts.Count) && order == 0; part++)
            {
                var leftPart = part < leftParts.Count ? leftParts[part] : 0;
                var rightPart = part < rightParts.Count ? rightParts[part] : 0;
                order = leftPart.CompareTo(rightPart);
            }

            return test switch
            {
                "VERSION_EQUAL" => order == 0,
                "VERSION_LESS" => order < 0,
                "VERSION_GREATER" => order > 0,
                "VERSION_LESS_EQUAL" => order <= 0,
                "VERSION_GREATER_EQUAL" => order >= 0,
                _ => false,
            };
        }

        private static List<long> VersionParts(string version) =>
            version.Split('.').Select(part => Regex.Match(part, @"^\d+") is { Success: true } digits && long.TryParse(digits.Value, out var number) ? number : 0).ToList();
    }
}
