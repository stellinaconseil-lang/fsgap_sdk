using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FSGAP.Fenix.Failures;

namespace FSGAP.Fenix.Tests;

/// <summary>
/// An in-memory Fenix EFB behind a fake <see cref="HttpMessageHandler"/>: the two audited endpoints, the live failed
/// state of every catalog failure, and scriptable misbehaviour. No network.
/// </summary>
internal sealed class FakeEfb : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, bool> _failed = new(StringComparer.OrdinalIgnoreCase);
    private int _requests;

    public FakeEfb()
    {
        foreach (var raw in FenixFailureCatalogData.Default.Raw)
        {
            _failed[raw.Id] = false;
        }
    }

    /// <summary>Every request, in order: method, path, body.</summary>
    public ConcurrentQueue<(string Method, string Path, string? Body)> Requests { get; } = new();

    public int RequestCount => Volatile.Read(ref _requests);

    /// <summary>Connection refused on every request.</summary>
    public bool Unreachable { get; set; }

    /// <summary>Requests never answer (until cancelled).</summary>
    public bool Hang { get; set; }

    /// <summary>Only saveManual hangs.</summary>
    public bool HangOnSave { get; set; }

    /// <summary>Only the failure list hangs.</summary>
    public bool HangOnList { get; set; }

    /// <summary>Status code returned by saveManual (200 by default).</summary>
    public HttpStatusCode SaveStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>Status code returned by the list (200 by default).</summary>
    public HttpStatusCode ListStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>saveManual applies the state but echoes the opposite (the tyre-pressure quirk).</summary>
    public bool StaleEcho { get; set; }

    /// <summary>saveManual answers 200 with this body instead of the echo.</summary>
    public string? SaveBodyOverride { get; set; }

    /// <summary>saveManual answers 200 but does not apply the state.</summary>
    public bool IgnoreSaves { get; set; }

    /// <summary>The list answers with this body instead of the real list.</summary>
    public string? ListBodyOverride { get; set; }

    /// <summary>A request started (to synchronise with a hanging request).</summary>
    public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsFailed(string fenixId) => _failed[fenixId];

    public void SetFailed(string fenixId, bool failed) => _failed[fenixId] = failed;

    public IEnumerable<string> ActiveIds => _failed.Where(p => p.Value).Select(p => p.Key);

    public IReadOnlyList<JsonObject> SaveBodies => Requests.Where(r => r.Method == "POST").Select(r => JsonNode.Parse(r.Body!)!.AsObject()).ToArray();

    public void ResetStarted() => Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Enqueue((request.Method.Method, request.RequestUri!.AbsolutePath, body));
        Started.TrySetResult();

        if (Unreachable)
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");
        }

        var isSave = request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/fenix/failures/saveManual";
        var isList = request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/fenix/failures/manual";
        if (Hang || (HangOnSave && isSave) || (HangOnList && isList))
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (isSave)
        {
            if (SaveStatus != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(SaveStatus);
            }

            var node = JsonNode.Parse(body!)!.AsObject();
            var id = node["id"]!.GetValue<string>();
            var failed = node["failed"]!.GetValue<bool>();
            if (!IgnoreSaves)
            {
                _failed[id] = failed;
            }

            if (SaveBodyOverride is { } overrideBody)
            {
                return Json(overrideBody);
            }

            node["failed"] = StaleEcho ? !failed : failed;
            return Json(node.ToJsonString());
        }

        if (isList)
        {
            if (ListStatus != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(ListStatus);
            }

            return Json(ListBodyOverride ?? ListJson());
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>The list in the EFB's shape, grouped by ATA and group like the real one.</summary>
    public string ListJson(IEnumerable<object>? extraFailures = null)
    {
        var atas = FenixFailureCatalogData.Default.Raw
            .GroupBy(r => (r.Ata, r.AtaTitle))
            .Select(ata => new
            {
                id = ata.Key.Ata,
                title = ata.Key.AtaTitle,
                groups = ata.GroupBy(r => r.Group).Select(g => new
                {
                    groupName = g.Key,
                    failures = g.Select(r => (object)new { id = r.Id, title = r.Title, failureCondition = (object?)null, failed = _failed[r.Id] })
                        .Concat(extraFailures ?? []).ToArray(),
                }).ToArray(),
            });
        return JsonSerializer.Serialize(new { atas });
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
