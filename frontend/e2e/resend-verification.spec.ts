import { randomUUID } from "node:crypto";
import { test, expect, type APIRequestContext } from "@playwright/test";
import { API_URL, MAILPIT_URL, PASSWORD_FORTESTS } from "./env";
import { registerVerifiedUser } from "./support/api";

const RESEND_URL = `${API_URL}/api/auth/resend-verification`;
const SAME_MESSAGE = {
  message: "If an account exists for this email and isn't verified yet, we've sent a new link.",
};

async function register(request: APIRequestContext, email: string) {
  const res = await request.post(`${API_URL}/api/auth/register`, {
    data: { name: "E2E Resend", email, password: PASSWORD_FORTESTS },
  });
  expect(res.ok(), `register failed: ${res.status()} ${await res.text()}`).toBeTruthy();
}

// Mailpit returns the newest message first.
async function mailTo(request: APIRequestContext, email: string) {
  const search = await request.get(`${MAILPIT_URL}/api/v1/search`, {
    params: { query: `to:${email}` },
  });
  const { messages } = await search.json();
  return (messages ?? []) as { ID: string }[];
}

async function tokenIn(request: APIRequestContext, messageId: string) {
  const mail = await (await request.get(`${MAILPIT_URL}/api/v1/message/${messageId}`)).json();
  return /verify-email\?token=([^\s"<>]+)/.exec(mail.Text)?.[1] ?? "";
}

// The limiter is in memory and keyed on the caller's IP for this anonymous endpoint, and the e2e
// API runs with a limit of 5 (playwright.config.ts). Every call here counts against that one
// partition, so the whole walk is a single test: 400, 400, 200, 200, 200, then 429.
test("resend endpoint answers alike and is rate limited", async ({ request }) => {
  const unverified = `e2e-${randomUUID()}@example.com`;
  await register(request, unverified);
  await expect.poll(async () => (await mailTo(request, unverified)).length).toBe(1);
  const verified = (await registerVerifiedUser(request)).email;
  const verifiedMailBefore = (await mailTo(request, verified)).length;
  const unknown = `e2e-${randomUUID()}@example.com`;

  await test.step("blank or missing email is a 400 under errors.email", async () => {
    for (const data of [{ email: "" }, {}]) {
      const res = await request.post(RESEND_URL, { data });
      expect(res.status(), JSON.stringify(data)).toBe(400);
      const body = await res.json();
      expect(body.errors.email).toEqual(["Email is required."]);
      expect(body.code).toBeNull();
    }
  });

  await test.step("unknown and already-verified emails get the same 200 and no mail", async () => {
    for (const email of [unknown, verified]) {
      const res = await request.post(RESEND_URL, { data: { email } });
      expect(res.status(), email).toBe(200);
      expect(await res.json()).toEqual(SAME_MESSAGE);
    }
    expect(await mailTo(request, unknown)).toHaveLength(0);
    expect(await mailTo(request, verified)).toHaveLength(verifiedMailBefore);
  });

  await test.step("an unverified email gets the same 200, a second mail and a working token", async () => {
    const res = await request.post(RESEND_URL, { data: { email: unverified } });
    expect(res.status()).toBe(200);
    expect(await res.json()).toEqual(SAME_MESSAGE);

    const messages = await mailTo(request, unverified);
    expect(messages).toHaveLength(2);
    const token = await tokenIn(request, messages[0].ID);
    expect(token).not.toBe("");

    const verify = await request.post(`${API_URL}/api/auth/verify-email`, {
      data: { token: decodeURIComponent(token) },
    });
    expect(verify.status(), await verify.text()).toBe(200);
  });

  await test.step("the sixth call in the window is a 429", async () => {
    const res = await request.post(RESEND_URL, { data: { email: unknown } });
    expect(res.status()).toBe(429);
    const body = await res.json();
    expect(body.message).toMatch(/limit of 5 .*per 15 minutes/);
    expect(body.errors).toBeNull();
    expect(body.code).toBeNull();
  });
});

// Generation shares the one OnRejected with the resend policy; this pins that it kept its own
// message. The limiter runs before the handler, so an empty body (a 400) still uses a permit.
test("generate keeps its own rate limit message", async ({ request }) => {
  const { email, password } = await registerVerifiedUser(request);
  const login = await request.post(`${API_URL}/api/auth/login`, { data: { email, password } });
  const { accessToken } = await login.json();
  const headers = { Authorization: `Bearer ${accessToken}` };

  for (let i = 1; i <= 10; i++) {
    const res = await request.post(`${API_URL}/api/forms/generate/text`, { headers, data: {} });
    expect(res.status(), `call ${i}`).toBe(400);
  }

  const limited = await request.post(`${API_URL}/api/forms/generate/text`, { headers, data: {} });
  expect(limited.status()).toBe(429);
  expect((await limited.json()).message).toMatch(/limit of 10 form generations per 60 minutes/);
});
