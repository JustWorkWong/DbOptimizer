import { test, expect } from '@playwright/test';

test('DbOptimizer 完整流程测试', async ({ page }) => {
  console.log('=== 开始测试 ===');

  // 1. 打开前端
  console.log('1. 打开前端 http://localhost:9158');
  await page.goto('http://localhost:9158');

  // 2. 等待页面加载
  await page.waitForLoadState('networkidle');
  console.log('2. 页面加载完成');

  // 3. 截图查看初始状态
  await page.screenshot({ path: 'test-results/01-initial.png', fullPage: true });
  console.log('3. 截图保存: 01-initial.png');

  // 4. 查找 SQL 输入框
  const sqlInput = page.locator('textarea, input[type="text"]').first();
  await sqlInput.fill('SELECT * FROM users WHERE id = 1');
  console.log('4. 输入 SQL');

  // 5. 查找分析按钮
  const analyzeBtn = page.locator('button:has-text("分析"), button:has-text("Analyze")').first();
  await analyzeBtn.click();
  console.log('5. 点击分析按钮');

  // 6. 等待结果
  await page.waitForTimeout(3000);
  console.log('6. 等待 3 秒');

  // 7. 截图查看结果
  await page.screenshot({ path: 'test-results/02-result.png', fullPage: true });
  console.log('7. 截图保存: 02-result.png');

  // 8. 检查是否有错误
  const errors = await page.locator('.error, .el-message--error').count();
  console.log('8. 错误数量:', errors);

  // 9. 获取页面文本内容
  const bodyText = await page.locator('body').textContent();
  console.log('9. 页面内容预览:', bodyText?.substring(0, 200));

  console.log('=== 测试完成 ===');
});
