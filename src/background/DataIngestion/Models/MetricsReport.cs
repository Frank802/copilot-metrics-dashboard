using System.Text.Json.Serialization;

namespace Microsoft.CopilotDashboard.DataIngestion.Models;

/// <summary>
/// Response from the new Copilot metrics reports endpoints.
/// Contains download links to NDJSON report files.
/// </summary>
public class MetricsReportResponse
{
    [JsonPropertyName("download_links")]
    public string[] DownloadLinks { get; set; } = Array.Empty<string>();

    [JsonPropertyName("report_day")]
    public string? ReportDay { get; set; }

    [JsonPropertyName("report_start_day")]
    public string? ReportStartDay { get; set; }

    [JsonPropertyName("report_end_day")]
    public string? ReportEndDay { get; set; }
}

/// <summary>
/// A single record inside a downloaded NDJSON report file (enterprise/org level).
/// </summary>
public class MetricsReportRecord
{
    [JsonPropertyName("day_totals")]
    public DayTotal[] DayTotals { get; set; } = Array.Empty<DayTotal>();

    [JsonPropertyName("enterprise_id")]
    public string? EnterpriseId { get; set; }

    [JsonPropertyName("organization_id")]
    public string? OrganizationId { get; set; }

    [JsonPropertyName("report_start_day")]
    public string? ReportStartDay { get; set; }

    [JsonPropertyName("report_end_day")]
    public string? ReportEndDay { get; set; }
}

/// <summary>
/// Daily aggregate metrics within a report record.
/// </summary>
public class DayTotal
{
    [JsonPropertyName("day")]
    public string Day { get; set; } = string.Empty;

    [JsonPropertyName("enterprise_id")]
    public string? EnterpriseId { get; set; }

    [JsonPropertyName("organization_id")]
    public string? OrganizationId { get; set; }

    [JsonPropertyName("daily_active_users")]
    public int DailyActiveUsers { get; set; }

    [JsonPropertyName("weekly_active_users")]
    public int WeeklyActiveUsers { get; set; }

    [JsonPropertyName("monthly_active_users")]
    public int MonthlyActiveUsers { get; set; }

    [JsonPropertyName("monthly_active_chat_users")]
    public int? MonthlyActiveChatUsers { get; set; }

    [JsonPropertyName("monthly_active_agent_users")]
    public int? MonthlyActiveAgentUsers { get; set; }

    [JsonPropertyName("code_generation_activity_count")]
    public int CodeGenerationActivityCount { get; set; }

    [JsonPropertyName("code_acceptance_activity_count")]
    public int CodeAcceptanceActivityCount { get; set; }

    [JsonPropertyName("loc_suggested_to_add_sum")]
    public int LocSuggestedToAddSum { get; set; }

    [JsonPropertyName("loc_suggested_to_delete_sum")]
    public int LocSuggestedToDeleteSum { get; set; }

    [JsonPropertyName("loc_added_sum")]
    public int LocAddedSum { get; set; }

    [JsonPropertyName("loc_deleted_sum")]
    public int LocDeletedSum { get; set; }

    [JsonPropertyName("user_initiated_interaction_count")]
    public int UserInitiatedInteractionCount { get; set; }

    [JsonPropertyName("totals_by_ide")]
    public TotalsByIde[] TotalsByIde { get; set; } = Array.Empty<TotalsByIde>();

    [JsonPropertyName("totals_by_feature")]
    public TotalsByFeature[] TotalsByFeature { get; set; } = Array.Empty<TotalsByFeature>();

    [JsonPropertyName("totals_by_language_feature")]
    public TotalsByLanguageFeature[] TotalsByLanguageFeature { get; set; } = Array.Empty<TotalsByLanguageFeature>();

    [JsonPropertyName("totals_by_model_feature")]
    public TotalsByModelFeature[] TotalsByModelFeature { get; set; } = Array.Empty<TotalsByModelFeature>();

    [JsonPropertyName("totals_by_language_model")]
    public TotalsByLanguageModel[] TotalsByLanguageModel { get; set; } = Array.Empty<TotalsByLanguageModel>();

    [JsonPropertyName("pull_requests")]
    public ReportPullRequestMetrics? PullRequests { get; set; }

    [JsonPropertyName("daily_active_cli_users")]
    public int? DailyActiveCliUsers { get; set; }
}

public class TotalsByIde
{
    [JsonPropertyName("ide")]
    public string Ide { get; set; } = string.Empty;

