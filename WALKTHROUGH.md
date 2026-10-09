# The Oracle — 10-stop code walkthrough

A $2 plastic Magic 8-Ball, rebuilt as a real-time, observable, cloud-native .NET
microservice. The joke is the over-engineering. The point is that every
over-engineered piece is a real, idiomatic .NET pattern you can defend line by
line in an interview.

Demo it live first: open the app, ask it something, watch the ball shake and
the answer stream back. Then walk through the code.

1. **`src/Oracle.Api/wwwroot/`** — It's a toy. Click the ball.

2. **`src/Oracle.Api/OracleHub.cs` — `Ask`** — The hub does zero divination. It
   validates the question, checks a per-connection rate limit, and enqueues a
   `DivinationRequest` onto the channel. Ingest is fully decoupled from
   processing.

3. **`src/Oracle.Api/DivinationChannel.cs`** — A *bounded* `Channel<T>`
   (capacity 50). `Writer.TryWrite` never blocks — if the channel is full it
   returns `false` immediately, which the hub turns into a "the cosmos is
   busy" rejection. Back-pressure for free.

4. **`src/Oracle.Api/DivinationWorker.cs`** — A singleton `BackgroundService`,
   but `OracleDbContext` is scoped. A fresh `IServiceScopeFactory`-created
   scope is resolved per message — the classic captive-dependency trap,
   handled correctly. This is also where the actual divination happens, the
   artificial "cosmic latency" is applied, the result is persisted, and the
   answer is pushed back over SignalR via `IHubContext<OracleHub>`.

5. **`src/Oracle.Core/OracleEngine.cs` + `OracleOptions.cs`** — Framework-free
   domain code (`Oracle.Core` has no ASP.NET, no EF reference at all).
   Weighted selection by mood, seedable `Random` for deterministic tests. Mood
   and cosmic-latency range are bound from `appsettings.json` → `Oracle:*`,
   overridable via env var (`Oracle__Mood=Grumpy`) — never client-supplied.

6. **`src/Oracle.Generators/ProphecyCatalogGenerator.cs`** — A true
   incremental Roslyn source generator. No magic strings anywhere at
   runtime — the 20-answer catalog is generated at compile time from
   `prophecies.txt`. Malformed lines or unrecognized kinds emit a real
   compiler diagnostic (`ORCL001`/`ORCL002`) instead of failing silently.

7. **`src/Oracle.Api/Observability/OracleTelemetry.cs`** — A custom
   `ActivitySource` and `Meter`. Every ask produces an `oracle.divine` span
   tagged with question length, prophecy kind, and cosmic latency, plus a
   counter and a latency histogram. Watch the console exporter while you ask
   questions.

8. **Rate limiter + health checks** (`Program.cs`) — A fixed-window limiter
   (5 asks / 10s) on the REST fallback, a matching per-connection limiter on
   the hub, and `/health` wired to an EF Core DB check. Production hygiene on
   a plastic toy.

9. **Tests + CI** — `Oracle.Core.Tests` has a real seeded-determinism test
   (two independently-seeded engines, same first draw) and a statistical
   mood-bias test (`Optimistic` beats `Grumpy` on affirmatives over 1000
   draws, fixed seed — not a flaky threshold). `Oracle.Generators.Tests`
   compiles the generator's output and checks all 20 prophecies and all three
   kinds exist. `.github/workflows/ci.yml` runs all of it, plus a Docker
   build, on every push.

10. **Close:** It's a $2 toy. It's also a tour of modern .NET — real-time
    pipelines, source generation, observability, and the scoped-DbContext-in-
    a-singleton trap handled correctly. That was the joke.

## Running it

```bash
dotnet restore
dotnet run --project src/Oracle.Api
# → http://localhost:8080 (the DB migration runs automatically on startup)

dotnet test

docker compose up --build
# → http://localhost:8080
```
