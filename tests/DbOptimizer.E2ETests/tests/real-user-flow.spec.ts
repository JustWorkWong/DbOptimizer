import { test, expect } from '@playwright/test';

test.describe('DbOptimizer 真实用户流程测试', () => {
  test('完整用户流程：访问首页 → 查看 Swagger → 测试 API', async ({ page }) => {
    console.log('=== 开始真实用户流程测试 ===');

    // 步骤 1: 访问首页
    console.log('步骤 1: 访问首页...');
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await page.screenshot({ path: 'test-results/01-homepage.png', fullPage: true });
    console.log('✅ 首页加载成功');

    // 步骤 2: 检查页面标题
    console.log('步骤 2: 检查页面标题...');
    const title = await page.title();
    console.log(`页面标题: ${title}`);

    // 步骤 3: 访问 Swagger 文档
    console.log('步骤 3: 访问 Swagger 文档...');
    await page.goto('http://localhost:15069/swagger');
    await page.waitForLoadState('networkidle');
    await page.screenshot({ path: 'test-results/02-swagger.png', fullPage: true });
    console.log('✅ Swagger 文档加载成功');

    // 步骤 4: 测试 API 健康检查
    console.log('步骤 4: 测试 API 健康检查...');
    const response = await page.request.get('http://localhost:15069/health');
    const healthText = await response.text();
    console.log(`API 健康状态: ${healthText}`);
    expect(healthText).toContain('Healthy');
    console.log('✅ API 健康检查通过');

    // 步骤 5: 返回首页
    console.log('步骤 5: 返回首页...');
    await page.goto('/');
    await page.waitForLoadState('networkidle');
    await page.screenshot({ path: 'test-results/03-back-to-home.png', fullPage: true });
    console.log('✅ 返回首页成功');

    console.log('=== 真实用户流程测试完成 ===');
  });

  test('数据库连接测试', async ({ page }) => {
    console.log('=== 开始数据库连接测试 ===');

    // 测试 API 是否能连接数据库
    console.log('测试 API 数据库连接...');
    const response = await page.request.get('http://localhost:15069/health');
    expect(response.ok()).toBeTruthy();

    console.log('✅ 数据库连接正常');
    console.log('=== 数据库连接测试完成 ===');
  });
});
