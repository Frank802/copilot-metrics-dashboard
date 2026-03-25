import { formatResponseError, unknownResponseError } from "@/features/common/response-error";
import { CopilotMetrics, CopilotUsageOutput } from "@/features/common/models";
import { ServerActionResponse } from "@/features/common/server-action-response";
import { SqlQuerySpec } from "@azure/cosmos";
import { format } from "date-fns";
import { cosmosClient, cosmosConfiguration } from "./cosmos-db-service";
import { ensureGitHubEnvConfig } from "./env-service";
import { stringIsNullOrEmpty, applyTimeFrameLabel } from "../utils/helpers";
import { sampleData } from "./sample-data";
import { fetchMetricsReport, convertReportRecordsToLegacy } from "./metrics-api-adapter";

export interface IFilter {
  startDate?: Date;
  endDate?: Date;
  enterprise: string;
  organization: string;
  team: string[];
}

export const getCopilotMetrics = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const env = ensureGitHubEnvConfig();
  const isCosmosConfig = cosmosConfiguration();

  if (env.status !== "OK") {
    return env;
  }

  const { enterprise, organization } = env.response;

  try {
    switch (process.env.GITHUB_API_SCOPE) {
      case "enterprise":
        if (stringIsNullOrEmpty(filter.enterprise)) {
          filter.enterprise = enterprise;
        }
        break;
      default:
        if (stringIsNullOrEmpty(filter.organization)) {
          filter.organization = organization;
        }
        break;
    }

    if (isCosmosConfig) {
      return getCopilotMetricsFromDatabase(filter);
    }

    return getCopilotMetricsFromApi(filter);
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Constructs the GitHub API base URL from environment or default.
 */
function getApiBaseUrl(): string {
  return process.env.GITHUB_API_BASEURL || "https://api.github.com";
}

/**
 * Fetches metrics from the new Copilot Usage Metrics API (v2).
 *
 * The new API works in two steps:
 *   1. Call a reports endpoint → get download_links
 *   2. Download the NDJSON files → parse records → convert to legacy format
 *
 * For date ranges within the last 28 days, uses the 28-day/latest endpoint.
 * For specific dates or wider ranges, iterates 1-day endpoints in parallel.
 *
 * Note: The new API does not support team-specific metrics endpoints.
 * Team filtering is only available via the Cosmos DB path.
 */
export const getCopilotMetricsFromApi = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const env = ensureGitHubEnvConfig();

  if (env.status !== "OK") {
    return env;
  }

  const { token, version } = env.response;
  const baseUrl = getApiBaseUrl();

  try {
    const entityName = filter.enterprise || filter.organization;

    // Determine whether to use the 28-day or day-by-day approach
    const needsSpecificDays = filter.startDate && filter.endDate;

    if (needsSpecificDays) {
      // For specific date ranges, fetch each day individually in parallel
      return fetchDayRange(filter, token, version, baseUrl);
    }

    // Default: use the 28-day latest report
    let reportUrl: string;
    if (filter.enterprise) {
      reportUrl = `${baseUrl}/enterprises/${filter.enterprise}/copilot/metrics/reports/enterprise-28-day/latest`;
    } else {
      reportUrl = `${baseUrl}/orgs/${filter.organization}/copilot/metrics/reports/organization-28-day/latest`;
    }

    const result = await fetchMetricsReport(
      reportUrl,
      token,
      version,
      entityName
    );

    if (result.status !== "OK") {
      return result;
    }

    const metrics = convertReportRecordsToLegacy(result.response);
    const dataWithTimeFrame = applyTimeFrameLabel(metrics);

    return {
      status: "OK",
      response: dataWithTimeFrame,
    };
  } catch (e) {
    return unknownResponseError(e);
  }
};

/**
 * Fetches metrics for a specific date range by calling the 1-day endpoint
 * for each day in parallel with bounded concurrency.
 */
