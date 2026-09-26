import { useEffect, useState, type ReactNode } from "react";
import api, { refreshAccessToken } from "../api/axios";
import { clearUserHint, readUserHint, tokenStore, writeUserHint } from "../auth/tokenStore";
import type { AuthUser } from "../types/auth";
import { AuthContext } from "./AuthContext";

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(readUserHint);
  // The access token is memory-only, so a reload leaves a signed-in user without one. The stored
  // user is only a hint that the refresh cookie is worth trying; anonymous visitors skip the call.
  const [isBooting, setIsBooting] = useState(() => user !== null && tokenStore.get() === null);

  useEffect(() => {
    if (!isBooting) return;

    refreshAccessToken()
      .catch(() => {
        clearUserHint();
        setUser(null);
      })
      .finally(() => setIsBooting(false));
  }, [isBooting]);

  function login(accessToken: string, user: AuthUser) {
    tokenStore.set(accessToken);
    writeUserHint(user);
    setUser(user);
  }

  async function logout() {
    try {
      await api.post("/auth/logout");
    } catch {
      // Signing out must work even when the server can't be reached.
    }
    tokenStore.clear();
    clearUserHint();
    setUser(null);
    window.location.href = "/";
  }

  if (isBooting) return null;

  return (
    <AuthContext.Provider value={{ user, isAuthenticated: user !== null, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}
