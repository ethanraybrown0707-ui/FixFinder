using System.Globalization;
using System.Text.Json;
using FixFinder.Core.Analysis.Ir;

namespace FixFinder.Core.Analysis.Recognition;

/// <summary>
/// A pair of functions named to undo each other, tried: the one that does it is called with words or numbers of
/// FixFinder's own - and a key beside each, when both take one by the same name - and what it gives back is given to the
/// other, which has to give back what the first was given.
/// </summary>
/// <remarks>
/// What a pair is written for is not known from its names: a cipher takes words and a shift, a Roman numeral pair numbers.
/// So it is tried with each kind in turn - words, with a number and then a word for a key, or words and then numbers - and
/// a kind on which every input came back as it was is the kind it is written for, and nothing is found. Only when no kind
/// came back right every time is the first that came back wrong a finding: JavaScript turns a word given to a pair written
/// for numbers into something else without stopping, and what comes back of that is no mistake of the pair's. The words
/// are lower-case letters alone, so a cipher that keeps letters only, or makes them capitals, is not held to anything
/// else; and an answer that differs only in its letters' case is not judged.
/// </remarks>
public static partial class AlgorithmTrial
{
    /// <summary>
    /// The words a pair is tried with, shortest first: one letter, the start and end of the alphabet - which a cipher has to
    /// wrap round - words with letters repeated, which a run-length code counts, one letter twelve times over, whose count
    /// takes two digits, and the whole alphabet.
    /// </summary>
    private static readonly string[] WordsToTurnBack = ["a", "abc", "xyz", "hello", "zebra", "aaabccdddd", "mississippi", "aaaaaaaaaaaab", "abcdefghijklmnopqrstuvwxyz"];

    /// <summary>The numbers a pair is tried with: from 1, as a Roman numeral has no 0, to 3999, the largest it writes plainly.</summary>
    private static readonly int[] NumbersToTurnBack = [1, 4, 9, 14, 40, 90, 400, 1994, 2024, 3999];

    /// <summary>The keys given beside each word when the key is a number: a small shift and one that wraps almost all the way round.</summary>
    private static readonly int[] NumberKeys = [3, 25];

    /// <summary>The key given beside each word when the key is a word, as a Vigenère cipher's is.</summary>
    private const string WordKey = "key";

    /// <summary>How many inputs have to come back for a pair to be said to undo each other.</summary>
    private const int FewestTurnedBack = 3;

    /// <summary>
    /// Two functions of a pair, the one that goes first and the one that undoes it, each with the part its parameters play
    /// - the value it works on, the key both take, or none, for one left to its default.
    /// </summary>
    private sealed record PairCalls(IrFunction Doing, IrFunction Undoing, List<(string Name, string? Role)> DoingRoles, List<(string Name, string? Role)> UndoingRoles)
    {
        public bool Keyed => DoingRoles.Any(role => role.Role == "key");

        public PairCalls OtherWayRound => new(Undoing, Doing, UndoingRoles, DoingRoles);
    }

    /// <summary>One kind of input a pair is tried with - its trials - and how they are said.</summary>
    private sealed record InputKind(IReadOnlyList<Trial> Trials, string Described);

    /// <summary>
    /// The pair's two functions as they can be called, or null when they cannot be: not Python functions at the top of their
    /// file, JavaScript ones at the top of a CommonJS file nor static methods of the same Java class; a parameter neither
    /// the value, a key both take by the same name, nor one with a default; or, in Java, values and keys that are not an
    /// int or a String, or a value the one gives back that the other does not take.
    /// </summary>
    private static PairCalls? PairToTurnBack(AlgorithmSeen recognition, IrProgram program)
    {
        var language = Path.GetExtension(recognition.File).ToLowerInvariant() switch
        {
            ".py" => SourceLanguage.Python,
            ".js" or ".cjs" => SourceLanguage.JavaScript,
            ".java" => SourceLanguage.Java,
            _ => (SourceLanguage?)null,
        };
        if (language is not { } chosen || chosen != program.Language || recognition.UndoneBy is not { } undoneBy) return null;

        IrFunction? Found(string name) => chosen == SourceLanguage.Java ? JavaMethod(name, recognition.File, program) : TopLevelFunction(name, recognition.File, program);
        if (Found(recognition.Function) is not { } doing || Found(undoneBy) is not { } undoing ||
            doing.IsAsync || doing.IsGenerator || undoing.IsAsync || undoing.IsGenerator)
            return null;

        if (PairRoles(doing, undoing, chosen) is not { } doingRoles || PairRoles(undoing, doing, chosen) is not { } undoingRoles) return null;

        // Java's types are known: what the one gives back has to be what the other takes, each way round.
        if (chosen == SourceLanguage.Java &&
            (doing.ReturnType.Name != undoing.Parameters[0].Type.Name || undoing.ReturnType.Name != doing.Parameters[0].Type.Name))
            return null;

        return new PairCalls(doing, undoing, doingRoles, undoingRoles);
    }

