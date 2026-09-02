# Calcpad.Web Backend (Local Mode)

> **Localhost-only build.** This branch (`calcpad-web`) only supports running the server bound to a loopback address. The startup loopback guard in [Program.cs](Program.cs) throws `InvalidOperationException` if the resolved bind URL is anything other than `localhost`, `127.0.0.0/8`, or `::1`. Multi-user / hosted / Docker deployment, auth, file storage, and per-user caching live on the `calcpad-experimental` branch.

The Calcpad.Web backend is an ASP.NET Core Web API that powers the standalone web editor, the VS Code extension, and the Tauri desktop wrapper. It exposes conversion, linting, highlighting, definitions, symbol-resolution, snippet, and PDF endpoints over a single local HTTP listener.

## Running

```bash
cd Calcpad.Web/backend
dotnet run
```

With no port set, the server binds `http://127.0.0.1:0` and takes a free port from the OS, so instances never collide. The bound URL is on the `Now listening on:` startup line and in the port file (`.calcpad-server.port` beside the binary, or wherever `--port-file` points).

Pin the port with the `CALCPAD_PORT` environment variable, or set the full bind URL with `--urls` — as long as it still resolves to a loopback address. `ASPNETCORE_URLS` is ignored, since the host calls `UseUrls` explicitly.

```bash
CALCPAD_PORT=9500 dotnet run
dotnet run -- --urls http://127.0.0.1:9500
```

The server also exits when its stdin reaches EOF, so a launch with piped stdin shuts down immediately. Set `CALCPAD_DETACHED=1` to opt out — the VS Code extension does, so one server can be shared across windows.

Any attempt to bind to a non-loopback host (`0.0.0.0`, a LAN address, a domain name) is rejected at startup with `InvalidOperationException`.

## Health check

```
GET  /api/calcpad/health          → { "status": "ok" }
GET  /api/calcpad/pdf/health      → { "status": "ok", "service": "calcpad-pdf", ... }
```

`/health` is process liveness — it does no work, writes no log line, and is safe to poll. `/pdf/health` is the Chromium readiness check for PDF export.

## Endpoints

Documented in [API_SCHEMA.md](API_SCHEMA.md). Summary:

- `GET  /api/calcpad/health` — liveness probe; does no work and writes no log line
- `POST /api/calcpad/convert` — Calcpad source to HTML (`?unwrap=true` for the expanded source)
- `POST /api/calcpad/docx`, `/pdf` — document export (`/pdf/health` for readiness, `/pdf/browser` and `/pdf/browser/install` for the Chromium dependency)
- `GET  /api/calcpad/sample` — sample document
- `POST /api/calcpad/highlight`, `/highlight-line` — tokenization
- `POST /api/calcpad/lint` — diagnostics with CPD codes
- `POST /api/calcpad/definitions` — symbol index
- `POST /api/calcpad/symbol-at-position` — the symbol under a cursor and all its occurrences
- `GET  /api/calcpad/snippets` — autocomplete catalog
- `POST /api/calcpad/prettify` — pretty-print Calcpad source
- `POST /api/calcpad/cpdz/decode`, `/cpdz/encode` — compiled `.cpdz` worksheets
- `POST /api/calcpad/portable/bundle`, `/portable/package` — self-contained worksheet and ZIP export
- `GET  /api/calcpad/debug-crash` — deliberately crash the server (Development only)
- `GET  /api/calcpad/log-level`, `POST /api/calcpad/log-level` — read and set log verbosity at runtime

## Configuration

`appsettings.json` carries the browser settings and the bind URL. It has no `Logging` section: verbosity comes from `CALCPAD_LOG_LEVEL` and `POST /api/calcpad/log-level`, which govern the framework's logging as well as Calcpad's. There are no JWT, Auth, Storage, or S3 sections on this branch.

| Variable | Default | Description |
|----------|---------|-------------|
| `CALCPAD_PORT` | *(unset — OS-assigned)* | Pins the bind port (host is always loopback) |
| `CALCPAD_HOST` | `127.0.0.1` | Host part of the bind URL |
| `CALCPAD_API_TOKEN` | *(unset — unauthenticated)* | Required in `X-Calcpad-Token` on every `/api` request when set |
| `CALCPAD_DETACHED` | *(unset)* | `1` disables the stdin-EOF watchdog and the default port file |
| `CALCPAD_LOG_LEVEL` | `warning` | Startup verbosity: `error`, `warning`, `information` or `verbose`. Covers ASP.NET's own logs too. Change it at runtime via `POST /api/calcpad/log-level` |
| `CALCPAD_LOG_DIR` | *(executable-adjacent `logs/`)* | Where `CalcpadServer-{date}.log` is written. Hosts set this when the install dir is read-only |
| `CALCPAD_HANG_THRESHOLD_SECONDS` | `60` | How long without a completed request before the hang watchdog reports |
| `CALCPAD_HANG_DUMP` | *(unset)* | `1` also spawns `createdump` on a detected hang |
| `CALCPAD_CONTENT_CACHE_SIZE_LIMIT` | `50000` | Flattened source lines budgeted across the resolved-content cache |
| `BROWSER_PATH` | *(auto-detect)* | Chromium-family executable used for PDF export |
| `ALLOW_CHROMIUM_DOWNLOAD` | `false` | Lets the render path download Chromium on its own |

`--urls` is a command-line flag only; `ASPNETCORE_URLS` is overridden by the host's own `UseUrls` call.