async function fetchDayRange(
  filter: IFilter,
  token: string,
  version: string,
  baseUrl: string
): Promise<ServerActionResponse<CopilotUsageOutput[]>> {
  const entityName = filter.enterprise || filter.organization;
  const start = filter.startDate!;
  const end = filter.endDate!;

  // Generate the list of days
  const days: string[] = [];
  const currentTime = new Date(start).getTime();
  const endTime = end.getTime();
  const msPerDay = 86400000;
  for (let t = currentTime; t <= endTime; t += msPerDay) {
    days.push(format(new Date(t), "yyyy-MM-dd"));
  }

  // Cap at a reasonable number to avoid excessive API calls
  const maxDays = 90;
  const daysToFetch = days.slice(0, maxDays);

  // Fetch each day in parallel batches
  const batchSize = 10;
  const allMetrics: CopilotMetrics[] = [];

  for (let i = 0; i < daysToFetch.length; i += batchSize) {
    const batch = daysToFetch.slice(i, i + batchSize);
    const batchPromises = batch.map(async (day) => {
      let reportUrl: string;
      if (filter.enterprise) {
        reportUrl = `${baseUrl}/enterprises/${filter.enterprise}/copilot/metrics/reports/enterprise-1-day?day=${day}`;
      } else {
        reportUrl = `${baseUrl}/orgs/${filter.organization}/copilot/metrics/reports/organization-1-day?day=${day}`;
      }

      return fetchMetricsReport(reportUrl, token, version, entityName);
    });

    const batchResults = await Promise.all(batchPromises);

    for (const result of batchResults) {
      if (result.status === "OK" && result.response.length > 0) {
        allMetrics.push(...convertReportRecordsToLegacy(result.response));
      }
    }
  }

  // Sort by date
  allMetrics.sort(
    (a, b) => new Date(a.date).getTime() - new Date(b.date).getTime()
  );

  const dataWithTimeFrame = applyTimeFrameLabel(allMetrics);

  return {
    status: "OK",
    response: dataWithTimeFrame,
  };
}

export const getCopilotMetricsFromDatabase = async (
  filter: IFilter
): Promise<ServerActionResponse<CopilotUsageOutput[]>> => {
  const client = cosmosClient();
  const database = client.database("platform-engineering");
  const container = database.container("metrics_history");

  let start = "";
  let end = "";
  const maxDays = 365 * 2; // maximum 2 years of data
  const maximumDays = 31;

  if (filter.startDate && filter.endDate) {
    start = format(filter.startDate, "yyyy-MM-dd");
    end = format(filter.endDate, "yyyy-MM-dd");
  } else {
    // set the start date to today and the end date to 31 days ago
    const todayDate = new Date();
    const startDate = new Date(todayDate);
    startDate.setDate(todayDate.getDate() - maximumDays);

    start = format(startDate, "yyyy-MM-dd");
    end = format(todayDate, "yyyy-MM-dd");
  }

  let querySpec: SqlQuerySpec = {
    query: `SELECT * FROM c WHERE c.date >= @start AND c.date <= @end`,
    parameters: [
      { name: "@start", value: start },
      { name: "@end", value: end },
    ],
  };

  if (filter.enterprise) {
    querySpec.query += ` AND c.enterprise = @enterprise`;
    querySpec.parameters?.push({
      name: "@enterprise",
      value: filter.enterprise,
    });
  }

  if (filter.organization) {
    querySpec.query += ` AND c.organization = @organization`;
    querySpec.parameters?.push({
      name: "@organization",
      value: filter.organization,
    });
  }
  if (filter.team && filter.team.length > 0) {
    if (filter.team.length === 1) {
      querySpec.query += ` AND c.team = @team`;
      querySpec.parameters?.push({ name: "@team", value: filter.team[0] });
    } else {
      const teamConditions = filter.team
        .map((_, index) => `c.team = @team${index}`)
        .join(" OR ");
      querySpec.query += ` AND (${teamConditions})`;
      filter.team.forEach((team, index) => {
        querySpec.parameters?.push({ name: `@team${index}`, value: team });
      });
    }
  }else {
    querySpec.query += ` AND c.team = null`;
  }

  const { resources } = await container.items
    .query<CopilotMetrics>(querySpec, {
      maxItemCount: maxDays,
    })
    .fetchAll();

  const dataWithTimeFrame = applyTimeFrameLabel(resources);
  return {
    status: "OK",
    response: dataWithTimeFrame,
  };
};

export const _getCopilotMetrics = (): Promise<CopilotUsageOutput[]> => {
  const promise = new Promise<CopilotUsageOutput[]>((resolve) => {
    setTimeout(() => {
      const weekly = applyTimeFrameLabel(sampleData);
      resolve(weekly);
    }, 1000);
  });

  return promise;
};