    /// <summary>
    /// The part each of one function's parameters plays beside the other's: its first is what it works on; one the other
    /// takes too, by the same name, is the key both are given - at most one; any other has to have a default.
    /// </summary>
    private static List<(string Name, string? Role)>? PairRoles(IrFunction function, IrFunction other, SourceLanguage language)
    {
        var sharedNames = other.Parameters.Skip(1).Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var roles = new List<(string Name, string? Role)>();

        foreach (var (parameter, position) in function.Parameters.Select((parameter, position) => (parameter, position)))
        {
            if (parameter.Kind is ParameterKind.Rest or ParameterKind.Keywords || !Identifier().IsMatch(parameter.Name)) return null;

            string? role = position == 0 ? "value" : sharedNames.Contains(parameter.Name) ? "key" : null;
            if (role is null && parameter.Default is null) return null;
            if (language == SourceLanguage.Java && role is not null && parameter.Type.Name is not ("int" or "String")) return null;
            roles.Add((parameter.Name, role));
        }

        return roles.Count(role => role.Role == "key") <= 1 ? roles : null;
    }

    /// <summary>
    /// The kinds of input a pair is tried with, in turn: for Java, the one its types take; for Python and JavaScript, words
    /// with a number and then a word for a key, or, with no key, words and then numbers.
    /// </summary>
    private static IEnumerable<InputKind> InputKindsFor(PairCalls pair, SourceLanguage language)
    {
        var words = WordsToTurnBack.Select(word => new Trial([], null, Text: word)).ToList();
        var numbers = NumbersToTurnBack.Select(number => new Trial([], null, number)).ToList();
        var wordsSaid = $"{WordsToTurnBack.Length} words of FixFinder's own, of lower-case letters alone, from one letter to the whole alphabet";
        var numbersSaid = $"{NumbersToTurnBack.Length} numbers of FixFinder's own, from {NumbersToTurnBack[0]} to {NumbersToTurnBack[^1]}";

        IReadOnlyList<Trial> WithNumberKeys(IEnumerable<Trial> trials) => [.. trials.SelectMany(trial => NumberKeys.Select(key => trial with { KeyNumber = key }))];
        IReadOnlyList<Trial> WithWordKey(IEnumerable<Trial> trials) => [.. trials.Select(trial => trial with { KeyText = WordKey })];
        var numberKeysSaid = $", each with the key {string.Join(" and then with ", NumberKeys)}";
        var wordKeySaid = $", each with the word {WordKey} as the key";

        if (language == SourceLanguage.Java)
        {
            var valuesAreWords = pair.Doing.Parameters[0].Type.Name == "String";
            var (values, valuesSaid) = valuesAreWords ? (words, wordsSaid) : (numbers, numbersSaid);
            var keyType = pair.Doing.Parameters.Zip(pair.DoingRoles).FirstOrDefault(each => each.Second.Role == "key").First?.Type.Name;
            yield return keyType switch
            {
                "int" => new InputKind(WithNumberKeys(values), valuesSaid + numberKeysSaid),
                "String" => new InputKind(WithWordKey(values), valuesSaid + wordKeySaid),
                _ => new InputKind(values, valuesSaid),
            };
            yield break;
        }

        if (pair.Keyed)
        {
            yield return new InputKind(WithNumberKeys(words), wordsSaid + numberKeysSaid);
            yield return new InputKind(WithWordKey(words), wordsSaid + wordKeySaid);
        }
        else
        {
            yield return new InputKind(words, wordsSaid);
            yield return new InputKind(numbers, numbersSaid);
        }
    }

