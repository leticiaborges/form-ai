import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../context/useAuth";
import { Button } from "./Button";

export function BasePage({ children }: Readonly<{ children: ReactNode }>) {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      <header className="border-b border-gray-200 bg-white px-6 py-1.5 shadow-sm flex items-center justify-between">
        <Link to="/dashboard" className="text-xl font-bold text-brand-600">
          FormAI
        </Link>
        {user && (
          <div className="flex items-center gap-3">
            <span className="text-sm text-gray-600">{user.name}</span>
            <Button variant="outline" onClick={logout}>
              Log out
            </Button>
          </div>
        )}
      </header>
      <div className="flex-1 flex flex-col">{children}</div>
    </div>
  );
}
