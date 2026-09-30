using System.Text.Json.Serialization;

namespace LookupTax.Models;

/// <summary>Canonical outcome values. Mutually exclusive.</summary>
public static class ValidationStatus
{
    public const string Valid = "VALID";
    public const string Invalid = "INVALID";
    /// <summary>Format checked only — a pass is NOT proof the business exists.</summary>
    public const string Unverified = "UNVERIFIED";
    /// <summary>A registry was temporarily unreachable. Retry.</summary>
    public const string Indeterminate = "INDETERMINATE";
    public const string Unsupported = "UNSUPPORTED";
}

/// <summary>EU-only routing preference.</summary>
public static class ValidationSource
{
    public const string Auto = "auto";
    public const string Vies = "vies";
    public const string Local = "local";
}

/// <summary>
/// The canonical outcome. <see cref="Status"/> is the single source of truth.
/// </summary>
/// <remarks>
/// Deliberately do not branch on <c>Validation.overall.isValid</c>: it is true for
/// VALID, UNVERIFIED and INDETERMINATE alike, so a format-only pass looks identical
/// to a registry-confirmed one.
/// </remarks>
public class ValidationResult
{
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("retryable")] public bool Retryable { get; set; }

    /// <summary>Registry-confirmed. Deliberately narrow — UNVERIFIED is not "valid".</summary>
    [JsonIgnore] public bool IsValid => Status == ValidationStatus.Valid;

    /// <summary>A temporary registry failure worth retrying.</summary>
    [JsonIgnore] public bool ShouldRetry => Status == ValidationStatus.Indeterminate;
}

public class Entity
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("address")] public string? Address { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

public class TinInfo
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("formattedTin")] public string? FormattedTin { get; set; }
}

public class ValidationResponse
{
    [JsonPropertyName("referenceId")] public string ReferenceId { get; set; } = "";
    [JsonPropertyName("countryCode")] public string CountryCode { get; set; } = "";
    [JsonPropertyName("requestDate")] public string? RequestDate { get; set; }
    [JsonPropertyName("tin")] public string Tin { get; set; } = "";
    [JsonPropertyName("result")] public ValidationResult Result { get; set; } = new();
    [JsonPropertyName("tinInfo")] public TinInfo? TinInfo { get; set; }
    [JsonPropertyName("entity")] public Entity? Entity { get; set; }
    [JsonPropertyName("metadata")] public Dictionary<string, object>? Metadata { get; set; }
}
