import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { LandingPage } from "./LandingPage";
import { server } from "../test/server";
import { renderWithProviders } from "../test/renderWithProviders";
import { tokenStore } from "../auth/tokenStore";

const DEMO_URL = "*/api/auth/demo";

function fakeJwt(payload: object) {
  return `header.${btoa(JSON.stringify(payload))}.signature`;
}

function renderLanding() {
  return renderWithProviders(<LandingPage />, { route: "/", path: "/" });
}

describe("LandingPage demo", () => {
  it("leads the hero with a primary Try demo", () => {
    renderLanding();

    const hero = screen.getByRole("main");
    const buttons = within(hero).getAllByRole("button");
    expect(buttons[0]).toHaveTextContent("Try demo");
    expect(buttons[0]).toHaveClass("bg-brand-600");
    const startForFree = within(hero).getByRole("button", { name: "Start for free" });
    expect(startForFree).toHaveClass("border-brand-600");
    expect(startForFree).not.toHaveClass("bg-brand-600");
  });

  it("signs in through Try demo and opens the dashboard", async () => {
    const accessToken = fakeJwt({
      sub: "demo-1",
      name: "Demo user",
      email: "demo-1@demo.invalid",
      is_demo: "true",
    });
    server.use(http.post(DEMO_URL, () => HttpResponse.json({ accessToken })));

    renderLanding();
    await userEvent.setup().click(screen.getByRole("button", { name: "Try demo" }));

    expect(await screen.findByText("Dashboard page")).toBeInTheDocument();
    expect(tokenStore.get()).toBe(accessToken);
  });

  it("shows why the demo could not start", async () => {
    server.use(http.post(DEMO_URL, () => HttpResponse.error()));

    renderLanding();
    await userEvent.setup().click(screen.getByRole("button", { name: "Try demo" }));

    expect(await screen.findByText("Could not start the demo. Please try again.")).toBeVisible();
    expect(screen.queryByText("Dashboard page")).not.toBeInTheDocument();
    expect(screen.getByRole("main")).toBeInTheDocument();
  });
});
