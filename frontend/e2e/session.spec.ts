import { test, expect, type Page } from "@playwright/test";
import { API_URL } from "./env";
import { registerVerifiedUser } from "./support/api";

async function signIn(page: Page, user: { email: string; password: string }) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(user.email);
  await page.getByLabel("Password").fill(user.password);
  await page.getByRole("button", { name: "Log in" }).click();
  await expect(page).toHaveURL(/\/dashboard/);
  await expect(page.getByText("Welcome, E2E Owner!")).toBeVisible();
}

test("the refresh token is an HttpOnly cookie and the tokens never reach JavaScript storage", async ({
  page,
  request,
}) => {
  const user = await registerVerifiedUser(request);
  await signIn(page, user);

  const cookies = await page.context().cookies();
  const refresh = cookies.find((c) => c.name === "refresh_token");
  expect(refresh).toMatchObject({ httpOnly: true, secure: true, sameSite: "Strict", path: "/api/auth" });

  expect(await page.evaluate<string>("document.cookie")).not.toContain("refresh_token");
  const storage = await page.evaluate<string[]>("Object.keys(localStorage)");
  expect(storage).not.toContain("accessToken");
  expect(storage).not.toContain("refreshToken");
});

test("reloading the page keeps the user signed in", async ({ page, request }) => {
  const user = await registerVerifiedUser(request);
  await signIn(page, user);

  await page.reload();

  await expect(page).toHaveURL(/\/dashboard/);
  await expect(page.getByText("Welcome, E2E Owner!")).toBeVisible();
});

test("a rejected access token is refreshed and the request replayed without signing out", async ({
  page,
  request,
}) => {
  const user = await registerVerifiedUser(request);

  // Simulates an expired access token: the first list request is answered with 401.
  let rejected = false;
  await page.route("**/api/forms", async (route) => {
    if (!rejected && route.request().method() === "GET") {
      rejected = true;
      await route.fulfill({ status: 401 });
      return;
    }
    await route.continue();
  });
  const refreshCalls: string[] = [];
  page.on("request", (req) => {
    if (req.url().endsWith("/api/auth/refresh")) refreshCalls.push(req.url());
  });

  await signIn(page, user);

  expect(rejected).toBe(true);
  expect(refreshCalls).toHaveLength(1);
  await expect(page).toHaveURL(/\/dashboard/);
});

test("logging out revokes the refresh token", async ({ page, request }) => {
  const user = await registerVerifiedUser(request);
  await signIn(page, user);
  const cookie = (await page.context().cookies()).find((c) => c.name === "refresh_token")!;

  await page.getByRole("button", { name: "Log out" }).click();
  await expect(page).toHaveURL(/\/$/);

  const refreshed = await request.post(`${API_URL}/api/auth/refresh`, {
    headers: { Cookie: `refresh_token=${cookie.value}` },
  });
  expect(refreshed.status()).toBe(401);

  await page.goto("/dashboard");
  await expect(page.getByText("Welcome, E2E Owner!")).not.toBeVisible();
});