    [JsonPropertyName("code_generation_activity_count")]
    public int CodeGenerationActivityCount { get; set; }

    [JsonPropertyName("code_acceptance_activity_count")]
    public int CodeAcceptanceActivityCount { get; set; }

    [JsonPropertyName("loc_suggested_to_add_sum")]
    public int LocSuggestedToAddSum { get; set; }

    [JsonPropertyName("loc_suggested_to_delete_sum")]
    public int LocSuggestedToDeleteSum { get; set; }

    [JsonPropertyName("loc_added_sum")]
    public int LocAddedSum { get; set; }

    [JsonPropertyName("loc_deleted_sum")]
    public int LocDeletedSum { get; set; }

    [JsonPropertyName("user_initiated_interaction_count")]
    public int UserInitiatedInteractionCount { get; set; }
}

public class TotalsByFeature
{
    [JsonPropertyName("feature")]
    public string Feature { get; set; } = string.Empty;

    [JsonPropertyName("code_generation_activity_count")]
    public int CodeGenerationActivityCount { get; set; }

    [JsonPropertyName("code_acceptance_activity_count")]
    public int CodeAcceptanceActivityCount { get; set; }

    [JsonPropertyName("loc_suggested_to_add_sum")]
    public int LocSuggestedToAddSum { get; set; }

    [JsonPropertyName("loc_suggested_to_delete_sum")]
    public int LocSuggestedToDeleteSum { get; set; }

    [JsonPropertyName("loc_added_sum")]
    public int LocAddedSum { get; set; }

    [JsonPropertyName("loc_deleted_sum")]
    public int LocDeletedSum { get; set; }

    [JsonPropertyName("user_initiated_interaction_count")]
    public int UserInitiatedInteractionCount { get; set; }
}

public class TotalsByLanguageFeature
{
    [JsonPropertyName("feature")]
    public string Feature { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("code_generation_activity_count")]
    public int CodeGenerationActivityCount { get; set; }

    [JsonPropertyName("code_acceptance_activity_count")]
    public int CodeAcceptanceActivityCount { get; set; }

    [JsonPropertyName("loc_suggested_to_add_sum")]
    public int LocSuggestedToAddSum { get; set; }

    [JsonPropertyName("loc_suggested_to_delete_sum")]
    public int LocSuggestedToDeleteSum { get; set; }

    [JsonPropertyName("loc_added_sum")]
    public int LocAddedSum { get; set; }

    [JsonPropertyName("loc_deleted_sum")]
    public int LocDeletedSum { get; set; }
}

public class TotalsByModelFeature
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("feature")]
    public string? Feature { get; set; }

    [JsonPropertyName("code_generation_activity_count")]
    public int CodeGenerationActivityCount { get; set; }

    [JsonPropertyName("code_acceptance_activity_count")]
    public int CodeAcceptanceActivityCount { get; set; }

    [JsonPropertyName("user_initiated_interaction_count")]
    public int UserInitiatedInteractionCount { get; set; }
}

public class TotalsByLanguageModel
{
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("code_generation_activity_count")]
    public int CodeGenerationActivityCount { get; set; }

    [JsonPropertyName("code_acceptance_activity_count")]
    public int CodeAcceptanceActivityCount { get; set; }
}

public class ReportPullRequestMetrics
{
    [JsonPropertyName("total_created")]
    public int TotalCreated { get; set; }

    [JsonPropertyName("total_reviewed")]
    public int TotalReviewed { get; set; }

    [JsonPropertyName("total_merged")]
    public int TotalMerged { get; set; }

    [JsonPropertyName("median_minutes_to_merge")]
    public double? MedianMinutesToMerge { get; set; }

    [JsonPropertyName("total_suggestions")]
    public int TotalSuggestions { get; set; }

    [JsonPropertyName("total_applied_suggestions")]
    public int TotalAppliedSuggestions { get; set; }

    [JsonPropertyName("total_created_by_copilot")]
    public int TotalCreatedByCopilot { get; set; }

    [JsonPropertyName("total_reviewed_by_copilot")]
    public int TotalReviewedByCopilot { get; set; }

    [JsonPropertyName("total_merged_created_by_copilot")]
    public int TotalMergedCreatedByCopilot { get; set; }

    [JsonPropertyName("total_copilot_suggestions")]
    public int TotalCopilotSuggestions { get; set; }

    [JsonPropertyName("total_copilot_applied_suggestions")]
    public int TotalCopilotAppliedSuggestions { get; set; }
}
