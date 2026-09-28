import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { AuthProvider } from "../context/AuthProvider";

interface Options {
  route?: string;
  path?: string;
  state?: unknown;
}

export function renderWithProviders(ui: ReactElement, { route = "/", path = "*", state }: Options = {}) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: route, state }]}>
      <AuthProvider>
        <Routes>
          <Route path={path} element={ui} />
          {/* Navigation target used by LoginPage; lets tests assert "we navigated". */}
          <Route path="/dashboard" element={<p>Dashboard page</p>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}
