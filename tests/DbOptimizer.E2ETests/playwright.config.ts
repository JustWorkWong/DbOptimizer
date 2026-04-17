import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: 'html',

  use: {
    baseURL: 'http://localhost:9158',
    trace: 'on',
    screenshot: 'on',
    video: 'on',
    headless: false, // 显示浏览器
    slowMo: 500, // 每个操作延迟 500ms，方便观察
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],

  webServer: {
    command: 'echo "Aspire already running"',
    url: 'http://localhost:9158',
    reuseExistingServer: true,
    timeout: 120 * 1000,
  },
});
