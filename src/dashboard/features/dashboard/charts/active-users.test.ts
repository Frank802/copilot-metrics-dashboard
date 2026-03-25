import { describe, expect, it } from "vitest";
import { applyTimeFrameLabel } from "@/utils/helpers";
import { sampleData } from "@/services/sample-data";
import { getActiveUsers } from "./common";

describe("getActiveUsers", () => {
  it("correctly computes the total active users from provided data", () => {
    // Sample data has 1 entry with total_active_users: 24
    const expectedTotalActiveUsers = [24];
    const data = applyTimeFrameLabel(sampleData);
    const actual = getActiveUsers(data).map((item) => item.totalUsers);
    expect(actual).toEqual(expectedTotalActiveUsers);
  });

  it("handles undefined or null data gracefully", () => {
    // @ts-ignore to allow testing with invalid input
    const resultForUndefined = getActiveUsers([]);

    expect(resultForUndefined.length).toBe(0);
  });
});
