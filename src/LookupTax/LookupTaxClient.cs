using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using LookupTax.Models;

namespace LookupTax;

/// <summary>
/// Client for the LookupTax API.
/// </summary>
/// <remarks>
/// <para>The API separates two kinds of failure, and so does this client:</para>
/// <list type="bullet">
///   <item>A <b>request error</b> (HTTP 4xx/5xx) means the call could not be processed —
///   authentication, quota, rate limit, a malformed request. These throw
///   <see cref="LookupTaxException"/>.</item>
///   <item>A <b>validation outcome</b> (HTTP 200) means the call succeeded but the tax ID
///   itself is invalid or could not be confirmed. These are returned, and you read
///   <c>Result.Status</c>.</item>
/// </list>
/// <para>An INVALID tax ID is a successful call, not an error.</para>
/// </remarks>
public sealed class LookupTaxClient : IDisposable
{
    /// <summary>
    /// Default API root. Overridable via <see cref="LookupTaxOptions.BaseUrl"/>: the
    /// published path prefix is deployment configuration, not a property of the SDK.
    /// </summary>
    public const string DefaultBaseUrl = "https://api.lookuptax.com/v1";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly int _maxRetries;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public LookupTaxClient(string apiKey, LookupTaxOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("LookupTax: apiKey is required", nameof(apiKey));

        options ??= new LookupTaxOptions();
        _apiKey = apiKey;
        _baseUrl = (options.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _maxRetries = options.MaxRetries;
        _ownsHttp = options.HttpClient is null;
        _http = options.HttpClient ?? new HttpClient();
        if (_ownsHttp) _http.Timeout = options.Timeout;
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // ─── single validation ───────────────────────────────────────────

    /// <summary>
    /// Validate one tax ID.
    /// </summary>
    /// <remarks>
    /// Returns for every validation outcome, <b>including INVALID</b> — read
    /// <c>Result.Status</c>. Throws only when the request itself could not be processed.
    /// </remarks>
    public async Task<ValidationResponse> ValidateAsync(
        string countryIso,
        string tin,
        ValidateOptions? options = null,
        CancellationToken ct = default)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        q["country_iso"] = countryIso;
        q["tin"] = tin;
        if (options?.ReferenceId is { } r) q["reference_id"] = r;
        if (options?.ValidationSource is { } vs) q["validation_source"] = vs;
        if (options?.AdditionalParams is { } ap)
            // Nested params render as additional_params[name]=…, the shape the API
            // expects; a flat ?name= is stripped before it reaches routing.
            foreach (var kv in ap) q[$"additional_params[{kv.Key}]"] = kv.Value;

        return (await SendAsync<ValidationResponse>(HttpMethod.Get, "/validate", q.ToString(), null, true, ct))!;
    }

    // ─── batch ───────────────────────────────────────────────────────

    /// <summary>
    /// Submit up to 100 tax IDs. Enterprise plan only.
    /// </summary>
    /// <remarks>
    /// Never retried automatically: a retried submission that actually succeeded would
    /// create a second batch and reserve quota twice.
    /// </remarks>
    public async Task<CreateBatchResponse> CreateBatchAsync(
        IEnumerable<TaxIdInput> taxIds,
        CreateBatchOptions? options = null,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["tax_ids"] = taxIds,
            ["user_id"] = options?.UserId,
            ["metadata"] = options?.Metadata,
            ["validation_source"] = options?.ValidationSource,
        };
        return (await SendAsync<CreateBatchResponse>(HttpMethod.Post, "/batch", null, body, false, ct))!;
    }

