import type { AuthUser } from "../types/auth";

const USER_HINT_KEY = "user";

let accessToken: string | null = null;

export const tokenStore = {
  get: () => accessToken,
  set: (token: string) => {
    accessToken = token;
  },
  clear: () => {
    accessToken = null;
  },
};

export function readUserHint(): AuthUser | null {
  const stored = localStorage.getItem(USER_HINT_KEY);
  if (!stored) return null;
  try {
    return JSON.parse(stored);
  } catch {
    localStorage.removeItem(USER_HINT_KEY);
    return null;
  }
}

export function writeUserHint(user: AuthUser) {
  localStorage.setItem(USER_HINT_KEY, JSON.stringify(user));
}

export function clearUserHint() {
  localStorage.removeItem(USER_HINT_KEY);
}

interface AccessTokenClaims {
  sub: string;
  name: string;
  email: string;
  is_demo?: string;
  exp?: number;
}

function decodeClaims(token: string): AccessTokenClaims {
  const base64 = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
  return JSON.parse(new TextDecoder().decode(bytes));
}

export function userFromAccessToken(token: string): AuthUser {
  const { sub, name, email, is_demo } = decodeClaims(token);
  return { id: sub, name, email, isDemo: is_demo === "true" };
}

/** True when the token has expired or will within `withinMs`. A token without `exp` never does. */
export function expiresWithin(token: string, withinMs: number): boolean {
  const { exp } = decodeClaims(token);
  return exp !== undefined && exp * 1000 - Date.now() <= withinMs;
}
