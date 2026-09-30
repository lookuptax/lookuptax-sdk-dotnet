namespace LookupTax;

/// <summary>
/// A request error: the call could not be processed at all.
/// </summary>
/// <remarks>
/// A validation outcome (an INVALID tax ID, say) is <b>not</b> an exception — it
/// arrives in the response with <c>Result.Status</c> set. Treating an invalid tax ID
/// as an error is the most common integration mistake.
/// </remarks>
public class LookupTaxException : Exception
{
    /// <summary>Stable machine-readable code, e.g. <c>quota_exceeded</c>. Branch on this, never on the message.</summary>
    public string Code { get; }

    public int Status { get; }

    /// <summary>Seconds to wait, from the <c>Retry-After</c> header. Only set for 429.</summary>
    public double? RetryAfter { get; }

    public LookupTaxException(string message, string code, int status, double? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Status = status;
        RetryAfter = retryAfter;
    }

    /// <summary>Whether retrying the identical request could plausibly succeed.</summary>
    public bool IsRetryable => Status is 429 or 500 or 503;

    public bool IsAuthError => Status is 401 or 403;
    public bool IsQuotaExceeded => Status == 402;
    public bool IsRateLimited => Status == 429;
    public bool IsNotFound => Status == 404;
}
