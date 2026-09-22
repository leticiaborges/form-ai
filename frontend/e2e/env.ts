export const API_PORT = Number(process.env.E2E_API_PORT ?? 5255);
export const WEB_PORT = Number(process.env.E2E_WEB_PORT ?? 5273);
export const API_URL = `http://localhost:${API_PORT}`;
export const WEB_URL = `http://localhost:${WEB_PORT}`;
export const MAILPIT_URL = process.env.MAILPIT_URL ?? "http://localhost:8025";
export const PASSWORD_FORTESTS = "Secret123!";