    /// <summary>Fetch batch progress and one page of results.</summary>
    public async Task<BatchResults> GetBatchAsync(
        string batchId, int? limit = null, int? cursor = null, CancellationToken ct = default)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        if (limit is { } l) q["limit"] = l.ToString();
        if (cursor is { } c) q["cursor"] = c.ToString();
        var path = $"/batch/{Uri.EscapeDataString(batchId)}";
        return (await SendAsync<BatchResults>(HttpMethod.Get, path, q.ToString(), null, true, ct))!;
    }

    /// <summary>List your organization's batches, newest first.</summary>
    public async Task<BatchList> ListBatchesAsync(int? page = null, int? limit = null, CancellationToken ct = default)
    {
        var q = HttpUtility.ParseQueryString(string.Empty);
        if (page is { } p) q["page"] = p.ToString();
        if (limit is { } l) q["limit"] = l.ToString();
        return (await SendAsync<BatchList>(HttpMethod.Get, "/batch", q.ToString(), null, true, ct))!;
    }

    /// <summary>Cancel a pending/processing batch. Reserved quota is released.</summary>
    public async Task<CreateBatchResponse> CancelBatchAsync(string batchId, CancellationToken ct = default)
    {
        var path = $"/batch/{Uri.EscapeDataString(batchId)}/cancel";
        return (await SendAsync<CreateBatchResponse>(HttpMethod.Post, path, null, null, false, ct))!;
    }

    /// <summary>Permanently delete a terminal batch. Cannot be undone.</summary>
    public Task DeleteBatchAsync(string batchId, CancellationToken ct = default)
    {
        var path = $"/batch/{Uri.EscapeDataString(batchId)}";
        return SendAsync<object>(HttpMethod.Delete, path, null, null, false, ct);
    }

    /// <summary>Fetch one item from a batch by its request_id, without paging.</summary>
    public async Task<BatchItem> GetTaxIdAsync(string requestId, CancellationToken ct = default)
    {
        var path = $"/batch/tax-id/{Uri.EscapeDataString(requestId)}";
        return (await SendAsync<BatchItem>(HttpMethod.Get, path, null, null, true, ct))!;
    }

    /// <summary>
    /// Poll until the batch reaches a terminal state.
    /// </summary>
    /// <remarks>
    /// Polls on a relaxed cadence by design: when a registry is unreachable an item keeps
    /// retrying for roughly three days, so a long-running item is not a stuck one.
    /// </remarks>
    public async Task<BatchResults> WaitForBatchAsync(
        string batchId, TimeSpan? interval = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var step = interval ?? TimeSpan.FromSeconds(5);
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(30));
        while (true)
        {
            var batch = await GetBatchAsync(batchId, ct: ct);
            if (batch.IsTerminal) return batch;
            if (DateTime.UtcNow >= deadline)
                throw new LookupTaxException($"batch {batchId} still {batch.Status} after timeout", "timeout", 0);
            await Task.Delay(step, ct);
        }
    }

    /// <summary>Stream every result across all pages, following next_cursor.</summary>
    public async IAsyncEnumerable<BatchItem> IterateBatchResultsAsync(
        string batchId,
        int? limit = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        int? cursor = null;
        while (true)
        {
            var page = await GetBatchAsync(batchId, limit, cursor, ct);
            foreach (var item in page.Results) yield return item;
            if (page.NextCursor is null) yield break;
            cursor = page.NextCursor;
        }
    }

    // ─── transport ───────────────────────────────────────────────────

    private async Task<T?> SendAsync<T>(
        HttpMethod method, string path, string? query, object? body, bool retryable, CancellationToken ct)
    {
        var url = _baseUrl + path + (string.IsNullOrEmpty(query) ? "" : "?" + query);
        var attempts = retryable ? _maxRetries + 1 : 1;
        Exception? last = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Add("X-API-Key", _apiKey);
            req.Headers.Add("Accept", "application/json");
            if (body is not null)
                req.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                resp = await _http.SendAsync(req, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                last = ex;
                if (attempt < attempts - 1) { await Backoff(attempt, null, ct); continue; }
                throw new LookupTaxException(ex.Message, "network_error", 0, null, ex);
            }

            using (resp)
            {
                if (resp.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object))
                    return default;

                var payload = await resp.Content.ReadAsStringAsync(ct);

                // 422 is an OUTCOME, not a failure: the body still carries `result`
                // with status UNSUPPORTED. Parse it like any success.
                var unsupportedOutcome = (int)resp.StatusCode == 422 && payload.Contains("\"result\"");
                if (resp.IsSuccessStatusCode || unsupportedOutcome)
                    return JsonSerializer.Deserialize<T>(payload, Json);

                double? retryAfter = resp.Headers.RetryAfter?.Delta?.TotalSeconds;
                var err = BuildError((int)resp.StatusCode, payload, retryAfter);

                if (err.IsRetryable && attempt < attempts - 1)
                {
                    last = err;
                    await Backoff(attempt, err.RetryAfter, ct);
                    continue;
                }
                throw err;
            }
        }
        throw last ?? new LookupTaxException("request failed", "error", 0);
    }

    private static Task Backoff(int attempt, double? retryAfter, CancellationToken ct) =>
        Task.Delay(retryAfter is { } s ? TimeSpan.FromSeconds(s) : TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 500), ct);

    private static LookupTaxException BuildError(int status, string payload, double? retryAfter)
    {
        string code = $"http_{status}", message = $"Request failed with HTTP {status}";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String) code = e.GetString()!;
            else if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString()!;
            if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String) message = m.GetString()!;
        }
        catch (JsonException)
        {
            // Non-JSON body — keep the defaults rather than masking the status.
        }
        return new LookupTaxException(message, code, status, retryAfter);
    }
}

/// <summary>Client configuration.</summary>
public sealed class LookupTaxOptions
{
    /// <summary>API root. Defaults to <see cref="LookupTaxClient.DefaultBaseUrl"/>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-request timeout. Ignored when you supply your own HttpClient.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Retries for transient failures (429/500/503), honouring Retry-After. Zero
    /// disables. Batch submission is never retried regardless.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Inject your own HttpClient — for proxies, custom handlers, or tests.</summary>
    public HttpClient? HttpClient { get; set; }
}

/// <summary>Optional parameters for <see cref="LookupTaxClient.ValidateAsync"/>.</summary>
public sealed class ValidateOptions
{
    /// <summary>Idempotency key, echoed back as ReferenceId.</summary>
    public string? ReferenceId { get; set; }

    /// <summary>
    /// EU only: "auto" (default), "vies" or "local". With vies/local there is no
    /// fallback to the other family.
    /// </summary>
    public string? ValidationSource { get; set; }

    /// <summary>Extra registry params, e.g. name for Mexico.</summary>
    public Dictionary<string, string>? AdditionalParams { get; set; }
}

/// <summary>Optional parameters for <see cref="LookupTaxClient.CreateBatchAsync"/>.</summary>
public sealed class CreateBatchOptions
{
    public string? UserId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>Applies to every tax ID in the batch; non-EU rows are unaffected.</summary>
    public string? ValidationSource { get; set; }
}
