# The Oracle — a cloud-native Magic 8-Ball

A $2 plastic fortune-telling toy, rebuilt as a real-time, observable,
cloud-native .NET microservice. See [WALKTHROUGH.md](WALKTHROUGH.md) for the
full 10-stop code tour.

## What's in here

* `Oracle.Core` — framework-free domain: the divination engine, mood-weighted
  selection, seedable for deterministic tests.
* `Oracle.Generators` — a real incremental Roslyn source generator that emits
  the 20-answer catalog at compile time from `prophecies.txt`. No magic
  strings at runtime.
* `Oracle.Infrastructure` — EF Core + SQLite, with a real `dotnet ef`-generated
  migration committed to the repo.
* `Oracle.Api` — ASP.NET Core Minimal API: a SignalR hub that only enqueues, a
  bounded `Channel<T>` for back-pressure, a singleton `BackgroundService`
  worker that does the actual divination (with artificial "cosmic latency"),
  persists it, and pushes the answer back over SignalR. Plus a REST fallback,
  OpenTelemetry tracing/metrics, rate limiting, and health checks.
* A vanilla HTML/CSS/JS front end (`Oracle.Api/wwwroot`) — no build step, no
  CDNs, SignalR JS client vendored locally.

## SDK version

`global.json` pins **.NET 10**. The host this was built and verified on had
the .NET 10 SDK installed and building clean; if you're on an older machine
with only .NET 9, `rollForward: latestFeature` will fall back automatically.

## Running it locally

```bash
dotnet restore
dotnet run --project src/Oracle.Api
```

Opens on `http://localhost:8080` (or `$PORT` if set). The database migration
runs automatically on startup — no manual `dotnet ef database update` needed
on a clean clone.

* `http://localhost:8080/health` → `Healthy`
* `http://localhost:8080` → the actual demo UI
* REST fallback: `curl -X POST http://localhost:8080/api/v1/ask -H "Content-Type: application/json" -d "{\"question\":\"Will I get the job?\"}"`
* Recent history: `curl http://localhost:8080/api/v1/history?take=5`

## Tests

```bash
dotnet test
```

4 tests: engine determinism (two independently-seeded engines produce the
same first draw), a statistical mood-bias check (`Optimistic` beats `Grumpy`
over 1000 seeded draws), empty-question validation, and a generator snapshot
test (20 prophecies, all three kinds present in the generated catalog).

## Docker

```bash
docker compose up --build
```

Single `oracle` service, SQLite file persisted to a named volume, reachable at
`http://localhost:8080`.

*Not verified in this environment* — the machine this was built on has no
Docker available (no admin rights). The Dockerfile and compose file were
written and reviewed carefully, but you should confirm `docker compose up`
actually works on your machine before relying on it.

## Deploying to Render

`render.yaml` at the repo root defines a free Docker web service pointed at
`src/Oracle.Api/Dockerfile`. Push to GitHub, connect the repo in Render, and
it auto-detects the blueprint.

**Known limitation:** Render's free tier filesystem is ephemeral — the SQLite
file resets on redeploy/restart unless you attach a paid persistent disk.
Fine for an interview demo (history just resets occasionally), but worth
knowing.

## Configuration

`Oracle:Mood` (`Balanced` / `Optimistic` / `Grumpy`) and
`Oracle:CosmicLatencyMinMs` / `CosmicLatencyMaxMs` are bound from
`appsettings.json`, overridable via environment variables
(e.g. `Oracle__Mood=Grumpy`) — never client-supplied, by design.
