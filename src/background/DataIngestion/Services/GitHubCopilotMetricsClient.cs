using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.CopilotDashboard.DataIngestion.Functions;
using Microsoft.CopilotDashboard.DataIngestion.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.CopilotDashboard.DataIngestion.Services
{
    internal enum MetricsType
    {
        Ent,
        Org
    }

    public class GitHubCopilotMetricsClient
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public GitHubCopilotMetricsClient(HttpClient httpClient, ILogger<GitHubCopilotMetricsClient> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Fetches Copilot metrics for an enterprise using the new reports API.
        /// Uses enterprise-1-day endpoint for a specific day (today) or
        /// enterprise-28-day/latest for the default 28-day window.
        /// </summary>
        public Task<Metrics[]> GetCopilotMetricsForEnterpriseAsync(string? team)
        {
            var enterprise = Environment.GetEnvironmentVariable("GITHUB_ENTERPRISE")!;

            // The new API does not support team-specific metrics endpoints.
            // Team filtering should be handled at the data layer (Cosmos DB).
            if (!string.IsNullOrWhiteSpace(team))
            {
                _logger.LogWarning("Team-specific metrics are not supported by the new API. Team filter '{Team}' will be ignored for API calls.", team);
            }

            var requestUri = $"/enterprises/{enterprise}/copilot/metrics/reports/enterprise-28-day/latest";
            return GetMetricsFromReport(requestUri, MetricsType.Ent, enterprise, team);
        }

        /// <summary>
        /// Fetches Copilot metrics for an organization using the new reports API.
        /// </summary>
        public Task<Metrics[]> GetCopilotMetricsForOrganizationAsync(string? team)
        {
            var organization = Environment.GetEnvironmentVariable("GITHUB_ORGANIZATION")!;

            if (!string.IsNullOrWhiteSpace(team))
            {
                _logger.LogWarning("Team-specific metrics are not supported by the new API. Team filter '{Team}' will be ignored for API calls.", team);
            }

            var requestUri = $"/orgs/{organization}/copilot/metrics/reports/organization-28-day/latest";
            return GetMetricsFromReport(requestUri, MetricsType.Org, organization, team);
        }

        /// <summary>
        /// Fetches metrics using the new two-step API:
        /// 1. Call the reports endpoint to get download links
        /// 2. Download and parse the NDJSON files
        /// 3. Convert the new format to the existing Metrics model
        /// </summary>
        private async Task<Metrics[]> GetMetricsFromReport(string requestUri, MetricsType type, string orgOrEnterpriseName, string? team = null)
        {
            try
            {
                // Step 1: Get the download links
                var response = await _httpClient.GetAsync(requestUri);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("Report not found at {RequestUri}. Returning empty data.", requestUri);
                        return Array.Empty<Metrics>();
                    }
                    throw new HttpRequestException($"Error fetching report links: {response.StatusCode}");
                }

                _logger.LogInformation("Fetched report links from {RequestUri}", requestUri);
                var reportResponse = await response.Content.ReadFromJsonAsync<MetricsReportResponse>();

                if (reportResponse?.DownloadLinks == null || reportResponse.DownloadLinks.Length == 0)
                {
                    _logger.LogWarning("No download links returned from {RequestUri}", requestUri);
                    return Array.Empty<Metrics>();
                }

                // Step 2: Download and parse each NDJSON file
                var allRecords = new List<MetricsReportRecord>();
                foreach (var link in reportResponse.DownloadLinks)
                {
                    try
                    {
                        var fileResponse = await _httpClient.GetAsync(link);
                        if (!fileResponse.IsSuccessStatusCode)
                        {
                            _logger.LogWarning("Failed to download report file from {Link}: {StatusCode}", link, fileResponse.StatusCode);
                            continue;
                        }

                        var content = await fileResponse.Content.ReadAsStringAsync();
                        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                        foreach (var line in lines)
                        {
                            try
                            {
                                var record = JsonSerializer.Deserialize<MetricsReportRecord>(line);
                                if (record != null)
                                {
                                    allRecords.Add(record);
                                }
                            }
                            catch (JsonException ex)
                            {
                                _logger.LogWarning(ex, "Failed to parse NDJSON line");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to download report file from {Link}", link);
                    }
                }

                // Step 3: Convert to legacy Metrics format
                var metrics = ConvertReportRecordsToMetrics(allRecords, type, orgOrEnterpriseName, team);
                return metrics;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Error fetching data from {RequestUri}", requestUri);
                return Array.Empty<Metrics>();
            }
        }

        /// <summary>
        /// Converts new API report records into the existing Metrics model format
        /// for storage in Cosmos DB.
        /// </summary>
        private Metrics[] ConvertReportRecordsToMetrics(List<MetricsReportRecord> records, MetricsType type, string orgOrEnterpriseName, string? team)
        {
            var metricsDict = new Dictionary<string, Metrics>();

            foreach (var record in records)
            {
                foreach (var dayTotal in record.DayTotals ?? Array.Empty<DayTotal>())
                {
                    if (metricsDict.ContainsKey(dayTotal.Day))
                        continue;

                    var metric = ConvertDayTotalToMetrics(dayTotal);
                    metric.Team = team;

                    if (type == MetricsType.Ent)
                    {
                        metric.Enterprise = orgOrEnterpriseName;
                    }
                    else
                    {
                        metric.Organization = orgOrEnterpriseName;
                    }

                    metricsDict[dayTotal.Day] = metric;
                }
            }

            return metricsDict.Values.OrderBy(m => m.Date).ToArray();
        }

        /// <summary>
        /// Converts a single DayTotal from the new API into a Metrics object.
        /// </summary>
        private static Metrics ConvertDayTotalToMetrics(DayTotal day)
        {
            var codeCompletionLangs = (day.TotalsByLanguageFeature ?? Array.Empty<TotalsByLanguageFeature>())
                .Where(lf => lf.Feature == "code_completion")
                .ToArray();

            // Build IDE code completions
            var editors = (day.TotalsByIde ?? Array.Empty<TotalsByIde>()).Select(ide =>
            {
                var languages = codeCompletionLangs.Select(lf => new IdeCodeCompletionModelLanguage
                {
                    Name = lf.Language,
                    TotalEngagedUsers = 0,
                    TotalCodeSuggestions = lf.CodeGenerationActivityCount,
                    TotalCodeAcceptances = lf.CodeAcceptanceActivityCount,
                    TotalCodeLinesSuggested = lf.LocSuggestedToAddSum,
                    TotalCodeLinesAccepted = lf.LocAddedSum,
                }).ToArray();

                return new IdeCodeCompletionEditor
                {
                    Name = ide.Ide,
                    TotalEngagedUsers = 0,
                    Models = new[]
                    {
                        new IdeCodeCompletionModel
                        {
                            Name = "default",
                            IsCustomModel = false,
                            TotalEngagedUsers = 0,
                            Languages = languages,
                        }
                    }
                };
            }).ToArray();

            // If no IDE entries but we have language data, create a synthetic editor
            if (editors.Length == 0 && codeCompletionLangs.Length > 0)
            {
                editors = new[]
                {
                    new IdeCodeCompletionEditor
                    {
                        Name = "unknown",
                        TotalEngagedUsers = 0,
                        Models = new[]
                        {
                            new IdeCodeCompletionModel
                            {
                                Name = "default",
                                IsCustomModel = false,
                                TotalEngagedUsers = 0,
                                Languages = codeCompletionLangs.Select(lf => new IdeCodeCompletionModelLanguage
                                {
                                    Name = lf.Language,
                                    TotalEngagedUsers = 0,
                                    TotalCodeSuggestions = lf.CodeGenerationActivityCount,
                                    TotalCodeAcceptances = lf.CodeAcceptanceActivityCount,
                                    TotalCodeLinesSuggested = lf.LocSuggestedToAddSum,
                                    TotalCodeLinesAccepted = lf.LocAddedSum,
                                }).ToArray()
                            }
                        }
                    }
                };
            }

            // Distribute language metrics proportionally across editors
            if (editors.Length > 1)
            {
                var totalLocAdded = (day.TotalsByIde ?? Array.Empty<TotalsByIde>()).Sum(ide => ide.LocAddedSum);
                for (int i = 0; i < editors.Length; i++)
                {
                    var ideData = (day.TotalsByIde ?? Array.Empty<TotalsByIde>())[i];
                    var share = totalLocAdded > 0
                        ? (double)ideData.LocAddedSum / totalLocAdded
                        : 1.0 / editors.Length;

                    editors[i].Models[0].Languages = codeCompletionLangs.Select(lf => new IdeCodeCompletionModelLanguage
                    {
                        Name = lf.Language,
                        TotalEngagedUsers = 0,
                        TotalCodeSuggestions = (int)Math.Round(lf.CodeGenerationActivityCount * share),
                        TotalCodeAcceptances = (int)Math.Round(lf.CodeAcceptanceActivityCount * share),
                        TotalCodeLinesSuggested = (int)Math.Round(lf.LocSuggestedToAddSum * share),
                        TotalCodeLinesAccepted = (int)Math.Round(lf.LocAddedSum * share),
                    }).ToArray();
                }
            }

            var ideCodeCompletions = new IdeCodeCompletions
            {
                TotalEngagedUsers = day.DailyActiveUsers,
                Languages = codeCompletionLangs.Select(lf => new IdeCodeCompletionLanguage
                {
                    Name = lf.Language,
                    TotalEngagedUsers = 0,
                }).ToArray(),
                Editors = editors,
            };

            // Build IDE chat metrics
            var chatFeatures = (day.TotalsByFeature ?? Array.Empty<TotalsByFeature>())
                .Where(f => f.Feature != "code_completion")
                .ToArray();

            var totalChatInteractions = chatFeatures.Sum(f => f.UserInitiatedInteractionCount);

            var chatEditors = Array.Empty<IdeChatEditor>();
            if (totalChatInteractions > 0)
            {
                var chatModel = new IdeChatModel
                {
                    Name = "default",
                    IsCustomModel = false,
                    TotalEngagedUsers = day.MonthlyActiveChatUsers ?? 0,
                    TotalChats = totalChatInteractions,
                    TotalChatInsertionEvents = chatFeatures.Sum(f => f.CodeAcceptanceActivityCount),
                    TotalChatCopyEvents = 0,
                };

                chatEditors = new[]
                {
                    new IdeChatEditor
                    {
                        Name = "unknown",
                        TotalEngagedUsers = 0,
                        Models = new[] { chatModel },
                    }
                };
            }

            var ideChat = new IdeChat
            {
                TotalEngagedUsers = day.MonthlyActiveChatUsers ?? 0,
                Editors = chatEditors,
            };

            // Build dotcom chat (not distinguished from IDE chat in new API)
            var dotComChat = new DotComChat
            {
                TotalEngagedUsers = 0,
                Models = Array.Empty<DotComChatModel>(),
            };

            // Build PR metrics
            var dotComPullRequests = new DotComPullRequest
            {
                TotalEngagedUsers = 0,
                Repositories = day.PullRequests != null
                    ? new[]
                    {
                        new DotComPullRequestRepository
                        {
                            Name = "all",
                            TotalEngagedUsers = 0,
                            Models = new[]
                            {
                                new DotComPullRequestRepositoryModel
                                {
                                    Name = "default",
                                    IsCustomModel = false,
                                    TotalEngagedUsers = 0,
                                    TotalPrSummariesCreated = day.PullRequests.TotalCreatedByCopilot,
                                }
                            }
                        }
                    }
                    : Array.Empty<DotComPullRequestRepository>(),
            };

            return new Metrics
            {
                Date = DateOnly.Parse(day.Day),
                TotalActiveUsers = day.DailyActiveUsers,
                TotalEngagedUsers = day.DailyActiveUsers,
                CoPilotIdeCodeCompletions = ideCodeCompletions,
                IdeChat = ideChat,
                DotComChat = dotComChat,
                DotComPullRequests = dotComPullRequests,
            };
        }

        public async ValueTask<Metrics[]> GetTestCopilotMetrics(string? team)
        {
            await using var reader = typeof(CopilotMetricsIngestion)
                    .Assembly
                    .GetManifestResourceStream(
                        "Microsoft.CopilotDashboard.DataIngestion.TestData.metrics.json")!;

            return AddInfo((await JsonSerializer.DeserializeAsync<Metrics[]>(reader))!, MetricsType.Org, "test", team);
        }

        private Metrics[] AddInfo(Metrics[] metrics, MetricsType type, string orgOrEnterpriseName, string? team = null)
        {
            foreach (var metric in metrics)
            {
                metric.Team = team;
                if(type == MetricsType.Ent)
                {
                    metric.Enterprise = orgOrEnterpriseName;
                }
                else
                {
                    metric.Organization = orgOrEnterpriseName;
                }
            }

            return metrics;
        }
    }
}
