import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig(({ command }) => {
  const isServe = command === 'serve'
  const apiBaseUrl = process.env.VITE_API_BASE_URL

  if (isServe && !apiBaseUrl) {
    throw new Error('Missing required environment variable: VITE_API_BASE_URL. DbOptimizer.Web must be started by Aspire or with an explicit VITE_API_BASE_URL.')
  }

  return {
    plugins: [vue()],
    server: isServe
      ? {
          host: '0.0.0.0',
          strictPort: false,
          proxy: {
            '/api': {
              target: apiBaseUrl!,
              changeOrigin: true,
            },
          },
        }
      : undefined,
  }
})