    /// <summary>
    /// The pair tried with each kind of input, the one that does it first - and, for a pair named each for turning one thing
    /// into the other, the other first too: a sentence saying every input came back, or the first that came back wrong when
    /// none did, to be a finding; both null when neither could be said.
    /// </summary>
    private static async Task<(string? Said, string? Broken)> TurnedBackAsync(AlgorithmSeen recognition, PairCalls pair, IrProgram program,
        string runtime, string? java, CancellationToken cancellationToken)
    {
        string? firstWrong = null;
        IEnumerable<PairCalls> orders = recognition.EitherWayRound ? [pair, pair.OtherWayRound] : [pair];

        foreach (var order in orders)
        {
            foreach (var kind in InputKindsFor(order, program.Language))
            {
                var results = program.Language switch
                {
                    SourceLanguage.Python => await RunPythonScriptAsync(recognition.File, RoundTripScript, [order.Doing.FullName, order.Undoing.FullName],
                        RoundTripPlan(order, kind.Trials), kind.Trials, runtime, cancellationToken),
                    SourceLanguage.JavaScript => await RunJavaScriptCopyAsync(recognition.File, RoundTripJavaScript(order, RoundTripPlan(order, kind.Trials)),
                        kind.Trials, program, runtime, cancellationToken),
                    SourceLanguage.Java when java is not null => await RunJavaTrialClassAsync(recognition.File, order.Doing.Owner!,
                        owner => RoundTripJava(owner, order, kind.Trials), kind.Trials, runtime, java, cancellationToken),
                    _ => null,
                };
                if (results is null) continue;

                var (said, wrong) = JudgeRoundTrip(order, kind, results, recognition.File);
                if (said is not null) return (said, null);
                firstWrong ??= wrong;
            }
        }

        return firstWrong is null ? (null, null) : (firstWrong + ".", firstWrong);
    }

    /// <summary>
    /// Whether what the one gave back for each input, the other gave back as the input: a sentence when at least three did
    /// and none did not, or the first that did not. An input the one stopped on, or gave back nothing - or nothing but
    /// an empty word or list - for, is not one it is written for, and is passed over, as is one it gave back something
    /// other than a word, a number or a list of them for, and an answer of that kind, or one that differs from the input
    /// only in its letters' case; an input the other stopped on, given what the one gave back, came back wrong.
    /// </summary>
    private static (string? Said, string? Wrong) JudgeRoundTrip(PairCalls order, InputKind kind, IReadOnlyList<Result> results, string file)
    {
        var doing = $"`{order.Doing.FullName}`";
        var undoing = $"`{order.Undoing.FullName}`";
        string Written(string word) => AsWritten(JsonSerializer.SerializeToElement(word), file);
        var turnedBack = 0;

        foreach (var result in results)
        {
            var trial = result.Trial;
            var given = trial.Text is { } word ? Written(word) : trial.Number!.Value.ToString(CultureInfo.InvariantCulture);
            var keyed = trial.KeyNumber is { } shift ? $" and {shift}" : trial.KeyText is { } key ? $" and {Written(key)}" : "";

            // What the first gave back has to be a plain value - a word, a number, or a list of them: an object, a buffer or a
            // map is what a pair written for other things than FixFinder's words and numbers gives back for them.
            if (result.Stage != "undoing" || result.Between is not { } between || IsNothing(between) || !IsPlain(between)) continue;
            var gaveBack = AsWritten(between, file);
            if (result.Error is { } error)
                return (null, $"Called with {given}{keyed}, {doing} gave back {gaveBack}; given that{keyed}, {undoing} stopped with {error}");

            if (result.Returned is not { ValueKind: not JsonValueKind.Null } returned || !IsPlain(returned)) continue;
            if (IsTheInput(returned, trial))
            {
                turnedBack++;
                continue;
            }

            if (returned.ValueKind == JsonValueKind.String && trial.Text is { } input && string.Equals(returned.GetString(), input, StringComparison.OrdinalIgnoreCase)) continue;
            return (null, $"Called with {given}{keyed}, {doing} gave back {gaveBack}, and given that{keyed}, {undoing} gave back {AsWritten(returned, file)} - " +
                          $"not {given}, what {doing} was given");
        }

        if (turnedBack < FewestTurnedBack) return (null, null);
        return (turnedBack == results.Count
            ? $"Called with {kind.Described} - {results.Count} calls - {doing} gave back something every time, and {undoing} turned each back into what it was."
            : $"Called with {kind.Described} - {results.Count} calls - {doing} gave back something {turnedBack} times, and {undoing} turned each of those back into what it was.",
            null);

        static bool IsNothing(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => value.GetString()!.Length == 0,
            JsonValueKind.Array => value.GetArrayLength() == 0,
            _ => false,
        };

        // Plain values are written as JSON writes them; anything else as {"repr": ...}, the text it is shown as.
        static bool IsPlain(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => true,
            JsonValueKind.Array => value.EnumerateArray().All(IsPlain),
            _ => false,
        };

        static bool IsTheInput(JsonElement returned, Trial trial) => trial.Text is { } word
            ? returned.ValueKind == JsonValueKind.String && returned.GetString() == word
            : returned.ValueKind == JsonValueKind.Number && returned.TryGetInt64(out var number) && number == trial.Number;
    }

