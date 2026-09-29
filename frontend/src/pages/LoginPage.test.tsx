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
      isDemo: false,
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

describe("LoginPage demo", () => {
  it("replaces an earlier login error with the demo failure", async () => {
    server.use(
      http.post("*/api/auth/login", () =>
        HttpResponse.json({ message: "Invalid user or password." }, { status: 401 }),
      ),
      http.post("*/api/auth/demo", () => HttpResponse.error()),
    );
    const user = userEvent.setup();
    renderLogin();

    await fillAndSubmit(user);
    expect(await screen.findByText("Invalid user or password.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Try demo" }));

    expect(
      await screen.findByText("Could not start the demo. Please try again."),
    ).toBeInTheDocument();
    expect(screen.queryByText("Invalid user or password.")).not.toBeInTheDocument();
  });

  const DEMO_URL = "*/api/auth/demo";
  const demoToken = () =>
    fakeJwt({ sub: "demo-1", name: "Demo user", email: "demo-1@demo.invalid", is_demo: "true" });

  it("offers Try demo as a link above a primary Log in", () => {
    renderLogin();

    const tryDemo = screen.getByRole("button", { name: "Try demo" });
    expect(tryDemo).toHaveClass("text-brand-600");
    expect(tryDemo).not.toHaveClass("bg-brand-600");
    expect(tryDemo).not.toHaveClass("w-full");
    const email = screen.getByLabelText("Email");
    expect(tryDemo.compareDocumentPosition(email) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    const logIn = screen.getByRole("button", { name: "Log in" });
    expect(logIn).toHaveClass("bg-brand-600");
    expect(logIn).not.toHaveClass("border-brand-600");
  });

  it("signs in through Try demo and opens the dashboard", async () => {
    const accessToken = demoToken();
    let calls = 0;
    let body: string | undefined;
    server.use(
      http.post(DEMO_URL, async ({ request }) => {
        calls++;
        body = await request.text();
        return HttpResponse.json({ accessToken });
      }),
    );

    renderLogin();
    await userEvent.setup().click(screen.getByRole("button", { name: "Try demo" }));

    expect(await screen.findByText("Dashboard page")).toBeInTheDocument();
    expect(calls).toBe(1);
    expect(body).toBe("");
    expect(tokenStore.get()).toBe(accessToken);
  });

  it("disables Try demo while the request is pending", async () => {
    let calls = 0;
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    server.use(
      http.post(DEMO_URL, async () => {
        calls++;
        await gate;
        return HttpResponse.json({ accessToken: demoToken() });
      }),
    );

    const user = userEvent.setup();
    renderLogin();
    const button = screen.getByRole("button", { name: "Try demo" });
    await user.click(button);

    expect(button).toBeDisabled();
    await user.click(button);
    release();
    await screen.findByText("Dashboard page");
    expect(calls).toBe(1);
  });

  it("shows why the demo could not start", async () => {
    server.use(
      http.post(DEMO_URL, () =>
        HttpResponse.json(
          { message: "You have reached the limit.", errors: null, code: null },
          { status: 429 },
        ),
      ),
    );
    const user = userEvent.setup();
    renderLogin();

    await user.click(screen.getByRole("button", { name: "Try demo" }));
    expect(await screen.findByText("You have reached the limit.")).toBeInTheDocument();
    expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toBeInTheDocument();

    server.use(http.post(DEMO_URL, () => HttpResponse.error()));
    await user.click(screen.getByRole("button", { name: "Try demo" }));
    expect(
      await screen.findByText("Could not start the demo. Please try again."),
    ).toBeInTheDocument();
  });
});
