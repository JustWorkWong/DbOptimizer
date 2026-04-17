# Aspire Frontend Integration Testing With Chrome DevTools MCP

## Goal

This runbook is for real frontend integration testing of DbOptimizer with:

- local Aspire startup
- real browser access from the frontend
- Chrome DevTools MCP as the browser control channel

Use this document when we need to verify the full user flow instead of calling backend APIs directly.

## Why This Exists

We hit two repeatable Aspire startup problems during local testing:

1. `dotnet run --no-launch-profile` causes Aspire dashboard startup to fail because required dashboard environment variables are missing.
2. `dotnet run --launch-profile http` still fails unless `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` is set.

This document records the correct startup sequence so we do not repeat those mistakes.

## Prerequisites

- Docker is running
- .NET SDK is installed
- Node.js and npm are installed
- Google Chrome is installed
- Codex global config has a `chrome-devtools` MCP entry

Configured locally in:

`C:\Users\Tengfengsu\.codex\config.toml`

Recommended MCP entry:

```toml
[mcp_servers.chrome-devtools]
command = "npx"
args = ["-y", "chrome-devtools-mcp@latest", "--browserUrl", "http://127.0.0.1:9222"]
startup_timeout_sec = 60
```

Important:

- after adding or changing this MCP entry, restart Codex before trying to use the new tools

## Step 1: Start Chrome With Remote Debugging

Chrome DevTools MCP needs a debuggable browser instance.

Start Chrome on Windows:

```powershell
Start-Process "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe" `
  -ArgumentList @(
    "--remote-debugging-port=9222",
    "--user-data-dir=E:\wfcodes\DbOptimizer\.chrome-devtools-profile"
  )
```

Verify the debugger endpoint:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:9222/json/version
```

Expected:

- HTTP `200`
- response JSON contains a `webSocketDebuggerUrl`

## Step 2: Start Aspire Correctly

Do not use:

```powershell
dotnet run --project E:\wfcodes\DbOptimizer\src\DbOptimizer.AppHost\DbOptimizer.AppHost.csproj --no-launch-profile
```

That mode misses Aspire dashboard variables and will fail before the app graph is ready.

Use this instead:

```powershell
$env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = "true"
dotnet run --project E:\wfcodes\DbOptimizer\src\DbOptimizer.AppHost\DbOptimizer.AppHost.csproj --launch-profile http
```

Expected console output includes lines like:

- `Now listening on: http://localhost:15258`
- `Login to the dashboard at http://localhost:15258/login?...`

## Step 3: Open Aspire Dashboard

Open the login URL printed by AppHost.

Typical example:

```text
http://localhost:15258/login?t=<token>
```

Inside the dashboard:

- confirm `api` is healthy
- confirm `web` is healthy
- confirm `postgres`, `mysql`, and `redis` resources are healthy
- confirm admin pages such as `pgAdmin`, `phpMyAdmin`, and `RedisInsight` are reachable

## Step 4: Find The Frontend And API URLs

Aspire resource ports are dynamic. Do not assume the frontend is always `5173` or the API is always `5080`.

Find the real endpoints from the dashboard resource list.

Example from one local run:

- frontend: `http://127.0.0.1:14779`
- API health endpoint: `http://127.0.0.1:18103/health`
- dashboard: `http://127.0.0.1:15258`

Because ports change between runs, always trust the Aspire dashboard first.

## Step 5: Prepare Test Data

Before testing SQL analysis, make sure the target database actually has a usable table.

Example PostgreSQL test data:

```powershell
docker exec <postgres-container-name> psql -U postgres -d dboptimizer -c "CREATE TABLE IF NOT EXISTS users (id serial PRIMARY KEY, age int NOT NULL, created_at timestamptz NOT NULL DEFAULT now(), email text); CREATE INDEX IF NOT EXISTS idx_users_created_at ON users(created_at DESC); INSERT INTO users(age, email) SELECT (random()*60)::int + 18, md5(random()::text) || '@example.com' FROM generate_series(1, 500) ON CONFLICT DO NOTHING;"
```

Tip:

- the Aspire PostgreSQL container name is dynamic too, so confirm it with `docker ps`

## Step 6: Run Frontend Integration Test Through Chrome DevTools MCP

After restarting Codex so the `chrome-devtools` MCP is loaded:

1. navigate to the Aspire frontend URL
2. enter the SQL to analyze
3. submit from the page
4. watch the progress panel update in real time
5. verify executor details, tool results, and token usage are visible
6. switch to review view and approve the generated review task
7. switch to history and replay views and confirm the execution chain is preserved

Core checks:

- the flow is launched from the frontend page, not by direct API calls
- live progress shows real executor details instead of only generic labels
- history detail contains:
  - executor input and output
  - tool calls
  - decisions
  - errors
  - token usage
- replay shows rich event payloads for the same workflow

## Step 7: Database And Logs Verification

After browser testing completes:

- confirm the workflow reaches `Completed`
- confirm `review_tasks` is approved
- confirm `agent_executions`, `tool_calls`, `decision_records`, and `error_logs` are persisted
- scan AppHost and API logs for startup issues, degraded fallback paths, and workflow completion

## Common Failures

### Failure 1: Aspire dashboard variables missing

Symptom:

- AppHost fails with missing dashboard environment variables

Cause:

- started with `--no-launch-profile`

Fix:

- start with `--launch-profile http`
- do not skip launch settings for Aspire

### Failure 2: Unsecured transport validation error

Symptom:

- AppHost fails with:
  - `The 'applicationUrl' setting must be an https address unless the 'ASPIRE_ALLOW_UNSECURED_TRANSPORT' environment variable is set to true`

Cause:

- HTTP launch profile used without the extra Aspire override

Fix:

```powershell
$env:ASPIRE_ALLOW_UNSECURED_TRANSPORT = "true"
dotnet run --project E:\wfcodes\DbOptimizer\src\DbOptimizer.AppHost\DbOptimizer.AppHost.csproj --launch-profile http
```

### Failure 3: Resource ports changed

Symptom:

- old frontend or API URLs stop responding

Cause:

- Aspire assigned new dynamic ports on this run

Fix:

- re-open the Aspire dashboard
- use the current resource URLs from that run

### Failure 4: Chrome DevTools MCP tool not visible

Symptom:

- MCP config exists but Codex cannot call `chrome-devtools` tools

Cause:

- current session was started before the MCP entry was added

Fix:

- restart Codex after editing `C:\Users\Tengfengsu\.codex\config.toml`

### Failure 5: Chrome DevTools MCP cannot connect

Symptom:

- MCP server starts but browser actions fail

Cause:

- Chrome remote debugging was not started on port `9222`

Fix:

- restart Chrome with `--remote-debugging-port=9222`
- verify `http://127.0.0.1:9222/json/version`

## Recommended Real Test Checklist

- Chrome started with remote debugging
- Codex restarted after MCP configuration change
- Aspire started with `--launch-profile http`
- `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` set in the same terminal
- dashboard login URL opened successfully
- dynamic frontend and API endpoints confirmed from the dashboard
- browser flow executed from the frontend page
- review approval executed from the UI flow
- history and replay verified
- DB records confirmed
- logs checked for degraded paths and workflow completion
