import { test, expect } from '@playwright/test';

test('DbOptimizer 完整流程测试', async ({ page }) => {
  // 1. 打开前端
  await page.goto('http://localhost:9158');

  // 2. 等待页面加载
  await page.waitForLoadState('networkidle');

  // 3. 截图查看初始状态
  await page.screenshot({ path: 'artifacts/01-initial.png', fullPage: true });

  // 4. 查找 SQL 输入框
  const sqlInput = page.locator('textarea, input[type="text"]').first();
  await sqlInput.fill('SELECT * FROM users WHERE id = 1');

  // 5. 查找分析按钮
  const analyzeBtn = page.locator('button:has-text("分析"), button:has-text("Analyze")').first();
  await analyzeBtn.click();

  // 6. 等待结果
  await page.waitForTimeout(3000);

  // 7. 截图查看结果
  await page.screenshot({ path: 'artifacts/02-result.png', fullPage: true });

  // 8. 检查是否有错误
  const errors = await page.locator('.error, .el-message--error').count();
  console.log('错误数量:', errors);

  // 9. 获取 console 日志
  page.on('console', msg => console.log('浏览器日志:', msg.text()));
});
