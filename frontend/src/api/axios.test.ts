import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import api, { getFreshAccessToken } from "./axios";
import { tokenStore, writeUserHint } from "../auth/tokenStore";
import { server } from "../test/server";

const REFRESH_URL = "*/api/auth/refresh";
const LOGIN_URL = "*/api/auth/login";
const FORMS_URL = "*/api/forms";

const OLD_TOKEN = "old-token";
const NEW_TOKEN = "new-token";

function fakeJwt(payload: object) {
  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

// MSW resolves relative request URLs against location.href, so the stub needs an absolute one.
const START_URL = "http://localhost:3000/dashboard";

let location: { href: string };

beforeEach(() => {
  location = { href: START_URL };
  vi.stubGlobal("location", location);
});

describe("api 401 handling", () => {
  it("refreshes once and replays the original request with the new token", async () => {
    tokenStore.set(OLD_TOKEN);
    const seenAuth: (string | null)[] = [];
    server.use(
      http.get(FORMS_URL, ({ request }) => {
        const auth = request.headers.get("Authorization");
        seenAuth.push(auth);
        return auth === `Bearer ${NEW_TOKEN}`
          ? HttpResponse.json({ ok: true })
          : new HttpResponse(null, { status: 401 });
      }),
      http.post(REFRESH_URL, () => HttpResponse.json({ accessToken: NEW_TOKEN })),
    );

    const response = await api.get("/forms");

    expect(response.data).toEqual({ ok: true });
    expect(seenAuth).toEqual([`Bearer ${OLD_TOKEN}`, `Bearer ${NEW_TOKEN}`]);
    expect(tokenStore.get()).toBe(NEW_TOKEN);
    expect(location.href).toBe(START_URL);
  });

  it("makes exactly one refresh call for parallel 401s", async () => {
    tokenStore.set(OLD_TOKEN);
    let refreshCalls = 0;
    server.use(
      http.get(FORMS_URL, ({ request }) =>
        request.headers.get("Authorization") === `Bearer ${NEW_TOKEN}`
          ? HttpResponse.json({ ok: true })
          : new HttpResponse(null, { status: 401 }),
      ),
      http.post(REFRESH_URL, async () => {
        refreshCalls++;
        await new Promise((resolve) => setTimeout(resolve, 20));
        return HttpResponse.json({ accessToken: NEW_TOKEN });
      }),
    );

    const responses = await Promise.all([api.get("/forms"), api.get("/forms"), api.get("/forms")]);

    expect(responses.map((r) => r.data)).toEqual([{ ok: true }, { ok: true }, { ok: true }]);
    expect(refreshCalls).toBe(1);
  });

  it("clears the session and redirects to /login when the refresh fails", async () => {
    tokenStore.set(OLD_TOKEN);
    writeUserHint({ id: "1", name: "A", email: "a@b.c" });
    server.use(
      http.get(FORMS_URL, () => new HttpResponse(null, { status: 401 })),
      http.post(REFRESH_URL, () => new HttpResponse(null, { status: 401 })),
    );

    await expect(api.get("/forms")).rejects.toMatchObject({ response: { status: 401 } });

    expect(tokenStore.get()).toBeNull();
    expect(localStorage.getItem("user")).toBeNull();
    expect(location.href).toBe("/login");
  });

  it("ends the session cleanly when the refresh cannot be reached", async () => {
    tokenStore.set(OLD_TOKEN);
    server.use(
      http.get(FORMS_URL, () => new HttpResponse(null, { status: 401 })),
      http.post(REFRESH_URL, () => HttpResponse.error()),
    );

    await expect(api.get("/forms")).rejects.toBeDefined();

    expect(tokenStore.get()).toBeNull();
    expect(location.href).toBe("/login");
  });

  it("does not refresh or redirect for a 401 on a request that sent no token", async () => {
    server.use(
      http.post(LOGIN_URL, () =>
        HttpResponse.json({ message: "Invalid user or password." }, { status: 401 }),
      ),
    );

    await expect(api.post("/auth/login", {})).rejects.toMatchObject({
      response: { status: 401, data: { message: "Invalid user or password." } },
    });

    expect(location.href).toBe(START_URL);
  });

  it("does not retry a replayed request that is rejected again", async () => {
    tokenStore.set(OLD_TOKEN);
    let refreshCalls = 0;
    server.use(
      http.get(FORMS_URL, () => new HttpResponse(null, { status: 401 })),
      http.post(REFRESH_URL, () => {
        refreshCalls++;
        return HttpResponse.json({ accessToken: NEW_TOKEN });
      }),
    );

    await expect(api.get("/forms")).rejects.toMatchObject({ response: { status: 401 } });

    expect(refreshCalls).toBe(1);
  });
});

describe("getFreshAccessToken", () => {
  it("returns an empty string without calling the server when signed out", async () => {
    expect(await getFreshAccessToken()).toBe("");
  });

  it("returns the current token while it is not close to expiring", async () => {
    const token = fakeJwt({ exp: Math.floor(Date.now() / 1000) + 600 });
    tokenStore.set(token);

    expect(await getFreshAccessToken()).toBe(token);
  });

  it("refreshes a token that expires within 30 seconds", async () => {
    tokenStore.set(fakeJwt({ exp: Math.floor(Date.now() / 1000) + 10 }));
    server.use(http.post(REFRESH_URL, () => HttpResponse.json({ accessToken: NEW_TOKEN })));

    expect(await getFreshAccessToken()).toBe(NEW_TOKEN);
    expect(tokenStore.get()).toBe(NEW_TOKEN);
  });

  it("ends the session when the refresh fails", async () => {
    tokenStore.set(fakeJwt({ exp: Math.floor(Date.now() / 1000) - 5 }));
    server.use(http.post(REFRESH_URL, () => new HttpResponse(null, { status: 401 })));

    expect(await getFreshAccessToken()).toBe("");
    expect(tokenStore.get()).toBeNull();
    expect(location.href).toBe("/login");
  });
});
