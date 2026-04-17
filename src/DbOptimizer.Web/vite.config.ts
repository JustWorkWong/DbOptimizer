import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig(({ command }) => {
  const isServe = command === 'serve'
  const webPort = process.env.PORT
  const apiProxyTarget = process.env.VITE_API_PROXY_TARGET

  if (isServe && !webPort) {
    throw new Error('Missing required environment variable: PORT. DbOptimizer.Web must be started by Aspire or with an explicit PORT.')
  }

  if (isServe && !apiProxyTarget) {
    throw new Error('Missing required environment variable: VITE_API_PROXY_TARGET. Refusing to guess an API port.')
  }

  return {
    plugins: [vue()],
    server: isServe
      ? {
          host: '0.0.0.0',
          port: Number.parseInt(webPort!, 10),
          strictPort: true,
          proxy: {
            '/api': {
              target: apiProxyTarget!,
              changeOrigin: true,
            },
          },
        }
      : undefined,
  }
})
