import { configDefaults, defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

const apiTarget = process.env.API_URL ?? "http://localhost:5155";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      // Requests to /api are forwarded to the backend — no CORS in dev
      "/api": {
        target: apiTarget,
        changeOrigin: true,
      },
      "/hubs": {
        target: apiTarget,
        changeOrigin: true,
        ws: true,
      },
    },
  },
  test: {
    environment: "jsdom",
    exclude: [...configDefaults.exclude, "e2e/**"],
    setupFiles: ["./src/test/setup.ts"],
    css: false,
    restoreMocks: true,
    unstubGlobals: true,
    coverage: {
      provider: "v8",
      include: ["src/**/*.{ts,tsx}"],
      exclude: ["src/main.tsx", "src/types/**", "src/test/**", "src/**/*.test.{ts,tsx}"],
    },
  },
});
