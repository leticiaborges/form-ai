import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { RegisterPage } from "./RegisterPage";
import { RegisterSuccessPage } from "./RegisterSuccessPage";
import { server } from "../test/server";

const EMAIL = "testuser@example.com";
const PASSWORD = "Secret123!";

describe("RegisterPage", () => {
  it("carries the registered email to the success page", async () => {
    server.use(
      http.post("*/api/auth/register", () =>
        HttpResponse.json({ id: "u1", name: "TestUser", email: EMAIL }, { status: 201 }),
      ),
    );
    const user = userEvent.setup();
    render(
      <MemoryRouter initialEntries={["/register"]}>
        <Routes>
          <Route path="/register" element={<RegisterPage />} />
          <Route path="/register/success" element={<RegisterSuccessPage />} />
        </Routes>
      </MemoryRouter>,
    );

    await user.type(screen.getByLabelText("Full name"), "TestUser");
    await user.type(screen.getByLabelText("Email"), EMAIL);
    await user.type(screen.getByLabelText("Password"), PASSWORD);
    await user.type(screen.getByLabelText("Confirm password"), PASSWORD);
    await user.click(screen.getByRole("button", { name: "Create account" }));

    expect(await screen.findByRole("heading", { name: "Check your inbox" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /resend verification email/i })).toBeInTheDocument();
  });
});