    /// <summary>
    /// What to call a pair with: each function's parameters' parts, by name and in order, and each trial's input - a word or
    /// a number - and key.
    /// </summary>
    private static string RoundTripPlan(PairCalls order, IReadOnlyList<Trial> trials) => JsonSerializer.Serialize(new
    {
        doing = order.DoingRoles.Where(role => role.Role is not null).ToDictionary(role => role.Name, role => role.Role),
        undoing = order.UndoingRoles.Where(role => role.Role is not null).ToDictionary(role => role.Name, role => role.Role),
        doingParameters = order.DoingRoles.Select(role => role.Role),
        undoingParameters = order.UndoingRoles.Select(role => role.Role),
        trials = trials.Select(trial => new { value = (object?)trial.Text ?? trial.Number, key = (object?)trial.KeyNumber ?? trial.KeyText }),
    });

    /// <summary>
    /// Calls the one of a pair on each trial's input and key, from the file's definitions alone, and the other on what it
    /// gave back, printing what each gave back or what stopped it. A trial still going after 2 seconds is said, and the
    /// trials end there.
    /// </summary>
    private const string RoundTripScript = PythonTrialHelpers + "\n\n" + """
        target, doing_name, undoing_name, plan_file = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
        with open(plan_file, encoding="utf-8") as given:
            plan = json.load(given)

        space = definitions(target)
        doing, undoing = space[doing_name], space[undoing_name]

        def given_to(roles, value, trial):
            return {parameter: value if role == "value" else trial["key"] for parameter, role in roles.items()}

        for index, trial in enumerate(plan["trials"]):
            outcome = {"trial": index, "stage": "doing"}

            def call():
                try:
                    between = doing(**given_to(plan["doing"], trial["value"], trial))
                    outcome["between"] = plain(between)
                    if between is None:
                        return
                    outcome["stage"] = "undoing"
                    outcome["returned"] = plain(undoing(**given_to(plan["undoing"], between, trial)))
                except BaseException as error:
                    said = str(error)
                    outcome["error"] = type(error).__name__ + (": " + said[:200] if said else "")

            worker = threading.Thread(target=call, daemon=True)
            worker.start()
            worker.join(2)
            if worker.is_alive():
                say({"trial": index, "unfinished": True})
                os._exit(0)
            say(outcome)

        os._exit(0)
        """;

    /// <summary>
    /// The calls put at the end of a JavaScript copy, inside the file's own scope: the one of the pair on each trial's input
    /// and key, then the other on what it gave back, and what each gave back or what stopped them written out a line each.
    /// </summary>
    private static string RoundTripJavaScript(PairCalls order, string plan) => $$"""
        ;(function () {
            const fixfinderPlan = {{plan}};
            {{JavaScriptPlain}}
            const fixfinderGiven = (parameters, value, trial) =>
                parameters.map(role => role === "value" ? value : role === "key" ? trial.key : undefined);
            fixfinderPlan.trials.forEach((trial, index) => {
                const outcome = { trial: index, stage: "doing" };
                try {
                    const between = {{order.Doing.FullName}}(...fixfinderGiven(fixfinderPlan.doingParameters, trial.value, trial));
                    outcome.between = fixfinderPlain(between, 0);
                    if (between !== undefined && between !== null) {
                        outcome.stage = "undoing";
                        outcome.returned = fixfinderPlain({{order.Undoing.FullName}}(...fixfinderGiven(fixfinderPlan.undoingParameters, between, trial)), 0);
                    }
                } catch (failure) {
                    outcome.error = ((failure && failure.name) || "Error") + (failure && failure.message ? ": " + String(failure.message).slice(0, 200) : "");
                }
                require("fs").writeSync(1, "{{Said}}" + JSON.stringify(outcome) + "\n");
            });
            process.exit(0);
        })();
        """;

