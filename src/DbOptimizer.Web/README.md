# DbOptimizer.Web

Vue 3 + TypeScript + Vite frontend for `DbOptimizer`.

## Run modes

### Run with Aspire AppHost

Start:

```bash
dotnet run --project ../DbOptimizer.AppHost
```

In this mode:
- Aspire injects `VITE_API_PROXY_TARGET` for the web app.
- Aspire injects the fixed `PORT` for the web app.
- The browser-facing web port is `http://localhost:10817`.
- The API's stable published endpoint is `http://localhost:15069`.

### Run frontend standalone

Start:

```bash
set PORT=10817
set VITE_API_PROXY_TARGET=http://localhost:15069
npm install
npm run dev
```

In this mode:
- Vite serves the frontend locally.
- `/api/*` is proxied to `VITE_API_PROXY_TARGET`.
- Missing `PORT` or `VITE_API_PROXY_TARGET` is treated as a startup error.

## Port model

- `15069`: API published port for browsers, frontend proxying, Swagger, and health checks.
- `10817`: Web published port for browsers.
- Any other observed process port is not a browser contract and should not be used as the frontend API base.

Best practice is to treat only the published AppHost ports as contracts and fail fast when that wiring is missing.
