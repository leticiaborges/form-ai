import { test, expect, type APIRequestContext } from "@playwright/test";
import { API_URL, MAILPIT_URL } from "./env";

interface Claims {
  sub: string;
  name: string;
  email: string;
  is_demo?: string;
}

function decode(token: string): Claims {
  return JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString("utf8"));
}

async function startDemo(request: APIRequestContext) {
  return request.post(`${API_URL}/api/auth/demo`);
}

// Every call to the demo endpoint shares one IP partition (fullyParallel would split separate
// tests across workers), so the whole walk is a single test.
test("starts, refreshes and rate limits a demo session", async ({ request }) => {
  const first = await startDemo(request);
  expect(first.status()).toBe(200);
  const { accessToken } = await first.json();
  const claims = decode(accessToken);

  // C8
  expect(claims.is_demo).toBe("true");
  expect(claims.name).toBe("Demo user");
  expect(claims.email).toMatch(/^demo-[0-9a-f-]{36}@demo\.invalid$/);
  const setCookie = first
    .headersArray()
    .filter((h) => h.name.toLowerCase() === "set-cookie")
    .map((h) => h.value)
    .find((v) => v.startsWith("refresh_token="));
  expect(setCookie).toBeDefined();
  expect(setCookie).toMatch(/httponly/i);
  expect(setCookie).toMatch(/secure/i);
  expect(setCookie).toMatch(/samesite=strict/i);
  expect(setCookie).toMatch(/path=\/api\/auth/i);

  // C12
  const second = await startDemo(request);
  expect(second.status()).toBe(200);
  const other = decode((await second.json()).accessToken);
  expect(other.sub).not.toBe(claims.sub);
  expect(other.email).not.toBe(claims.email);

  // C13
  const refreshed = await request.post(`${API_URL}/api/auth/refresh`, {
    headers: { cookie: setCookie!.split(";")[0] },
  });
  expect(refreshed.status()).toBe(200);
  const refreshedClaims = decode((await refreshed.json()).accessToken);
  expect(refreshedClaims.sub).toBe(claims.sub);
  expect(refreshedClaims.is_demo).toBe("true");

  // C14
  const login = await request.post(`${API_URL}/api/auth/login`, {
    data: { email: claims.email, password: "e2e-demo-password" },
  });
  expect(login.status()).toBe(200);

  // C15
  const mail = await request.get(`${MAILPIT_URL}/api/v1/search`, {
    params: { query: `to:${claims.email}` },
  });
  expect((await mail.json()).messages ?? []).toHaveLength(0);

  // C16: two permits used above (the refresh and login are not limited); three more, then the sixth.
  for (let i = 0; i < 3; i++) expect((await startDemo(request)).status()).toBe(200);
  const limited = await startDemo(request);
  expect(limited.status()).toBe(429);
  const body = await limited.json();
  expect(body.errors).toBeNull();
  expect(body.code).toBeNull();
  expect(body.message).toContain("5 demo sessions");
  expect(body.message).toContain("15 minutes");
});
