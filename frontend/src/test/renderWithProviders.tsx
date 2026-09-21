import { render } from "@testing-library/react";
import type { ReactElement } from "react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { AuthProvider } from "../context/AuthProvider";

interface Options {
  route?: string;
  path?: string;
}

export function renderWithProviders(ui: ReactElement, { route = "/", path = "*" }: Options = {}) {
  return render(
    <MemoryRouter initialEntries={[route]}>
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
