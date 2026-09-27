import axios, { type InternalAxiosRequestConfig } from "axios";
import { clearUserHint, expiresWithin, tokenStore } from "../auth/tokenStore";
import type { LoginResponse } from "../types/auth";

const BASE_URL = "/api";
const REFRESH_LOCK_NAME = "formai-token-refresh";
const FRESH_TOKEN_MARGIN_MS = 30_000;

const api = axios.create({
  baseURL: BASE_URL,
  headers: { "Content-Type": "application/json" },
});

// A bare instance with no interceptors: the refresh request's own 401 must never re-enter the
// response interceptor below, or a failing refresh would wait on itself.
const refreshClient = axios.create({ baseURL: BASE_URL });

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

async function requestNewAccessToken(): Promise<string> {
  const { data } = await refreshClient.post<LoginResponse>("/auth/refresh");
  tokenStore.set(data.accessToken);
  return data.accessToken;
}

let inFlightRefresh: Promise<string> | null = null;

/**
 * Exchanges the refresh cookie for a new access token. One refresh at a time per tab (shared
 * promise) and across tabs (Web Locks): rotation revokes the old cookie, so two refreshes racing
 * would log one tab out. Has no side effect on failure; callers decide whether the session ends.
 */
export function refreshAccessToken(): Promise<string> {
  if (!inFlightRefresh) {
    const run = navigator.locks
      ? navigator.locks.request(REFRESH_LOCK_NAME, requestNewAccessToken)
      : requestNewAccessToken();
    inFlightRefresh = run.finally(() => {
      inFlightRefresh = null;
    });
  }
  return inFlightRefresh;
}

/** The session is over: forget it locally and send the user to the login page. */
function expireSession() {
  tokenStore.clear();
  clearUserHint();
  window.location.href = "/login";
}

/** For SignalR: the current access token, refreshed first if it expires within ~30s. */
export async function getFreshAccessToken(): Promise<string> {
  const token = tokenStore.get();
  if (!token) return "";
  if (!expiresWithin(token, FRESH_TOKEN_MARGIN_MS)) return token;

  try {
    return await refreshAccessToken();
  } catch {
    expireSession();
    return "";
  }
}

api.interceptors.request.use((config) => {
  const token = tokenStore.get();
  if (token) config.headers.Authorization = `Bearer ${token}`;

  return config;
});

api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const original: RetriableConfig | undefined = error.config;
    const sentToken = original?.headers?.Authorization;

    // Only a request that carried a token can have an expired session. A 401 without one (a wrong
    // password on /auth/login) belongs to the caller.
    if (error.response?.status !== 401 || !original || !sentToken || original._retried) {
      return Promise.reject(error);
    }
    original._retried = true;

    try {
      // Another request may already have refreshed while this one was in flight.
      const current = tokenStore.get();
      const token =
        current && `Bearer ${current}` !== sentToken ? current : await refreshAccessToken();
      original.headers.Authorization = `Bearer ${token}`;
    } catch {
      expireSession();
      return Promise.reject(error);
    }

    return api(original);
  },
);

export default api;
