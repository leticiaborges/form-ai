import { render, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { StrictMode } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { VerifyEmailPage } from "./VerifyEmailPage";
import { server } from "../test/server";

describe("VerifyEmailPage", () => {
  it("verifies the token once even when StrictMode runs the effect twice", async () => {
    let calls = 0;
    server.use(
      http.post("*/api/auth/verify-email", () => {
        calls++;
        // Like the real API: the first call verifies, any later one is rejected.
        return calls === 1
          ? HttpResponse.json({ message: "Email verified successfully." })
          : HttpResponse.json(
              { errors: { email: ["This email is already verified."] } },
              { status: 400 },
            );
      }),
    );

    render(
      <StrictMode>
        <MemoryRouter initialEntries={["/verify-email?token=abc"]}>
          <Routes>
            <Route path="/verify-email" element={<VerifyEmailPage />} />
          </Routes>
        </MemoryRouter>
      </StrictMode>,
    );

    expect(await screen.findByText("Email verified!")).toBeInTheDocument();
    expect(calls).toBe(1);
  });
});
