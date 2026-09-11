using System.Text.Json;
using FixFinder.Core.Engine;
using FixFinder.Core.Fingerprinting;
using FixFinder.Core.Http;

namespace FixFinder.Core.Sources;

/// <summary>
/// Searches Stack Overflow through the Stack Exchange API.
/// </summary>
/// <remarks>
/// Always advisory, never appliable. Stack Overflow answers are prose with code blocks in them,
/// essentially never unified diffs, so there is nothing here a patch engine could apply without
/// a language model deciding what the prose meant - and there is deliberately not one in this
/// tool. What this source is genuinely good at is the case web search is good at: an error from
/// a library you did not write, which hundreds of other people have already hit.
/// <para>
/// Two requests per search, no matter how many results come back: one search, then a single
/// batched call that fetches the answers to every question at once. The API charges per
/// request, not per item, so fetching answers one question at a time would cost ten times as
/// much for exactly the same data.
/// </para>
/// <para>
/// Rendering an answer body obliges us to attribute it under CC BY-SA, per the API terms. Every
/// candidate therefore carries its own attribution line rather than the window assembling one,
/// so the obligation cannot be lost by displaying a candidate somewhere new.
/// </para>
/// </remarks>
public sealed class StackOverflowFixSource(FixFinderHttpClient http) : IFixSource
{
    public const string Bucket = "stackexchange";

    private const string ApiRoot = "https://api.stackexchange.com/2.3";

    /// <summary>Questions carried through to the answers call. The API caps a batch at 100 ids.</summary>
    private const int QuestionsToDetail = 10;

    public string Name => "Stack Overflow";
    public bool RequiresNetwork => true;

    /// <summary>Always true - the API works unkeyed. A key only raises the daily allowance.</summary>
    public bool IsConfigured => true;

    public string QuotaBucket => Bucket;
    public QuotaStatus? Quota => http.Quota.Get(Bucket);

    /// <summary>Written to the log so a run can be retraced. Never includes the app key.</summary>
    public event Action<string>? Log;

    public async Task<FixSearchResult> SearchAsync(
        ErrorFingerprint fingerprint, SearchBudget budget, CancellationToken cancellationToken)
    {
        var queries = new List<string>();
        var requests = 0;

        var search = await RunSearchAsync(fingerprint.Tight.Text, budget, queries, cancellationToken);
        requests += search.NetworkRequests;

        if (search.Failure is not null)
            return FixSearchResult.Failed(Name, search.Failure, queries);

        // The relaxed query drops the quoted literals - the file name, the missing key, the
        // assembly version - which is exactly what makes a tight query unmatchable when the
        // value is unique to this run. Only worth a second request when the first found nothing.
        if (search.Questions.Count == 0 && queries.Count < budget.MaxSearchCalls)
        {
            Log?.Invoke("Stack Overflow: no results for the tight query, retrying with the relaxed one.");

            var relaxed = await RunSearchAsync(fingerprint.Relaxed.Text, budget, queries, cancellationToken);
            requests += relaxed.NetworkRequests;

            if (relaxed.Failure is not null)
                return FixSearchResult.Failed(Name, relaxed.Failure, queries);

            search = relaxed;
        }

        if (search.Questions.Count == 0)
            return new FixSearchResult(Name, [], queries, requests);

        var chosen = search.Questions.Take(Math.Min(QuestionsToDetail, budget.MaxCandidates)).ToArray();

        var (answers, answerRequests) = await FetchAnswersAsync(
            chosen.Select(q => q.Id), budget, cancellationToken);

        requests += answerRequests;

        var candidates = chosen
            .Select(question => ToCandidate(question, answers))
            .Take(budget.MaxCandidates)
            .ToArray();

        Log?.Invoke($"Stack Overflow: {candidates.Length} candidates from {requests} network request(s).");

        return new FixSearchResult(Name, candidates, queries, requests);
    }

    // ------------------------------------------------------------------ requests

    private sealed record SearchOutcome(
        IReadOnlyList<QuestionRecord> Questions, int NetworkRequests, string? Failure);

    private async Task<SearchOutcome> RunSearchAsync(
        string query, SearchBudget budget, List<string> queries, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchOutcome([], 0, "There was nothing in the error worth searching for.");

        queries.Add(query);

        var url =
            $"{ApiRoot}/search/advanced" +
            "?order=desc&sort=relevance&site=stackoverflow&filter=withbody&pagesize=20" +
            $"&q={Uri.EscapeDataString(Trim(query))}" +
            KeyParameter();

        var result = await http.GetAsync(url, Bucket, budget.Cache, ct: ct);
        var network = result.FromCache ? 0 : 1;

        if (!result.Ok) return new SearchOutcome([], network, Describe(result));

        using var document = Parse(result.Body, out var parseFailure);
        if (document is null) return new SearchOutcome([], network, parseFailure);

        ReadWrapper(document.RootElement, result.FromCache);

        var questions = document.RootElement
            .ArrayOrEmpty("items")
            .Select(ReadQuestion)
            .Where(q => q.Id > 0)
            .ToArray();

        return new SearchOutcome(questions, network, null);
    }

