import { test, expect } from '@playwright/test';

test('DbOptimizer 详细诊断测试', async ({ page }) => {
  console.log('=== 开始详细诊断 ===');

  // 监听所有 console 消息
  const consoleLogs: string[] = [];
  page.on('console', msg => {
    const text = `[${msg.type()}] ${msg.text()}`;
    consoleLogs.push(text);
    console.log('浏览器日志:', text);
  });

  // 监听页面错误
  const pageErrors: string[] = [];
  page.on('pageerror', error => {
    const text = `页面错误: ${error.message}`;
    pageErrors.push(text);
    console.log(text);
  });

  // 监听网络请求失败
  const failedRequests: string[] = [];
  page.on('requestfailed', request => {
    const text = `请求失败: ${request.url()} - ${request.failure()?.errorText}`;
    failedRequests.push(text);
    console.log(text);
  });

  // 1. 打开前端
  console.log('1. 打开前端 http://localhost:9158');
  await page.goto('http://localhost:9158');
  await page.waitForLoadState('networkidle');

  // 2. 截图初始状态
  await page.screenshot({ path: 'test-results/diagnostic-01-initial.png', fullPage: true });

  // 3. 查找所有错误元素
  const errorElements = await page.locator('.error, .el-message--error, [class*="error"]').all();
  console.log(`3. 发现 ${errorElements.length} 个错误元素`);

  for (let i = 0; i < errorElements.length; i++) {
    const text = await errorElements[i].textContent();
    console.log(`   错误 ${i + 1}: ${text}`);
  }

  // 4. 查找 SQL 输入框
  console.log('4. 查找 SQL 输入框');
  const sqlInput = page.locator('textarea').first();
  const isVisible = await sqlInput.isVisible();
  console.log(`   SQL 输入框可见: ${isVisible}`);

  if (isVisible) {
    await sqlInput.fill('SELECT * FROM users WHERE id = 1');
    console.log('   已填写 SQL');
  }

  // 5. 查找分析按钮
  console.log('5. 查找分析按钮');
  const buttons = await page.locator('button').all();
  console.log(`   找到 ${buttons.length} 个按钮`);

  for (let i = 0; i < buttons.length; i++) {
    const text = await buttons[i].textContent();
    console.log(`   按钮 ${i + 1}: ${text}`);
  }

  const analyzeBtn = page.locator('button:has-text("开始")').first();
  const btnVisible = await analyzeBtn.isVisible();
  console.log(`   分析按钮可见: ${btnVisible}`);

  if (btnVisible) {
    await analyzeBtn.click();
    console.log('   已点击分析按钮');
    await page.waitForTimeout(3000);
  }

  // 6. 截图结果
  await page.screenshot({ path: 'test-results/diagnostic-02-result.png', fullPage: true });

  // 7. 检查数据库连接
  console.log('7. 检查后端 API');
  const response = await page.request.get('http://localhost:15069/health').catch(() => null);
  console.log(`   后端健康检查: ${response ? response.status() : '无法连接'}`);

  // 8. 总结
  console.log('\n=== 诊断总结 ===');
  console.log(`Console 日志数: ${consoleLogs.length}`);
  console.log(`页面错误数: ${pageErrors.length}`);
  console.log(`失败请求数: ${failedRequests.length}`);

  if (pageErrors.length > 0) {
    console.log('\n页面错误详情:');
    pageErrors.forEach(err => console.log(`  - ${err}`));
  }

  if (failedRequests.length > 0) {
    console.log('\n失败请求详情:');
    failedRequests.forEach(req => console.log(`  - ${req}`));
  }
});
