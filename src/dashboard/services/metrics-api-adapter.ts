/**
 * Adapter module that fetches from the new Copilot Usage Metrics API (2026-03-10)
 * and converts the NDJSON report data into the internal CopilotMetrics format
 * used by the dashboard.
 */

import {
  CopilotMetrics,
  CopilotIDEMetrics,
  CopilotIDEChatMetrics,
  CopilotDotcomChatMetrics,
  CopilotDotcomPullRequestsMetrics,
  MetricsReportResponse,
  MetricsReportRecord,
  DayTotal,
  Languages,
  EditorMetrics,
  ModelMetrics,
  LanguageMetrics,
} from "@/features/common/models";
import { ServerActionResponse } from "@/features/common/server-action-response";
import { formatResponseError, unknownResponseError } from "@/features/common/response-error";

/**
 * Fetches the metrics report endpoint, downloads the NDJSON files, and returns
 * the parsed report records.
 */
export async function fetchMetricsReport(
  reportUrl: string,
  token: string,
  version: string,
  entityName: string
): Promise<ServerActionResponse<MetricsReportRecord[]>> {
  // Step 1: Call the reports endpoint to get download links
  const response = await fetch(reportUrl, {
    cache: "no-store",
    headers: {
      Accept: "application/vnd.github+json",
      Authorization: `Bearer ${token}`,
      "X-GitHub-Api-Version": version,
    },
  });

  if (!response.ok) {
    return formatResponseError(entityName, response);
  }

  const reportResponse: MetricsReportResponse = await response.json();

  if (
    !reportResponse.download_links ||
    reportResponse.download_links.length === 0
  ) {
    return { status: "OK", response: [] };
  }

  // Step 2: Download each NDJSON file and parse the records
  const allRecords: MetricsReportRecord[] = [];

  for (const link of reportResponse.download_links) {
    const fileResponse = await fetch(link, { cache: "no-store" });
    if (!fileResponse.ok) {
      continue; // skip individual file failures
    }

    const text = await fileResponse.text();
    const lines = text.split("\n").filter((line) => line.trim().length > 0);

    for (const line of lines) {
      try {
        const record = JSON.parse(line) as MetricsReportRecord;
        allRecords.push(record);
      } catch {
        // skip malformed lines
      }
    }
  }

  return { status: "OK", response: allRecords };
}

/**
 * Converts an array of new-API MetricsReportRecords into the internal
 * CopilotMetrics[] format used by the dashboard charts and helpers.
 */
export function convertReportRecordsToLegacy(
  records: MetricsReportRecord[]
): CopilotMetrics[] {
  const metricsMap = new Map<string, CopilotMetrics>();

  for (const record of records) {
    for (const dayTotal of record.day_totals || []) {
      const key = dayTotal.day;
      // If the same day appears in multiple records, merge by summing
      if (metricsMap.has(key)) {
        continue; // keep first occurrence per day
      }
      metricsMap.set(key, convertDayTotalToLegacy(dayTotal));
    }
  }

  return Array.from(metricsMap.values()).sort(
    (a, b) => new Date(a.date).getTime() - new Date(b.date).getTime()
  );
}

/**
 * Converts a single DayTotal from the new API into a CopilotMetrics object.
 */
function convertDayTotalToLegacy(day: DayTotal): CopilotMetrics {
  return {
    date: day.day,
    total_active_users: day.daily_active_users || 0,
    total_engaged_users: day.daily_active_users || 0,
    copilot_ide_code_completions: buildIdeCodeCompletions(day),
    copilot_ide_chat: buildIdeChatMetrics(day),
    copilot_dotcom_chat: buildDotcomChatMetrics(day),
    copilot_dotcom_pull_requests: buildDotcomPullRequests(day),
  };
}

/**
 * Builds the CopilotIDEMetrics (code completions) from the new flat data.
 *
 * Maps:
 *   totals_by_ide → editors (one editor per IDE)
 *   totals_by_language_feature (feature=code_completion) → languages
 */