    /// <summary>
    /// The small class that calls a pair of Java static methods on each trial, found by reflection and made callable when
    /// private: the one on the input and key, then the other on what it gave back, both in a thread given 2 seconds, and
    /// what each gave back or what stopped them written out a line each.
    /// </summary>
    private static string RoundTripJava(string owner, PairCalls order, IReadOnlyList<Trial> trials)
    {
        static string ClassesOf(IrFunction method) => string.Join("", method.Parameters.Select(parameter => ", " + (parameter.Type.Name == "int" ? "int.class" : "String.class")));
        static string GivenTo(List<(string Name, string? Role)> roles, string value) =>
            string.Join(", ", roles.Select(role => role.Role == "value" ? value : role.Role == "key" ? "key" : "null"));

        var values = string.Join(", ", trials.Select(trial => trial.Text is { } word ? JsonSerializer.Serialize(word) : trial.Number!.Value.ToString(CultureInfo.InvariantCulture)));
        var keys = string.Join(", ", trials.Select(trial => trial.KeyNumber is { } shift ? shift.ToString(CultureInfo.InvariantCulture)
            : trial.KeyText is { } key ? JsonSerializer.Serialize(key) : "null"));

        return $$"""
            public class {{JavaTrialClass}} {
                {{JavaTrialHelpers}}

                public static void main(String[] args) throws Exception {
                    Class<?> owner = Class.forName("{{owner}}");
                    final java.lang.reflect.Method doing = owner.getDeclaredMethod("{{order.Doing.Name}}"{{ClassesOf(order.Doing)}});
                    final java.lang.reflect.Method undoing = owner.getDeclaredMethod("{{order.Undoing.Name}}"{{ClassesOf(order.Undoing)}});
                    doing.setAccessible(true);
                    undoing.setAccessible(true);
                    final Object[] values = { {{values}} };
                    final Object[] keys = { {{keys}} };
                    for (int index = 0; index < values.length; index++) {
                        final Object value = values[index];
                        final Object key = keys[index];
                        final StringBuilder outcome = new StringBuilder("\"stage\":\"doing\"");
                        Thread call = new Thread(() -> {
                            try {
                                Object between = doing.invoke(null, new Object[] { {{GivenTo(order.DoingRoles, "value")}} });
                                if (between == null) {
                                    outcome.append(",\"between\":null");
                                    return;
                                }
                                outcome.setLength(0);
                                outcome.append("\"stage\":\"undoing\",\"between\":").append(plain(between));
                                Object returned = undoing.invoke(null, new Object[] { {{GivenTo(order.UndoingRoles, "between")}} });
                                outcome.append(",\"returned\":").append(plain(returned));
                            } catch (java.lang.reflect.InvocationTargetException thrown) {
                                Throwable cause = thrown.getCause();
                                String message = cause.getMessage();
                                outcome.append(",\"error\":").append(quote(cause.getClass().getSimpleName() + (message != null ? ": " + message : "")));
                            } catch (Throwable other) {
                                outcome.append(",\"error\":").append(quote(other.toString()));
                            }
                        });
                        call.setDaemon(true);
                        call.start();
                        call.join(2000);
                        if (call.isAlive()) {
                            System.out.println("{{Said}}{\"trial\":" + index + ",\"unfinished\":true}");
                            System.out.flush();
                            System.exit(0);
                        }
                        System.out.println("{{Said}}{\"trial\":" + index + "," + outcome + "}");
                    }
                    System.out.flush();
                    System.exit(0);
                }
            }
            """;
    }
}
