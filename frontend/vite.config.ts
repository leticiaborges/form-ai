import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  server: {
    proxy: {
      // Requests to /api are forwarded to the backend — no CORS in dev
      '/api': {
        target: 'http://localhost:5155',
        changeOrigin: true,
      },
    },
  },
})