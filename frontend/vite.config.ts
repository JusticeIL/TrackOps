import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react()],
  server: {
    // `npm run dev` talks to the API of the running compose stack.
    proxy: { '/api': 'http://localhost:8080' },
  },
  build: {
    sourcemap: false,
  },
})
