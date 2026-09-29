import { describe, expect, it } from "vitest";
import { userFromAccessToken } from "./tokenStore";

function fakeJwt(payload: object) {
  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

describe("userFromAccessToken", () => {
  it("reads the is_demo claim", () => {
    const base = { sub: "user-1", name: "TestUser", email: "testuser@example.com" };

    expect(userFromAccessToken(fakeJwt({ ...base, is_demo: "true" })).isDemo).toBe(true);
    expect(userFromAccessToken(fakeJwt(base)).isDemo).toBe(false);
  });
});
