/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    // The browser only ever talks to the Vite server. Requests to /api are forwarded to the
    // .NET API, so they're same-origin and no CORS rules are needed locally.
    proxy: {
      '/api': {
        target: 'http://localhost:5105',
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
})