    /// <summary>
    /// Fetches the answers to every chosen question in one request.
    /// </summary>
    /// <remarks>
    /// The ids go in the path separated by semicolons, which is the API's batching convention
    /// and the single most valuable thing to know about it here: it turns ten requests into one
    /// against a daily allowance of three hundred.
    /// </remarks>
    private async Task<(IReadOnlyList<AnswerRecord> Answers, int Requests)> FetchAnswersAsync(
        IEnumerable<long> questionIds, SearchBudget budget, CancellationToken ct)
    {
        var ids = questionIds.Distinct().ToArray();
        if (ids.Length == 0 || budget.MaxDetailCalls < 1) return ([], 0);

        var url =
            $"{ApiRoot}/questions/{string.Join(";", ids)}/answers" +
            "?order=desc&sort=votes&site=stackoverflow&filter=withbody&pagesize=100" +
            KeyParameter();

        var result = await http.GetAsync(url, Bucket, budget.Cache, ct: ct);
        var network = result.FromCache ? 0 : 1;

        if (!result.Ok)
        {
            // Losing the answers is a degraded result, not a failed search: the questions still
            // carry their own bodies and their titles, which is often enough to recognise the
            // problem. Saying so beats returning nothing.
            Log?.Invoke($"Stack Overflow: could not fetch answer bodies - {Describe(result)}");
            return ([], network);
        }

        using var document = Parse(result.Body, out _);
        if (document is null) return ([], network);

        ReadWrapper(document.RootElement, result.FromCache);

        var answers = document.RootElement
            .ArrayOrEmpty("items")
            .Select(ReadAnswer)
            .Where(a => a.QuestionId > 0)
            .ToArray();

        return (answers, network);
    }

    /// <summary>
    /// Reads the fields Stack Exchange wraps around every response: the quota, and the backoff.
    /// </summary>
    /// <remarks>
    /// The backoff is the one that bites. It arrives in the body rather than a header, so no
    /// HTTP handler can see it, and ignoring it does not produce an error - it produces a
    /// temporary ban some minutes later, by which time the cause is long out of sight.
    /// <para>
    /// The quota is only reported when the response actually came from the network. A cached
    /// response carries the figures from whenever it was stored, and replaying those would make
    /// the label in the window count down while nothing was being requested at all.
    /// </para>
    /// </remarks>
    private void ReadWrapper(JsonElement root, bool fromCache)
    {
        if (root.Int64OrNull("backoff") is { } seconds and > 0)
        {
            Log?.Invoke($"Stack Overflow asked for a {seconds}s backoff; honouring it.");
            http.Limiter.RequireBackoff(Bucket, TimeSpan.FromSeconds(seconds));
        }

        if (fromCache) return;

        if (root.TryGet("quota_remaining", out _))
        {
            var remaining = root.IntOrZero("quota_remaining");
            var max = root.IntOrZero("quota_max");

            http.Quota.Report(new QuotaStatus(
                Bucket, remaining, max > 0 ? max : null,
                DateTimeOffset.UtcNow.Date.AddDays(1),
                http.StackExchangeKey is null
                    ? "300 a day unkeyed; a free Stack Apps key raises it to 10,000."
                    : null));
        }
    }

    private string KeyParameter() =>
        http.StackExchangeKey is { Length: > 0 } key ? $"&key={Uri.EscapeDataString(key)}" : "";

    // ------------------------------------------------------------------ reading

    private sealed record QuestionRecord(
        long Id, string Title, string Link, string BodyHtml, int Score, int AnswerCount,
        bool IsAnswered, long? AcceptedAnswerId, string? ClosedReason,
        DateTimeOffset? Created, DateTimeOffset? LastActivity, IReadOnlyList<string> Tags);

    private sealed record AnswerRecord(
        long Id, long QuestionId, string BodyHtml, int Score, bool IsAccepted,
        string Owner, DateTimeOffset? LastActivity);

    private static QuestionRecord ReadQuestion(JsonElement item) => new(
        item.Int64OrNull("question_id") ?? 0,
        WebDecode(item.StringOrEmpty("title")),
        item.StringOrEmpty("link"),
        item.StringOrEmpty("body"),
        item.IntOrZero("score"),
        item.IntOrZero("answer_count"),
        item.BoolOrFalse("is_answered"),
        item.Int64OrNull("accepted_answer_id"),
        item.StringOrNull("closed_reason"),
        item.UnixSecondsOrNull("creation_date"),
        item.UnixSecondsOrNull("last_activity_date"),
        item.StringArray("tags"));

