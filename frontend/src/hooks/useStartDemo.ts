import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { startDemo as requestDemo } from "../api/auth";
import { userFromAccessToken } from "../auth/tokenStore";
import { useAuth } from "../context/useAuth";
import type { CustomResponse } from "../types/CustomResponse";

/** Starts a throwaway demo account, signs in as it and opens the dashboard. */
export function useStartDemo() {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [isStarting, setIsStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function startDemo() {
    setError(null);
    setIsStarting(true);
    try {
      const { accessToken } = await requestDemo();
      login(accessToken, userFromAccessToken(accessToken));
      navigate("/dashboard");
    } catch (err: unknown) {
      const e = err as CustomResponse;
      setError(e.response?.data?.message ?? "Could not start the demo. Please try again.");
      setIsStarting(false);
    }
  }

  return { startDemo, isStarting, error };
}
