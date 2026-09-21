import { createContext } from "react";
import type { AuthUser } from "../types/auth";

export interface AuthContextValue {
  user: AuthUser | null;
  isAuthenticated: boolean;
  login: (accessToken: string, refreshToken: string, user: AuthUser) => void;
  logout: () => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
