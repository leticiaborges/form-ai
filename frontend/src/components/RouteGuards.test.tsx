import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { RedirectIfAuthenticated } from "./RedirectIfAuthenticated";
import { RequireAuth } from "./RequireAuth";

const auth = vi.hoisted(() => ({ isAuthenticated: false }));
vi.mock("../context/useAuth", () => ({ useAuth: () => auth }));

function renderAt(route: string) {
  render(
    <MemoryRouter initialEntries={[route]}>
      <Routes>
        <Route element={<RedirectIfAuthenticated />}>
          <Route path="/" element={<p>Landing page</p>} />
          <Route path="/login" element={<p>Login page</p>} />
        </Route>
        <Route element={<RequireAuth />}>
          <Route path="/dashboard" element={<p>Dashboard page</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe("route guards", () => {
  beforeEach(() => {
    auth.isAuthenticated = false;
  });

  it("sends an anonymous visitor from /dashboard to the landing page", () => {
    renderAt("/dashboard");
    expect(screen.getByText("Landing page")).toBeInTheDocument();
  });

  it("lets a signed-in user open /dashboard", () => {
    auth.isAuthenticated = true;
    renderAt("/dashboard");
    expect(screen.getByText("Dashboard page")).toBeInTheDocument();
  });

  it("sends a signed-in user from / and /login to the dashboard", () => {
    auth.isAuthenticated = true;
    renderAt("/");
    expect(screen.getByText("Dashboard page")).toBeInTheDocument();
  });

  it("shows the landing page to an anonymous visitor", () => {
    renderAt("/");
    expect(screen.getByText("Landing page")).toBeInTheDocument();
  });
});
