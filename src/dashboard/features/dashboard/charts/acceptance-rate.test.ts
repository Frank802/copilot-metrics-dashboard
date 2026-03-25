import { describe, expect, it } from "vitest";
import { applyTimeFrameLabel } from "@/utils/helpers";
import { sampleData } from "@/services/sample-data";
import { computeAcceptanceAverage } from "./common";

describe("computeAcceptanceAverage", () => {
  it("correctly computes the acceptance average for provided data", () => {
    // Sample data has 1 entry:
    // vscode python: 249 suggestions / 123 acceptances
    // vscode ruby: 496 suggestions / 253 acceptances
    // neovim typescript: 112 suggestions / 56 acceptances
    // neovim go: 132 suggestions / 67 acceptances
    // Total: 989 suggestions / 499 acceptances = 50.45...%
    // Total lines suggested: 225+520+143+154 = 1042
    // Total lines accepted: 135+270+61+72 = 538
    // Lines rate: 538/1042 = 51.63...%
    const results = computeAcceptanceAverage(applyTimeFrameLabel(sampleData));
    expect(results.length).toBe(1);
    expect(results[0].acceptanceRate).toBeCloseTo(50.45, 0);
    expect(results[0].acceptanceLinesRate).toBeCloseTo(51.63, 0);
    expect(results[0].timeFrameDisplay).toBe("Jun 24");
  });

  it("handles empty input data gracefully", () => {
    const results = computeAcceptanceAverage([]);
    expect(results).toEqual([]);
  });

  it("handles data with zero suggested lines", () => {
    const transformed = applyTimeFrameLabel(sampleData);
    const modifiedData = [
      {
        ...transformed[0],
        breakdown: transformed[0].breakdown.map((breakdown) => ({
          ...breakdown,
          suggestions_count: 0,
          lines_suggested: 0,
        })),
      },
    ];
    const results = computeAcceptanceAverage(modifiedData);
    expect(results.length).toBe(1);
    expect(results[0].acceptanceRate).toBe(0);
    expect(results[0].acceptanceLinesRate).toBe(0);
  });
});
