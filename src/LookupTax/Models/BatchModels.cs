using System.Text.Json.Serialization;

namespace LookupTax.Models;

/// <summary>One row of a batch submission.</summary>
public class TaxIdInput
{
    [JsonPropertyName("country_iso")] public string CountryIso { get; set; } = "";
    [JsonPropertyName("tin")] public string Tin { get; set; } = "";
    /// <summary>Required where the registry matches on name (e.g. MX).</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    public TaxIdInput() { }

    public TaxIdInput(string countryIso, string tin, string? name = null)
    {
        CountryIso = countryIso;
        Tin = tin;
        Name = name;
    }
}

public class CreateBatchResponse
{
    [JsonPropertyName("batch_id")] public string BatchId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
}

public class BatchItem
{
    [JsonPropertyName("request_id")] public string RequestId { get; set; } = "";
    [JsonPropertyName("country_iso")] public string CountryIso { get; set; } = "";
    [JsonPropertyName("tin")] public string Tin { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("validation_result")] public ValidationResponse? ValidationResult { get; set; }
    [JsonPropertyName("error_code")] public string? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    /// <summary>Set while the item is waiting to be retried.</summary>
    [JsonPropertyName("next_retry_at")] public string? NextRetryAt { get; set; }
}

public class BatchResults
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
    [JsonPropertyName("completed_count")] public int CompletedCount { get; set; }
    [JsonPropertyName("success_count")] public int SuccessCount { get; set; }
    [JsonPropertyName("error_count")] public int ErrorCount { get; set; }
    [JsonPropertyName("canceled_count")] public int CanceledCount { get; set; }
    [JsonPropertyName("results")] public List<BatchItem> Results { get; set; } = new();
    [JsonPropertyName("next_cursor")] public int? NextCursor { get; set; }

    /// <summary>Whether the batch has finished in any way.</summary>
    [JsonIgnore]
    public bool IsTerminal => Status is "completed" or "failed" or "canceled";
}

public class BatchSummary
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("total_count")] public int TotalCount { get; set; }
    [JsonPropertyName("completed_count")] public int CompletedCount { get; set; }
    [JsonPropertyName("success_count")] public int SuccessCount { get; set; }
    [JsonPropertyName("error_count")] public int ErrorCount { get; set; }
    [JsonPropertyName("user_id")] public string? UserId { get; set; }
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
}

public class BatchList
{
    [JsonPropertyName("batches")] public List<BatchSummary> Batches { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
}
