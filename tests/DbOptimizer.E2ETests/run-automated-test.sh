#!/bin/bash
set -e

echo "=== Claude Code 自动化测试开始 ==="

# 清理旧进程
cleanup() {
    echo "清理环境..."
    kill $API_PID $WEB_PID 2>/dev/null || true
    docker-compose -f ../../docker-compose.test.yml down -v
}
trap cleanup EXIT

# 1. 启动 Docker 容器
echo "[1/8] 启动 Docker 容器..."
docker-compose -f ../../docker-compose.test.yml up -d

# 2. 等待容器健康
echo "[2/8] 等待容器健康检查..."
for i in {1..30}; do
    if docker-compose -f ../../docker-compose.test.yml ps | grep -q "healthy"; then
        echo "✅ 容器健康"
        break
    fi
    echo "等待容器启动... ($i/30)"
    sleep 2
done

# 3. 启动 API（后台）
echo "[3/8] 启动 API..."
cd ../../src/DbOptimizer.API
export ConnectionStrings__PostgreSql="Host=localhost;Port=15432;Database=dboptimizer;Username=postgres;Password=postgres"
export ConnectionStrings__redis="localhost:15379"
export OTEL_EXPORTER_OTLP_ENDPOINT="http://localhost:4317"
dotnet run > api.log 2>&1 &
API_PID=$!
echo "API PID: $API_PID"
cd -

# 4. 启动前端（后台）
echo "[4/8] 启动前端..."
cd ../../src/DbOptimizer.Web
npm run dev > web.log 2>&1 &
WEB_PID=$!
echo "Web PID: $WEB_PID"
cd -

# 5. 等待服务就绪
echo "[5/8] 等待服务就绪..."
for i in {1..60}; do
    if curl -s http://localhost:15069/health 2>/dev/null | grep -q "Healthy"; then
        echo "✅ API 就绪"
        break
    fi
    echo "等待 API 启动... ($i/60)"
    sleep 2
done

for i in {1..30}; do
    if curl -s http://localhost:5173 > /dev/null 2>&1; then
        echo "✅ 前端就绪"
        break
    fi
    echo "等待前端启动... ($i/30)"
    sleep 2
done

echo "✅ 所有服务已就绪"

# 6. 运行测试（显示浏览器）
echo "[6/8] 运行 Playwright 测试（可见浏览器）..."
npx playwright test --headed --reporter=list || true

# 7. 分析结果
echo "[7/8] 分析测试结果..."
if [ -f "test-results.json" ] && grep -q '"status":"passed"' test-results.json; then
    echo "✅ 测试通过"
    EXIT_CODE=0
else
    echo "❌ 测试失败"
    echo "查看详细报告: npx playwright show-report"
    echo "API 日志: ../../src/DbOptimizer.API/api.log"
    echo "前端日志: ../../src/DbOptimizer.Web/web.log"
    EXIT_CODE=1
fi

echo "=== 自动化测试完成 ==="
exit $EXIT_CODE
