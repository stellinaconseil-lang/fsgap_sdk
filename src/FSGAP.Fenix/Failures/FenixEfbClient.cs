using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FSGAP.Fenix.Failures;

/// <summary>How an EFB call ended, before any failure semantics are applied.</summary>
internal enum EfbOutcome
{
    /// <summary>A 2xx answer was received and read.</summary>
    Ok,

    /// <summary>No connection could be made (EFB not running, port closed): nothing reached the EFB.</summary>
    Unreachable,

    /// <summary>No answer within <see cref="FenixOptions.RequestTimeout"/>: the request may have been processed.</summary>
    TimedOut,

    /// <summary>The exchange broke after the connection was made: the request may have been processed.</summary>
    Interrupted,

    /// <summary>The EFB answered with a non-success HTTP status.</summary>
    HttpError,

    /// <summary>The EFB answered 2xx with a body that cannot be interpreted.</summary>
    Malformed,
}

/// <summary>One entry of the EFB's failure list as read, unvalidated.</summary>
/// <param name="Id">Fenix id, or <see langword="null"/> when missing.</param>
/// <param name="Title">Fenix title, or <see langword="null"/>.</param>
/// <param name="Failed">Live failed state, or <see langword="null"/> when missing or not a boolean.</param>
internal readonly record struct EfbFailureEntry(string? Id, string? Title, bool? Failed);

/// <summary>Result of a <c>saveManual</c> call.</summary>
/// <param name="Outcome">How the call ended.</param>
/// <param name="HttpStatus">HTTP status when one was received.</param>
/// <param name="EchoedFailed">The <c>failed</c> state echoed by the EFB, when the answer carries one.</param>
internal readonly record struct EfbSaveResult(EfbOutcome Outcome, int? HttpStatus = null, bool? EchoedFailed = null);

/// <summary>Result of a failure-list read.</summary>
/// <param name="Outcome">How the call ended.</param>
/// <param name="HttpStatus">HTTP status when one was received.</param>
/// <param name="Entries">The entries, when <see cref="Outcome"/> is <see cref="EfbOutcome.Ok"/>.</param>
internal readonly record struct EfbListResult(EfbOutcome Outcome, int? HttpStatus = null, IReadOnlyList<EfbFailureEntry>? Entries = null);

/// <summary>
/// The Fenix EFB failure API over HTTP: the only code in FSGAP that knows its endpoints and payloads.
/// </summary>
/// <remarks>
/// <para>
/// The API is local, unofficial and was reverse-engineered by the audited FSHANGAR client from the EFB's own
/// JavaScript, then verified live (research/FENIX_FAILURE_PROTOCOL.md there):
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>POST /fenix/failures/saveManual</c>, body <c>{"id","title","failureCondition":null,"failed"}</c>. The title must be
/// Fenix's own. The answer echoes the saved object, including <c>failed</c>.
/// </description></item>
/// <item><description>
/// <c>GET /fenix/failures/manual</c>: <c>{"atas":[{"groups":[{"failures":[{"id","title","failureCondition","failed"}]}]}]}</c>,
/// with <c>failed</c> the live state of every manual failure.
/// </description></item>
/// </list>
/// <para>
/// Every call has its own timeout (<see cref="FenixOptions.RequestTimeout"/>, measured on the injected clock), so the
/// shared <see cref="HttpClient"/>'s own timeout is irrelevant. Nothing here retries: the caller decides, knowing a
/// command may not be idempotent. Transport errors are classified, never thrown, except cancellation by the caller.
/// </para>
/// </remarks>
internal sealed class FenixEfbClient
{
    private const string SavePath = "fenix/failures/saveManual";
    private const string ListPath = "fenix/failures/manual";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    private readonly HttpClient _http;
    private readonly FenixOptions _options;
    private readonly TimeProvider _time;
    private readonly Uri _saveUri;
    private readonly Uri _listUri;