function buildIdeCodeCompletions(day: DayTotal): CopilotIDEMetrics {
  // Extract code-completion-specific language data
  const codeCompletionLangs = (day.totals_by_language_feature || []).filter(
    (lf) => lf.feature === "code_completion"
  );

  const languages: Languages[] = codeCompletionLangs.map((lf) => ({
    name: lf.language,
    total_engaged_users: 0,
  }));

  // Build an editor entry for each IDE, with a single "default" model
  // containing the language breakdown scoped to code completions
  const editors: EditorMetrics[] = (day.totals_by_ide || []).map((ide) => {
    const languageMetrics: LanguageMetrics[] = codeCompletionLangs.map(
      (lf) => ({
        name: lf.language,
        total_engaged_users: 0,
        total_code_suggestions: lf.code_generation_activity_count || 0,
        total_code_acceptances: lf.code_acceptance_activity_count || 0,
        total_code_lines_suggested: lf.loc_suggested_to_add_sum || 0,
        total_code_lines_accepted: lf.loc_added_sum || 0,
      })
    );

    const model: ModelMetrics = {
      name: "default",
      is_custom_model: false,
      custom_model_training_date: null,
      total_engaged_users: 0,
      languages: languageMetrics,
    };

    return {
      name: ide.ide,
      total_engaged_users: 0,
      models: [model],
    };
  });

  // If there are no IDE entries but we have language data, create a synthetic editor
  if (editors.length === 0 && codeCompletionLangs.length > 0) {
    const languageMetrics: LanguageMetrics[] = codeCompletionLangs.map(
      (lf) => ({
        name: lf.language,
        total_engaged_users: 0,
        total_code_suggestions: lf.code_generation_activity_count || 0,
        total_code_acceptances: lf.code_acceptance_activity_count || 0,
        total_code_lines_suggested: lf.loc_suggested_to_add_sum || 0,
        total_code_lines_accepted: lf.loc_added_sum || 0,
      })
    );

    editors.push({
      name: "unknown",
      total_engaged_users: 0,
      models: [
        {
          name: "default",
          is_custom_model: false,
          custom_model_training_date: null,
          total_engaged_users: 0,
          languages: languageMetrics,
        },
      ],
    });
  }

  // When there are multiple editors, distribute language metrics proportionally
  // by each IDE's share of loc_added_sum
  if (editors.length > 1) {
    const totalLocAdded = (day.totals_by_ide || []).reduce(
      (sum, ide) => sum + (ide.loc_added_sum || 0),
      0
    );

    editors.forEach((editor, idx) => {
      const ideData = (day.totals_by_ide || [])[idx];
      const share =
        totalLocAdded > 0 ? (ideData?.loc_added_sum || 0) / totalLocAdded : 1 / editors.length;

      editor.models[0].languages = codeCompletionLangs.map((lf) => ({
        name: lf.language,
        total_engaged_users: 0,
        total_code_suggestions: Math.round(
          (lf.code_generation_activity_count || 0) * share
        ),
        total_code_acceptances: Math.round(
          (lf.code_acceptance_activity_count || 0) * share
        ),
        total_code_lines_suggested: Math.round(
          (lf.loc_suggested_to_add_sum || 0) * share
        ),
        total_code_lines_accepted: Math.round(
          (lf.loc_added_sum || 0) * share
        ),
      }));
    });
  }

  // Compute total engaged users from the code_completion feature
  const ccFeature = (day.totals_by_feature || []).find(
    (f) => f.feature === "code_completion"
  );
  const totalEngagedUsers = ccFeature
    ? day.daily_active_users || 0
    : 0;

  return {
    total_engaged_users: totalEngagedUsers,
    languages,
    editors,
  };
}

/**
 * Builds IDE chat metrics from new API data.
 * Chat features in the new API are captured via user_initiated_interaction_count
 * and chat panel modes.
 */
function buildIdeChatMetrics(day: DayTotal): CopilotIDEChatMetrics {
  // Chat features are those that are not code_completion
  const chatFeatures = (day.totals_by_feature || []).filter(
    (f) => f.feature !== "code_completion"
  );

  const totalChatInteractions = chatFeatures.reduce(
    (sum, f) => sum + (f.user_initiated_interaction_count || 0),
    0
  );

  // Build a single synthetic editor entry for all chat activity
  const chatModels: ModelMetrics[] = [];
  if (totalChatInteractions > 0 || (day.totals_by_model_feature || []).length > 0) {
    // Use model-feature breakdown if available
    const modelFeatures = (day.totals_by_model_feature || []).filter(
      (mf) => mf.feature !== "code_completion"
    );

    if (modelFeatures.length > 0) {
      for (const mf of modelFeatures) {
        chatModels.push({
          name: mf.model || "default",
          is_custom_model: false,
          custom_model_training_date: null,
          total_engaged_users: 0,
          total_chats: mf.user_initiated_interaction_count || 0,
          total_chat_insertion_events: mf.code_acceptance_activity_count || 0,
          total_chat_copy_events: 0,
        });
      }
    } else {
      chatModels.push({
        name: "default",
        is_custom_model: false,
        custom_model_training_date: null,
        total_engaged_users: day.monthly_active_chat_users || 0,
        total_chats: totalChatInteractions,
        total_chat_insertion_events: chatFeatures.reduce(
          (sum, f) => sum + (f.code_acceptance_activity_count || 0),
          0
        ),
        total_chat_copy_events: 0,
      });
    }
  }

  const editors: EditorMetrics[] = [];
  if (chatModels.length > 0) {
    // Group by IDE if we have IDE-level chat data
    const ideEntries = (day.totals_by_ide || []).filter(
      (ide) => (ide.user_initiated_interaction_count || 0) > 0
    );

    if (ideEntries.length > 0) {
      for (const ide of ideEntries) {
        editors.push({
          name: ide.ide,
          total_engaged_users: 0,
          models: chatModels,
        });
      }
    } else {
      editors.push({
        name: "unknown",
        total_engaged_users: 0,
        models: chatModels,
      });
    }
  }

  return {
    total_engaged_users: day.monthly_active_chat_users || 0,
    editors,
  };
}

/**
 * Builds dotcom chat metrics.
 * The new API doesn't distinguish IDE chat from dotcom chat at the aggregate level,
 * so we provide an empty structure.
 */
function buildDotcomChatMetrics(day: DayTotal): CopilotDotcomChatMetrics {
  return {
    total_engaged_users: 0,
    models: [],
  };
}

/**
 * Builds dotcom pull request metrics from the new pull_requests object.
 */
function buildDotcomPullRequests(
  day: DayTotal
): CopilotDotcomPullRequestsMetrics {
  if (!day.pull_requests) {
    return { total_engaged_users: 0, repositories: [] };
  }

  const pr = day.pull_requests;
  return {
    total_engaged_users: 0,
    repositories: [
      {
        name: "all",
        total_engaged_users: 0,
        models: [
          {
            name: "default",
            is_custom_model: false,
            custom_model_training_date: null,
            total_engaged_users: 0,
            total_pr_summaries_created: pr.total_created_by_copilot || 0,
          },
        ],
      },
    ],
  };
}
