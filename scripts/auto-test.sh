#!/bin/bash
set -e

echo "=== Starting Aspire ==="
cd "$(dirname "$0")/.."
dotnet run --project src/DbOptimizer.AppHost &
ASPIRE_PID=$!

echo "Aspire PID: $ASPIRE_PID"

# 等待服务就绪
echo "Waiting for services..."
for i in {1..30}; do
  if curl -f http://localhost:5173 >/dev/null 2>&1 && \
     curl -f http://localhost:5000/health >/dev/null 2>&1; then
    echo "✅ Services ready!"
    break
  fi
  sleep 2
done

echo ""
echo "=== Services Started ==="
echo "Frontend: http://localhost:5173"
echo "Backend: http://localhost:5000"
echo "Aspire Dashboard: http://localhost:18888"
echo ""
echo "Now tell Claude to start testing!"
echo ""
echo "To stop: kill $ASPIRE_PID"
