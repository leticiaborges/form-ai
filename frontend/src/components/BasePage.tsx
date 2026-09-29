import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { useAuth } from "../context/useAuth";
import { Button } from "./Button";
import formAiLogo from "../assets/FormAI.png";

interface BasePageProps {
  children: ReactNode;
  stickyHeader?: ReactNode;
}

export function BasePage({ children, stickyHeader }: Readonly<BasePageProps>) {
  const { user, logout } = useAuth();

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      <div className="sticky top-0 z-10 flex flex-col">
        <header className="border-b border-gray-200 bg-white px-6 py-1.5 shadow-sm flex items-center justify-between">
          <Link to="/dashboard" className="flex items-center">
            <img src={formAiLogo} alt="FormAI" className="h-10 w-auto" />
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
        {stickyHeader}
      </div>
      {user?.isDemo && (
        <div
          role="status"
          className="border-b border-amber-200 bg-amber-50 px-6 py-2 text-center text-sm text-amber-900"
        >
          You're using a demo account. Your forms are only available for 1 day.{" "}
          <Link to="/register" className="font-semibold underline">
            Create an account
          </Link>{" "}
          to create forms and keep them.
        </div>
      )}
      <div className="flex-1 flex flex-col">{children}</div>
    </div>
  );
}
