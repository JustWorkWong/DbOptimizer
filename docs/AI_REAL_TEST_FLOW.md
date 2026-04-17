# AI Real Test Flow

## Purpose

This document records the end-to-end real test flow for DbOptimizer so the team can quickly repeat it after future changes.

It covers:

- starting the required local services
- starting the API and frontend
- creating a real SQL analysis session
- checking live progress, history replay, database records, and logs
- approving the review task and confirming final completion

## Preconditions

- Docker is available
- `.NET SDK` and `Node.js` are installed
- the local PostgreSQL and Redis containers can run
- frontend dependencies are installed under `src/DbOptimizer.Web`

Optional:

- if you want to test the AI runtime configuration itself, keep the local-only file `src/DbOptimizer.API/appsettings.Local.json` populated and excluded from git

## Local Dependencies

Start PostgreSQL and Redis for local real tests:

```powershell
docker run -d --name dbopt-postgres-test -p 15432:5432 -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=dboptimizer postgres:16
docker run -d --name dbopt-redis-test -p 16379:6379 redis:7
```

If the containers already exist:

```powershell
docker start dbopt-postgres-test
docker start dbopt-redis-test
```

Prepare a real target table for SQL analysis:

```powershell
docker exec dbopt-postgres-test psql -U postgres -d dboptimizer -c "CREATE TABLE IF NOT EXISTS users (id serial PRIMARY KEY, age int NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), email text); CREATE INDEX IF NOT EXISTS idx_users_created_at ON users(created_at DESC); INSERT INTO users(age, email) SELECT (random()*60)::int + 18, md5(random()::text) || '@example.com' FROM generate_series(1, 500) ON CONFLICT DO NOTHING;"
```

## Start API

Run the API with explicit local connection strings:

```powershell
$env:ASPNETCORE_URLS='http://127.0.0.1:5080'
$env:ConnectionStrings__PostgreSql='Host=127.0.0.1;Port=15432;Database=dboptimizer;Username=postgres;Password=postgres'
$env:ConnectionStrings__Redis='127.0.0.1:16379'
$env:DB_MYSQL_CONNECTION='Server=127.0.0.1;Port=3306;Database=dboptimizer;Uid=root;Pwd=rootpass'
dotnet run --project E:\wfcodes\DbOptimizer\src\DbOptimizer.API\DbOptimizer.API.csproj --no-launch-profile
```

Health check:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5080/health
```

Expected result:

- status code `200`
- response body contains `{"status":"ok"}`

## Start Frontend

```powershell
cd E:\wfcodes\DbOptimizer\src\DbOptimizer.Web
npm run dev -- --host 127.0.0.1 --port 5173
```

Open:

- `http://127.0.0.1:5173`
- `http://127.0.0.1:5080/swagger/index.html`

## Real SQL Analysis Test

Create a real SQL analysis session:

```powershell
$body = @{
  sqlText = 'SELECT * FROM users WHERE age > 18 ORDER BY created_at DESC'
  databaseId = 'postgres-local'
  databaseEngine = 'postgresql'
  options = @{
    enableIndexRecommendation = $true
    enableSqlRewrite = $true
  }
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5080/api/workflows/sql-analysis -ContentType 'application/json' -Body $body
```

Capture the returned `sessionId`.

## Check Live Progress

Poll the workflow status:

```powershell
$sessionId = '<SESSION_ID>'
Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/workflows/$sessionId"
```

Check SSE stream:

```powershell
Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:5080/api/workflows/$sessionId/events"
```

What to verify in the UI and SSE payloads:

- current executor name is readable
- executor payload includes `stage`, `message`, and `details`
- completed executor payload includes `durationMs`
- payload can expose `tokenUsage` when available
- progress view is not limited to raw internal event names

## Check History Detail And Replay

History detail:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/history/$sessionId" | ConvertTo-Json -Depth 12
```

Replay:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/history/$sessionId/replay" | ConvertTo-Json -Depth 10
```

What to verify:

- `executors` contains one item per execution node
- each executor includes:
  - `inputData`
  - `outputData`
  - `tokenUsage`
  - `toolCalls`
  - `decisions`
  - `errors`
- replay events include rich executor payloads, not only generic status strings

## Review Approval Flow

Find the pending review:

```powershell
Invoke-RestMethod -Uri 'http://127.0.0.1:5080/api/reviews?status=Pending&page=1&pageSize=10'
```

Approve it:

```powershell
$taskId = '<TASK_ID>'
$body = @{ action = 'approve'; comment = 'real test approval' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:5080/api/reviews/$taskId/submit" -ContentType 'application/json' -Body $body
```

Re-check:

```powershell
Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/workflows/$sessionId"
Invoke-RestMethod -Uri "http://127.0.0.1:5080/api/history/$sessionId" | ConvertTo-Json -Depth 12
```

Expected result:

- workflow status becomes `Completed`
- final history detail remains replayable
- review task status becomes `Approved`

## Database Verification

Check persistence counts for the session:

```powershell
docker exec dbopt-postgres-test psql -U postgres -d dboptimizer -t -A -c "SELECT 'workflow_sessions=' || count(*) FROM workflow_sessions WHERE session_id = '<SESSION_ID>' UNION ALL SELECT 'agent_executions=' || count(*) FROM agent_executions WHERE session_id = '<SESSION_ID>' UNION ALL SELECT 'tool_calls=' || count(*) FROM tool_calls WHERE execution_id IN (SELECT execution_id FROM agent_executions WHERE session_id = '<SESSION_ID>') UNION ALL SELECT 'decision_records=' || count(*) FROM decision_records WHERE execution_id IN (SELECT execution_id FROM agent_executions WHERE session_id = '<SESSION_ID>') UNION ALL SELECT 'error_logs=' || count(*) FROM error_logs WHERE session_id = '<SESSION_ID>' UNION ALL SELECT 'review_tasks=' || count(*) FROM review_tasks WHERE session_id = '<SESSION_ID>';"
```

Expected result:

- `workflow_sessions=1`
- `agent_executions > 0`
- `tool_calls >= 0`
- `decision_records >= 0`
- `error_logs >= 0`
- `review_tasks=1`

## Log Verification

During the test, review API logs for:

- health startup success
- EF migration completion
- executor transitions
- retry and fallback logs
- review submission completion

Useful log examples:

- `ExecutionPlanExecutor`
- `IndexAdvisorExecutor`
- `fallback`
- `WorkflowCompleted`
- `WorkflowFailed`

## Real Test Snapshot From 2026-04-16

One verified session from the latest real test:

- `sessionId = 25acbb80-c907-4b67-9d5c-5002d2e14adc`
- `reviewTaskId = 5752052f-b52b-4950-8e95-57a17f473d32`

Observed persistence counts:

- `workflow_sessions = 1`
- `agent_executions = 5`
- `tool_calls = 3`
- `decision_records = 5`
- `error_logs = 2`
- `review_tasks = 1`

Observed behavior:

- `history/{sessionId}` returned executor-level details, tool calls, decisions, errors, and aggregated token usage
- `history/{sessionId}/replay` returned rich event payloads with `message`, `stage`, and `details`
- workflow reached `WaitingForReview`, then `Completed` after approval

## Regression Checklist

Use this checklist after future changes:

- API builds successfully
- frontend builds successfully
- `/health` returns `200`
- `/swagger` returns `200`
- SQL analysis session can be created
- progress panel shows real executor details
- history detail shows executor/tool/decision/error/token sections
- replay endpoint shows rich event payloads
- review approval completes the workflow
- database records are present for the full execution chain
