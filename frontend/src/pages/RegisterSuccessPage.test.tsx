import { act, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse, delay } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RegisterSuccessPage } from "./RegisterSuccessPage";
import { server } from "../test/server";
import { renderWithProviders } from "../test/renderWithProviders";

const RESEND_URL = "*/api/auth/resend-verification";
const EMAIL = "testuser@example.com";
const RESEND = { name: /resend verification email/i };

function renderPage(state?: unknown) {
  return renderWithProviders(<RegisterSuccessPage />, {
    route: "/register/success",
    path: "/register/success",
    state,
  });
}

function resendReturns(status: number, body: object = { message: "ok" }) {
  server.use(http.post(RESEND_URL, () => HttpResponse.json(body, { status })));
}

describe("RegisterSuccessPage resend", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("offers a resend when the email is known", () => {
    renderPage({ email: EMAIL });

    expect(screen.getByRole("button", RESEND)).toBeEnabled();
    expect(screen.getByText(/in a few minutes/i)).toBeInTheDocument();
  });

  it("hides the resend when the email is unknown", () => {
    renderPage();

    expect(screen.queryByRole("button", RESEND)).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Check your inbox" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Go to login" })).toBeInTheDocument();
  });

  it("posts the known email to the resend endpoint", async () => {
    let body: unknown;
    server.use(
      http.post(RESEND_URL, async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ message: "ok" });
      }),
    );
    const user = userEvent.setup();
    renderPage({ email: EMAIL });

    await user.click(screen.getByRole("button", RESEND));

    await waitFor(() => expect(body).toEqual({ email: EMAIL }));
  });

  it("disables the control while the request is in flight", async () => {
    server.use(
      http.post(RESEND_URL, async () => {
        await delay(200);
        return HttpResponse.json({ message: "ok" });
      }),
    );
    const user = userEvent.setup();
    renderPage({ email: EMAIL });

    await user.click(screen.getByRole("button", RESEND));

    expect(screen.getByRole("button", { name: /loading/i })).toBeDisabled();
    await screen.findByRole("button", { name: /\(60s\)/ });
  });

  it("starts a 60 second countdown without a success message after a successful resend", async () => {
    resendReturns(200);
    const user = userEvent.setup();
    renderPage({ email: EMAIL });

    await user.click(screen.getByRole("button", RESEND));

    const button = await screen.findByRole("button", { name: /\(60s\)/ });
    expect(button).toBeDisabled();
    expect(button).toHaveTextContent("60");
    expect(screen.queryByText(/we've sent a new link/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("counts down and re-enables after 60 seconds", async () => {
    resendReturns(200);
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderPage({ email: EMAIL });

    await user.click(screen.getByRole("button", RESEND));
    await screen.findByRole("button", { name: /\(60s\)/ });

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(screen.getByRole("button", RESEND)).toHaveTextContent("59");
    expect(screen.getByRole("button", RESEND)).toBeDisabled();

    await act(() => vi.advanceTimersByTimeAsync(59_000));
    expect(screen.getByRole("button", RESEND)).toBeEnabled();
  });

  it("shows a generic retry message and stays enabled when the request fails", async () => {
    const user = userEvent.setup();
    renderPage({ email: EMAIL });

    resendReturns(500, { message: "boom" });
    await user.click(screen.getByRole("button", RESEND));
    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
    expect(screen.queryByText("boom")).not.toBeInTheDocument();
    expect(screen.getByRole("button", RESEND)).toBeEnabled();
    expect(screen.getByRole("button", RESEND)).not.toHaveTextContent("60");

    server.use(http.post(RESEND_URL, () => HttpResponse.error()));
    await user.click(screen.getByRole("button", RESEND));
    expect(await screen.findByText(/something went wrong/i)).toBeInTheDocument();
    expect(screen.getByRole("button", RESEND)).toBeEnabled();
    expect(screen.getByRole("button", RESEND)).not.toHaveTextContent("60");
  });

  it("shows the server message when rate limited", async () => {
    resendReturns(429, {
      message: "You have reached the limit of 3 verification emails per 15 minutes.",
      errors: null,
      code: null,
    });
    const user = userEvent.setup();
    renderPage({ email: EMAIL });

    await user.click(screen.getByRole("button", RESEND));

    expect(await screen.findByText(/limit of 3 verification emails/i)).toBeInTheDocument();
    expect(screen.getByRole("button", RESEND)).toBeEnabled();
  });
});
