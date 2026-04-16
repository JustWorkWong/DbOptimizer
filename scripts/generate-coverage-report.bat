@echo off
REM 生成测试覆盖率报告 (Windows 版本)

echo ==========================================
echo 开始收集测试覆盖率...
echo ==========================================

REM 清理旧的覆盖率数据
if exist coverage rmdir /s /q coverage
mkdir coverage

REM 运行所有测试并收集覆盖率
dotnet test ^
  --collect:"XPlat Code Coverage" ^
  --results-directory ./coverage ^
  --logger "console;verbosity=detailed" ^
  /p:CollectCoverage=true ^
  /p:CoverletOutputFormat=cobertura ^
  /p:CoverletOutput=./coverage/ ^
  /p:Threshold=80 ^
  /p:ThresholdType=line

if %ERRORLEVEL% neq 0 (
  echo 测试执行失败
  exit /b %ERRORLEVEL%
)

echo.
echo ==========================================
echo 生成 HTML 覆盖率报告...
echo ==========================================

REM 查找生成的 coverage.cobertura.xml 文件
for /r coverage %%f in (coverage.cobertura.xml) do set COVERAGE_FILE=%%f

if not defined COVERAGE_FILE (
  echo 错误: 未找到 coverage.cobertura.xml 文件
  echo 尝试查找其他覆盖率文件...
  dir /s /b coverage\*.xml
  exit /b 1
)

echo 找到覆盖率文件: %COVERAGE_FILE%

REM 检查是否安装了 reportgenerator
where reportgenerator >nul 2>nul
if %ERRORLEVEL% neq 0 (
  echo 安装 ReportGenerator 工具...
  dotnet tool install --global dotnet-reportgenerator-globaltool
)

REM 生成 HTML 报告
reportgenerator ^
  -reports:"%COVERAGE_FILE%" ^
  -targetdir:./coverage/report ^
  -reporttypes:Html ^
  -verbosity:Info

echo.
echo ==========================================
echo 覆盖率报告生成完成！
echo ==========================================
echo 报告位置: ./coverage/report/index.html
echo.
echo 在浏览器中打开报告:
echo   start ./coverage/report/index.html
echo.

REM 生成文本摘要
reportgenerator ^
  -reports:"%COVERAGE_FILE%" ^
  -targetdir:./coverage/summary ^
  -reporttypes:TextSummary ^
  -verbosity:Error

if exist ./coverage/summary/Summary.txt (
  echo 覆盖率摘要:
  type ./coverage/summary/Summary.txt
)

pause
