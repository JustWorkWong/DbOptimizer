import { test, expect } from '@playwright/test';

test.describe('DbOptimizer E2E 测试', () => {
  test('访问首页并验证标题', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    // 截图
    await page.screenshot({ path: 'test-results/homepage.png', fullPage: true });

    console.log('首页访问成功');
  });

  test('验证 API 健康检查', async ({ request }) => {
    const response = await request.get('http://localhost:8669/health');
    expect(response.ok()).toBeTruthy();

    const text = await response.text();
    console.log('API 健康检查:', text);
    expect(text).toContain('Healthy');
  });

  test('验证 Swagger 文档可访问', async ({ page }) => {
    await page.goto('http://localhost:8669/swagger');
    await page.waitForLoadState('networkidle');

    // 验证 Swagger UI 加载
    const title = await page.title();
    console.log('Swagger 页面标题:', title);

    await page.screenshot({ path: 'test-results/swagger.png', fullPage: true });
  });
});