    private static AnswerRecord ReadAnswer(JsonElement item) => new(
        item.Int64OrNull("answer_id") ?? 0,
        item.Int64OrNull("question_id") ?? 0,
        item.StringOrEmpty("body"),
        item.IntOrZero("score"),
        item.BoolOrFalse("is_accepted"),
        item.TryGet("owner", out var owner) ? owner.StringOrEmpty("display_name") : "",
        item.UnixSecondsOrNull("last_activity_date"));

    /// <summary>
    /// Pairs a question with its best answer.
    /// </summary>
    /// <remarks>
    /// The accepted answer wins over a higher-voted one, which is the opposite of what raw
    /// popularity would suggest and is right here: the person who actually had the problem said
    /// this was what fixed it. Score decides only when nothing was accepted.
    /// </remarks>
    private FixCandidate ToCandidate(QuestionRecord question, IReadOnlyList<AnswerRecord> answers)
    {
        var mine = answers.Where(a => a.QuestionId == question.Id).ToArray();

        var best =
            mine.FirstOrDefault(a => a.IsAccepted) ??
            mine.OrderByDescending(a => a.Score).FirstOrDefault();

        var questionText = HtmlText.ToPlainText(question.BodyHtml);

        var bodyText = best is null
            ? questionText
            : $"{HtmlText.Excerpt(questionText, 800)}\n\n--- answer ---\n\n{HtmlText.ToPlainText(best.BodyHtml)}";

        var owner = string.IsNullOrWhiteSpace(best?.Owner) ? "an anonymous user" : best!.Owner;
        var link = best is not null ? $"https://stackoverflow.com/a/{best.Id}" : question.Link;

        var attribution = best is not null
            ? $"Answer by {owner} on Stack Overflow, CC BY-SA — {link}"
            : $"Question on Stack Overflow, CC BY-SA — {question.Link}";

        var duplicate = string.Equals(question.ClosedReason, "Duplicate", StringComparison.OrdinalIgnoreCase);

        return new FixCandidate
        {
            SourceName = Name,
            Id = $"SO {question.Id}",
            Title = question.Title,
            Url = question.Link,
            BodyText = bodyText,

            // The answer's HTML, not the question's: M5 pulls code blocks out of this, and the
            // code that fixes the problem is in the answer.
            RawBody = best?.BodyHtml ?? question.BodyHtml,
            RawBodyIsHtml = true,

            Tags = question.Tags,
            CreatedAt = question.Created,
            LastActivityAt = best?.LastActivity ?? question.LastActivity,

            Votes = best?.Score ?? question.Score,
            AnswerCount = question.AnswerCount,
            AnswerNoun = "answers",
            HasAcceptedAnswer = question.AcceptedAnswerId is not null,

            IsClosed = question.ClosedReason is { Length: > 0 },
            ClosedReason = question.ClosedReason,

            // A closed question here is one the site turned down, not one it settled.
            Closure = ClosureMeaning.Rejected,
            DuplicateOfUrl = duplicate ? question.Link : null,

            Attribution = attribution,

            // Stack Overflow is advisory by definition. Nothing here is ever auto-applied.
            Tier = FixTier.Advisory,
        };
    }

    // ------------------------------------------------------------------ plumbing

    private static string WebDecode(string text) => System.Net.WebUtility.HtmlDecode(text);

    /// <summary>Keeps a query inside a length the API will accept without complaint.</summary>
    private static string Trim(string query) => query.Length <= 240 ? query : query[..240];

    private static JsonDocument? Parse(string body, out string? failure)
    {
        try
        {
            failure = null;
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Almost always one specific mistake: Stack Exchange gzips every response whether
            // or not you asked for it, so a client without automatic decompression reads the
            // compressed bytes as text and lands here.
            failure =
                "Stack Exchange returned something that was not JSON. If this persists, the " +
                "response was probably still compressed when it was read.";

            return null;
        }
    }

    /// <summary>Prefers the API's own error text over the generic HTTP description.</summary>
    private static string Describe(HttpResult result)
    {
        if (result.WasCacheMiss) return "Cached results only is on, and this search is not cached.";

        try
        {
            using var document = JsonDocument.Parse(result.Body);
            var message = document.RootElement.StringOrNull("error_message");
            var name = document.RootElement.StringOrNull("error_name");

            if (message is { Length: > 0 })
                return $"Stack Exchange refused the request: {message}{(name is null ? "" : $" ({name})")}";
        }
        catch (JsonException)
        {
            // Fall through to the HTTP-level description.
        }

        return result.Failure ?? $"Stack Exchange returned {(int)result.Status}.";
    }
}
