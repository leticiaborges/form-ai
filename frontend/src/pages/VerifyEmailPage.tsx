import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { verifyEmail } from "../api/auth";
import { Button } from "../components/Button";
import type { CustomResponse } from "../types/CustomResponse";
import { StatusCard } from "../components/StatusCard";

type PageState =
  { status: "loading" } | { status: "success" } | { status: "error"; errorMessage: string };

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams();
  const token = searchParams.get("token");

  const [state, setState] = useState<PageState>(
    token ? { status: "loading" } : { status: "error", errorMessage: "No token found in the URL" },
  );

  // The token is single-use, so a second call would fail with "already verified" and overwrite the
  // success. StrictMode runs this effect twice in development but keeps refs between the runs.
  const requestedToken = useRef<string | null>(null);

  useEffect(() => {
    if (!token || requestedToken.current === token) return;
    requestedToken.current = token;

    verifyEmail(token)
      .then(() => setState({ status: "success" }))
      .catch((err: unknown) => {
        const e = err as CustomResponse;
        setState({
          status: "error",
          errorMessage:
            e.response?.data?.message ?? "Verification failed. The link may have expired.",
        });
      });
  }, [token]);

  if (state.status === "loading") {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="flex flex-col items-center gap-3">
          <svg className="animate-spin h-10 w-10 text-brand-600" viewBox="0 0 24 24" fill="none">
            <circle
              className="opacity-25"
              cx="12"
              cy="12"
              r="10"
              stroke="currentColor"
              strokeWidth="4"
            />
            <path
              className="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"
            />
          </svg>
          <p className="text-lg font-medium text-gray-700">Verifying your email…</p>
        </div>
      </div>
    );
  }

  if (state.status === "success") {
    return (
      <StatusCard title="Email verified!" message="Your account is now active. You can log in.">
        <div className="mt-6">
          <Link to="/login">
            <Button className="w-full">Go to login</Button>
          </Link>
        </div>
      </StatusCard>
    );
  }

  return (
    <StatusCard
      title="Verification failed"
      variant="error"
      message={state.status === "error" ? state.errorMessage : ""}
    >
      <div className="mt-6 flex flex-col gap-3">
        <Link to="/register">
          <Button variant="outline" className="w-full">
            Register again
          </Button>
        </Link>
      </div>
    </StatusCard>
  );
}
