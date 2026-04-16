#!/bin/bash
# 生成测试覆盖率报告

set -e

echo "=========================================="
echo "开始收集测试覆盖率..."
echo "=========================================="

# 清理旧的覆盖率数据
rm -rf coverage/
mkdir -p coverage/

# 运行所有测试并收集覆盖率
dotnet test \
  --collect:"XPlat Code Coverage" \
  --results-directory ./coverage \
  --logger "console;verbosity=detailed" \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=./coverage/ \
  /p:Threshold=80 \
  /p:ThresholdType=line

echo ""
echo "=========================================="
echo "生成 HTML 覆盖率报告..."
echo "=========================================="

# 查找生成的 coverage.cobertura.xml 文件
COVERAGE_FILE=$(find ./coverage -name "coverage.cobertura.xml" | head -n 1)

if [ -z "$COVERAGE_FILE" ]; then
  echo "错误: 未找到 coverage.cobertura.xml 文件"
  echo "尝试查找其他覆盖率文件..."
  find ./coverage -name "*.xml" -type f
  exit 1
fi

echo "找到覆盖率文件: $COVERAGE_FILE"

# 检查是否安装了 reportgenerator
if ! command -v reportgenerator &> /dev/null; then
  echo "安装 ReportGenerator 工具..."
  dotnet tool install --global dotnet-reportgenerator-globaltool
fi

# 生成 HTML 报告
reportgenerator \
  -reports:"$COVERAGE_FILE" \
  -targetdir:./coverage/report \
  -reporttypes:Html \
  -verbosity:Info

echo ""
echo "=========================================="
echo "覆盖率报告生成完成！"
echo "=========================================="
echo "报告位置: ./coverage/report/index.html"
echo ""
echo "在浏览器中打开报告:"
echo "  Windows: start ./coverage/report/index.html"
echo "  macOS:   open ./coverage/report/index.html"
echo "  Linux:   xdg-open ./coverage/report/index.html"
echo ""

# 提取覆盖率摘要
if command -v reportgenerator &> /dev/null; then
  echo "覆盖率摘要:"
  reportgenerator \
    -reports:"$COVERAGE_FILE" \
    -targetdir:./coverage/summary \
    -reporttypes:TextSummary \
    -verbosity:Error

  if [ -f ./coverage/summary/Summary.txt ]; then
    cat ./coverage/summary/Summary.txt
  fi
fi
