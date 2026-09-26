import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AuthProvider } from "./AuthProvider";
import { useAuth } from "./useAuth";
import { tokenStore } from "../auth/tokenStore";
import { server } from "../test/server";

const REFRESH_URL = "*/api/auth/refresh";
const LOGOUT_URL = "*/api/auth/logout";
const USER = { id: "user-1", name: "TestUser", email: "testuser@example.com" };

// MSW resolves relative request URLs against location.href, so the stub needs an absolute one.
const START_URL = "http://localhost:3000/";

let location: { href: string };

beforeEach(() => {
  location = { href: START_URL };
  vi.stubGlobal("location", location);
});

function Probe() {
  const { user, logout } = useAuth();
  return (
    <>
      <p>{user ? `Hello ${user.name}` : "Signed out"}</p>
      <button onClick={logout}>Log out</button>
    </>
  );
}

function renderProvider() {
  return render(
    <AuthProvider>
      <Probe />
    </AuthProvider>,
  );
}

describe("AuthProvider boot", () => {
  it("refreshes once before rendering when the user hint exists but no token is in memory", async () => {
    localStorage.setItem("user", JSON.stringify(USER));
    let refreshCalls = 0;
    server.use(
      http.post(REFRESH_URL, () => {
        refreshCalls++;
        return HttpResponse.json({ accessToken: "fresh" });
      }),
    );

    renderProvider();

    expect(await screen.findByText("Hello TestUser")).toBeInTheDocument();
    expect(refreshCalls).toBe(1);
    expect(tokenStore.get()).toBe("fresh");
  });

  it("does not call refresh for an anonymous visitor", async () => {
    // No refresh handler: an unhandled request would fail the test.
    renderProvider();

    expect(await screen.findByText("Signed out")).toBeInTheDocument();
  });

  it("drops the hint, without redirecting, when the boot refresh fails", async () => {
    localStorage.setItem("user", JSON.stringify(USER));
    server.use(http.post(REFRESH_URL, () => new HttpResponse(null, { status: 401 })));

    renderProvider();

    expect(await screen.findByText("Signed out")).toBeInTheDocument();
    expect(localStorage.getItem("user")).toBeNull();
    expect(location.href).toBe(START_URL);
  });
});

describe("AuthProvider logout", () => {
  it("calls the logout endpoint, clears the session and returns to the landing page", async () => {
    localStorage.setItem("user", JSON.stringify(USER));
    tokenStore.set("in-memory");
    let logoutCalls = 0;
    server.use(
      http.post(LOGOUT_URL, () => {
        logoutCalls++;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderProvider();
    await userEvent.setup().click(screen.getByRole("button", { name: "Log out" }));

    await waitFor(() => expect(location.href).toBe("/"));
    expect(logoutCalls).toBe(1);
    expect(tokenStore.get()).toBeNull();
    expect(localStorage.getItem("user")).toBeNull();
  });

  it("still signs out locally when the server cannot be reached", async () => {
    localStorage.setItem("user", JSON.stringify(USER));
    tokenStore.set("in-memory");
    server.use(http.post(LOGOUT_URL, () => HttpResponse.error()));

    renderProvider();
    await userEvent.setup().click(screen.getByRole("button", { name: "Log out" }));

    expect(await screen.findByText("Signed out")).toBeInTheDocument();
    expect(tokenStore.get()).toBeNull();
  });
});