    internal FenixEfbClient(HttpClient http, FenixOptions options, TimeProvider time)
    {
        _http = http;
        _options = options;
        _time = time;
        var baseAddress = options.EfbBaseAddress.AbsoluteUri.EndsWith('/') ? options.EfbBaseAddress : new Uri(options.EfbBaseAddress.AbsoluteUri + "/");
        _saveUri = new Uri(baseAddress, SavePath);
        _listUri = new Uri(baseAddress, ListPath);
    }

    /// <summary>The EFB origin, for log messages.</summary>
    internal string Origin => _options.EfbBaseAddress.GetLeftPart(UriPartial.Authority);

    /// <summary>Sets one manual failure's state.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    internal async Task<EfbSaveResult> SaveManualAsync(FenixRawFailure failure, bool failed, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var body = new SaveManualRequest(failure.Id, failure.Title, failed);
            using var response = await _http.PostAsJsonAsync(_saveUri, body, Json, linked.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new EfbSaveResult(EfbOutcome.HttpError, status);
            }

            var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            return new EfbSaveResult(EfbOutcome.Ok, status, ReadEchoedFailed(text));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new EfbSaveResult(EfbOutcome.TimedOut);
        }
        catch (HttpRequestException ex)
        {
            return new EfbSaveResult(IsUnreachable(ex) ? EfbOutcome.Unreachable : EfbOutcome.Interrupted);
        }
    }

    /// <summary>Reads the live state of every manual failure.</summary>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    internal async Task<EfbListResult> GetManualAsync(CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_options.RequestTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await _http.GetAsync(_listUri, linked.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new EfbListResult(EfbOutcome.HttpError, status);
            }

            var text = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            var entries = ReadList(text);
            return entries is null ? new EfbListResult(EfbOutcome.Malformed, status) : new EfbListResult(EfbOutcome.Ok, status, entries);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new EfbListResult(EfbOutcome.TimedOut);
        }
        catch (HttpRequestException ex)
        {
            return new EfbListResult(IsUnreachable(ex) ? EfbOutcome.Unreachable : EfbOutcome.Interrupted);
        }
    }

    /// <summary>
    /// The request never reached the EFB: connection refused, host not found. Anything else (a reset mid-exchange)
    /// may have happened after the EFB received the request.
    /// </summary>
    private static bool IsUnreachable(HttpRequestException ex) =>
        ex.HttpRequestError is HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError;

    /// <summary>The <c>failed</c> value of a <c>saveManual</c> answer, or <see langword="null"/> when there is none.</summary>
    internal static bool? ReadEchoedFailed(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("failed", out var failed)
                && failed.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? failed.GetBoolean()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Flattens <c>atas[].groups[].failures[]</c>. Returns <see langword="null"/> when the overall shape is not
    /// recognized; individual malformed entries are kept with their missing parts as <see langword="null"/>.
    /// </summary>
    internal static IReadOnlyList<EfbFailureEntry>? ReadList(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("atas", out var atas)
                || atas.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var entries = new List<EfbFailureEntry>();
            foreach (var failure in atas.EnumerateArray()
                .SelectMany(ata => Children(ata, "groups"))
                .SelectMany(group => Children(group, "failures")))
            {
                entries.Add(new EfbFailureEntry(
                    StringOf(failure, "id"),
                    StringOf(failure, "title"),
                    failure.ValueKind == JsonValueKind.Object && failure.TryGetProperty("failed", out var f) && f.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? f.GetBoolean()
                        : null));
            }

            return entries;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonElement> Children(JsonElement parent, string property) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToArray()
            : [];

    private static string? StringOf(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The exact payload the EFB's own client posts for a plain on/off toggle. <c>failureCondition</c> (Fenix's
    /// conditional arming by IAS/altitude/time) is always null: never used by any audited consumer.
    /// </summary>
    private sealed record SaveManualRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("failed")] bool Failed)
    {
        [JsonPropertyName("failureCondition")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public object? FailureCondition => null;
    }
}
