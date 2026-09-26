import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { LoginPage } from "./LoginPage";
import { server } from "../test/server";
import { renderWithProviders } from "../test/renderWithProviders";
import { tokenStore } from "../auth/tokenStore";

const LOGIN_URL = "*/api/auth/login";
const EMAIL = "testuser@example.com";
const PASSWORD = "Secret123!";
const NAME = "TestUser";

function fakeJwt(payload: object) {
  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

function renderLogin() {
  return renderWithProviders(<LoginPage />, {
    route: "/login",
    path: "/login",
  });
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText("Email"), EMAIL);
  await user.type(screen.getByLabelText("Password"), PASSWORD);
  await user.click(screen.getByRole("button", { name: "Log in" }));
}

describe("LoginPage", () => {
  it("shows validation errors and does not call the API when the form is empty", async () => {
    let calls = 0;
    server.use(
      http.post(LOGIN_URL, () => {
        calls++;
        return HttpResponse.json({});
      }),
    );

    const user = userEvent.setup();
    renderLogin();

    await user.click(screen.getByRole("button", { name: "Log in" }));

    expect(await screen.findByText("Enter a valid email address")).toBeInTheDocument();
    expect(screen.getByText("Password is required")).toBeInTheDocument();
    expect(calls).toBe(0);
  });

  it("keeps the access token in memory and navigates to the dashboard on success", async () => {
    const accessToken = fakeJwt({
      sub: "user-1",
      name: NAME,
      email: EMAIL,
    });
    let requestBody: unknown;
    server.use(
      http.post(LOGIN_URL, async ({ request }) => {
        requestBody = await request.json();
        return HttpResponse.json({ accessToken });
      }),
    );

    const user = userEvent.setup();
    renderLogin();

    await fillAndSubmit(user);

    expect(await screen.findByText("Dashboard page")).toBeInTheDocument();
    expect(requestBody).toEqual({ email: EMAIL, password: PASSWORD });
    expect(tokenStore.get()).toBe(accessToken);
    expect(localStorage.getItem("accessToken")).toBeNull();
    expect(localStorage.getItem("refreshToken")).toBeNull();
    expect(JSON.parse(localStorage.getItem("user")!)).toEqual({
      id: "user-1",
      name: NAME,
      email: EMAIL,
    });
  });

  it("shows the server message and stays on the page when credentials are wrong", async () => {
    server.use(
      http.post(LOGIN_URL, () =>
        HttpResponse.json({ message: "Invalid user or password." }, { status: 401 }),
      ),
    );

    const user = userEvent.setup();
    renderLogin();

    await fillAndSubmit(user);

    expect(await screen.findByText("Invalid user or password.")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument();
    expect(tokenStore.get()).toBeNull();
  });

  it("falls back to a generic message when the server cannot be reached", async () => {
    server.use(http.post(LOGIN_URL, () => HttpResponse.error()));
    const user = userEvent.setup();
    renderLogin();

    await fillAndSubmit(user);

    expect(await screen.findByText("Login failed. Check your credentials.")).toBeInTheDocument();
  });
});
