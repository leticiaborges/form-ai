import { screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { BasePage } from "./BasePage";
import { renderWithProviders } from "../test/renderWithProviders";
import { tokenStore } from "../auth/tokenStore";

const BANNER =
  "You're using a demo account. Your forms are only available for 1 day. Create an account to create forms and keep them.";

function renderAs(isDemo: boolean) {
  localStorage.setItem(
    "user",
    JSON.stringify({ id: "user-1", name: "TestUser", email: "testuser@example.com", isDemo }),
  );
  // A token in memory means the provider does not try to refresh on boot.
  tokenStore.set("in-memory");
  return renderWithProviders(
    <BasePage>
      <p>Page content</p>
    </BasePage>,
  );
}

beforeEach(() => tokenStore.clear());

describe("BasePage demo banner", () => {
  it("warns a demo user and links to register", () => {
    renderAs(true);

    const banner = screen.getByRole("status");
    expect(banner).toHaveTextContent(BANNER);
    expect(within(banner).getByRole("link", { name: "Create an account" })).toHaveAttribute(
      "href",
      "/register",
    );
  });

  it("shows no banner to a regular user", () => {
    renderAs(false);

    expect(screen.getByText("Page content")).toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.queryByText(/demo account/i)).not.toBeInTheDocument();
  });

  it("offers no way to dismiss the banner", () => {
    renderAs(true);

    const banner = screen.getByRole("status");
    expect(within(banner).queryAllByRole("button")).toHaveLength(0);
    expect(within(banner).getAllByRole("link")).toHaveLength(1);
  });
});
