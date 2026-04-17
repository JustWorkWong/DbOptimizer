import { test, expect } from '@playwright/test';

test.describe('基础导航测试', () => {
  test('访问首页并验证标题', async ({ page }) => {
    await page.goto('/');

    // 等待页面加载
    await page.waitForLoadState('networkidle');

    // 验证页面标题
    await expect(page).toHaveTitle(/DbOptimizer/);

    // 截图
    await page.screenshot({ path: 'test-results/homepage.png', fullPage: true });
  });

  test('验证 API 健康检查', async ({ request }) => {
    const response = await request.get('http://localhost:8669/health');
    expect(response.ok()).toBeTruthy();

    const text = await response.text();
    console.log('Health check response:', text);
  });
});
